using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using Ro3Emu.Patcher;

namespace Ro3Emu.Launcher;

public class App : Application
{
	/// <summary>Argument of the launcher started by <see cref="RestartAfterUpdate"/>: wait for the old instance to exit.</summary>
	private const string UpdatedArgument = "--updated";

	private Mutex? _singleInstance;

	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);
		_singleInstance = new Mutex(initiallyOwned: true, "Local\\Ro3Emu.Launcher", out var createdNew);
		if (!createdNew && e.Args.Contains(UpdatedArgument))
		{
			createdNew = WaitForPreviousInstance(_singleInstance);
		}
		if (!createdNew)
		{
			Shutdown();
			return;
		}
		string text = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Ro3Launcher.exe");
		SelfUpdate.DeleteLeftovers(text);
		LauncherConfig config = LoadConfig(text);
		UserSettings userSettings = UserSettings.Load();
		string initialLang = !string.IsNullOrWhiteSpace(userSettings.Language) ? userSettings.Language : config.Language;
		Strings.Configure(initialLang, config.ServerName);
		MainWindow = new MainWindow(config, text);
		MainWindow.Show();
	}

	/// <summary>
	/// launcher.json (embedded + beside the exe) with the configuration of the last verified manifest laid over it, so
	/// the realms and tunnels of the latest release apply from the first second, even offline. The fresh manifest is
	/// fetched later by <see cref="LauncherController"/>, before the tunnels start.
	/// </summary>
	private static LauncherConfig LoadConfig(string exePath)
	{
		string launcherDir = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;
		string besideExe = Path.Combine(launcherDir, "launcher.json");
		LauncherConfig config = LauncherConfig.Load(besideExe);
		if (config.ManifestUri is not { } manifestUrl || !ManifestSignature.IsKeyConfigured(config.ManifestPublicKey))
		{
			return config;
		}
		ManagedFiles.CleanLeftovers(launcherDir);
		RemoteManifest? cached = new ManifestCache(ManifestCache.DefaultDirectory(manifestUrl)).TryLoad(config.ManifestPublicKey);
		return cached != null ? LauncherConfig.Load(besideExe, cached) : config;
	}

	private static bool WaitForPreviousInstance(Mutex mutex)
	{
		try
		{
			return mutex.WaitOne(TimeSpan.FromSeconds(15));
		}
		catch (AbandonedMutexException)
		{
			return true; // the old instance exited without releasing it: the mutex is ours now
		}
	}

	/// <summary>
	/// Starts the launcher exe that just replaced this one and exits. The single-instance mutex is released first and the
	/// new process (started with <see cref="UpdatedArgument"/>) waits for it, so it does not quit on seeing this instance.
	/// </summary>
	public static void RestartAfterUpdate(string exePath)
	{
		Current.Dispatcher.Invoke(() =>
		{
			if (Current is App app && app._singleInstance != null)
			{
				try
				{
					app._singleInstance.ReleaseMutex();
				}
				catch (ApplicationException)
				{
				}
				app._singleInstance.Dispose();
				app._singleInstance = null;
			}
			SelfUpdate.Restart(exePath, UpdatedArgument);
			Current.Shutdown();
		});
	}

	protected override void OnExit(ExitEventArgs e)
	{
		_singleInstance?.Dispose();
		base.OnExit(e);
	}

	[STAThread]
	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.13.0")]
	public static void Main()
	{
		new App().Run();
	}
}
