using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace Konsor.LegacyBootstrap
{
	/// <summary>Progress window: fetch, verify, download, swap, start the real launcher, close. Errors offer a retry.</summary>
	internal sealed class BootstrapForm : Form
	{
		private const string DownloadHint = "Descargá el launcher desde https://konsor.com.ar";

		private readonly Options _options;
		private readonly Label _status = new Label { AutoSize = false, Dock = DockStyle.Top, Height = 48 };
		private readonly ProgressBar _progress = new ProgressBar { Dock = DockStyle.Top, Height = 22, Maximum = 1000 };
		private readonly Button _retry = new Button { Text = "Reintentar", AutoSize = true, Visible = false };
		private readonly Button _close = new Button { Text = "Cerrar", AutoSize = true, Visible = false };

		public BootstrapForm(Options options)
		{
			_options = options;
			Text = "Konsor";
			FormBorderStyle = FormBorderStyle.FixedDialog;
			MaximizeBox = false;
			StartPosition = FormStartPosition.CenterScreen;
			ClientSize = new Size(460, 150);
			Padding = new Padding(16);
			Font = SystemFonts.MessageBoxFont;
			Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);

			Label title = new Label { Text = "Actualizando Konsor…", Dock = DockStyle.Top, Height = 28, Font = new Font(Font.FontFamily, 11f, FontStyle.Bold) };
			FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 34 };
			buttons.Controls.Add(_close);
			buttons.Controls.Add(_retry);
			Controls.Add(buttons);
			Controls.Add(_status);
			Controls.Add(_progress);
			Controls.Add(title);

			_retry.Click += (s, e) => Start();
			_close.Click += (s, e) => Close();
			Shown += (s, e) => Start();
			// Closing mid-swap would end the process between the two renames and leave no launcher exe behind.
			FormClosing += (s, e) => e.Cancel = _swapping;
		}

		private volatile bool _swapping;

		private void Start()
		{
			_retry.Visible = false;
			_close.Visible = false;
			_progress.Value = 0;
			SetStatus("Buscando la última versión…");
			new Thread(Run) { IsBackground = true }.Start();
		}

		private void Run()
		{
			string exe = Assembly.GetExecutingAssembly().Location;
			string staged = exe + ".new";
			try
			{
				Release release = Updater.FetchRelease(_options);
				Ui(() => SetStatus("Descargando el launcher " + release.Version + "…"));
				Updater.Download(release, staged, (done, total) => Ui(() =>
				{
					_progress.Value = (int)(done * 1000 / total);
					SetStatus(string.Format("Descargando el launcher {0}… {1:0.0} / {2:0.0} MB", release.Version, done / 1048576.0, total / 1048576.0));
				}));
				Ui(() => SetStatus("Instalando…"));
				_swapping = true;
				try
				{
					BootstrapSwap.Apply(exe, staged, TimeSpan.FromSeconds(10));
				}
				catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
				{
					Updater.TryDelete(staged);
					throw new BootstrapException("No se pudo reemplazar el launcher (" + ex.Message + ").");
				}
				finally
				{
					_swapping = false;
				}
			}
			catch (BootstrapException ex)
			{
				Ui(() => ShowError(ex.Message, canRetry: true));
				return;
			}
			catch (Exception ex)
			{
				// Anything unexpected on this thread would otherwise end the process without a word.
				Updater.TryDelete(staged);
				Ui(() => ShowError("Error inesperado al actualizar (" + ex.Message + ").", canRetry: true));
				return;
			}
			try
			{
				Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe), UseShellExecute = true });
			}
			catch (Exception ex)
			{
				// The launcher is installed; starting it again by hand is enough.
				Ui(() => ShowError("El launcher se actualizó pero no se pudo iniciar (" + ex.Message + "). Abrilo de nuevo.", canRetry: false));
				return;
			}
			Ui(Close);
		}

		private void ShowError(string message, bool canRetry)
		{
			SetStatus(message + (canRetry ? "\n" + DownloadHint : string.Empty));
			_retry.Visible = canRetry;
			_close.Visible = true;
		}

		private void SetStatus(string text) => _status.Text = text;

		private void Ui(Action action)
		{
			try
			{
				if (!IsDisposed)
				{
					BeginInvoke(action);
				}
			}
			catch (InvalidOperationException)
			{
				// The window closed between the check and the call: nothing left to show.
			}
		}
	}
}
