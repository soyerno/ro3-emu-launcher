using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Ro3Emu.Launcher;

public sealed class RemoteUpdateOutcome
{
	public RemoteManifest? Manifest { get; init; }

	public ManifestSource Source { get; init; }

	/// <summary>Why the manifest was not fetched or not used, for launcher.log.</summary>
	public string? Error { get; init; }

	/// <summary>Set when the feature is off or misconfigured (no "manifestUrl", no public key).</summary>
	public string? DisabledReason { get; init; }

	/// <summary>The launcher exe was replaced: start it again and exit before doing anything else.</summary>
	public bool RestartRequired { get; init; }

	public FileApplyResult? Files { get; init; }
}

/// <summary>What the main screen shows about the installed build: the applied manifest and the installed client hooks.</summary>
public static class InstalledBuild
{
	/// <summary>Where the manifest entry of the client hooks lands, relative to the launcher folder.</summary>
	public const string HooksPath = "client/Ro3Emu.ClientHooks.dll";

	/// <summary>The first 8 lowercase hex digits of the installed hooks' SHA-256, or null when the file cannot be read.</summary>
	public static string? HooksHash(string launcherDir)
	{
		try
		{
			using FileStream stream = File.OpenRead(Path.Combine(launcherDir, HooksPath.Replace('/', Path.DirectorySeparatorChar)));
			return Convert.ToHexStringLower(SHA256.HashData(stream))[..8];
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	/// <summary>
	/// The sequence of the manifest whose files are in place after <paramref name="outcome"/>: its own once every file
	/// was applied, otherwise <paramref name="previous"/> (a manifest fetched but not fully applied does not count).
	/// Never lower than <paramref name="previous"/>: manifests only move forward (a rollback is refused), so a later,
	/// lower sequence cannot replace a higher one already known to be applied.
	/// </summary>
	public static long? AppliedSequence(RemoteUpdateOutcome outcome, long? previous)
	{
		return outcome.Manifest is { } manifest && outcome.Files is { NotWritable: false, Failed.Count: 0 }
			? Math.Max(manifest.Sequence, previous ?? long.MinValue)
			: previous;
	}
}

/// <summary>
/// The start-up update, run before the tunnels start: fetch and verify the manifest (or use the cached one), update the
/// launcher first (then restart), otherwise bring the server files beside the launcher in line with the manifest.
/// Failures never stop the launcher; they are logged and the previous files and configuration stay in use.
/// </summary>
public sealed class RemoteUpdater
{
	private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(10);

	private readonly LauncherConfig _config;
	private readonly string _exePath;
	private readonly string _runningVersion;
	private readonly HttpMessageHandler? _handler;
	private readonly string? _cacheDirectory;
	private readonly Action<string>? _log;

	public RemoteUpdater(LauncherConfig config, string exePath, string runningVersion, HttpMessageHandler? handler = null, string? cacheDirectory = null, Action<string>? log = null)
	{
		_config = config;
		_exePath = exePath;
		_runningVersion = runningVersion;
		_handler = handler;
		_cacheDirectory = cacheDirectory;
		_log = log;
	}

	/// <summary><paramref name="status"/> receives player-facing progress, only when something is actually being updated.</summary>
	public async Task<RemoteUpdateOutcome> RunAsync(Action<string>? status, CancellationToken ct)
	{
		if (string.IsNullOrWhiteSpace(_config.ManifestUrl))
		{
			return new RemoteUpdateOutcome { DisabledReason = "no manifestUrl configured" };
		}
		if (_config.ManifestUri is not { } manifestUrl)
		{
			return Disabled($"manifestUrl '{_config.ManifestUrl}' is not an https URL; auto-update disabled.");
		}
		if (!ManifestSignature.IsKeyConfigured(_config.ManifestPublicKey))
		{
			return Disabled("Manifest key not configured (launcher.json \"manifestPublicKey\"); auto-update disabled.");
		}

		using HttpClient manifestHttp = CreateClient(ManifestClient.FetchTimeout + TimeSpan.FromSeconds(1));
		ManifestCache cache = new ManifestCache(_cacheDirectory ?? ManifestCache.DefaultDirectory(manifestUrl));
		ManifestFetchResult fetched = await new ManifestClient(manifestHttp, cache, _config.ManifestPublicKey).GetAsync(manifestUrl, ct);
		_log?.Invoke(fetched.Source switch
		{
			ManifestSource.Remote => $"Update: manifest sequence {fetched.Manifest!.Sequence} verified.",
			ManifestSource.Cache => $"Update: using the cached manifest sequence {fetched.Manifest!.Sequence} ({fetched.Error}).",
			_ => $"Update: no manifest available ({fetched.Error}).",
		});
		if (fetched.Manifest is not { } manifest)
		{
			return new RemoteUpdateOutcome { Source = fetched.Source, Error = fetched.Error };
		}

		using HttpClient downloads = CreateClient(DownloadTimeout);
		if (manifest.Launcher is { } release)
		{
			if (LauncherSelfUpdater.IsUpdateAvailable(release, _runningVersion))
			{
				status?.Invoke(Strings.Get("update.launcher", release.Version));
				_log?.Invoke($"Update: launcher {_runningVersion} -> {release.Version}.");
				if (await LauncherSelfUpdater.ApplyAsync(downloads, manifestUrl, release, _exePath, ct))
				{
					return new RemoteUpdateOutcome { Manifest = manifest, Source = fetched.Source, Error = fetched.Error, RestartRequired = true };
				}
				_log?.Invoke($"Update: launcher {release.Version} could not be downloaded or verified; keeping {_runningVersion}.");
			}
			else if (LauncherVersion.IsDevelopment(_runningVersion))
			{
				_log?.Invoke($"Update: development build {_runningVersion} never replaces itself (manifest launcher {release.Version}).");
			}
		}

		string launcherDir = Path.GetDirectoryName(Path.GetFullPath(_exePath)) ?? AppContext.BaseDirectory;
		FileApplyResult files = await ManagedFiles.ApplyAsync(manifest.Files, manifestUrl, launcherDir, downloads,
			(n, total) => status?.Invoke(Strings.Get("update.files", n, total)), _log, ct);
		return new RemoteUpdateOutcome { Manifest = manifest, Source = fetched.Source, Error = fetched.Error, Files = files };

		RemoteUpdateOutcome Disabled(string reason)
		{
			_log?.Invoke(reason);
			return new RemoteUpdateOutcome { DisabledReason = reason };
		}
	}

	private HttpClient CreateClient(TimeSpan timeout)
	{
		HttpClient client = _handler != null ? new HttpClient(_handler, disposeHandler: false) : new HttpClient();
		client.Timeout = timeout;
		client.DefaultRequestHeaders.UserAgent.ParseAdd("Ro3Launcher/" + (LauncherVersion.TryParse(_runningVersion, out Version core, out _) ? core.ToString(3) : "0.0.0"));
		return client;
	}
}
