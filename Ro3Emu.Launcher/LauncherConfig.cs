using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
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

	/// <summary>
	/// Optional page the game's purchase / recharge buttons open, passed to RO3Emu.exe join --purchase-url. Empty: those
	/// buttons open nothing (RO3Emu never sends a player to the publisher's store).
	/// </summary>
	[JsonPropertyName("purchaseUrl")]
	public string? PurchaseUrl { get; set; }

	/// <summary>Cloudflare TCP tunnels opened before launch. Empty: the server is reached directly.</summary>
	[JsonPropertyName("tunnels")]
	public List<TunnelSpec> Tunnels { get; set; } = [];

	/// <summary>
	/// Optional realms (game worlds sharing accounts), each with its own server and tunnels. Empty: the top-level
	/// <see cref="Server"/> and <see cref="Tunnels"/> are the only realm and the launcher shows no realm picker.
	/// </summary>
	[JsonPropertyName("realms")]
	public List<RealmSpec>? Realms { get; set; }

	/// <summary>Id of the realm selected when the player has not picked one, or picked one that no longer exists.</summary>
	[JsonPropertyName("defaultRealm")]
	public string? DefaultRealm { get; set; }

	private IReadOnlyList<RealmSpec>? _realms;

	/// <summary>The configured realms with an id, or one implicit realm built from <see cref="Server"/> and <see cref="Tunnels"/>.</summary>
	public IReadOnlyList<RealmSpec> GetRealms()
	{
		if (_realms != null)
		{
			return _realms;
		}
		List<RealmSpec> realms = [];
		foreach (RealmSpec? realm in Realms ?? [])
		{
			if (realm != null && !string.IsNullOrWhiteSpace(realm.Id) && !realms.Exists(r => string.Equals(r.Id, realm.Id, StringComparison.OrdinalIgnoreCase)))
			{
				realm.Id = realm.Id.Trim();
				if (string.IsNullOrWhiteSpace(realm.Name))
				{
					realm.Name = realm.Id;
				}
				realm.Tunnels ??= [];
				realms.Add(realm);
			}
		}
		if (realms.Count == 0)
		{
			realms.Add(new RealmSpec { Id = RealmSpec.ImplicitId, Name = ServerName, Server = Server, Tunnels = Tunnels });
		}
		return _realms = realms;
	}

	/// <summary>The realm with <paramref name="id"/>; otherwise <see cref="DefaultRealm"/>; otherwise the first realm.</summary>
	public RealmSpec SelectRealm(string? id)
	{
		IReadOnlyList<RealmSpec> realms = GetRealms();
		return Find(id) ?? Find(DefaultRealm) ?? realms[0];

		RealmSpec? Find(string? wanted)
		{
			foreach (RealmSpec realm in realms)
			{
				if (string.Equals(realm.Id, wanted?.Trim(), StringComparison.OrdinalIgnoreCase))
				{
					return realm;
				}
			}
			return null;
		}
	}

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

	/// <summary>
	/// The launcher.json embedded in the build, overlaid with the fields of the launcher.json beside the executable:
	/// a field the file does not have keeps the embedded value, so an older file (from a previous release) does not
	/// switch off what a newer build configures (tunnels, language, links). An unreadable file is ignored.
	/// </summary>
	public static LauncherConfig Load(string besideExe)
	{
		JsonObject merged = ReadEmbedded() ?? new JsonObject();
		try
		{
			if (File.Exists(besideExe) && JsonNode.Parse(File.ReadAllBytes(besideExe), null, NodeOptions) is JsonObject disk)
			{
				foreach (KeyValuePair<string, JsonNode?> field in disk)
				{
					merged[field.Key] = field.Value?.DeepClone();
				}
			}
		}
		catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
		{
		}
		return FromJson(merged);
	}

	/// <summary>A configuration from launcher.json text alone, without the embedded defaults. Invalid JSON gives the defaults.</summary>
	public static LauncherConfig Parse(string json)
	{
		try
		{
			return JsonNode.Parse(json, null, NodeOptions) is JsonObject node ? FromJson(node) : new LauncherConfig();
		}
		catch (JsonException)
		{
			return new LauncherConfig();
		}
	}

	private static LauncherConfig FromJson(JsonObject node)
	{
		try
		{
			return node.Deserialize<LauncherConfig>(Options) ?? new LauncherConfig();
		}
		catch (JsonException)
		{
			return new LauncherConfig();
		}
	}

	private static readonly JsonDocumentOptions NodeOptions = new JsonDocumentOptions
	{
		CommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true
	};

	private static JsonObject? ReadEmbedded()
	{
		try
		{
			using Stream? stream = typeof(LauncherConfig).Assembly.GetManifestResourceStream("launcher.json");
			return stream == null ? null : JsonNode.Parse(stream, null, NodeOptions) as JsonObject;
		}
		catch (JsonException)
		{
			return null;
		}
	}
}
