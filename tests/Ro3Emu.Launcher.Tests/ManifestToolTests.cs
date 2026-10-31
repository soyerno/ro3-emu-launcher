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
}
