using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Ro3Emu.Launcher.Auth;
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

	private readonly LauncherConfig _config;

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

	private readonly string _version = typeof(LauncherController).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

	private readonly CancellationTokenSource _life = new CancellationTokenSource();

	private InstallLayout _layout;

	private ChannelInfo? _channel;

	private LocalState? _installed;

	private string _phase = "checking";

	private CancellationTokenSource? _work;

	private bool _started;

	private string? _sdkToken;

	private string? _sdkAccount;
	private bool _registrationEnabled = true;
	private readonly TunnelManager _tunnels;

	// Game files from Genesis' update servers (unless launcher.json "gameFiles" is "repository").
	private readonly GameInstaller _genesis;

	private string? _genesisInstalled;

	private string? _genesisRemote;

	public LauncherController(LauncherConfig config, string exePath, Action<object> post, Func<string, string?> pickFolder, Action minimize)
	{
		//IL_00a6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b0: Expected Obj, but got Unknown
		//IL_00c7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d1: Expected Obj, but got Unknown
		_config = config;
		_tunnels = new TunnelManager(config.Tunnels);
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
		case "auth_register_local":
		{
			string text9 = (message.TryGetProperty("username", out var value11) ? value11.GetString() : null);
			string text10 = (message.TryGetProperty("password", out var value12) ? value12.GetString() : null);
			string email = (message.TryGetProperty("email", out var value13) ? value13.GetString() : null);
			string confirm = (message.TryGetProperty("confirmPassword", out var value14) ? value14.GetString() : null);
			AuthResult authResult7 = await RegisterAsync(text9 ?? "", text10 ?? "", email, confirm, ReadRemember(message));
			_post(new
			{
				type = "auth_result",
				success = authResult7.Success,
				message = authResult7.Message,
				username = authResult7.Username
			});
			break;
		}
		case "auth_login_local":
		{
			string text5 = (message.TryGetProperty("username", out var value7) ? value7.GetString() : null);
			string text6 = (message.TryGetProperty("password", out var value8) ? value8.GetString() : null);
			AuthResult authResult5 = await LoginAsync(text5 ?? "", text6 ?? "", ReadRemember(message));
			_post(new
			{
				type = "auth_result",
				success = authResult5.Success,
				message = authResult5.Message,
				username = authResult5.Username
			});
			break;
		}
		case "auth_verify_email":
		{
			string text7 = (message.TryGetProperty("username", out var value9) ? value9.GetString() : null);
			string text8 = (message.TryGetProperty("code", out var value10) ? value10.GetString() : null);
			AuthResult authResult6 = await VerifyEmailAsync(text7 ?? "", text8 ?? "");
			_post(new
			{
				type = "auth_result",
				success = authResult6.Success,
				message = authResult6.Message,
				username = authResult6.Username
			});
			break;
		}
		case "auth_request_reset":
		{
			AuthResult authResult4 = await AuthDb.RequestPasswordResetAsync((message.TryGetProperty("email", out var value6) ? value6.GetString() : null) ?? "");
			_post(new
			{
				type = "auth_result",
				success = authResult4.Success,
				message = authResult4.Message,
				username = authResult4.Username
			});
			break;
		}
		case "auth_reset_password":
		{
			object obj = (message.TryGetProperty("username", out var value2) ? value2.GetString() : null);
			string text2 = (message.TryGetProperty("code", out var value3) ? value3.GetString() : null);
			string text3 = (message.TryGetProperty("password", out var value4) ? value4.GetString() : null);
			string text4 = (message.TryGetProperty("confirmPassword", out var value5) ? value5.GetString() : null);
			if (obj == null)
			{
				obj = "";
			}
			AuthResult authResult3 = await AuthDb.ResetPasswordAsync((string)obj, text2 ?? "", text3 ?? "", text4 ?? "");
			_post(new
			{
				type = "auth_result",
				success = authResult3.Success,
				message = authResult3.Message,
				username = authResult3.Username
			});
			break;
		}
		case "auth_google":
		{
			_post(new
			{
				type = "auth_status",
				status = "waiting",
				message = "Abriendo Google en tu navegador..."
			});
			AuthResult authResult2 = await OAuthHelper.StartGoogleAuthAsync(_life.Token);
			_post(new
			{
				type = "auth_result",
				success = authResult2.Success,
				message = authResult2.Message,
				username = authResult2.Username,
				provider = "google"
			});
			break;
		}
		case "auth_microsoft":
		{
			_post(new
			{
				type = "auth_status",
				status = "waiting",
				message = "Abriendo Microsoft en tu navegador..."
			});
			AuthResult authResult = await OAuthHelper.StartMicrosoftAuthAsync(_life.Token);
			_post(new
			{
				type = "auth_result",
				success = authResult.Success,
				message = authResult.Message,
				username = authResult.Username,
				provider = "microsoft"
			});
			break;
		}
		case "auth_switch_account":
		{
			_sdkToken = null;
			_sdkAccount = null;
			SessionStore.Clear();
			_post(new
			{
				type = "auth_current",
				username = (string?)null
			});
			break;
		}
		case "auth_get_registration_status":
			await CheckRegistrationStatusAsync();
			break;
		case "language":
		{
			if (message.TryGetProperty("lang", out var langProp))
			{
				Strings.SetLanguage(langProp.GetString());
			}
			break;
		}
		case "auth_get_last":
			await PostInitialAuthAsync();
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
			if (!_started)
			{
				_started = true;
				_ = StartTunnelsAndPingAsync();
				_ = LoadNewsAsync();
				_ = InitializeSessionAsync();
				_ = CheckRegistrationStatusAsync();
			}
			await CheckAsync();
			break;
		case "primary":
		{
			if (_work != null)
			{
				break;
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
	}

	private async Task InitializeSessionAsync()
	{
		try
		{
			await AuthDb.InitializeAsync().ConfigureAwait(false);
			// No auto-login: only the remembered account name is pre-filled; the player types the password.
			PostCurrentAccount();
		}
		catch (Exception ex)
		{
			Trace("InitializeSessionAsync error: " + ex.Message);
		}
	}

	private Task PostInitialAuthAsync()
	{
		PostCurrentAccount();
		return Task.CompletedTask;
	}

	/// <summary>
	/// Tells the UI who is signed in this session; otherwise sends the remembered account (if any)
	/// so the login form can pre-fill it, without treating it as signed in.
	/// </summary>
	private void PostCurrentAccount()
	{
		if (!string.IsNullOrEmpty(_sdkAccount))
		{
			_post(new
			{
				type = "auth_current",
				username = _sdkAccount,
				loggedIn = true,
				remembered = (string?)null
			});
			return;
		}
		_post(new
		{
			type = "auth_current",
			username = (string?)null,
			loggedIn = false,
			remembered = SessionStore.Load()
		});
	}

	private static bool ReadRemember(JsonElement message)
	{
		return message.TryGetProperty("remember", out var value) && value.ValueKind == JsonValueKind.True;
	}

	private static void RememberAccount(string account, bool remember)
	{
		if (remember)
		{
			SessionStore.Save(account);
		}
		else
		{
			SessionStore.Clear();
		}
	}

	private async Task<AuthResult> LoginAsync(string identity, string password, bool remember)
	{
		if (string.IsNullOrWhiteSpace(identity) || string.IsNullOrWhiteSpace(password))
		{
			return new AuthResult(Success: false, Strings.Get("login.missing"), identity);
		}

		identity = identity.Trim();

		string? server = _config.ServerBaseUrl;
		if (server == null)
		{
			return new AuthResult(Success: false, _config.ServerError!, identity);
		}

		foreach (string ep in new[] { server + "/sdk/login" })
		{
			try
			{
				using FormUrlEncodedContent form = new FormUrlEncodedContent(new[]
				{
					new KeyValuePair<string, string>("identity", identity),
					new KeyValuePair<string, string>("credential", password)
				});

				using HttpResponseMessage response = await _http.PostAsync(ep, form, _life.Token);
				if (response.IsSuccessStatusCode)
				{
					string body = await response.Content.ReadAsStringAsync(_life.Token);
					using JsonDocument doc = JsonDocument.Parse(body);
					int code = doc.RootElement.TryGetProperty("code", out var codeVal) ? codeVal.GetInt32() : -1;

					if (code == 0)
					{
						string? token = null;
						if (doc.RootElement.TryGetProperty("data", out var dataVal) &&
							dataVal.TryGetProperty("token", out var tokVal))
						{
							token = tokVal.GetString();
						}

						_sdkToken = token;
						_sdkAccount = identity;
						RememberAccount(identity, remember);
						_ = AuthDb.RecordLoginAsync(identity);
						Trace($"Sign-in succeeded for {identity} via {ep}");
						return new AuthResult(Success: true, Strings.Get("login.welcome", identity), identity);
					}
					else if (code == 401)
					{
						return new AuthResult(Success: false, Strings.Get("login.invalid"), identity);
					}
					else if (code == 403)
					{
						return new AuthResult(Success: false, Strings.Get("login.forbidden"), identity);
					}
				}
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Trace($"Sign-in failed at {ep}: {ex.Message}");
			}
		}

		// Fallback offline a AuthDb local
		try
		{
			AuthResult local = await AuthDb.VerifyLocalAsync(identity, password);
			if (local.Success && local.Username != null)
			{
				_sdkAccount = local.Username;
				RememberAccount(local.Username, remember);
				Trace($"Offline local sign-in for {local.Username}");
				return new AuthResult(Success: true, Strings.Get("login.offline", local.Username), local.Username);
			}
		}
		catch { }

		return new AuthResult(Success: false, Strings.Get("login.unreachable"), identity);
	}

	private async Task<AuthResult> RegisterAsync(string username, string password, string? email, string? confirm, bool remember)
	{
		if (!_registrationEnabled)
		{
			return new AuthResult(Success: false, Strings.Get("reg.disabled"), username);
		}

		username = (username ?? "").Trim();
		password = password ?? "";

		if (string.IsNullOrWhiteSpace(username))
		{
			return new AuthResult(Success: false, Strings.Get("reg.user.missing"), username);
		}
		if (username.Length < 3 || username.Length > 20)
		{
			return new AuthResult(Success: false, Strings.Get("reg.user.length"), username);
		}
		if (password.Length < 6)
		{
			return new AuthResult(Success: false, Strings.Get("reg.pass.length"), username);
		}
		if (password != confirm)
		{
			return new AuthResult(Success: false, Strings.Get("reg.pass.mismatch"), username);
		}

		string? server = _config.ServerBaseUrl;
		if (server == null)
		{
			return new AuthResult(Success: false, _config.ServerError!, username);
		}

		foreach (string ep in new[] { server + "/sdk/login" })
		{
			try
			{
				var pairs = new List<KeyValuePair<string, string>>
				{
					new KeyValuePair<string, string>("identity", username),
					new KeyValuePair<string, string>("credential", password)
				};
				if (!string.IsNullOrEmpty(email))
				{
					pairs.Add(new KeyValuePair<string, string>("email", email));
				}

				using FormUrlEncodedContent form = new FormUrlEncodedContent(pairs);
				using HttpResponseMessage response = await _http.PostAsync(ep, form, _life.Token);
				if (response.IsSuccessStatusCode)
				{
					string body = await response.Content.ReadAsStringAsync(_life.Token);
					using JsonDocument doc = JsonDocument.Parse(body);
					int code = doc.RootElement.TryGetProperty("code", out var codeVal) ? codeVal.GetInt32() : -1;

					if (code == 0)
					{
						string? token = null;
						if (doc.RootElement.TryGetProperty("data", out var dataVal) &&
							dataVal.TryGetProperty("token", out var tokVal))
						{
							token = tokVal.GetString();
						}

						_sdkToken = token;
						_sdkAccount = username;
						RememberAccount(username, remember);
						_ = AuthDb.RecordLoginAsync(username);
						try
						{
							_ = AuthDb.RegisterLocalAsync(username, password, email, confirm);
						}
						catch { }

						Trace($"Registration succeeded for {username} via {ep}");
						return new AuthResult(Success: true, Strings.Get("reg.ok", username), username);
					}
					else if (code == 409)
					{
						return new AuthResult(Success: false, Strings.Get("reg.conflict", username), username);
					}
					else if (code == 401)
					{
						return new AuthResult(Success: false, Strings.Get("reg.exists", username), username);
					}
					else if (code == 403)
					{
						return new AuthResult(Success: false, Strings.Get("reg.disabled.short"), username);
					}
				}
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Trace($"Registration failed at {ep}: {ex.Message}");
			}
		}

		return new AuthResult(Success: false, Strings.Get("reg.unreachable"), username);
	}

	private async Task<AuthResult> VerifyEmailAsync(string username, string code)
	{
		string? text = _config.ServerBaseUrl;
		if (!string.IsNullOrEmpty(text))
		{
			try
			{
				using StringContent payload = new StringContent(JsonSerializer.Serialize(new { username, code }), Encoding.UTF8, MediaTypeHeaderValue.Parse("application/json"));
				using HttpResponseMessage response = await _http.PostAsync(text + "/api/auth/verify", payload, _life.Token);
				using JsonDocument jsonDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync(_life.Token));
				int num = (jsonDocument.RootElement.TryGetProperty("code", out var value) ? value.GetInt32() : (-1));
				string message = (jsonDocument.RootElement.TryGetProperty("message", out var value2) ? (value2.GetString() ?? "") : "");
				string username2 = (jsonDocument.RootElement.TryGetProperty("username", out var value3) ? (value3.GetString() ?? username) : username);
				return new AuthResult(num == 0, message, username2);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
			}
		}
		return await AuthDb.VerifyEmailAsync(username, code);
	}

	private async Task FetchSdkTokenAsync(string username, string password)
	{
		_ = 1;
		try
		{
			string? text = _config.ServerBaseUrl;
			if (string.IsNullOrEmpty(text))
			{
				return;
			}
			using MultipartFormDataContent form = new MultipartFormDataContent
			{
				{
					new StringContent(username),
					"identity"
				},
				{
					new StringContent(password),
					"credential"
				}
			};
			using HttpResponseMessage response = await _http.PostAsync(text + "/sdk/login", form, _life.Token);
			if (!response.IsSuccessStatusCode)
			{
				return;
			}
			using JsonDocument jsonDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync(_life.Token));
			if (jsonDocument.RootElement.TryGetProperty("code", out var value) && value.GetInt32() == 0 && jsonDocument.RootElement.TryGetProperty("data", out var value2) && value2.TryGetProperty("token", out var value3))
			{
				_sdkToken = value3.GetString();
				_sdkAccount = username;
			}
		}
		catch (Exception)
		{
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
		if (!hasExe)
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
		PostState("checking", Strings.Get("state.selfupdate", launcher.Version));
		if (!await SelfUpdate.ApplyAsync(_repo, launcher, _version, _exePath, ct))
		{
			return false;
		}
		SelfUpdate.Restart(_exePath, "");
		((DispatcherObject)Application.Current).Dispatcher.Invoke((Action)(() =>
		{
			Application.Current.Shutdown();
		}));
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

		if (_config.ServerError is { } serverError)
		{
			Trace(serverError);
			PostState("error", serverError);
			return;
		}

		string baseDir = Path.GetDirectoryName(_exePath) ?? AppContext.BaseDirectory;

		if (_tunnels.HasTunnels && !_tunnels.IsConnected)
		{
			PostState("launching", Strings.Get("state.tunnels.check"));
			bool tunnelOk = await _tunnels.StartTunnelsAsync(baseDir, _life.Token, msg => PostState("launching", msg));
			if (!tunnelOk)
			{
				Trace("The tunnels could not connect.");
				PostState("error", Strings.Get("state.tunnels.fail"));
				return;
			}
		}

		PostState("launching", Strings.Get("state.patching"));
		// Only an account signed in during this session is passed on; a merely remembered name is not a session.
		string? accountToUse = _sdkAccount;
		Trace($"Calling JoinAndLaunchAsync with gameDir='{gameDir}', account='{accountToUse}'...");
		Task<string?> Join() => GameOrchestrator.JoinAndLaunchAsync(gameDir, _config.ServerBaseUrl, baseDir, accountToUse, msg => PostState("launching", msg), _config.PurchaseUrl);
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
	private async Task StartTunnelsAndPingAsync()
	{
		string baseDir = Path.GetDirectoryName(_exePath) ?? AppContext.BaseDirectory;
		Trace("Starting the Cloudflare tunnels...");
		try
		{
			bool ok = await _tunnels.StartTunnelsAsync(baseDir, _life.Token, msg => Trace($"tunnel: {msg}"));
			if (ok) Trace("Tunnels active and connected.");
		}
		catch (Exception ex)
		{
			Trace($"Could not start the tunnels: {ex.Message}");
		}
		_ = PingLoopAsync();
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


	private async Task<string?> FetchLaunchCodeAsync(string? token)
	{
		if (string.IsNullOrEmpty(token))
		{
			return null;
		}
		string? server = _config.ServerBaseUrl;
		if (string.IsNullOrEmpty(server))
		{
			return null;
		}
		try
		{
			using StringContent payload = new StringContent(JsonSerializer.Serialize(new { token }), Encoding.UTF8, MediaTypeHeaderValue.Parse("application/json"));
			using HttpResponseMessage response = await _http.PostAsync(server + "/api/auth/launchcode", payload, _life.Token);
			Trace($"launchcode: {(int)response.StatusCode} from {server}");
			if (!response.IsSuccessStatusCode)
			{
				return null;
			}
			using JsonDocument jsonDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync(_life.Token));
			if (jsonDocument.RootElement.TryGetProperty("code", out var value) && value.GetInt32() == 0 && jsonDocument.RootElement.TryGetProperty("data", out var value2) && value2.TryGetProperty("launch_code", out var value3))
			{
				return value3.GetString();
			}
			Trace("launchcode: body code != 0");
		}
		catch (Exception ex)
		{
			Trace("launchcode failed: " + ex.GetType().Name + ": " + ex.Message);
		}
		return null;
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

	private async Task CheckRegistrationStatusAsync()
	{
		if (!LauncherConfig.TryParseHttpUrl(_config.StatusUrl, out Uri? statusUri))
		{
			PostRegistrationStatus();
			return;
		}

		foreach (string ep in new[] { statusUri!.AbsoluteUri })
		{
			try
			{
				using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, ep);
				req.Headers.Add("User-Agent", "Ro3Launcher");
				using HttpResponseMessage resp = await _http.SendAsync(req, _life.Token);
				if (resp.IsSuccessStatusCode)
				{
					string json = await resp.Content.ReadAsStringAsync(_life.Token);
					using JsonDocument doc = JsonDocument.Parse(json);
					bool enabled = true;
					if (doc.RootElement.TryGetProperty("enabled", out var prop))
					{
						enabled = prop.GetBoolean();
					}
					else if (doc.RootElement.TryGetProperty("registration_enabled", out var prop2))
					{
						enabled = prop2.GetBoolean();
					}

					if (_registrationEnabled != enabled)
					{
						_registrationEnabled = enabled;
						Trace($"Registration status updated: enabled={_registrationEnabled}");
					}

					PostRegistrationStatus();
					return;
				}
			}
			catch { }
		}

		PostRegistrationStatus();
	}

	private void PostRegistrationStatus()
	{
		_post(new
		{
			type = "registration_status",
			enabled = _registrationEnabled,
			message = _registrationEnabled
				? Strings.Get("reg.available")
				: Strings.Get("reg.disabled")
		});
	}

	private async Task PingLoopAsync()
	{
		string? server = _config.ServerBaseUrl;
		if (server == null)
		{
			return;
		}
		while (!_life.IsCancellationRequested)
		{
			bool online = false;
			int? players = null;
			try
			{
				using HttpResponseMessage status = await _http.GetAsync(server + "/ro3emu/status", _life.Token);
				if (status.IsSuccessStatusCode)
				{
					online = true;
					using JsonDocument jsonDocument = JsonDocument.Parse(await status.Content.ReadAsStringAsync(_life.Token));
					if (jsonDocument.RootElement.TryGetProperty("players", out var value))
					{
						players = value.GetInt32();
					}
				}
				else
				{
					using HttpResponseMessage httpResponseMessage = await _http.GetAsync(server + "/ro3emu/gate-key.pem", _life.Token);
					online = httpResponseMessage.IsSuccessStatusCode;
				}
			}
			catch (Exception)
			{
			}
			if (_life.IsCancellationRequested)
			{
				break;
			}
			_post(new
			{
				type = "server",
				online = online,
				players = players
			});
			_ = CheckRegistrationStatusAsync();
			try
			{
				await Task.Delay(TimeSpan.FromSeconds(30L), _life.Token);
			}
			catch (TaskCanceledException)
			{
				break;
			}
		}
	}

	private async Task LoadNewsAsync()
	{
		string[] candidateUrls = LauncherConfig.TryParseHttpUrl(_config.Repository, out Uri? repo)
			? [new Uri(repo!, "news.json").AbsoluteUri]
			: [];

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
		_tunnels.Dispose();
		_genesis.Dispose();
	}
}
