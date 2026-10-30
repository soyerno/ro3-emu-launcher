using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ro3Emu.Launcher.Auth;

/// <summary>
/// Remembers the last account name on this PC ("Remember me"). It never stores a password.
/// </summary>
[SupportedOSPlatform("windows")]
public static class SessionStore
{
	private sealed record StoredAccount(string Account);

	private static string SessionPath => Path.Combine(AppContext.BaseDirectory, "data", "session.bin");

	private static byte[] Entropy => Encoding.UTF8.GetBytes("Ro3Emu.Launcher.SessionStore.v1");

	public static void Save(string account)
	{
		if (string.IsNullOrWhiteSpace(account))
		{
			Clear();
			return;
		}
		try
		{
			string directoryName = Path.GetDirectoryName(SessionPath);
			if (!string.IsNullOrEmpty(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			byte[] bytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(new StoredAccount(account.Trim())), Entropy, DataProtectionScope.CurrentUser);
			File.WriteAllBytes(SessionPath, bytes);
		}
		catch
		{
		}
	}

	/// <summary>
	/// Returns the remembered account name, or null. A file written by an older launcher that still
	/// holds a password is rewritten account-only, so the password is erased from disk.
	/// </summary>
	public static string? Load()
	{
		try
		{
			if (!File.Exists(SessionPath))
			{
				return null;
			}
			byte[] json = ProtectedData.Unprotect(File.ReadAllBytes(SessionPath), Entropy, DataProtectionScope.CurrentUser);
			using JsonDocument doc = JsonDocument.Parse(json);
			string? account = doc.RootElement.TryGetProperty("Account", out JsonElement accountProp) && accountProp.ValueKind == JsonValueKind.String
				? accountProp.GetString()
				: null;
			if (string.IsNullOrWhiteSpace(account))
			{
				Clear();
				return null;
			}
			// Legacy format {"Account":..,"Password":..}: drop the password right away.
			if (doc.RootElement.TryGetProperty("Password", out _))
			{
				Save(account);
			}
			return account;
		}
		catch
		{
			Clear();
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
