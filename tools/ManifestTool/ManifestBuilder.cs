using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ro3Emu.Launcher;

namespace ManifestTool;

/// <param name="DistDir">The release folder: the launcher exe plus the server files beside it (RO3Emu.exe, client\, data\...).</param>
/// <param name="OutDir">Receives the manifest and the files, copied flat under content-addressed names (see <see cref="ManifestBuilder.PublishedName"/>).</param>
/// <param name="LauncherFileName">The launcher exe inside <paramref name="DistDir"/> (for example KonsorRO.exe), or null to leave it out.</param>
/// <param name="Config">The manifest "config" overlay (only LauncherConfig.RemoteFields), or null.</param>
/// <param name="BaseUrl">Prefix for the file urls; null writes urls relative to the manifest (recommended).</param>
public sealed record BuildOptions(
	string DistDir,
	string OutDir,
	string Version,
	long Sequence,
	string? LauncherFileName,
	JsonObject? Config,
	string? BaseUrl = null,
	string ManifestName = "manifest.json");

/// <param name="Skipped">Files of the dist folder the launcher would never update (not in its allow-list).</param>
public sealed record BuildResult(string ManifestPath, IReadOnlyList<string> Included, IReadOnlyList<string> Skipped);

public static class ManifestBuilder
{
	public static BuildResult Build(BuildOptions options)
	{
		string dist = Path.GetFullPath(options.DistDir);
		if (!Directory.Exists(dist))
		{
			throw new ToolException($"dist folder '{dist}' does not exist");
		}
		if (options.Sequence < 0)
		{
			throw new ToolException("the sequence must be 0 or more");
		}
		ValidateConfig(options.Config);
		Directory.CreateDirectory(options.OutDir);

		JsonObject? launcher = null;
		JsonArray files = [];
		List<string> included = [];
		List<string> skipped = [];
		foreach (string path in Directory.EnumerateFiles(dist, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
		{
			string relative = Path.GetRelativePath(dist, path).Replace('\\', '/');
			bool isLauncher = options.LauncherFileName != null && string.Equals(relative, options.LauncherFileName, StringComparison.OrdinalIgnoreCase);
			if (!isLauncher && !ManagedFiles.TryNormalizePath(relative, out relative))
			{
				skipped.Add(Path.GetRelativePath(dist, path).Replace('\\', '/'));
				continue;
			}
			(string sha256, long size, string url) = Publish(path, options);
			if (isLauncher)
			{
				if (LauncherVersion.IsDevelopment(options.Version))
				{
					throw new ToolException($"version '{options.Version}' is a development version; publish the launcher with -p:Version=<major.minor.patch>");
				}
				launcher = new JsonObject { ["version"] = options.Version, ["url"] = url, ["sha256"] = sha256, ["size"] = size };
			}
			else
			{
				files.Add(new JsonObject { ["path"] = relative, ["size"] = size, ["sha256"] = sha256, ["url"] = url });
			}
			included.Add(relative);
		}
		if (options.LauncherFileName != null && launcher == null)
		{
			throw new ToolException($"launcher '{options.LauncherFileName}' is not in '{dist}'");
		}

		JsonObject manifest = new JsonObject
		{
			["schema"] = RemoteManifest.SupportedSchema,
			["sequence"] = options.Sequence,
			["version"] = options.Version,
			["generated"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
		};
		if (launcher != null)
		{
			manifest["launcher"] = launcher;
		}
		manifest["files"] = files;
		if (options.Config != null)
		{
			manifest["config"] = options.Config.DeepClone();
		}
		byte[] json = Encoding.UTF8.GetBytes(manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
		RemoteManifest.Parse(json); // the launcher must accept what we publish
		string manifestPath = Path.Combine(options.OutDir, options.ManifestName);
		File.WriteAllBytes(manifestPath, json);
		return new BuildResult(manifestPath, included, skipped);
	}

	private static (string Sha256, long Size, string Url) Publish(string source, BuildOptions options)
	{
		byte[] data = File.ReadAllBytes(source);
		string sha256 = Convert.ToHexStringLower(SHA256.HashData(data));
		string relativeUrl = PublishedName(Path.GetFileName(source), sha256);
		string target = Path.Combine(options.OutDir, relativeUrl);
		if (File.Exists(target) && !File.ReadAllBytes(target).AsSpan().SequenceEqual(data))
		{
			throw new ToolException($"{target} already exists with other bytes; a published name must never change content");
		}
		File.WriteAllBytes(target, data);
		string url = string.IsNullOrWhiteSpace(options.BaseUrl) ? relativeUrl : options.BaseUrl.TrimEnd('/') + "/" + relativeUrl;
		return (sha256, data.LongLength, url);
	}

	/// <summary>
	/// The flat, content-addressed name a file is published under, next to the manifest: <c>RO3Emu.exe</c> becomes
	/// <c>RO3Emu.&lt;first 12 hex of sha256&gt;.exe</c>. The web server caches these names forever (immutable) and only
	/// serves top-level names matching <see cref="PublishedNamePattern"/>, so a name never changes content.
	/// </summary>
	public static string PublishedName(string fileName, string sha256)
	{
		string name = $"{Path.GetFileNameWithoutExtension(fileName)}.{sha256[..12]}{Path.GetExtension(fileName)}";
		if (!PublishedNamePattern.IsMatch(name))
		{
			throw new ToolException($"'{fileName}' cannot be published: the server only serves names matching {PublishedNamePattern}");
		}
		return name;
	}

	/// <summary>Top-level file names the /launcher/ route of the web server serves.</summary>
	public static readonly Regex PublishedNamePattern = new Regex(@"^[A-Za-z0-9._-]+\.(exe|dll|tsv|txt|zip)$", RegexOptions.CultureInvariant);

	private static void ValidateConfig(JsonObject? config)
	{
		if (config == null)
		{
			return;
		}
		foreach (KeyValuePair<string, JsonNode?> field in config)
		{
			if (!LauncherConfig.RemoteFields.Contains(field.Key))
			{
				throw new ToolException($"config field '{field.Key}' cannot be set by the manifest (allowed: {string.Join(", ", LauncherConfig.RemoteFields)})");
			}
		}
		try
		{
			config.Deserialize<LauncherConfig>();
		}
		catch (JsonException ex)
		{
			throw new ToolException("the config does not match launcher.json: " + ex.Message);
		}
	}
}
