using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ro3Emu.Launcher;

public sealed class LauncherConfig
{
	private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
	{
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true
	};

	[JsonPropertyName("repository")]
	public string Repository { get; set; } = "http://127.0.0.1:5380/";

	[JsonPropertyName("channel")]
	public string Channel { get; set; } = "live";

	[JsonPropertyName("server")]
	public string? Server { get; set; } = "http://127.0.0.1:5380";

	[JsonPropertyName("serverName")]
	public string ServerName { get; set; } = "KonsorRO";

	public static LauncherConfig Load(string besideExe)
	{
		try
		{
			if (File.Exists(besideExe))
			{
				return JsonSerializer.Deserialize<LauncherConfig>(File.ReadAllBytes(besideExe), Options) ?? new LauncherConfig();
			}
			using Stream stream = typeof(LauncherConfig).Assembly.GetManifestResourceStream("launcher.json");
			return (stream == null) ? new LauncherConfig() : (JsonSerializer.Deserialize<LauncherConfig>(stream, Options) ?? new LauncherConfig());
		}
		catch (JsonException)
		{
			return new LauncherConfig();
		}
	}
}
