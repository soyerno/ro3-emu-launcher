using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Ro3Emu.Launcher.Tests;

public class ManagedFilePathTests
{
	[Theory]
	[InlineData("RO3Emu.exe", "RO3Emu.exe")]
	[InlineData("cloudflared.exe", "cloudflared.exe")]
	[InlineData("client/Ro3Emu.Loc.dll", "client/Ro3Emu.Loc.dll")]
	[InlineData("client\\Ro3Emu.ClientHooks.dll", "client/Ro3Emu.ClientHooks.dll")]
	[InlineData("data/loc_es.tsv", "data/loc_es.tsv")]
	public void AllowedPaths_AreAccepted(string path, string expected)
	{
		Assert.True(ManagedFiles.TryNormalizePath(path, out string normalized));
		Assert.Equal(expected, normalized);
	}

	[Theory]
	[InlineData("")]
	[InlineData("../RO3Emu.exe")]
	[InlineData("client/../RO3Emu.exe")]
	[InlineData("client/./Ro3Emu.Loc.dll")]
	[InlineData("C:\\Windows\\RO3Emu.exe")]
	[InlineData("C:RO3Emu.exe")]
	[InlineData("/RO3Emu.exe")]
	[InlineData("\\\\server\\share\\RO3Emu.exe")]
	[InlineData("client/x.exe")]
	[InlineData("data/x.dll")]
	[InlineData("other.exe")]
	[InlineData("KonsorRO.exe")]
	[InlineData("launcher.json")]
	[InlineData("client/sub/x.dll")]
	[InlineData("client//x.dll")]
	[InlineData("client/CON.dll")]
	[InlineData("data/loc_es.tsv:stream")]
	[InlineData("data/.tsv")]
	public void OtherPaths_AreRejected(string path)
	{
		Assert.False(ManagedFiles.TryNormalizePath(path, out _));
	}
}

public class ManagedFilesApplyTests
{
	private const string BaseUrl = "https://updates.example.test/launcher/";

	private static RemoteManifest Manifest(params JsonObject[] files)
		=> RemoteManifest.Parse(ManifestJson.Build(1, new JsonArray(files)));

	[Fact]
	public async Task OnlyMismatchedFiles_AreDownloadedAndSwapped()
	{
		using TempDir dir = new();
		dir.Write("RO3Emu.exe", "same");
		dir.Write("client/Ro3Emu.Loc.dll", "old loc");
		FakeHttpHandler handler = new();
		handler.Serve(BaseUrl + "files/loc.dll", "new loc");
		handler.Serve(BaseUrl + "files/es.tsv", "hola");
		RemoteManifest manifest = Manifest(
			ManifestJson.FileEntry("RO3Emu.exe", "same", "files/ro3emu.exe"),
			ManifestJson.FileEntry("client/Ro3Emu.Loc.dll", "new loc", "files/loc.dll"),
			ManifestJson.FileEntry("data/loc_es.tsv", "hola", "files/es.tsv"));

		FileApplyResult result = await ManagedFiles.ApplyAsync(manifest.Files, new Uri(BaseUrl), dir.Path, new HttpClient(handler), null, null, CancellationToken.None);

		Assert.Equal(["client/Ro3Emu.Loc.dll", "data/loc_es.tsv"], result.Updated);
		Assert.Equal(1, result.UpToDate);
		Assert.Empty(result.Failed);
		Assert.Equal([BaseUrl + "files/loc.dll", BaseUrl + "files/es.tsv"], handler.Requests);
		Assert.Equal("new loc", File.ReadAllText(dir.File("client/Ro3Emu.Loc.dll")));
		Assert.Equal("hola", File.ReadAllText(dir.File("data/loc_es.tsv")));
		Assert.Empty(Directory.GetFiles(dir.Path, "*.old", SearchOption.AllDirectories));
		Assert.Empty(Directory.GetFiles(dir.Path, "*.new", SearchOption.AllDirectories));
	}

	[Fact]
	public async Task DownloadWithTheWrongHash_LeavesTheFileUntouched()
	{
		using TempDir dir = new();
		dir.Write("client/Ro3Emu.Loc.dll", "old loc");
		FakeHttpHandler handler = new();
		handler.Serve(BaseUrl + "files/loc.dll", "evil loc");
		RemoteManifest manifest = Manifest(ManifestJson.FileEntry("client/Ro3Emu.Loc.dll", "new loc", "files/loc.dll"));

		FileApplyResult result = await ManagedFiles.ApplyAsync(manifest.Files, new Uri(BaseUrl), dir.Path, new HttpClient(handler), null, null, CancellationToken.None);

		Assert.Equal(["client/Ro3Emu.Loc.dll"], result.Failed);
		Assert.Empty(result.Updated);
		Assert.Equal("old loc", File.ReadAllText(dir.File("client/Ro3Emu.Loc.dll")));
		Assert.Empty(Directory.GetFiles(dir.Path, "*.new", SearchOption.AllDirectories));
	}

	[Fact]
	public async Task DisallowedPath_IsNeverDownloaded()
	{
		using TempDir dir = new();
		FakeHttpHandler handler = new();
		handler.Serve(BaseUrl + "files/x", "x");
		RemoteManifest manifest = Manifest(ManifestJson.FileEntry("../evil.exe", "x", "files/x"));

		FileApplyResult result = await ManagedFiles.ApplyAsync(manifest.Files, new Uri(BaseUrl), dir.Path, new HttpClient(handler), null, null, CancellationToken.None);

		Assert.Equal(["../evil.exe"], result.Rejected);
		Assert.Empty(handler.Requests);
	}

	[Fact]
	public void Leftovers_OfAnEarlierSwap_AreCleaned()
	{
		using TempDir dir = new();
		dir.Write("RO3Emu.exe.old", "x");
		dir.Write("client/Ro3Emu.Loc.dll.old", "x");
		dir.Write("data/loc_es.tsv.new", "x");
		dir.Write("client/keep.dll", "x");

		ManagedFiles.CleanLeftovers(dir.Path);

		Assert.Empty(Directory.GetFiles(dir.Path, "*.old", SearchOption.AllDirectories));
		Assert.Empty(Directory.GetFiles(dir.Path, "*.new", SearchOption.AllDirectories));
		Assert.True(File.Exists(dir.File("client/keep.dll")));
	}
}

public class RemoteConfigOverlayTests
{
	private const string BaseJson = """
	{
	  "server": "http://127.0.0.1:5380",
	  "serverName": "Base",
	  "manifestUrl": "https://updates.example.test/launcher/manifest.json",
	  "manifestPublicKey": "pinned",
	  "realms": [ { "id": "prontera", "name": "Prontera", "server": "http://127.0.0.1:5380" } ]
	}
	""";

	[Fact]
	public void ManifestConfig_WinsForTheFieldsItProvides()
	{
		JsonObject config = new()
		{
			["defaultRealm"] = "payon",
			["newsUrl"] = "https://site.example.test/news.json",
			["realms"] = new JsonArray(
				new JsonObject { ["id"] = "prontera", ["name"] = "Prontera", ["server"] = "http://127.0.0.1:5380" },
				new JsonObject { ["id"] = "payon", ["name"] = "Payon", ["server"] = "http://127.0.0.1:5390" })
		};
		RemoteManifest manifest = RemoteManifest.Parse(ManifestJson.Build(12, config: config));

		LauncherConfig merged = LauncherConfig.Parse(BaseJson, manifest);

		Assert.Equal(["prontera", "payon"], merged.GetRealms().Select(r => r.Id));
		Assert.Equal("payon", merged.SelectRealm(null).Id);
		Assert.Equal("https://site.example.test/news.json", merged.NewsUrl);
		Assert.Equal("Base", merged.ServerName);
		Assert.Equal(12, merged.ManifestSequence);
	}

	[Fact]
	public void ManifestConfig_CannotReplaceTheUpdateSourceOrItsKey()
	{
		JsonObject config = new()
		{
			["manifestUrl"] = "https://evil.example.test/m.json",
			["manifestPublicKey"] = "attacker",
			["repository"] = "https://evil.example.test/"
		};
		RemoteManifest manifest = RemoteManifest.Parse(ManifestJson.Build(1, config: config));

		LauncherConfig merged = LauncherConfig.Parse(BaseJson, manifest);

		Assert.Equal("https://updates.example.test/launcher/manifest.json", merged.ManifestUrl);
		Assert.Equal("pinned", merged.ManifestPublicKey);
		Assert.Equal(LauncherConfig.DefaultRepository, merged.Repository);
	}

	[Fact]
	public void ManifestConfigOfTheWrongShape_IsIgnored_InsteadOfWipingTheLocalConfig()
	{
		JsonObject config = new() { ["realms"] = "not a list" };
		RemoteManifest manifest = RemoteManifest.Parse(ManifestJson.Build(3, config: config));

		LauncherConfig merged = LauncherConfig.Parse(BaseJson, manifest);

		Assert.Equal("Base", merged.ServerName);
		Assert.Equal("prontera", Assert.Single(merged.GetRealms()).Id);
		Assert.Equal("pinned", merged.ManifestPublicKey);
	}

	[Theory]
	[InlineData("https://updates.example.test/m.json", true)]
	[InlineData("http://127.0.0.1:8080/m.json", true)]
	[InlineData("http://updates.example.test/m.json", false)]
	[InlineData("", false)]
	[InlineData(null, false)]
	public void ManifestUrl_MustBeHttpsOrLoopback(string? url, bool valid)
	{
		LauncherConfig config = new() { ManifestUrl = url };

		Assert.Equal(valid, config.ManifestUri != null);
	}
}

public class LauncherVersionTests
{
	[Theory]
	[InlineData("1.2.0", "1.1.9")]
	[InlineData("1.10.0", "1.9.0")]
	[InlineData("2.0", "1.9.9")]
	[InlineData("v1.2.1", "1.2.0")]
	[InlineData("1.2.0", "1.2.0-rc.1")]
	public void IsNewer_WhenTheCandidateIsHigher(string candidate, string running)
	{
		Assert.True(LauncherVersion.IsNewer(candidate, running));
		Assert.False(LauncherVersion.IsNewer(running, candidate));
	}

	[Theory]
	[InlineData("1.2.0", "1.2.0")]
	[InlineData("garbage", "1.0.0")]
	[InlineData("1.0.0", "garbage")]
	[InlineData("", "1.0.0")]
	public void IsNotNewer_WhenEqualOrUnparsable(string candidate, string running)
	{
		Assert.False(LauncherVersion.IsNewer(candidate, running));
	}

	[Theory]
	[InlineData("0.0.0-dev", true)]
	[InlineData("1.2.0-dev", true)]
	[InlineData("1.2.0-rc.1", true)]
	[InlineData("0.0.0", true)]
	[InlineData("garbage", true)]
	[InlineData("1.2.0", false)]
	public void DevelopmentBuilds_NeverSelfUpdate(string running, bool development)
	{
		Assert.Equal(development, LauncherVersion.IsDevelopment(running));
		Assert.Equal(!development, LauncherVersion.CanSelfUpdate(running));
	}
}

public class LauncherSelfUpdaterTests
{
	private const string BaseUrl = "https://updates.example.test/launcher/";

	private static ManifestLauncher Release(string version, string content) => new()
	{
		Version = version,
		Url = "files/launcher.exe",
		Sha256 = ManifestJson.Sha256(content),
		Size = Encoding.UTF8.GetByteCount(content)
	};

	[Fact]
	public void DevBuild_NeverUpdates_EvenWhenAReleaseIsNewer()
	{
		Assert.False(LauncherSelfUpdater.IsUpdateAvailable(Release("9.9.9", "x"), "0.0.0-dev"));
		Assert.True(LauncherSelfUpdater.IsUpdateAvailable(Release("1.3.0", "x"), "1.2.0"));
		Assert.False(LauncherSelfUpdater.IsUpdateAvailable(Release("1.2.0", "x"), "1.2.0"));
		Assert.False(LauncherSelfUpdater.IsUpdateAvailable(null, "1.2.0"));
	}

	[Fact]
	public async Task VerifiedDownload_ReplacesTheExe_AndKeepsTheOldOneAside()
	{
		using TempDir dir = new();
		dir.Write("Launcher.exe", "v1");
		FakeHttpHandler handler = new();
		handler.Serve(BaseUrl + "files/launcher.exe", "v2");

		bool applied = await LauncherSelfUpdater.ApplyAsync(new HttpClient(handler), new Uri(BaseUrl), Release("1.3.0", "v2"), dir.File("Launcher.exe"), CancellationToken.None);

		Assert.True(applied);
		Assert.Equal("v2", File.ReadAllText(dir.File("Launcher.exe")));
		Assert.False(File.Exists(dir.File("Launcher.exe.new")));
	}

	[Fact]
	public async Task DownloadWithTheWrongHash_KeepsTheRunningExe()
	{
		using TempDir dir = new();
		dir.Write("Launcher.exe", "v1");
		FakeHttpHandler handler = new();
		handler.Serve(BaseUrl + "files/launcher.exe", "tampered");

		bool applied = await LauncherSelfUpdater.ApplyAsync(new HttpClient(handler), new Uri(BaseUrl), Release("1.3.0", "v2"), dir.File("Launcher.exe"), CancellationToken.None);

		Assert.False(applied);
		Assert.Equal("v1", File.ReadAllText(dir.File("Launcher.exe")));
		Assert.False(File.Exists(dir.File("Launcher.exe.new")));
	}
}

public class RemoteUpdaterTests
{
	private static LauncherConfig Config(string? publicKey) => new()
	{
		ManifestUrl = ManifestJson.ManifestUrl,
		ManifestPublicKey = publicKey
	};

	private static byte[] PublishRelease(FakeHttpHandler handler, TestKey key, string launcherVersion)
	{
		const string baseUrl = "https://updates.example.test/launcher/";
		handler.Serve(baseUrl + "files/loc.dll", "loc v2");
		handler.Serve(baseUrl + "files/launcher.exe", "launcher v2");
		JsonArray files = [ManifestJson.FileEntry("client/Ro3Emu.Loc.dll", "loc v2", "files/loc.dll")];
		JsonObject launcher = new()
		{
			["version"] = launcherVersion,
			["url"] = "files/launcher.exe",
			["sha256"] = ManifestJson.Sha256("launcher v2"),
			["size"] = Encoding.UTF8.GetByteCount("launcher v2")
		};
		byte[] manifest = ManifestJson.Build(1, files, launcher: launcher);
		handler.Serve(ManifestJson.ManifestUrl, manifest);
		handler.Serve(ManifestJson.ManifestUrl + ".sig", key.Sign(manifest));
		return manifest;
	}

	[Fact]
	public async Task NewerLauncher_IsAppliedFirst_AndAsksForARestartBeforeTheFiles()
	{
		using TestKey key = new();
		using TempDir dir = new();
		using TempDir cache = new();
		dir.Write("Launcher.exe", "launcher v1");
		FakeHttpHandler handler = new();
		PublishRelease(handler, key, "1.3.0");
		RemoteUpdater updater = new(Config(key.PublicKeyPem), dir.File("Launcher.exe"), "1.2.0", handler, cache.Path);

		RemoteUpdateOutcome outcome = await updater.RunAsync(null, CancellationToken.None);

		Assert.True(outcome.RestartRequired);
		Assert.Equal("launcher v2", File.ReadAllText(dir.File("Launcher.exe")));
		Assert.False(File.Exists(dir.File("client/Ro3Emu.Loc.dll")));
	}

	[Fact]
	public async Task DevBuild_SkipsTheLauncher_ButAppliesTheFiles()
	{
		using TestKey key = new();
		using TempDir dir = new();
		using TempDir cache = new();
		dir.Write("Launcher.exe", "launcher v1");
		FakeHttpHandler handler = new();
		PublishRelease(handler, key, "9.9.9");
		RemoteUpdater updater = new(Config(key.PublicKeyPem), dir.File("Launcher.exe"), "0.0.0-dev", handler, cache.Path);

		RemoteUpdateOutcome outcome = await updater.RunAsync(null, CancellationToken.None);

		Assert.False(outcome.RestartRequired);
		Assert.Equal("launcher v1", File.ReadAllText(dir.File("Launcher.exe")));
		Assert.Equal("loc v2", File.ReadAllText(dir.File("client/Ro3Emu.Loc.dll")));
		Assert.Equal(ManifestSource.Remote, outcome.Source);
	}

	[Fact]
	public async Task WithoutAPublicKey_NothingIsFetched()
	{
		using TempDir dir = new();
		using TempDir cache = new();
		FakeHttpHandler handler = new();
		RemoteUpdater updater = new(Config(""), dir.File("Launcher.exe"), "1.2.0", handler, cache.Path);

		RemoteUpdateOutcome outcome = await updater.RunAsync(null, CancellationToken.None);

		Assert.Null(outcome.Manifest);
		Assert.NotNull(outcome.DisabledReason);
		Assert.Empty(handler.Requests);
	}

	[Fact]
	public async Task WithoutAManifestUrl_TheFeatureIsOff()
	{
		using TempDir dir = new();
		FakeHttpHandler handler = new();
		RemoteUpdater updater = new(new LauncherConfig(), dir.File("Launcher.exe"), "1.2.0", handler, dir.Path);

		RemoteUpdateOutcome outcome = await updater.RunAsync(null, CancellationToken.None);

		Assert.Null(outcome.Manifest);
		Assert.Empty(handler.Requests);
	}
}
