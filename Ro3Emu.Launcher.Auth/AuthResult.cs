namespace Ro3Emu.Launcher.Auth;

public sealed record AuthResult(bool Success, string Message, string? Username = null, string? Email = null, string? Provider = null);
