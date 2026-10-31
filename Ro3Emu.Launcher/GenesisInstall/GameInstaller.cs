using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Ro3Emu.Launcher.GenesisInstall;

/// <summary>
/// The launcher's install / update of the official client from Genesis' update servers (the part of RO3Emu's
/// InstallFlow it needs): ask for the signed release, compare the folder with it, download and extract what differs.
/// Pausing cancels the work and keeps the downloaded parts; the next run resumes them. RO3Emu patches the client at
/// launch, as before.
/// </summary>
public sealed class GameInstaller : IDisposable
{
	/// <summary>The release Genesis offers now.</summary>
	public sealed record Release(string Id, long Sequence);

	private readonly string?[] _configDirs;
	private readonly HttpClient _http;
	private readonly Action<string> _log;

	/// <param name="configDirs">Folders searched for RO3Emu.download.json, in order.</param>
	public GameInstaller(string?[] configDirs, string userAgent, Action<string> log)
	{
		_configDirs = configDirs;
		_log = log;
		_http = new HttpClient(new SocketsHttpHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
			ConnectTimeout = TimeSpan.FromSeconds(20)
		})
		{
			Timeout = Timeout.InfiniteTimeSpan
		};
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
	}

	/// <summary>Packages and cached manifests; outside the game folder, kept across pauses.</summary>
	public static string CacheDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RO3Emu", "downloads");

	private static string RecordFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RO3Emu", "installed-releases.json");

	private ClientInstaller NewInstaller() => new(PublisherConfig.Load(_configDirs), _http, CacheDir) { Log = _log };

	/// <summary>The current release, from the full manifest only (its ETag keeps a repeated check to a 304).</summary>
	public async Task<Release> CurrentReleaseAsync(CancellationToken ct)
	{
		var manifests = await NewInstaller().FetchAsync(withDelta: false, ct);
		return new Release(manifests.Full.Release.Id, manifests.Full.Release.Sequence);
	}

	/// <summary>
	/// The release installed in <paramref name="folder"/>: the newer of the one the Genesis launcher recorded
	/// (.ro3-update/installed-release.txt) and the one this launcher recorded last, or null when neither is known.
	/// </summary>
	public static long? InstalledSequence(string folder)
	{
		long? genesis = null;
		try
		{
			var path = Path.Combine(folder, ".ro3-update", "installed-release.txt");
			if (File.Exists(path))
			{
				var fields = File.ReadAllLines(path).Select(l => l.Split('\t')).Where(c => c.Length == 2).ToDictionary(c => c[0], c => c[1]);
				if (fields.TryGetValue("release_sequence", out var seq)
					&& long.TryParse(seq, NumberStyles.None, CultureInfo.InvariantCulture, out long n)) genesis = n;
			}
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
		long? own = LoadRecords().TryGetValue(Key(folder), out var recorded) ? recorded : null;
		return own == null || genesis > own ? genesis : own;
	}

	/// <summary>
	/// Brings <paramref name="folder"/> to Genesis' current release (only the files that differ). A DLL RO3Emu patched
	/// counts as present while its original in ro3emu-backup matches, so an update keeps the patch.
	/// </summary>
	public async Task InstallAsync(string folder, Action<string, long, long> progress, CancellationToken ct)
	{
		var installer = NewInstaller();
		installer.Progress = progress;
		progress("scanning", 0, 0);
		ClientInstaller.Manifests manifests;
		using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
		{
			timeout.CancelAfter(TimeSpan.FromMinutes(3));
			try { manifests = await installer.FetchAsync(withDelta: true, timeout.Token); }
			catch (OperationCanceledException) when (!ct.IsCancellationRequested)
			{
				throw new InstallException("Genesis' download servers did not answer in time.", "Try again.", InstallException.Unreachable);
			}
		}
		var release = manifests.Full.Release;
		_log($"Genesis release {release.Id} (sequence {release.Sequence}) from {manifests.Origin}");
		if (InstalledSequence(folder) is { } installed && installed > release.Sequence)
			throw new InstallException($"The servers offer release {release.Sequence}, older than the {installed} already installed here.",
				"Try again later; the launcher does not install an older release over a newer one.", InstallException.OlderRelease, release.Sequence, installed);

		var plan = installer.MakePlan(manifests, folder, ct: ct);
		_log($"{plan.Present} files already right ({plan.KeptPatched} patched by RO3Emu), {plan.Needed.Count} to get "
			+ $"({plan.DownloadBytes} bytes in {plan.Packages.Count} packages)");
		if (plan.Needed.Count > 0)
		{
			CheckSpace(folder, plan);
			await installer.ApplyAsync(plan, folder, ct);
		}
		Record(folder, release.Sequence);
	}

	/// <summary>
	/// The files plus the largest package must fit (each package is deleted once extracted, so they never pile up),
	/// with a margin; the download folder may sit on another drive.
	/// </summary>
	private static void CheckSpace(string folder, ClientInstaller.Plan plan)
	{
		const long margin = 1L << 30;
		Directory.CreateDirectory(folder);
		Directory.CreateDirectory(CacheDir);
		var gameDrive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder))!);
		var cacheDrive = new DriveInfo(Path.GetPathRoot(CacheDir)!);
		bool same = string.Equals(gameDrive.Name, cacheDrive.Name, StringComparison.OrdinalIgnoreCase);
		long needGame = plan.NeededBytes + (same ? plan.LargestPackage : 0) + margin;
		if (gameDrive.AvailableFreeSpace < needGame)
			throw new InstallException($"Drive {gameDrive.Name} has {Size(gameDrive.AvailableFreeSpace)} free; the game needs {Size(needGame)}.",
				"Free some space or choose a folder on another drive.", InstallException.NoSpace, gameDrive.Name, Size(gameDrive.AvailableFreeSpace), Size(needGame));
		long needCache = plan.LargestPackage + margin;
		if (!same && cacheDrive.AvailableFreeSpace < needCache)
			throw new InstallException($"Drive {cacheDrive.Name} (downloads) has {Size(cacheDrive.AvailableFreeSpace)} free; a package needs {Size(needCache)}.",
				"Free some space there.", InstallException.NoSpace, cacheDrive.Name, Size(cacheDrive.AvailableFreeSpace), Size(needCache));
	}

	private static string Size(long bytes) => (bytes / 1073741824.0).ToString("0.0", CultureInfo.InvariantCulture) + " GB";

	private static string Key(string folder) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)).ToLowerInvariant();

	private static Dictionary<string, long> LoadRecords()
	{
		try
		{
			return File.Exists(RecordFile)
				? JsonSerializer.Deserialize<Dictionary<string, long>>(File.ReadAllBytes(RecordFile)) ?? []
				: [];
		}
		catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
		{
			return [];
		}
	}

	private void Record(string folder, long sequence)
	{
		try
		{
			var records = LoadRecords();
			records[Key(folder)] = sequence;
			Directory.CreateDirectory(Path.GetDirectoryName(RecordFile)!);
			File.WriteAllText(RecordFile, JsonSerializer.Serialize(records));
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			_log($"Could not record the installed release: {e.Message}");
		}
	}

	public void Dispose() => _http.Dispose();
}
