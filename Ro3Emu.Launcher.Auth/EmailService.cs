using System;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Ro3Emu.Launcher.Auth;

public static class EmailService
{
	private static SmtpSettings? _settings;

	private static readonly object _gate = new object();

	public static string ConfigPath => Path.Combine(AppContext.BaseDirectory, "data", "smtp_config.json");

	public static string LogPath => Path.Combine(AppContext.BaseDirectory, "data", "sent_emails.log");

	public static SmtpSettings Settings
	{
		get
		{
			lock (_gate)
			{
				if (_settings != null)
				{
					return _settings;
				}
				_settings = LoadSettings();
				return _settings;
			}
		}
	}

	private static SmtpSettings LoadSettings()
	{
		try
		{
			if (File.Exists(ConfigPath))
			{
				return JsonSerializer.Deserialize<SmtpSettings>(File.ReadAllText(ConfigPath, Encoding.UTF8)) ?? new SmtpSettings();
			}
		}
		catch
		{
		}
		SmtpSettings smtpSettings = new SmtpSettings();
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
			File.WriteAllText(ConfigPath, JsonSerializer.Serialize(smtpSettings, new JsonSerializerOptions
			{
				WriteIndented = true
			}), Encoding.UTF8);
		}
		catch
		{
		}
		return smtpSettings;
	}

	public static async Task<(bool Sent, string Message)> SendVerificationEmailAsync(string toEmail, string username, string code)
	{
		string subject = "[Ragnarok Online 3] Código de Activación de Cuenta";
		string bodyText = $"Hola {username},\n\nTu código de verificación para activar tu cuenta de Ragnarok Online 3 es:\n\n{code}\n\nEste código es válido por 15 minutos.\n";
		string bodyHtml = $"<div style=\"font-family: Arial, sans-serif; background-color: #14171c; color: #f1f5f9; padding: 30px; border-radius: 8px;\">\n  <h2 style=\"color: #f59e0b; margin-top: 0;\">¡Bienvenido a Ragnarok Online 3!</h2>\n  <p>Hola <strong>{WebUtility.HtmlEncode(username)}</strong>,</p>\n  <p>Para activar tu cuenta y poder ingresar al juego, ingresa el siguiente código de 6 dígitos en el launcher:</p>\n  <div style=\"background-color: #1e293b; border: 1px solid #334155; padding: 15px 25px; display: inline-block; border-radius: 6px; margin: 15px 0;\">\n    <span style=\"font-size: 28px; font-weight: bold; letter-spacing: 6px; color: #38bdf8;\">{code}</span>\n  </div>\n  <p style=\"color: #94a3b8; font-size: 13px;\">Este código expira en 15 minutos.</p>\n  <hr style=\"border: 0; border-top: 1px solid #334155; margin: 25px 0;\" />\n  <p style=\"color: #64748b; font-size: 11px;\">Ragnarok Online 3 Server Emulator · Mensaje generado automáticamente.</p>\n</div>";
		return await SendAsync(toEmail, subject, bodyText, bodyHtml, code, "ACTIVACION").ConfigureAwait(continueOnCapturedContext: false);
	}

	public static async Task<(bool Sent, string Message)> SendPasswordResetEmailAsync(string toEmail, string username, string code)
	{
		string subject = "[Ragnarok Online 3] Código de Restablecimiento de Contraseña";
		string bodyText = $"Hola {username},\n\nHemos recibido una solicitud para restablecer tu contraseña. Tu código de recuperación es:\n\n{code}\n\nEste código expira en 15 minutos.\nSi no solicitaste este cambio, puedes ignorar este mensaje.\n";
		string bodyHtml = $"<div style=\"font-family: Arial, sans-serif; background-color: #14171c; color: #f1f5f9; padding: 30px; border-radius: 8px;\">\n  <h2 style=\"color: #ef4444; margin-top: 0;\">Restablecimiento de Contraseña</h2>\n  <p>Hola <strong>{WebUtility.HtmlEncode(username)}</strong>,</p>\n  <p>Hemos recibido una solicitud para cambiar tu contraseña en Ragnarok Online 3.</p>\n  <p>Ingresa el siguiente código de 6 dígitos en el launcher para definir tu nueva contraseña:</p>\n  <div style=\"background-color: #1e293b; border: 1px solid #334155; padding: 15px 25px; display: inline-block; border-radius: 6px; margin: 15px 0;\">\n    <span style=\"font-size: 28px; font-weight: bold; letter-spacing: 6px; color: #f87171;\">{code}</span>\n  </div>\n  <p style=\"color: #94a3b8; font-size: 13px;\">Este código expira en 15 minutos. Si no realizaste esta solicitud, tu cuenta sigue segura.</p>\n  <hr style=\"border: 0; border-top: 1px solid #334155; margin: 25px 0;\" />\n  <p style=\"color: #64748b; font-size: 11px;\">Ragnarok Online 3 Server Emulator · Mensaje generado automáticamente.</p>\n</div>";
		return await SendAsync(toEmail, subject, bodyText, bodyHtml, code, "RECUPERACION").ConfigureAwait(continueOnCapturedContext: false);
	}

	private static async Task<(bool Sent, string Message)> SendAsync(string toEmail, string subject, string bodyText, string bodyHtml, string code, string tag)
	{
		SmtpSettings cfg = Settings;
		bool hasSmtp = !string.IsNullOrWhiteSpace(cfg.Host) && !string.IsNullOrWhiteSpace(cfg.Username);
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
			string contents = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss UTC}] TAG={tag} TO={toEmail} CODE={code} SUBJECT={subject}\n";
			await File.AppendAllTextAsync(LogPath, contents, Encoding.UTF8).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch
		{
		}
		if (!hasSmtp)
		{
			return (Sent: true, Message: "[DEV] Código generado: " + code + " (guardado en data/sent_emails.log). Configura data/smtp_config.json para envío real.");
		}
		try
		{
			using MailMessage msg = new MailMessage
			{
				From = new MailAddress(string.IsNullOrWhiteSpace(cfg.FromAddress) ? cfg.Username : cfg.FromAddress, cfg.FromName),
				Subject = subject,
				Body = bodyText,
				IsBodyHtml = false
			};
			msg.To.Add(toEmail);
			if (!string.IsNullOrEmpty(bodyHtml))
			{
				AlternateView item = AlternateView.CreateAlternateViewFromString(bodyHtml, Encoding.UTF8, "text/html");
				msg.AlternateViews.Add(item);
			}
			using SmtpClient client = new SmtpClient(cfg.Host, cfg.Port)
			{
				EnableSsl = cfg.EnableSsl,
				Credentials = new NetworkCredential(cfg.Username, cfg.Password),
				Timeout = 10000
			};
			await client.SendMailAsync(msg).ConfigureAwait(continueOnCapturedContext: false);
			return (Sent: true, Message: "Correo enviado correctamente a " + toEmail);
		}
		catch (Exception ex)
		{
			try
			{
				string contents2 = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss UTC}] ERROR sending to {toEmail}: {ex.Message}\n";
				await File.AppendAllTextAsync(LogPath, contents2, Encoding.UTF8).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch
			{
			}
			return (Sent: false, Message: "Error al enviar correo vía SMTP: " + ex.Message + " (Código disponible en data/sent_emails.log)");
		}
	}
}
