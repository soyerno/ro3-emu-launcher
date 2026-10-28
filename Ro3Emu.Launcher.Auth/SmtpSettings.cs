namespace Ro3Emu.Launcher.Auth;

public sealed class SmtpSettings
{
	public string Host { get; set; } = "";

	public int Port { get; set; } = 587;

	public string Username { get; set; } = "";

	public string Password { get; set; } = "";

	public bool EnableSsl { get; set; } = true;

	public string FromAddress { get; set; } = "";

	public string FromName { get; set; } = "";
}
