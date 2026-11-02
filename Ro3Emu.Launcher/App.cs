using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Ro3Emu.Patcher;

namespace Ro3Emu.Launcher;

public class App : Application
{
	/// <summary>Argument of the launcher started by <see cref="RestartAfterUpdate"/>: wait for the old instance to exit.</summary>
	private const string UpdatedArgument = "--updated";

	private Mutex? _singleInstance;

	/// <summary>Set once the exit was logged, so the <see cref="AppDomain.ProcessExit"/> backstop does not log it twice.</summary>
	private static int _exitLogged;

	protected override void OnStartup(StartupEventArgs e)
	{
		HookCrashLogging();
		base.OnStartup(e);
		LauncherLog.Write($"Launcher start: version {BuildInfo.Version}, pid {Environment.ProcessId}, exe '{Environment.ProcessPath}', args [{string.Join(" ", e.Args)}], "
			+ $"{Environment.OSVersion.VersionString}, .NET {Environment.Version}.");
		_singleInstance = new Mutex(initiallyOwned: true, "Local\\Ro3Emu.Launcher", out var createdNew);
		if (!createdNew && e.Args.Contains(UpdatedArgument))
		{
			LauncherLog.Write("Waiting for the previous launcher to exit (restart after update)...");
			createdNew = WaitForPreviousInstance(_singleInstance);
		}
		if (!createdNew)
		{
			LauncherLog.Exit.Set("another launcher instance is already running");
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
			LauncherLog.Exit.Set("self-update restart");
			LauncherLog.Write($"Restarting into '{exePath}' {UpdatedArgument}.");
			SelfUpdate.Restart(exePath, UpdatedArgument);
			Current.Shutdown();
		});
	}

	protected override void OnExit(ExitEventArgs e)
	{
		LogExit($"exit code {e.ApplicationExitCode}");
		_singleInstance?.Dispose();
		base.OnExit(e);
	}

	protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
	{
		LauncherLog.Exit.Set($"Windows session ending ({e.ReasonSessionEnding})");
		LauncherLog.Write($"Windows session ending ({e.ReasonSessionEnding}).");
		base.OnSessionEnding(e);
	}

	private static void LogExit(string how)
	{
		if (Interlocked.Exchange(ref _exitLogged, 1) != 0)
		{
			return;
		}
		TimeSpan uptime = TimeSpan.Zero;
		try
		{
			using Process self = Process.GetCurrentProcess();
			uptime = DateTime.Now - self.StartTime;
		}
		catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
		{
		}
		LauncherLog.Write($"Launcher exit: reason '{LauncherLog.Exit.Reason}', {how}, pid {Environment.ProcessId}, uptime {LauncherLog.FormatDuration(uptime)}.");
	}

	/// <summary>
	/// Logs every exception nobody caught, before the process dies: the line is written (and the file closed) inside the
	/// handler. Only logs: whether the launcher crashes or carries on does not change.
	/// </summary>
	private void HookCrashLogging()
	{
		AppDomain.CurrentDomain.UnhandledException += (_, e) =>
		{
			if (e.IsTerminating)
			{
				LauncherLog.Exit.Set("crash (unhandled exception)");
			}
			LauncherLog.Write(LauncherLog.FormatException("AppDomain.UnhandledException", e.ExceptionObject, e.IsTerminating));
			if (e.IsTerminating)
			{
				LogExit("crash");
			}
		};
		TaskScheduler.UnobservedTaskException += (_, e) =>
		{
			LauncherLog.Write(LauncherLog.FormatException("TaskScheduler.UnobservedTaskException", e.Exception));
		};
		DispatcherUnhandledException += (_, e) =>
		{
			LauncherLog.Write(LauncherLog.FormatException("Dispatcher.UnhandledException", e.Exception));
		};
		// Environment.Exit or a host shutdown that skips OnExit: still leave a last line.
		AppDomain.CurrentDomain.ProcessExit += (_, _) => LogExit("process exit");
	}

	[STAThread]
	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.13.0")]
	public static void Main()
	{
		new App().Run();
	}
}
