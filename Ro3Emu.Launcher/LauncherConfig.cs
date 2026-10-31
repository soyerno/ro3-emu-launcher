using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

	/// <summary>
	/// Where the game files come from: "official" (default) installs and updates the original client from Genesis' update
	/// servers; "repository" keeps the older path that downloads them from <see cref="Repository"/>. Launcher
	/// self-updates come from the repository either way.
	/// </summary>
	[JsonPropertyName("gameFiles")]
	public string GameFiles { get; set; } = "official";

	/// <summary>True when the game files come from the operator repository instead of Genesis' servers.</summary>
	[JsonIgnore]
	public bool GameFilesFromRepository => string.Equals(GameFiles?.Trim(), "repository", StringComparison.OrdinalIgnoreCase);

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

	/// <summary>
	/// Optional URL of the signed update manifest (see docs/AUTO_UPDATE.md). Empty: no automatic update of the launcher,
	/// its server files or its remote configuration.
	/// </summary>
	[JsonPropertyName("manifestUrl")]
	public string? ManifestUrl { get; set; }

	/// <summary>PEM public key ("BEGIN PUBLIC KEY", ECDSA P-256) that must have signed the manifest. Empty: auto-update disabled.</summary>
	[JsonPropertyName("manifestPublicKey")]
	public string? ManifestPublicKey { get; set; }

	/// <summary>Optional URL of news.json; tried before "&lt;repository&gt;/news.json".</summary>
	[JsonPropertyName("newsUrl")]
	public string? NewsUrl { get; set; }

	/// <summary>Sequence of the manifest whose "config" was laid over this configuration, or null when none was.</summary>
	[JsonIgnore]
	public long? ManifestSequence { get; private set; }

	/// <summary>
	/// The launcher.json fields a manifest "config" may replace. The update source ("manifestUrl", "manifestPublicKey",
	/// "repository") is never among them, so a manifest cannot move the launcher to another key or server.
	/// </summary>
	public static readonly string[] RemoteFields = ["realms", "defaultRealm", "tunnels", "server", "newsUrl", "website", "discord", "purchaseUrl"];

	/// <summary><see cref="ManifestUrl"/> when it is https (or http to a loopback address, for local testing); otherwise null.</summary>
	[JsonIgnore]
	public Uri? ManifestUri => TryParseHttpUrl(ManifestUrl, out Uri? uri) && (uri!.Scheme == Uri.UriSchemeHttps || uri.IsLoopback) ? uri : null;

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
	/// switch off what a newer build configures (tunnels, language, links). An unreadable file is ignored. Last, the
	/// <see cref="RemoteFields"/> of a verified manifest's "config" win over both.
	/// </summary>
	public static LauncherConfig Load(string besideExe, RemoteManifest? remote = null)
	{
		JsonObject merged = ReadEmbedded() ?? new JsonObject();
		string dir = Path.GetDirectoryName(Path.GetFullPath(besideExe)) ?? AppContext.BaseDirectory;
		Action<string> log = source => AppendLog(dir, $"{source} sets server/tunnels without realms: inherited realms dropped");
		try
		{
			if (File.Exists(besideExe) && JsonNode.Parse(File.ReadAllBytes(besideExe), null, NodeOptions) is JsonObject disk
				&& Overlay(merged, disk, disk.Select(f => f.Key).ToList()))
			{
				log("launcher.json beside the exe");
			}
		}
		catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
		{
		}
		return FromJson(merged, remote, log);
	}

	/// <summary>
	/// Copies <paramref name="fields"/> of <paramref name="layer"/> over <paramref name="merged"/>. A layer that sets
	/// server or tunnels but not realms drops the inherited realms and defaultRealm, so its own server and tunnels form
	/// the implicit realm (see <see cref="GetRealms"/>). Returns true when inherited realms were dropped.
	/// </summary>
	private static bool Overlay(JsonObject merged, JsonObject layer, IReadOnlyCollection<string> fields)
	{
		bool drop = !layer.ContainsKey("realms") && merged["realms"] is JsonArray { Count: > 0 }
			&& fields.Any(f => f is "server" or "tunnels" && layer.ContainsKey(f));
		if (drop)
		{
			merged.Remove("realms");
			merged.Remove("defaultRealm");
		}
		foreach (string field in fields)
		{
			if (layer.TryGetPropertyValue(field, out JsonNode? value))
			{
				merged[field] = value?.DeepClone();
			}
		}
		return drop;
	}

	private static void AppendLog(string dir, string message)
	{
		try
		{
			File.AppendAllText(Path.Combine(dir, "launcher.log"), $"[{DateTime.Now:HH:mm:ss}] {message}\n");
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
		}
	}

	/// <summary>A configuration from launcher.json text alone (plus an optional manifest overlay), without the embedded defaults. Invalid JSON gives the defaults.</summary>
	public static LauncherConfig Parse(string json, RemoteManifest? remote = null)
	{
		try
		{
			return FromJson(JsonNode.Parse(json, null, NodeOptions) as JsonObject ?? new JsonObject(), remote);
		}
		catch (JsonException)
		{
			return FromJson(new JsonObject(), remote);
		}
	}

	private static LauncherConfig FromJson(JsonObject node, RemoteManifest? remote, Action<string>? log = null)
	{
		if (remote?.Config is not { } overlay)
		{
			return FromJson(node);
		}
		JsonObject withOverlay = (JsonObject)node.DeepClone();
		if (Overlay(withOverlay, overlay, RemoteFields))
		{
			log?.Invoke("manifest config");
		}
		try
		{
			LauncherConfig config = withOverlay.Deserialize<LauncherConfig>(Options) ?? new LauncherConfig();
			config.ManifestSequence = remote.Sequence;
			return config;
		}
		catch (JsonException)
		{
			// A manifest "config" of the wrong shape must not wipe the local configuration: ignore the overlay.
			return FromJson(node);
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
