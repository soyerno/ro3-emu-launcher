using System;
using System.Collections.Generic;
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

	public const string DefaultRepository = "http://127.0.0.1:5380/";

	[JsonPropertyName("repository")]
	public string Repository { get; set; } = DefaultRepository;

	[JsonPropertyName("channel")]
	public string Channel { get; set; } = "live";

	[JsonPropertyName("server")]
	public string? Server { get; set; } = "http://127.0.0.1:5380";

	[JsonPropertyName("serverName")]
	public string ServerName { get; set; } = "My RO3 Server";

	/// <summary>Default UI and message language: "en" or "es". Players can switch it in the launcher.</summary>
	[JsonPropertyName("language")]
	public string Language { get; set; } = Strings.DefaultLanguage;

	/// <summary>Optional website and Discord invite URLs; the matching buttons are hidden when empty.</summary>
	[JsonPropertyName("website")]
	public string? Website { get; set; }

	[JsonPropertyName("discord")]
	public string? Discord { get; set; }

	/// <summary>Optional URL returning { "enabled": bool } or { "registration_enabled": bool }. Empty: registration is assumed open.</summary>
	[JsonPropertyName("statusUrl")]
	public string? StatusUrl { get; set; }

	/// <summary>Cloudflare TCP tunnels opened before launch. Empty: the server is reached directly.</summary>
	[JsonPropertyName("tunnels")]
	public List<TunnelSpec> Tunnels { get; set; } = [];

	/// <summary><see cref="Server"/> normalized without a trailing slash, or null when missing or invalid.</summary>
	[JsonIgnore]
	public string? ServerBaseUrl => TryParseHttpUrl(Server, out Uri? uri) ? uri!.AbsoluteUri.TrimEnd('/') : null;

	/// <summary>Why <see cref="Server"/> cannot be used, or null when it is a valid http(s) URL.</summary>
	[JsonIgnore]
	public string? ServerError => TryParseHttpUrl(Server, out _)
		? null
		: Strings.Get("config.server.invalid", Server ?? "");

	/// <summary><see cref="Repository"/> as a URI, or null when missing or invalid.</summary>
	[JsonIgnore]
	public Uri? RepositoryUri => TryParseHttpUrl(Repository, out Uri? uri) ? uri : null;

	/// <summary>Why <see cref="Repository"/> cannot be used, or null when it is a valid http(s) URL.</summary>
	[JsonIgnore]
	public string? RepositoryError => RepositoryUri != null ? null : Strings.Get("config.repository.invalid", Repository ?? "");

	public static bool TryParseHttpUrl(string? value, out Uri? uri)
	{
		if (Uri.TryCreate(value?.Trim(), UriKind.Absolute, out uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && !string.IsNullOrEmpty(uri.Host))
		{
			return true;
		}
		uri = null;
		return false;
	}

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
