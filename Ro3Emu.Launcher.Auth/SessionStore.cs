using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ro3Emu.Launcher.Auth;

[SupportedOSPlatform("windows")]
public static class SessionStore
{
	public sealed record StoredSession(string Account, string Password);

	private static string SessionPath => Path.Combine(AppContext.BaseDirectory, "data", "session.bin");

	private static byte[] Entropy => Encoding.UTF8.GetBytes("Ro3Emu.Launcher.SessionStore.v1");

	public static void Save(string account, string password)
	{
		try
		{
			string directoryName = Path.GetDirectoryName(SessionPath);
			if (!string.IsNullOrEmpty(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			byte[] bytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(new StoredSession(account, password)), Entropy, DataProtectionScope.CurrentUser);
			File.WriteAllBytes(SessionPath, bytes);
		}
		catch
		{
		}
	}

	public static StoredSession? Load()
	{
		try
		{
			if (!File.Exists(SessionPath))
			{
				return null;
			}
			return JsonSerializer.Deserialize<StoredSession>(ProtectedData.Unprotect(File.ReadAllBytes(SessionPath), Entropy, DataProtectionScope.CurrentUser));
		}
		catch
		{
			return null;
		}
	}

	public static void Clear()
	{
		try
		{
			if (File.Exists(SessionPath))
			{
				File.Delete(SessionPath);
			}
		}
		catch
		{
		}
	}
}
