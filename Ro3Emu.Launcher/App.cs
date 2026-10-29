using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using Ro3Emu.Patcher;

namespace Ro3Emu.Launcher;

public class App : Application
{
	private Mutex? _singleInstance;

	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);
		_singleInstance = new Mutex(initiallyOwned: true, "Local\\Ro3Emu.Launcher", out var createdNew);
		if (!createdNew)
		{
			Shutdown();
			return;
		}
		string text = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Ro3Launcher.exe");
		SelfUpdate.DeleteLeftovers(text);
		LauncherConfig config = LauncherConfig.Load(Path.Combine(Path.GetDirectoryName(text), "launcher.json"));
		UserSettings userSettings = UserSettings.Load();
		string initialLang = !string.IsNullOrWhiteSpace(userSettings.Language) ? userSettings.Language : config.Language;
		Strings.Configure(initialLang, config.ServerName);
		MainWindow = new MainWindow(config, text);
		MainWindow.Show();
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
