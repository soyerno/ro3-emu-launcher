using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ro3Emu.Launcher;

public sealed class UserSettings
{
	[JsonPropertyName("clientDirectory")]
	public string? ClientDirectory { get; set; }

	[JsonPropertyName("language")]
	public string? Language { get; set; }

	private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RO3Emu", "launcher-settings.json");

	public static UserSettings Load()
	{
		try
		{
			return File.Exists(FilePath) ? (JsonSerializer.Deserialize<UserSettings>(File.ReadAllBytes(FilePath)) ?? new UserSettings()) : new UserSettings();
		}
		catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException)
		{
			return new UserSettings();
		}
	}

	public static void SaveLanguage(string lang)
	{
		var s = Load();
		s.Language = lang;
		s.Save();
	}

	public void Save()
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
			File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
		}
	}
}
