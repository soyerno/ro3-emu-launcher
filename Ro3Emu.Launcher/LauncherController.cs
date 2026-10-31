using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Ro3Emu.Launcher.GenesisInstall;
using Ro3Emu.Patcher;

namespace Ro3Emu.Launcher;

public sealed class LauncherController : IDisposable
{
	private sealed class SpeedMeter
	{
		private readonly Stopwatch _clock = Stopwatch.StartNew();

		private readonly Queue<(double At, long Bytes)> _samples = new Queue<(double, long)>();

		public (double Rate, double Eta) Sample(long done, long total)
		{
			double totalSeconds = _clock.Elapsed.TotalSeconds;
			_samples.Enqueue((totalSeconds, done));
			while (_samples.Count > 2 && totalSeconds - _samples.Peek().At > 5.0)
			{
				_samples.Dequeue();
			}
			(double, long) tuple = _samples.Peek();
			double num = totalSeconds - tuple.Item1;
			if (num < 1.0)
			{
				return (Rate: 0.0, Eta: 0.0);
			}
			double num2 = (double)(done - tuple.Item2) / num;
			if (!(num2 <= 0.0))
			{
				return (Rate: num2, Eta: (double)(total - done) / num2);
			}
			return (Rate: 0.0, Eta: 0.0);
		}
	}

	/// <summary>Replaced once at start when a newer verified manifest brings another remote configuration.</summary>
	private LauncherConfig _config;

	private readonly string _exePath;

	private readonly Action<object> _post;

	private readonly Func<string, string?> _pickFolder;

	private readonly Action _minimize;

	private readonly UserSettings _settings = UserSettings.Load();

	private readonly RepositoryClient _repo;

	private readonly HttpClient _http = new HttpClient
	{
		Timeout = TimeSpan.FromSeconds(5L)
	};

	/// <summary>The version stamped at publish (-p:Version); 0.0.0-dev for a local build, which never self-updates.</summary>
	private readonly string _version = BuildInfo.Version;

	private readonly CancellationTokenSource _life = new CancellationTokenSource();

	private InstallLayout _layout;

	private ChannelInfo? _channel;

	private LocalState? _installed;

	private string _phase = "checking";

	private CancellationTokenSource? _work;

	private bool _started;

	private TunnelManager _tunnels;

	/// <summary>The realm the player plays on: its server answers every call and its tunnels are the open ones.</summary>
	private RealmSpec _realm;

	/// <summary>Cancelled when the player switches realm, so the old realm's tunnel start and ping loop stop.</summary>
	private CancellationTokenSource _realmLife;

	/// <summary>True from the press of play until the game is started or the launch fails.</summary>
	private bool _launching;

	/// <summary>True while the start-up update (manifest, launcher, server files) runs; play waits for it.</summary>
	private bool _autoUpdating;

	/// <summary>A notice of the start-up update shown once the launcher is ready (for example a read-only folder).</summary>
	private string? _updateNotice;

	/// <summary>Sequence of the manifest whose files are in place beside the launcher; null when none is known.</summary>
	private long? _appliedSequence;

	// Game files from Genesis' update servers (unless launcher.json "gameFiles" is "repository").
	private readonly GameInstaller _genesis;

	private string? _genesisInstalled;

	private string? _genesisRemote;

	// The Genesis release check of the last CheckAsync; play waits for it so it never patches a client that is about to update.
	private Task _genesisCheck = Task.CompletedTask;

	public LauncherController(LauncherConfig config, string exePath, Action<object> post, Func<string, string?> pickFolder, Action minimize)
	{
		//IL_00a6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b0: Expected Obj, but got Unknown
		//IL_00c7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d1: Expected Obj, but got Unknown
		_config = config;
		_appliedSequence = config.ManifestSequence;
		_realm = config.SelectRealm(_settings.Realm);
		_tunnels = new TunnelManager(_realm.Tunnels);
		_realmLife = CancellationTokenSource.CreateLinkedTokenSource(_life.Token);
		_exePath = exePath;
		_post = post;
		_pickFolder = pickFolder;
		_minimize = minimize;
		// An invalid "repository" must not crash the start: the launcher opens and CheckAsync reports it.
		_repo = new RepositoryClient(config.RepositoryUri ?? new Uri(LauncherConfig.DefaultRepository), (HttpMessageHandler)null);
		_layout = new InstallLayout(_settings.ClientDirectory ?? FindDefaultClientDirectory(exePath), (string)null);
		string launcherDir = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;
		// RO3Emu.download.json (Genesis' hosts and key) is read next to the launcher, else next to RO3Emu.exe.
		_genesis = new GameInstaller([launcherDir, Path.GetDirectoryName(GameOrchestrator.FindRO3Emu(launcherDir) ?? launcherDir)], "Ro3Launcher/" + _version, Trace);
	}

	private static string FindDefaultClientDirectory(string exePath)
	{
		string? detected = GameOrchestrator.DetectGameDirectory(Path.GetDirectoryName(exePath));
		if (!string.IsNullOrEmpty(detected) && GameOrchestrator.IsValidGameDir(detected))
		{
			return detected;
		}
		string directoryName = Path.GetDirectoryName(exePath) ?? "";
		if (File.Exists(Path.Combine(directoryName, "ro3.exe")))
		{
			return directoryName;
		}
		string pRoo = Path.Combine(directoryName, "ROO_PC");
		if (File.Exists(Path.Combine(pRoo, "ro3.exe")))
		{
			return pRoo;
		}
		string pParentRoo = Path.Combine(directoryName, "..", "ROO_PC");
		if (File.Exists(Path.Combine(pParentRoo, "ro3.exe")))
		{
			return Path.GetFullPath(pParentRoo);
		}
		string text = Path.Combine(directoryName, "Client", "Ragnarok3");
		if (File.Exists(Path.Combine(text, "ro3.exe")))
		{
			return text;
		}
		string text2 = Path.Combine(directoryName, "Ragnarok3");
		if (File.Exists(Path.Combine(text2, "ro3.exe")))
		{
			return text2;
		}
		string text3 = Path.Combine(directoryName, "Client");
		if (File.Exists(Path.Combine(text3, "ro3.exe")))
		{
			return text3;
		}
		string text4 = Path.GetDirectoryName(directoryName) ?? "";
		if (File.Exists(Path.Combine(text4, "ro3.exe")))
		{
			return text4;
		}
		string text5 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "RO3Emu-win-x64");
		if (File.Exists(Path.Combine(text5, "ro3.exe")))
		{
			return text5;
		}
		return directoryName;
	}

	public async Task HandleMessageAsync(JsonElement message)
	{
		string text = (message.TryGetProperty("cmd", out var value) ? value.GetString() : null);
		if (text == null)
		{
			return;
		}
		switch (text)
		{
		case "language":
		{
			if (message.TryGetProperty("lang", out var langProp) && langProp.GetString() is string l)
			{
				Strings.SetLanguage(l);
				_config.Language = Strings.Language;
				UserSettings.SaveLanguage(Strings.Language);
			}
			break;
		}
		case "realm":
			await SelectRealmAsync(message.TryGetProperty("id", out var realmProp) ? realmProp.GetString() : null);
			break;
		case "openUrl":
		{
			if (message.TryGetProperty("url", out var urlProp) && urlProp.GetString() is string u)
			{
				if (Uri.TryCreate(u, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
				{
					try
					{
						Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
						{
							UseShellExecute = true
						});
					}
					catch
					{
					}
				}
			}
			break;
		}
		default:
			await HandleAsync(text);
			break;
		}
	}

	public async Task HandleAsync(string cmd)
	{
		switch (cmd)
		{
		case "ready":
			PostSettings();
			_ = PostVersionsAsync();
			if (!_started)
			{
				_started = true;
				// Before the tunnels: the update may replace cloudflared.exe and change the realms and their tunnels.
				if (!await RunStartupUpdateAsync())
				{
					break;
				}
				_ = PostVersionsAsync();
				_ = StartTunnelsAndPingAsync();
				_ = LoadNewsAsync();
			}
			await CheckAsync();
			if (_updateNotice != null && _phase == "ready")
			{
				PostState("ready", _updateNotice);
				_updateNotice = null;
			}
			break;
		case "primary":
		{
			if (_work != null || _autoUpdating)
			{
				break;
			}
			if (!_config.GameFilesFromRepository && !_genesisCheck.IsCompleted && _phase == "ready")
			{
				// The official game updates first and the server patch goes on top at play: wait for the release check.
				PostState("checking", Strings.Get("state.checking.game"));
				await _genesisCheck;
				if (_phase == "checking")
				{
					// Offline or blocked: the installed client still plays.
					PostState("ready", Strings.Get("state.ready"));
				}
				if (_phase == "update")
				{
					break;
				}
			}
			string phase = _phase;
			if (phase == "ready" || phase == "running")
			{
				await PlayAsync();
				break;
			}
			if (_phase == "install" && !File.Exists(Path.Combine(_layout.GameDirectory, "ro3.exe")) && !_config.GameFilesFromRepository)
			{
				// No game yet: the player picks where it goes (or an existing copy), unless a folder was already chosen.
				if (_settings.ClientDirectory == null)
				{
					await ChangeDirectoryAsync();
					if (_settings.ClientDirectory == null)
					{
						break;
					}
				}
				if (File.Exists(Path.Combine(_layout.GameDirectory, "ro3.exe")))
				{
					await PlayAsync();
				}
				else
				{
					await UpdateOfficialAsync();
				}
				break;
			}
			if (_phase == "install" && !File.Exists(Path.Combine(_layout.GameDirectory, "ro3.exe")))
			{
				await ChangeDirectoryAsync();
				if (File.Exists(Path.Combine(_layout.GameDirectory, "ro3.exe")))
				{
					await PlayAsync();
				}
				break;
			}
			bool flag;
			switch (_phase)
			{
			case "install":
			case "update":
			case "repair":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (flag)
			{
				await UpdateAsync(_phase == "repair");
			}
			else
			{
				await CheckAsync();
			}
			break;
		}
		case "repair":
			await RestoreOfficialAsync();
			break;
		case "pause":
			_work?.Cancel();
			break;
		case "changeDir":
			await ChangeDirectoryAsync();
			break;
		case "openDir":
			if (Directory.Exists(_layout.GameDirectory))
			{
				Process.Start(new ProcessStartInfo("explorer.exe", "\"" + _layout.GameDirectory + "\"")
				{
					UseShellExecute = true
				});
			}
			break;
		}
	}

	private void PostSettings()
	{
		_post(new
		{
			type = "settings",
			dir = _layout.GameDirectory,
			launcher = _version
		});
		PostRealms();
	}

	/// <summary>Tells the UI the launcher version, the applied manifest sequence and the installed client hooks' short hash.</summary>
	private async Task PostVersionsAsync()
	{
		string launcherDir = Path.GetDirectoryName(Path.GetFullPath(_exePath)) ?? AppContext.BaseDirectory;
		// Off the UI thread: it reads the hooks DLL from disk.
		string? hooks = await Task.Run(() => InstalledBuild.HooksHash(launcherDir));
		_post(new
		{
			type = "versions",
			launcher = _version,
			update = _appliedSequence?.ToString(CultureInfo.InvariantCulture),
			hooks
		});
	}

	/// <summary>Tells the UI the realms and the selected one; with one realm the UI shows no picker.</summary>
	private void PostRealms(string? message = null)
	{
		_post(new
		{
			type = "realms",
			selected = _realm.Id,
			realms = _config.GetRealms().Select(r => new { id = r.Id, name = r.Name }).ToArray(),
			message = message
		});
	}

	/// <summary>
	/// Switches to another realm: stops the old realm's tunnels and ping, then connects to the new one. Refused while
	/// the game runs or is being launched or updated, because join re-patches the client for one realm at a time.
	/// </summary>
	private async Task SelectRealmAsync(string? id)
	{
		RealmSpec next = _config.SelectRealm(id);
		if (next == _realm)
		{
			PostRealms();
			return;
		}
		if (_launching || _work != null || _phase is "launching" or "updating" or "running" || GameOrchestrator.IsGameRunning())
		{
			Trace($"Realm switch to '{next.Id}' refused: the game is running or busy.");
			PostRealms(Strings.Get("realm.locked"));
			return;
		}
		Trace($"Switching realm: '{_realm.Id}' -> '{next.Id}'.");
		_realmLife.Cancel();
		_realmLife.Dispose();
		_realmLife = CancellationTokenSource.CreateLinkedTokenSource(_life.Token);
		TunnelManager old = _tunnels;
		_realm = next;
		_tunnels = new TunnelManager(next.Tunnels);
		UserSettings.SaveRealm(next.Id);
		PostRealms();
		_post(new
		{
			type = "server",
			online = false,
			players = (int?)null
		});
		await Task.Run(old.Dispose);
		if (_started)
		{
			_ = StartTunnelsAndPingAsync();
		}
	}

	private void PostState(string phase, string? message = null)
	{
		_phase = phase;
		bool official = !_config.GameFilesFromRepository;
		_post(new
		{
			type = "state",
			phase = phase,
			installed = official ? _genesisInstalled : _installed?.Version,
			remote = official ? _genesisRemote : _channel?.Version,
			message = message
		});
	}

	private void Trace(string message)
	{
		try
		{
			File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "launcher.log"), $"[{DateTime.Now:HH:mm:ss}] {message}\n");
		}
		catch
		{
		}
	}

	private async Task CheckAsync()
	{
		bool official = !_config.GameFilesFromRepository;
		if (_config.RepositoryError is { } repositoryError)
		{
			Trace(repositoryError);
			// With the game from Genesis the repository only serves launcher updates: a bad one does not stop playing.
			if (!official)
			{
				PostState("error", repositoryError);
				return;
			}
		}

		bool hasExe = File.Exists(Path.Combine(_layout.GameDirectory, "ro3.exe"));
		// A folder the player chose stays chosen even when empty: that is where a fresh install goes.
		if (!hasExe && _settings.ClientDirectory == null)
		{
			string? detected = GameOrchestrator.DetectGameDirectory(Path.GetDirectoryName(_exePath));
			if (!string.IsNullOrEmpty(detected) && GameOrchestrator.IsValidGameDir(detected))
			{
				_layout = new InstallLayout(detected, (string?)null);
				_settings.ClientDirectory = detected;
				_settings.Save();
				PostSettings();
				hasExe = true;
			}
		}

		if (official)
		{
			await CheckOfficialAsync(hasExe);
			return;
		}

		_installed = new Updater(_layout, _repo).LoadState();
		if (hasExe || _installed != null)
		{
			PostState("ready", Strings.Get("state.ready"));
		}
		else
		{
			PostState("install", Strings.Get("state.noclient"));
			return;
		}

		try
		{
			using CancellationTokenSource timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2L));
			using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(_life.Token, timeoutCts.Token);
			Updater updater = new Updater(_layout, _repo);
			UpdateCheck check = await updater.CheckAsync(_config.Channel, linked.Token);
			_channel = check.Channel;
			_installed = check.Installed;
			if (await SelfUpdateAsync(linked.Token))
			{
				return;
			}
			if (check.Installed == null)
			{
				bool flag = File.Exists(Path.Combine(_layout.GameDirectory, "ro3.exe"));
				if (flag)
				{
					PostState("ready", Strings.Get("state.ready"));
				}
				else
				{
					PostState("install", Strings.Get("state.noclient"));
				}
				return;
			}
			if (check.UpdateRequired)
			{
				PostState("update", Strings.Get("state.update", _channel.Version));
				return;
			}
			List<string> list = updater.QuickVerify(check.Installed, Path.GetFileName(_exePath));
			if (list != null && list.Count > 0)
			{
				PostState("repair", Strings.Get("state.repair", list.Count));
			}
			else
			{
				PostState("ready");
			}
		}
		catch (Exception ex)
		{
			Trace("CheckAsync fallback: " + ex.Message);
			bool flag2 = (_installed != null) | hasExe;
			PostState(flag2 ? "ready" : "install", flag2 ? Strings.Get("state.ready") : Strings.Get("state.noclient"));
		}
	}

	// Applies a newer launcher from the operator channel; true when the launcher is restarting into it.
	private async Task<bool> SelfUpdateAsync(CancellationToken ct)
	{
		LauncherRelease? launcher = _channel?.Launcher;
		if (launcher == null || !SelfUpdate.IsNewer(launcher.Version, _version))
		{
			return false;
		}
		PostState("checking", Strings.Get("update.launcher", launcher.Version));
		if (!await SelfUpdate.ApplyAsync(_repo, launcher, _version, _exePath, ct))
		{
			return false;
		}
		App.RestartAfterUpdate(_exePath);
		return true;
	}

	// The game from Genesis: ready as soon as ro3.exe is there, then the operator channel (launcher self-update only) and
	// Genesis' current release, which turns the state into "update" when the folder's recorded release is older or unknown.
	private async Task CheckOfficialAsync(bool hasExe)
	{
		string folder = _layout.GameDirectory;
		_genesisInstalled = hasExe && GameInstaller.InstalledSequence(folder) is { } known ? known.ToString(CultureInfo.InvariantCulture) : null;
		PostState(hasExe ? "ready" : "install", Strings.Get(hasExe ? "state.ready" : "state.install"));

		if (_config.RepositoryError == null)
		{
			try
			{
				using CancellationTokenSource timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2L));
				using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(_life.Token, timeoutCts.Token);
				_channel = (await new Updater(_layout, _repo).CheckAsync(_config.Channel, linked.Token)).Channel;
				if (await SelfUpdateAsync(linked.Token))
				{
					return;
				}
			}
			catch (Exception ex)
			{
				Trace("Operator channel check skipped: " + ex.Message);
			}
		}
		if (!hasExe)
		{
			return;
		}

		_genesisCheck = CheckGenesisReleaseAsync(folder);
		await _genesisCheck;
	}

	// Genesis' current release against the folder's recorded one: "update" when older or unknown, else "ready".
	private async Task CheckGenesisReleaseAsync(string folder)
	{
		try
		{
			using CancellationTokenSource timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30L));
			using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(_life.Token, timeoutCts.Token);
			GameInstaller.Release release = await _genesis.CurrentReleaseAsync(linked.Token);
			_genesisRemote = release.Sequence.ToString(CultureInfo.InvariantCulture);
			// The player may have pressed Play or changed the folder meanwhile; then this result is stale.
			if (_phase is not ("ready" or "checking") || _work != null || folder != _layout.GameDirectory)
			{
				return;
			}
			long? installed = GameInstaller.InstalledSequence(folder);
			if (installed == null || installed < release.Sequence)
			{
				PostState("update", Strings.Get("state.update", _genesisRemote));
			}
			else
			{
				PostState("ready", Strings.Get("state.ready"));
			}
		}
		catch (Exception ex)
		{
			// Offline or blocked: the installed client still plays; the update check runs again next time.
			Trace("Genesis release check failed: " + ex.Message);
		}
	}

	// Installs or updates the game from Genesis. True when the folder is complete; false when it stopped (paused or failed).
	private async Task<bool> UpdateOfficialAsync()
	{
		string folder = _layout.GameDirectory;
		Directory.CreateDirectory(folder);
		_work = CancellationTokenSource.CreateLinkedTokenSource(_life.Token);
		PostState("updating");
		SpeedMeter meter = new SpeedMeter();
		Stopwatch clock = Stopwatch.StartNew();
		object gate = new object();
		string? shownStage = null;
		long shownAt = long.MinValue;
		void Report(string stage, long done, long total)
		{
			double rate = 0.0;
			double eta = 0.0;
			lock (gate)
			{
				long now = clock.ElapsedMilliseconds;
				if (stage == shownStage && now - shownAt < 150 && done < total)
				{
					return;
				}
				shownStage = stage;
				shownAt = now;
				if (stage == "downloading")
				{
					(rate, eta) = meter.Sample(done, total);
				}
			}
			_post(new
			{
				type = "progress",
				stage = stage,
				done = done,
				total = total,
				rate = rate,
				eta = eta
			});
		}
		try
		{
			CancellationToken token = _work.Token;
			await Task.Run(() => _genesis.InstallAsync(folder, Report, token));
			_work.Dispose();
			_work = null;
			await CheckAsync();
			return true;
		}
		catch (OperationCanceledException)
		{
			_work?.Dispose();
			_work = null;
			PostState(File.Exists(Path.Combine(folder, "ro3.exe")) ? "update" : "install", Strings.Get("state.paused"));
			return false;
		}
		catch (InstallException ex)
		{
			_work?.Dispose();
			_work = null;
			Trace("Game install failed: " + ex.Message + " " + ex.Fix);
			PostState("error", ex.Key != null ? Strings.Get(ex.Key, ex.Args) : Strings.Get("install.failed", ex.Message, ex.Fix));
			return false;
		}
		catch (Exception ex2)
		{
			_work?.Dispose();
			_work = null;
			Trace("Game install failed: " + ex2);
			PostState("error", Strings.Get("install.failed", ex2.Message, ""));
			return false;
		}
	}

	// True when the game files were verified and repaired; false when it stopped first (paused, failed or no channel).
	private async Task<bool> UpdateAsync(bool verifyAll)
	{
		if (!_config.GameFilesFromRepository)
		{
			return await UpdateOfficialAsync();
		}
		if (_channel == null)
		{
			await CheckAsync();
			if (_channel == null)
			{
				return false;
			}
		}
		Directory.CreateDirectory(_layout.GameDirectory);
		_work = CancellationTokenSource.CreateLinkedTokenSource(_life.Token);
		PostState("updating");
		SpeedMeter meter = new SpeedMeter();
		ThrottledProgress progress = new ThrottledProgress((UpdateProgress p) =>
		{
			//IL_0002: Unknown result type (might be due to invalid IL or missing references)
			//IL_0008: Invalid comparison between Unknown and I4
			//IL_005a: Unknown result type (might be due to invalid IL or missing references)
			//IL_005f: Unknown result type (might be due to invalid IL or missing references)
			double rate;
			double eta;
			if ((int)p.Stage == 1)
			{
				(rate, eta) = meter.Sample(p.BytesDone, p.BytesTotal);
			}
			else
			{
				rate = 0.0;
				eta = 0.0;
			}
			_post(new
			{
				type = "progress",
				stage = ((object)p.Stage/*cast due to constrained. prefix*/).ToString().ToLowerInvariant(),
				done = p.BytesDone,
				total = p.BytesTotal,
				rate = rate,
				eta = eta
			});
		});
		try
		{
			Updater updater = new Updater(_layout, _repo);
			ChannelInfo channel = _channel;
			CancellationToken token = _work.Token;
			await Task.Run(() => updater.UpdateAsync(channel, verifyAll, (IProgress<UpdateProgress>)progress, token));
			_work.Dispose();
			_work = null;
			await CheckAsync();
			return true;
		}
		catch (OperationCanceledException)
		{
			_work?.Dispose();
			_work = null;
			PostState((_installed == null) ? "install" : "update", Strings.Get("state.paused"));
			return false;
		}
		catch (Exception ex2)
		{
			_work?.Dispose();
			_work = null;
			PostState("error", ex2.Message);
			return false;
		}
	}

	private async Task PlayAsync()
	{
		if (_launching)
		{
			return;
		}
		_launching = true;
		try
		{
			await PlayCoreAsync();
		}
		finally
		{
			_launching = false;
		}
	}

	private async Task PlayCoreAsync()
	{
		// One realm for the whole launch, even if the player picks another one meanwhile.
		RealmSpec realm = _realm;
		TunnelManager tunnels = _tunnels;
		string gameDir = _layout.GameDirectory;
		Trace($"PlayAsync started with folder: '{gameDir}'");
		if (!GameOrchestrator.IsValidGameDir(gameDir))
		{
			string? detected = GameOrchestrator.DetectGameDirectory(Path.GetDirectoryName(_exePath));
			if (!string.IsNullOrEmpty(detected) && GameOrchestrator.IsValidGameDir(detected))
			{
				gameDir = detected;
				_layout = new InstallLayout(gameDir, (string?)null);
				_settings.ClientDirectory = gameDir;
				_settings.Save();
				PostSettings();
			}
			else
			{
				Trace("ro3.exe not found automatically, opening the folder picker...");
				await ChangeDirectoryAsync();
				gameDir = _layout.GameDirectory;
				if (!GameOrchestrator.IsValidGameDir(gameDir))
				{
					PostState("install", Strings.Get("state.pickdir"));
					return;
				}
			}
		}

		if (realm.ServerError is { } serverError)
		{
			Trace(serverError);
			PostState("error", serverError);
			return;
		}

		string baseDir = Path.GetDirectoryName(_exePath) ?? AppContext.BaseDirectory;

		if (tunnels.HasTunnels && !tunnels.IsConnected)
		{
			PostState("launching", Strings.Get("state.tunnels.check"));
			bool tunnelOk = await tunnels.StartTunnelsAsync(baseDir, _life.Token, msg => PostState("launching", msg));
			if (!tunnelOk)
			{
				Trace("The tunnels could not connect.");
				PostState("error", Strings.Get("state.tunnels.fail"));
				return;
			}
		}

		PostState("launching", Strings.Get("state.patching"));
		// No account is passed on: players sign in inside the game.
		Trace($"Calling JoinAndLaunchAsync with gameDir='{gameDir}', realm='{realm.Id}'...");
		Task<string?> Join() => GameOrchestrator.JoinAndLaunchAsync(gameDir, realm.ServerBaseUrl, baseDir, null, msg => PostState("launching", msg), _config.PurchaseUrl, Strings.Language);
		string? err = await Join();
		if (err != null && GameOrchestrator.LastJoinNeedsOfficialFiles)
		{
			// A DLL is already patched and there is no original to patch from: restore the official files, patch once more.
			Trace("RO3Emu found no original DLL to patch from; repairing the game files and patching again.");
			PostState("launching", Strings.Get("state.repair.retry"));
			if (!await UpdateAsync(true))
			{
				if (_phase == "ready")
				{
					PostState("error", err);
				}
				return;
			}
			PostState("launching", Strings.Get("state.patching"));
			err = await Join();
		}
		if (err != null)
		{
			Trace($"JoinAndLaunchAsync returned an error: {err}");
			PostState("error", err);
			return;
		}

		Trace("Game started.");
		PostState("running", Strings.Get("state.running"));
		_minimize();

		_ = Task.Run(async () =>
		{
			await GameOrchestrator.MonitorGameProcessAsync(() =>
			{
				Trace("ro3.exe process ended.");
				PostState("ready", Strings.Get("state.closed"));
			}, _life.Token);
		});
	}
	/// <summary>
	/// The start-up update from the signed manifest (launcher.json "manifestUrl"): launcher first (then restart), then
	/// the server files beside the launcher, then the manifest's configuration. Returns false when the launcher is
	/// restarting into a new version. Every failure is only logged: the player keeps the files and configuration they had.
	/// </summary>
	private async Task<bool> RunStartupUpdateAsync()
	{
		if (string.IsNullOrWhiteSpace(_config.ManifestUrl))
		{
			return true;
		}
		_autoUpdating = true;
		try
		{
			RemoteUpdater updater = new RemoteUpdater(_config, _exePath, _version, log: Trace);
			// Off the UI thread: hashing RO3Emu.exe and cloudflared.exe takes a moment. PostState reaches the UI through _post.
			RemoteUpdateOutcome outcome = await Task.Run(() => updater.RunAsync(message => PostState("checking", message), _life.Token));
			if (outcome.RestartRequired)
			{
				Trace("Update: restarting into the new launcher.");
				App.RestartAfterUpdate(_exePath);
				return false;
			}
			if (outcome.Files is { } files)
			{
				if (files.NotWritable)
				{
					_updateNotice = Strings.Get("update.readonly");
				}
				if (files.Updated.Count > 0 || files.Failed.Count > 0)
				{
					Trace($"Update: {files.Updated.Count} file(s) updated, {files.Failed.Count} failed, {files.UpToDate} up to date.");
				}
			}
			_appliedSequence = InstalledBuild.AppliedSequence(outcome, _appliedSequence);
			if (outcome.Manifest is { } manifest && manifest.Sequence != _config.ManifestSequence)
			{
				ApplyRemoteConfig(manifest);
			}
		}
		catch (OperationCanceledException) when (_life.IsCancellationRequested)
		{
			return false;
		}
		catch (Exception ex)
		{
			Trace("Update: failed: " + ex.Message);
		}
		finally
		{
			_autoUpdating = false;
		}
		return true;
	}

	/// <summary>Switches to the configuration of a newer manifest; called before any tunnel of this session started.</summary>
	private void ApplyRemoteConfig(RemoteManifest manifest)
	{
		string besideExe = Path.Combine(Path.GetDirectoryName(_exePath) ?? AppContext.BaseDirectory, "launcher.json");
		_config = LauncherConfig.Load(besideExe, manifest);
		RealmSpec realm = _config.SelectRealm(_realm.Id);
		TunnelManager old = _tunnels;
		_realm = realm;
		_tunnels = new TunnelManager(realm.Tunnels);
		old.Dispose();
		Trace($"Update: configuration of manifest sequence {manifest.Sequence} applied (realm '{realm.Id}').");
		PostSettings();
	}

	private async Task StartTunnelsAndPingAsync()
	{
		string baseDir = Path.GetDirectoryName(_exePath) ?? AppContext.BaseDirectory;
		TunnelManager tunnels = _tunnels;
		string? server = _realm.ServerBaseUrl;
		CancellationToken ct = _realmLife.Token;
		Trace($"Starting the Cloudflare tunnels of realm '{_realm.Id}'...");
		try
		{
			bool ok = await tunnels.StartTunnelsAsync(baseDir, ct, msg => Trace($"tunnel: {msg}"));
			if (ok) Trace("Tunnels active and connected.");
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested)
		{
			return;
		}
		catch (Exception ex)
		{
			Trace($"Could not start the tunnels: {ex.Message}");
		}
		_ = PingLoopAsync(server, ct);
	}

	private async Task RestoreOfficialAsync()
	{
		PostState("repair", Strings.Get("state.restoring"));
		string baseDir = Path.GetDirectoryName(_exePath) ?? AppContext.BaseDirectory;
		string? err = await GameOrchestrator.RestoreOfficialAsync(_layout.GameDirectory, baseDir, msg => PostState("repair", msg));
		if (err == null)
		{
			PostState("ready", Strings.Get("state.restored"));
		}
		else
		{
			PostState("error", Strings.Get("state.restore.error", err));
		}
	}

	private async Task ChangeDirectoryAsync()
	{
		if (_work != null)
		{
			return;
		}
		string text = _pickFolder("Game folder (an existing ROO_PC folder or an empty one)");
		if (text != null)
		{
			if (!File.Exists(Path.Combine(text, "ro3.exe")) && Directory.EnumerateFileSystemEntries(text).Any())
			{
				text = Path.Combine(text, "RO3Emu");
			}
			_settings.ClientDirectory = text;
			_settings.Save();
			_layout = new InstallLayout(text, (string)null);
			_installed = null;
			PostSettings();
			await CheckAsync();
		}
	}

	/// <summary>Reports the status of the selected realm's server until <paramref name="ct"/>, the realm's lifetime, ends.</summary>
	private async Task PingLoopAsync(string? server, CancellationToken ct)
	{
		if (server == null)
		{
			return;
		}
		while (!ct.IsCancellationRequested)
		{
			bool online = false;
			int? players = null;
			try
			{
				using HttpResponseMessage status = await _http.GetAsync(server + "/ro3emu/status", ct);
				if (status.IsSuccessStatusCode)
				{
					online = true;
					using JsonDocument jsonDocument = JsonDocument.Parse(await status.Content.ReadAsStringAsync(ct));
					if (jsonDocument.RootElement.TryGetProperty("players", out var value))
					{
						players = value.GetInt32();
					}
				}
				else
				{
					using HttpResponseMessage httpResponseMessage = await _http.GetAsync(server + "/ro3emu/gate-key.pem", ct);
					online = httpResponseMessage.IsSuccessStatusCode;
				}
			}
			catch (Exception)
			{
			}
			if (ct.IsCancellationRequested)
			{
				break;
			}
			_post(new
			{
				type = "server",
				online = online,
				players = players
			});
			try
			{
				await Task.Delay(TimeSpan.FromSeconds(30L), ct);
			}
			catch (TaskCanceledException)
			{
				break;
			}
		}
	}

	private async Task LoadNewsAsync()
	{
		List<string> candidateUrls = [];
		if (LauncherConfig.TryParseHttpUrl(_config.NewsUrl, out Uri? newsUrl))
		{
			candidateUrls.Add(newsUrl!.AbsoluteUri);
		}
		if (LauncherConfig.TryParseHttpUrl(_config.Repository, out Uri? repo))
		{
			candidateUrls.Add(new Uri(repo!, "news.json").AbsoluteUri);
		}

		foreach (string url in candidateUrls)
		{
			try
			{
				using HttpResponseMessage res = await _http.GetAsync(url, _life.Token);
				if (res.IsSuccessStatusCode)
				{
					string json = await res.Content.ReadAsStringAsync(_life.Token);
					using JsonDocument doc = JsonDocument.Parse(json);
					if (doc.RootElement.TryGetProperty("items", out var items))
					{
						_post(new
						{
							type = "news",
							items = items.Clone()
						});
						return;
					}
				}
			}
			catch { }
		}

		try
		{
			string localNews = Path.Combine(Path.GetDirectoryName(_exePath) ?? AppContext.BaseDirectory, "news.json");
			if (File.Exists(localNews))
			{
				using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(localNews));
				if (doc.RootElement.TryGetProperty("items", out var items))
				{
					_post(new
					{
						type = "news",
						items = items.Clone()
					});
					return;
				}
			}
		}
		catch { }

		try
		{
			using Stream? stream = typeof(LauncherController).Assembly.GetManifestResourceStream("news.json");
			if (stream != null)
			{
				using JsonDocument doc = JsonDocument.Parse(stream);
				if (doc.RootElement.TryGetProperty("items", out var items))
				{
					_post(new
					{
						type = "news",
						items = items.Clone()
					});
					return;
				}
			}
		}
		catch { }

		try
		{
			byte[]? array = await _repo.TryGetBytesAsync("news.json", _life.Token);
			if (array != null)
			{
				using JsonDocument jsonDocument = JsonDocument.Parse((ReadOnlyMemory<byte>)array, default(JsonDocumentOptions));
				if (jsonDocument.RootElement.TryGetProperty("items", out var value))
				{
					_post(new
					{
						type = "news",
						items = value.Clone()
					});
				}
			}
		}
		catch { }
	}

	public void Dispose()
	{
		_life.Cancel();
		_work?.Cancel();
		_realmLife.Dispose();
		_tunnels.Dispose();
		_genesis.Dispose();
	}
}
