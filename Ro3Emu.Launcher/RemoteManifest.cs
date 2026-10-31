using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Ro3Emu.Launcher;

/// <summary>
/// The signed update manifest published next to the launcher (launcher.json "manifestUrl", signature at
/// "&lt;manifestUrl&gt;.sig"). It names the launcher release, the server files kept beside the launcher and a
/// configuration overlay. Every url may be relative to the manifest url. See docs/AUTO_UPDATE.md.
/// </summary>
public sealed class RemoteManifest
{
	public const int SupportedSchema = 1;

	/// <summary>Largest manifest or signature the launcher reads; anything bigger is not a manifest.</summary>
	public const int MaxBytes = 1024 * 1024;

	private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
	{
		ReadCommentHandling = JsonCommentHandling.Disallow,
		AllowTrailingCommas = false
	};

	[JsonPropertyName("schema")]
	public int Schema { get; set; }

	/// <summary>Grows with every release; a manifest with a lower sequence than the cached one is a rollback and is refused.</summary>
	[JsonPropertyName("sequence")]
	public long Sequence { get; set; }

	[JsonPropertyName("launcher")]
	public ManifestLauncher? Launcher { get; set; }

	[JsonPropertyName("files")]
	public List<ManifestFile> Files { get; set; } = [];

	/// <summary>Fields laid over launcher.json; only <see cref="LauncherConfig.RemoteFields"/> are honoured.</summary>
	[JsonPropertyName("config")]
	public JsonObject? Config { get; set; }

	/// <summary>The manifest in <paramref name="json"/>, or <see cref="ManifestException"/> when it is malformed or of an unknown schema.</summary>
	public static RemoteManifest Parse(byte[] json)
	{
		RemoteManifest? manifest;
		try
		{
			manifest = JsonSerializer.Deserialize<RemoteManifest>(json, Options);
		}
		catch (JsonException ex)
		{
			throw new ManifestException("the manifest is not valid JSON: " + ex.Message);
		}
		if (manifest == null)
		{
			throw new ManifestException("the manifest is empty");
		}
		if (manifest.Schema != SupportedSchema)
		{
			throw new ManifestException($"unsupported manifest schema {manifest.Schema} (this launcher reads schema {SupportedSchema})");
		}
		if (manifest.Sequence < 0)
		{
			throw new ManifestException("the manifest sequence is negative");
		}
		manifest.Files ??= [];
		foreach (ManifestFile? file in manifest.Files)
		{
			if (file == null || string.IsNullOrWhiteSpace(file.Path) || string.IsNullOrWhiteSpace(file.Url) || file.Size < 0 || !IsSha256(file.Sha256))
			{
				throw new ManifestException($"invalid file entry '{file?.Path}'");
			}
		}
		if (manifest.Launcher is { } launcher
			&& (string.IsNullOrWhiteSpace(launcher.Version) || string.IsNullOrWhiteSpace(launcher.Url) || launcher.Size <= 0 || !IsSha256(launcher.Sha256)))
		{
			throw new ManifestException("invalid launcher entry");
		}
		return manifest;
	}

	public static bool IsSha256(string? value)
	{
		if (value == null || value.Length != 64)
		{
			return false;
		}
		foreach (char c in value)
		{
			if (!Uri.IsHexDigit(c))
			{
				return false;
			}
		}
		return true;
	}
}

public sealed class ManifestLauncher
{
	[JsonPropertyName("version")]
	public string Version { get; set; } = "";

	[JsonPropertyName("url")]
	public string Url { get; set; } = "";

	[JsonPropertyName("sha256")]
	public string Sha256 { get; set; } = "";

	[JsonPropertyName("size")]
	public long Size { get; set; }
}

public sealed class ManifestFile
{
	/// <summary>Where the file lives relative to the launcher folder; see <see cref="ManagedFiles.TryNormalizePath"/>.</summary>
	[JsonPropertyName("path")]
	public string Path { get; set; } = "";

	[JsonPropertyName("size")]
	public long Size { get; set; }

	[JsonPropertyName("sha256")]
	public string Sha256 { get; set; } = "";

	[JsonPropertyName("url")]
	public string Url { get; set; } = "";
}

public sealed class ManifestException(string message) : Exception(message);
