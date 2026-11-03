using System.Text.Json.Nodes;
using ManifestTool;
using Xunit;

namespace Ro3Emu.Launcher.Tests;

/// <summary>The release tool and the launcher agree: what the tool builds and signs, the launcher verifies and applies.</summary>
public class ManifestToolTests
{
	private const string PublishedRoot = "https://updates.example.test/launcher/";

	private static TempDir Dist()
	{
		TempDir dist = new();
		dist.Write("KonsorRO.exe", "launcher 1.4.0");
		dist.Write("RO3Emu.exe", "ro3emu");
		dist.Write("client/Ro3Emu.Loc.dll", "loc");
		dist.Write("client/Ro3Emu.ClientHooks.dll", "hooks");
		dist.Write("data/loc_es.tsv", "es");
		dist.Write("launcher.json", "{}");
		dist.Write("readme.txt", "x");
		return dist;
	}

	[Fact]
	public async Task BuiltAndSignedManifest_IsVerifiedAndAppliedByTheLauncher()
	{
		using TempDir dist = Dist();
		using TempDir output = new();
		using TempDir player = new();
		using TempDir cache = new();
		(string privateKey, string publicKey) = ManifestSigner.GenerateKey();
		JsonObject config = new() { ["defaultRealm"] = "payon" };

		BuildResult built = ManifestBuilder.Build(new BuildOptions(dist.Path, output.Path, "1.4.0", 42, "KonsorRO.exe", config));
		byte[] manifestBytes = File.ReadAllBytes(built.ManifestPath);
		string signature = ManifestSigner.Sign(manifestBytes, privateKey);

		Assert.Equal(["launcher.json", "readme.txt"], built.Skipped);
		Assert.True(ManifestSignature.Verify(manifestBytes, signature, publicKey));
		RemoteManifest manifest = RemoteManifest.Parse(manifestBytes);
		Assert.Equal(42, manifest.Sequence);
		Assert.Equal("1.4.0", manifest.Launcher!.Version);
		Assert.Equal(["RO3Emu.exe", "client/Ro3Emu.ClientHooks.dll", "client/Ro3Emu.Loc.dll", "data/loc_es.tsv"], manifest.Files.Select(f => f.Path).Order(StringComparer.Ordinal));
		// The web server serves only flat names and caches them forever: every url is a content-addressed top-level name.
		foreach ((string url, string sha256) in manifest.Files.Select(f => (f.Url, f.Sha256)).Append((manifest.Launcher.Url, manifest.Launcher.Sha256)))
		{
			Assert.Matches(ManifestBuilder.PublishedNamePattern, url);
			Assert.Contains("." + sha256[..12] + ".", url);
		}
		Assert.Equal("RO3Emu." + manifest.Files.Single(f => f.Path == "RO3Emu.exe").Sha256[..12] + ".exe", manifest.Files.Single(f => f.Path == "RO3Emu.exe").Url);
		Assert.Empty(Directory.GetDirectories(output.Path));

		// Serve the output folder as the website would, and let a development launcher apply it.
		FakeHttpHandler handler = new();
		foreach (string file in Directory.GetFiles(output.Path, "*", SearchOption.AllDirectories))
		{
			handler.Serve(PublishedRoot + Path.GetRelativePath(output.Path, file).Replace('\\', '/'), File.ReadAllBytes(file));
		}
		handler.Serve(PublishedRoot + "manifest.json.sig", signature);
		player.Write("KonsorRO.exe", "launcher dev");
		LauncherConfig launcherConfig = new() { ManifestUrl = PublishedRoot + "manifest.json", ManifestPublicKey = publicKey };

		RemoteUpdateOutcome outcome = await new RemoteUpdater(launcherConfig, player.File("KonsorRO.exe"), "0.0.0-dev", handler, cache.Path).RunAsync(null, CancellationToken.None);

		Assert.Equal(ManifestSource.Remote, outcome.Source);
		Assert.Equal(4, outcome.Files!.Updated.Count);
		Assert.Equal("hooks", File.ReadAllText(player.File("client/Ro3Emu.ClientHooks.dll")));
		Assert.Equal("es", File.ReadAllText(player.File("data/loc_es.tsv")));
		Assert.Equal("payon", LauncherConfig.Parse("{}", outcome.Manifest).DefaultRealm);
	}

	[Fact]
	public void ChangedBytes_GetANewPublishedName_SoACachedNameNeverChangesContent()
	{
		string first = ManifestBuilder.PublishedName("Ro3Emu.Loc.dll", ManifestJson.Sha256("v1"));
		string second = ManifestBuilder.PublishedName("Ro3Emu.Loc.dll", ManifestJson.Sha256("v2"));

		Assert.NotEqual(first, second);
		Assert.StartsWith("Ro3Emu.Loc.", first);
		Assert.EndsWith(".dll", first);
	}

	[Fact]
	public void NamesTheServerWouldNotServe_AreRefused()
	{
		Assert.Throws<ToolException>(() => ManifestBuilder.PublishedName("bad name.dll", ManifestJson.Sha256("x")));
		Assert.Throws<ToolException>(() => ManifestBuilder.PublishedName("script.ps1", ManifestJson.Sha256("x")));
	}

	[Fact]
	public void PatchOverlayFiles_GetServableNames_ButKeepTheirRealManifestPath()
	{
		string bundle = ManifestBuilder.PublishedName("0123456789abcdef.bundle", ManifestJson.Sha256("b"));
		string meta = ManifestBuilder.PublishedName("assetmeta.data", ManifestJson.Sha256("m"));
		string manifestFile = ManifestBuilder.PublishedName("ro3.manifest", ManifestJson.Sha256("r"));
		string baseJson = ManifestBuilder.PublishedName("base.json", ManifestJson.Sha256("j"));

		Assert.Matches(ManifestBuilder.PublishedNamePattern, bundle);
		Assert.Matches(ManifestBuilder.PublishedNamePattern, meta);
		Assert.Matches(ManifestBuilder.PublishedNamePattern, manifestFile);
		Assert.Matches(ManifestBuilder.PublishedNamePattern, baseJson);
		Assert.StartsWith("base.json.", baseJson);
		Assert.StartsWith("0123456789abcdef.bundle.", bundle);
		Assert.EndsWith(".txt", meta);
	}

	[Fact]
	public async Task PatchOverlay_IsBuiltSignedVerifiedAndStagedByTheLauncher()
	{
		using TempDir dist = new();
		dist.Write("RO3Emu.exe", "ro3emu");
		dist.Write("patch/ab/0123456789abcdef.bundle", "bundle bytes");
		dist.Write("patch/assetmeta.data", "meta");
		dist.Write("patch/ro3.manifest", "mani");
		dist.Write("patch/patched.data", "patched");
		dist.Write("patch/base.json", "{}");
		dist.Write("patch/ab/UPPER.bundle", "x");
		dist.Write("patch/notes.txt", "x");
		using TempDir output = new();
		using TempDir player = new();
		using TempDir cache = new();
		(string privateKey, string publicKey) = ManifestSigner.GenerateKey();

		BuildResult built = ManifestBuilder.Build(new BuildOptions(dist.Path, output.Path, "1.4.0", 7, null, null));
		byte[] manifestBytes = File.ReadAllBytes(built.ManifestPath);
		string signature = ManifestSigner.Sign(manifestBytes, privateKey);

		Assert.Equal(["patch/ab/UPPER.bundle", "patch/notes.txt"], built.Skipped);
		Assert.True(ManifestSignature.Verify(manifestBytes, signature, publicKey));
		RemoteManifest manifest = RemoteManifest.Parse(manifestBytes);
		Assert.Equal(
			["RO3Emu.exe", "patch/ab/0123456789abcdef.bundle", "patch/assetmeta.data", "patch/base.json", "patch/patched.data", "patch/ro3.manifest"],
			manifest.Files.Select(f => f.Path).Order(StringComparer.Ordinal));
		Assert.All(manifest.Files, f => Assert.Matches(ManifestBuilder.PublishedNamePattern, f.Url));

		FakeHttpHandler handler = new();
		foreach (string file in Directory.GetFiles(output.Path, "*", SearchOption.AllDirectories))
		{
			handler.Serve(PublishedRoot + Path.GetRelativePath(output.Path, file).Replace('\\', '/'), File.ReadAllBytes(file));
		}
		handler.Serve(PublishedRoot + "manifest.json.sig", signature);
		player.Write("KonsorRO.exe", "launcher dev");
		LauncherConfig launcherConfig = new() { ManifestUrl = PublishedRoot + "manifest.json", ManifestPublicKey = publicKey };

		RemoteUpdateOutcome outcome = await new RemoteUpdater(launcherConfig, player.File("KonsorRO.exe"), "0.0.0-dev", handler, cache.Path).RunAsync(null, CancellationToken.None);

		Assert.Equal(6, outcome.Files!.Updated.Count);
		Assert.Equal("bundle bytes", File.ReadAllText(player.File("patch/ab/0123456789abcdef.bundle")));
		Assert.Equal("meta", File.ReadAllText(player.File("patch/assetmeta.data")));
		Assert.Equal("patched", File.ReadAllText(player.File("patch/patched.data")));
		Assert.Equal("{}", File.ReadAllText(player.File("patch/base.json")));
	}

	[Fact]
	public void DevelopmentVersion_CannotBePublishedAsTheLauncher()
	{
		using TempDir dist = Dist();
		using TempDir output = new();

		Assert.Throws<ToolException>(() => ManifestBuilder.Build(new BuildOptions(dist.Path, output.Path, "0.0.0-dev", 1, "KonsorRO.exe", null)));
	}

	[Fact]
	public void ConfigFieldsOutsideTheAllowList_AreRefused()
	{
		using TempDir dist = Dist();
		using TempDir output = new();
		JsonObject config = new() { ["manifestPublicKey"] = "x" };

		Assert.Throws<ToolException>(() => ManifestBuilder.Build(new BuildOptions(dist.Path, output.Path, "1.4.0", 1, null, config)));
	}

	[Fact]
	public void PrivateKeys_AreRefusedInsideARepository()
	{
		using TempDir repo = new();
		Directory.CreateDirectory(repo.File(".git"));
		using TempDir outside = new();

		Assert.True(ManifestSigner.IsInsideRepository(repo.File("keys/private.pem")));
		Assert.False(ManifestSigner.IsInsideRepository(outside.File("private.pem")));
	}

	private const string LiveRoot = "https://updates.example.test/launcher/";

	private static byte[] LiveManifest(long sequence)
	{
		JsonObject root = new()
		{
			["schema"] = 1,
			["sequence"] = sequence,
			["version"] = "1.2.2",
			["generated"] = "2026-01-01T00:00:00Z",
			["launcher"] = new JsonObject { ["version"] = "1.2.8", ["url"] = LiveRoot + "KonsorRO.aaaaaaaaaaaa.exe", ["sha256"] = new string('a', 64), ["size"] = 10 },
			["files"] = new JsonArray(ManifestJson.FileEntry("RO3Emu.exe", "exe", LiveRoot + "RO3Emu.x.exe")),
			["config"] = new JsonObject { ["defaultRealm"] = "payon" },
		};
		return System.Text.Encoding.UTF8.GetBytes(root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n");
	}

	[Fact]
	public void Add_AppendsOverlayEntries_AndLeavesEverythingElseOfTheLiveManifestUntouched()
	{
		using TempDir live = new();
		using TempDir stage = new();
		using TempDir output = new();
		File.WriteAllBytes(live.File("konsor.json"), LiveManifest(100));
		stage.Write("patch/ab/0123456789abcdef.bundle", "bundle");
		stage.Write("patch/assetmeta.data", "meta");
		stage.Write("patch/base.json", "{}");

		AddResult added = ManifestBuilder.Add(new AddOptions(stage.Path, output.Path, live.File("konsor.json"), "https://konsor.com.ar/launcher", 200, "konsor.json"));

		Assert.Equal(["patch/ab/0123456789abcdef.bundle", "patch/assetmeta.data", "patch/base.json"], added.Added);
		JsonObject before = (JsonObject)JsonNode.Parse(File.ReadAllBytes(live.File("konsor.json")))!;
		JsonObject after = (JsonObject)JsonNode.Parse(File.ReadAllBytes(added.ManifestPath))!;
		Assert.Equal(200, (long)after["sequence"]!);
		Assert.Equal(before["version"]!.ToJsonString(), after["version"]!.ToJsonString());
		Assert.Equal(before["launcher"]!.ToJsonString(), after["launcher"]!.ToJsonString());
		Assert.Equal(before["config"]!.ToJsonString(), after["config"]!.ToJsonString());
		JsonArray files = (JsonArray)after["files"]!;
		Assert.Equal(4, files.Count);
		Assert.Equal(((JsonArray)before["files"]!)[0]!.ToJsonString(), files[0]!.ToJsonString());
		JsonObject meta = (JsonObject)files.Single(f => (string)f!["path"]! == "patch/assetmeta.data")!;
		Assert.Equal("https://konsor.com.ar/launcher/assetmeta.data." + ManifestJson.Sha256("meta")[..12] + ".txt", (string)meta["url"]!);
		Assert.True(File.Exists(output.File("assetmeta.data." + ManifestJson.Sha256("meta")[..12] + ".txt")));
		Assert.Equal("{}", File.ReadAllText(output.File("base.json." + ManifestJson.Sha256("{}")[..12] + ".txt")));
		RemoteManifest.Parse(File.ReadAllBytes(added.ManifestPath));
	}

	[Fact]
	public void Add_ReplacesAnExistingOverlayEntryInPlace_AndRefusesFilesOutsideTheAllowList()
	{
		using TempDir live = new();
		using TempDir stage = new();
		using TempDir output = new();
		File.WriteAllBytes(live.File("konsor.json"), LiveManifest(100));
		stage.Write("patch/assetmeta.data", "v1");
		AddResult first = ManifestBuilder.Add(new AddOptions(stage.Path, output.Path, live.File("konsor.json"), "https://konsor.com.ar/launcher", 200, "konsor.json"));
		stage.Write("patch/assetmeta.data", "v2");
		using TempDir output2 = new();

		AddResult second = ManifestBuilder.Add(new AddOptions(stage.Path, output2.Path, first.ManifestPath, "https://konsor.com.ar/launcher", 300, "konsor.json"));

		JsonArray files = (JsonArray)JsonNode.Parse(File.ReadAllBytes(second.ManifestPath))!["files"]!;
		Assert.Equal(2, files.Count);
		Assert.Equal(ManifestJson.Sha256("v2"), (string)files[1]!["sha256"]!);

		stage.Write("RO3Emu.exe", "not an overlay file");
		ToolException ex = Assert.Throws<ToolException>(() => ManifestBuilder.Add(new AddOptions(stage.Path, output2.Path, first.ManifestPath, "https://konsor.com.ar/launcher", 400, "konsor.json")));
		Assert.Contains("RO3Emu.exe", ex.Message);
	}

	[Fact]
	public void Add_RefusesASequenceThatDoesNotAdvance_AndDefaultsToOneAboveTheLiveOne()
	{
		using TempDir live = new();
		using TempDir stage = new();
		using TempDir output = new();
		File.WriteAllBytes(live.File("konsor.json"), LiveManifest(4102444800));
		stage.Write("patch/ro3.manifest", "m");

		Assert.Throws<ToolException>(() => ManifestBuilder.Add(new AddOptions(stage.Path, output.Path, live.File("konsor.json"), "https://konsor.com.ar/launcher", 4102444800, "konsor.json")));
		AddResult result = ManifestBuilder.Add(new AddOptions(stage.Path, output.Path, live.File("konsor.json"), "https://konsor.com.ar/launcher", null, "konsor.json"));

		Assert.Equal(4102444801, (long)JsonNode.Parse(File.ReadAllBytes(result.ManifestPath))!["sequence"]!);
	}
}
