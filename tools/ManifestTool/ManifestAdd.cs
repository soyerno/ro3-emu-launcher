using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ro3Emu.Launcher;

namespace ManifestTool;

/// <param name="DistDir">Folder laid out as manifest paths (for example <c>patch/assetmeta.data</c>); every file in it is added.</param>
/// <param name="OutDir">Receives the new manifest and the published (content-addressed) files.</param>
/// <param name="BaseManifestPath">The live manifest to extend; all its entries, launcher, version and config are kept as they are.</param>
/// <param name="BaseUrl">Absolute prefix for the new entries' urls (the live manifest's convention).</param>
/// <param name="Sequence">The new sequence; null means one above the base manifest's.</param>
public sealed record AddOptions(string DistDir, string OutDir, string BaseManifestPath, string BaseUrl, long? Sequence, string ManifestName = "konsor.json");

public sealed record AddResult(string ManifestPath, IReadOnlyList<string> Added);

public static class ManifestAdder
{
	/// <summary>
	/// Adds (or replaces, by path) the files of a folder in an existing manifest without touching its other entries.
	/// Only <c>sequence</c> and <c>generated</c> change besides the files list. Every file must be launcher-managed.
	/// </summary>
	public static AddResult Add(AddOptions options)
	{
		string dist = Path.GetFullPath(options.DistDir);
		if (!Directory.Exists(dist))
		{
			throw new ToolException($"folder '{dist}' does not exist");
		}
		if (string.IsNullOrWhiteSpace(options.BaseUrl) || !Uri.IsWellFormedUriString(options.BaseUrl, UriKind.Absolute))
		{
			throw new ToolException("--base-url must be an absolute url");
		}
		byte[] baseBytes = File.ReadAllBytes(options.BaseManifestPath);
		long baseSequence = RemoteManifest.Parse(baseBytes).Sequence;
		JsonObject manifest = JsonNode.Parse(baseBytes) as JsonObject ?? throw new ToolException("the base manifest is not a JSON object");
		JsonArray files = manifest["files"] as JsonArray ?? throw new ToolException("the base manifest has no files list");
		long sequence = options.Sequence ?? baseSequence + 1;
		if (sequence <= baseSequence)
		{
			throw new ToolException($"sequence {sequence} must be above the base manifest's {baseSequence}, or launchers ignore it");
		}

		List<(string Relative, string Path)> staged = [];
		foreach (string path in Directory.EnumerateFiles(dist, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
		{
			string relative = Path.GetRelativePath(dist, path).Replace('\\', '/');
			if (!ManagedFiles.TryNormalizePath(relative, out string normalized) || !normalized.StartsWith("patch/", StringComparison.Ordinal))
			{
				throw new ToolException($"'{relative}' is not a patch overlay file the launcher accepts");
			}
			staged.Add((normalized, path));
		}
		if (staged.Count == 0)
		{
			throw new ToolException($"nothing to add in '{dist}'");
		}

		Directory.CreateDirectory(options.OutDir);
		List<string> added = [];
		foreach ((string relative, string path) in staged)
		{
			(string sha256, long size, string url) = ManifestBuilder.Publish(path, options.OutDir, options.BaseUrl);
			JsonObject entry = new() { ["path"] = relative, ["size"] = size, ["sha256"] = sha256, ["url"] = url };
			int index = files.ToList().FindIndex(f => string.Equals((string?)f?["path"], relative, StringComparison.Ordinal));
			if (index >= 0)
			{
				files[index] = entry;
			}
			else
			{
				files.Add(entry);
			}
			added.Add(relative);
		}
		manifest["sequence"] = sequence;
		manifest["generated"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
		byte[] json = Encoding.UTF8.GetBytes(manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
		RemoteManifest.Parse(json);
		string manifestPath = Path.Combine(options.OutDir, options.ManifestName);
		File.WriteAllBytes(manifestPath, json);
		return new AddResult(manifestPath, added);
	}
}
