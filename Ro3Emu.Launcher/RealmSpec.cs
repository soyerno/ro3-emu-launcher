using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Ro3Emu.Launcher;

/// <summary>
/// One game world of the server: its own join server and tunnels. Realms of one server share accounts, so a
/// player signs in once and picks the realm before playing. See <see cref="LauncherConfig.GetRealms"/>.
/// </summary>
public sealed class RealmSpec
{
	/// <summary>Id of the realm built from the top-level "server" and "tunnels" when launcher.json has no "realms".</summary>
	public const string ImplicitId = "default";

	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("name")]
	public string Name { get; set; } = "";

	[JsonPropertyName("server")]
	public string? Server { get; set; }

	[JsonPropertyName("tunnels")]
	public List<TunnelSpec> Tunnels { get; set; } = [];

	/// <summary>
	/// Optional URL that answers 200 while the realm is up. The tunnel hostnames cannot tell (a Cloudflare TCP tunnel
	/// answers even when the realm behind it is down), so without it the realm is always treated as online.
	/// </summary>
	[JsonPropertyName("statusUrl")]
	public string? StatusUrl { get; set; }

	/// <summary><see cref="StatusUrl"/> when it is https (or http to a loopback address, for local testing); otherwise null.</summary>
	[JsonIgnore]
	public Uri? StatusUri => LauncherConfig.TryParseHttpUrl(StatusUrl, out Uri? uri) && (uri!.Scheme == Uri.UriSchemeHttps || uri.IsLoopback) ? uri : null;

	/// <summary><see cref="Server"/> normalized without a trailing slash, or null when missing or invalid.</summary>
	[JsonIgnore]
	public string? ServerBaseUrl => LauncherConfig.TryParseHttpUrl(Server, out Uri? uri) ? uri!.AbsoluteUri.TrimEnd('/') : null;

	/// <summary>Why <see cref="Server"/> cannot be used, or null when it is a valid http(s) URL.</summary>
	[JsonIgnore]
	public string? ServerError => LauncherConfig.TryParseHttpUrl(Server, out _)
		? null
		: Strings.Get("config.server.invalid", Server ?? "");
}
