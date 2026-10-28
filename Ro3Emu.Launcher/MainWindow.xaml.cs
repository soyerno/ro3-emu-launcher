using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;

namespace Ro3Emu.Launcher;

public partial class MainWindow : Window
{
	private const string Origin = "https://launcher.ro3emu/";

	private readonly LauncherConfig _config;

	private readonly LauncherController _controller;

	private readonly string? _uiOverride = Environment.GetEnvironmentVariable("RO3EMU_LAUNCHER_UI");

	
	public MainWindow(LauncherConfig config, string exePath)
	{
		InitializeComponent();
		_config = config;
		Title = config.ServerName;
		try
		{
			Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/app.ico", UriKind.Absolute));
		}
		catch { }
		_controller = new LauncherController(config, exePath, Post, PickFolder, () =>
		{
			WindowState = WindowState.Minimized;
		});
		SourceInitialized += (object? _, EventArgs _) =>
		{
			RoundCorners();
		};
		Loaded += async (object _, RoutedEventArgs _) =>
		{
			await InitializeWebAsync();
		};
		Closing += (object? _, CancelEventArgs _) =>
		{
			_controller.Dispose();
		};
	}

	private async Task InitializeWebAsync()
	{
		_ = 1;
		try
		{
			string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RO3Emu", "LauncherWebView");
			CoreWebView2Environment val = await CoreWebView2Environment.CreateAsync((string)null, text, (CoreWebView2EnvironmentOptions)null);
			await Web.EnsureCoreWebView2Async(val);
		}
		catch (WebView2RuntimeNotFoundException)
		{
			if (MessageBox.Show(Strings.Get("webview2.missing"), _config.ServerName, MessageBoxButton.YesNo, MessageBoxImage.Exclamation) == MessageBoxResult.Yes)
			{
				OpenExternal("https://go.microsoft.com/fwlink/p/?LinkId=2124703");
			}
			Application.Current.Shutdown();
			return;
		}
		catch (Exception ex2)
		{
			try
			{
				File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "launcher.log"), $"[WebView2 Init] {ex2}\n");
			}
			catch
			{
			}
			MessageBox.Show(Strings.Get("webview2.error", ex2.Message), _config.ServerName, MessageBoxButton.OK, MessageBoxImage.Hand);
			Application.Current.Shutdown();
			return;
		}
		CoreWebView2 coreWebView = Web.CoreWebView2;
		CoreWebView2Settings settings = coreWebView.Settings;
		settings.AreDevToolsEnabled = _uiOverride != null;
		settings.AreDefaultContextMenusEnabled = _uiOverride != null;
		settings.AreBrowserAcceleratorKeysEnabled = _uiOverride != null;
		settings.IsStatusBarEnabled = false;
		settings.IsZoomControlEnabled = false;
		settings.IsPasswordAutosaveEnabled = false;
		settings.IsGeneralAutofillEnabled = false;
		settings.IsNonClientRegionSupportEnabled = true;
		await coreWebView.AddScriptToExecuteOnDocumentCreatedAsync("window.LAUNCHER_CONFIG = " + JsonSerializer.Serialize(new
		{
			serverName = _config.ServerName,
			language = Strings.Language,
			website = LauncherConfig.TryParseHttpUrl(_config.Website, out Uri? website) ? website!.AbsoluteUri : null,
			discord = LauncherConfig.TryParseHttpUrl(_config.Discord, out Uri? discord) ? discord!.AbsoluteUri : null
		}) + ";");
		coreWebView.AddWebResourceRequestedFilter("https://launcher.ro3emu/*", (CoreWebView2WebResourceContext)0);
		coreWebView.WebResourceRequested += OnWebResourceRequested;
		coreWebView.WebMessageReceived += async (object? _, CoreWebView2WebMessageReceivedEventArgs e) =>
		{
			try
			{
				await OnMessageAsync(e.WebMessageAsJson);
			}
			catch
			{
			}
		};
		coreWebView.NavigationStarting += (object? _, CoreWebView2NavigationStartingEventArgs e) =>
		{
			if (!e.Uri.StartsWith("https://launcher.ro3emu/", StringComparison.OrdinalIgnoreCase))
			{
				e.Cancel = true;
				OpenExternal(e.Uri);
			}
		};
		coreWebView.NewWindowRequested += (object? _, CoreWebView2NewWindowRequestedEventArgs e) =>
		{
			e.Handled = true;
			OpenExternal(e.Uri);
		};
		coreWebView.Navigate("https://launcher.ro3emu/index.html");
	}

	private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
	{
		string text = Uri.UnescapeDataString(new Uri(e.Request.Uri).AbsolutePath.TrimStart('/'));
		if (text.Length == 0)
		{
			text = "index.html";
		}
		Stream stream = OpenUiFile(text);
		CoreWebView2Environment environment = Web.CoreWebView2.Environment;
		e.Response = ((stream == null) ? environment.CreateWebResourceResponse((Stream)null, 404, "Not Found", "") : environment.CreateWebResourceResponse(stream, 200, "OK", "Content-Type: " + ContentType(text) + "\r\nCache-Control: no-store"));
	}

	private Stream? OpenUiFile(string path)
	{
		if (path.Contains(".."))
		{
			return null;
		}
		if (_uiOverride != null)
		{
			string path2 = Path.Combine(_uiOverride, path.Replace('/', Path.DirectorySeparatorChar));
			if (File.Exists(path2))
			{
				return new MemoryStream(File.ReadAllBytes(path2));
			}
		}

		Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ui/" + path);
		if (stream != null)
		{
			return stream;
		}

		string[] diskCandidates =
		{
			Path.Combine(AppContext.BaseDirectory, path.Replace('/', Path.DirectorySeparatorChar)),
			Path.Combine(AppContext.BaseDirectory, "ui." + path.Replace('/', '.')),
			Path.Combine(AppContext.BaseDirectory, Path.GetFileName(path))
		};

		foreach (string candidate in diskCandidates)
		{
			if (File.Exists(candidate))
			{
				try
				{
					return new MemoryStream(File.ReadAllBytes(candidate));
				}
				catch
				{
				}
			}
		}

		return null;
	}

	private static string ContentType(string path)
	{
		switch (Path.GetExtension(path).ToLowerInvariant())
		{
		case ".html":
			return "text/html; charset=utf-8";
		case ".css":
			return "text/css; charset=utf-8";
		case ".js":
			return "text/javascript; charset=utf-8";
		case ".png":
			return "image/png";
		case ".jpeg":
		case ".jpg":
			return "image/jpeg";
		case ".webp":
			return "image/webp";
		case ".woff2":
			return "font/woff2";
		default:
			return "application/octet-stream";
		}
	}

	private async Task OnMessageAsync(string json)
	{
		try
		{
			JsonElement rootElement;
			try
			{
				rootElement = JsonDocument.Parse(json).RootElement;
			}
			catch (JsonException)
			{
				return;
			}
			string text = (rootElement.TryGetProperty("cmd", out var value) ? value.GetString() : null);
			if (text == "window")
			{
				JsonElement value2;
				switch (rootElement.TryGetProperty("action", out value2) ? value2.GetString() : null)
				{
				case "minimize":
					WindowState = WindowState.Minimized;
					break;
				case "close":
					Close();
					break;
				case "drag":
					BeginDrag();
					break;
				case "openUrl":
					if (rootElement.TryGetProperty("url", out var urlProp) && urlProp.GetString() is string targetUrl)
					{
						OpenExternal(targetUrl);
					}
					break;
				}
			}
			else if (text != null)
			{
				await _controller.HandleMessageAsync(rootElement);
			}
		}
		catch
		{
		}
	}

	private void Post(object message)
	{
		if (!((DispatcherObject)this).Dispatcher.CheckAccess())
		{
			((DispatcherObject)this).Dispatcher.InvokeAsync((Action)(() =>
			{
				Post(message);
			}));
			return;
		}
		CoreWebView2 coreWebView = Web.CoreWebView2;
		if (coreWebView != null)
		{
			coreWebView.PostWebMessageAsJson(JsonSerializer.Serialize(message));
		}
	}

	private string? PickFolder(string title)
	{
		string environmentVariable = Environment.GetEnvironmentVariable("RO3EMU_LAUNCHER_PICK");
		if (environmentVariable != null && environmentVariable.Length > 0)
		{
			return environmentVariable;
		}
		OpenFolderDialog openFolderDialog = new OpenFolderDialog
		{
			Title = title,
			Multiselect = false
		};
		if (openFolderDialog.ShowDialog(this) != true)
		{
			return null;
		}
		return openFolderDialog.FolderName;
	}

	private static void OpenExternal(string url)
	{
		bool flag = Uri.TryCreate(url, UriKind.Absolute, out Uri result);
		if (flag)
		{
			string scheme = result.Scheme;
			bool flag2 = ((scheme == "http" || scheme == "https") ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			try
			{
				Process.Start(new ProcessStartInfo(result.AbsoluteUri)
				{
					UseShellExecute = true
				});
			}
			catch
			{
			}
		}
	}

	[DllImport("user32.dll")]
	private static extern bool ReleaseCapture();

	[DllImport("user32.dll")]
	private static extern nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

	private void BeginDrag()
	{
		ReleaseCapture();
		SendMessage(new WindowInteropHelper(this).Handle, 161, 2, IntPtr.Zero);
	}

	private void RoundCorners()
	{
		int value = 2;
		DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 33, ref value, 4);
	}
}
