using Ro3Emu.Launcher;
using Xunit;

namespace Ro3Emu.Launcher.Tests;

public class RealmTests
{
	private const string SingleServerJson = """
	{
	  "server": "http://127.0.0.1:5380",
	  "serverName": "Solo",
	  "tunnels": [ { "hostname": "a.example.com", "port": 5380 } ]
	}
	""";

	private const string TwoRealmsJson = """
	{
	  "server": "http://127.0.0.1:5380",
	  "serverName": "Two",
	  "defaultRealm": "prontera",
	  "realms": [
	    { "id": "prontera", "name": "Prontera (5x)", "server": "http://127.0.0.1:5380",
	      "tunnels": [ { "hostname": "p-http.example.com", "port": 5380 }, { "hostname": "p-game.example.com", "port": 33100 } ] },
	    { "id": "payon", "name": "Payon (1x)", "server": "http://127.0.0.1:5390/",
	      "tunnels": [ { "hostname": "q-http.example.com", "port": 5390 }, { "hostname": "q-game.example.com", "port": 33102 } ] }
	  ]
	}
	""";

	[Fact]
	public void WithoutRealms_TheSingleServerIsOneImplicitRealm()
	{
		LauncherConfig config = LauncherConfig.Parse(SingleServerJson);

		RealmSpec realm = Assert.Single(config.GetRealms());
		Assert.Equal(RealmSpec.ImplicitId, realm.Id);
		Assert.Equal("Solo", realm.Name);
		Assert.Equal("http://127.0.0.1:5380", realm.ServerBaseUrl);
		Assert.Equal(new[] { 5380 }, realm.Tunnels.Select(t => t.Port));
		Assert.Same(realm, config.SelectRealm("payon"));
	}

	[Fact]
	public void WithRealms_EachRealmHasItsOwnServerAndTunnels()
	{
		LauncherConfig config = LauncherConfig.Parse(TwoRealmsJson);

		IReadOnlyList<RealmSpec> realms = config.GetRealms();
		Assert.Equal(new[] { "prontera", "payon" }, realms.Select(r => r.Id));
		Assert.Equal("http://127.0.0.1:5380", realms[0].ServerBaseUrl);
		Assert.Equal("http://127.0.0.1:5390", realms[1].ServerBaseUrl);
		Assert.Equal(new[] { 5380, 33100 }, realms[0].Tunnels.Select(t => t.Port));
		Assert.Equal(new[] { 5390, 33102 }, realms[1].Tunnels.Select(t => t.Port));
	}

	[Theory]
	[InlineData("payon", "payon")]
	[InlineData("PAYON", "payon")]
	[InlineData("prontera", "prontera")]
	[InlineData("geffen", "prontera")]
	[InlineData(null, "prontera")]
	[InlineData("", "prontera")]
	public void SelectRealm_FallsBackToTheDefaultForAnUnknownStoredId(string? stored, string expected)
	{
		Assert.Equal(expected, LauncherConfig.Parse(TwoRealmsJson).SelectRealm(stored).Id);
	}

	[Fact]
	public void SelectRealm_UsesTheFirstRealmWhenTheDefaultIsUnknown()
	{
		LauncherConfig config = LauncherConfig.Parse(TwoRealmsJson.Replace("\"defaultRealm\": \"prontera\"", "\"defaultRealm\": \"nowhere\""));

		Assert.Equal("prontera", config.SelectRealm("geffen").Id);
	}

	[Fact]
	public void RealmsWithoutAnIdAreIgnored()
	{
		LauncherConfig config = LauncherConfig.Parse("""{ "serverName": "X", "server": "http://127.0.0.1:1", "realms": [ { "name": "no id" } ] }""");

		Assert.Equal(RealmSpec.ImplicitId, Assert.Single(config.GetRealms()).Id);
	}

	[Fact]
	public void AnInvalidRealmServerReportsAnError()
	{
		LauncherConfig config = LauncherConfig.Parse("""{ "realms": [ { "id": "a", "name": "A", "server": "not a url" } ] }""");

		RealmSpec realm = config.SelectRealm(null);
		Assert.Null(realm.ServerBaseUrl);
		Assert.NotNull(realm.ServerError);
	}

	[Fact]
	public void TheFileBesideTheExeOverlaysTheEmbeddedRealmsFieldByField()
	{
		string path = Path.Combine(Path.GetTempPath(), "ro3-realm-test-" + Guid.NewGuid().ToString("N") + ".json");
		File.WriteAllText(path, """{ "defaultRealm": "payon" }""");
		try
		{
			LauncherConfig config = LauncherConfig.Load(path);

			Assert.Equal("payon", config.DefaultRealm);
			Assert.Equal("payon", config.SelectRealm(null).Id);
			Assert.Equal(2, config.GetRealms().Count);
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact]
	public void AFileBesideTheExeWithServerAndTunnelsButNoRealms_ReplacesTheEmbeddedRealms()
	{
		string dir = Path.Combine(Path.GetTempPath(), "ro3-realm-test-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		string path = Path.Combine(dir, "launcher.json");
		File.WriteAllText(path, """{ "server": "http://127.0.0.1:6000", "tunnels": [ { "hostname": "local.example.test", "port": 6000 } ] }""");
		try
		{
			LauncherConfig config = LauncherConfig.Load(path);

			RealmSpec realm = Assert.Single(config.GetRealms());
			Assert.Equal(RealmSpec.ImplicitId, realm.Id);
			Assert.Equal("http://127.0.0.1:6000", realm.ServerBaseUrl);
			Assert.Equal(("local.example.test", 6000), (realm.Tunnels.Single().Hostname, realm.Tunnels.Single().Port));
			Assert.Same(realm, config.SelectRealm(null));
			Assert.Contains("realms", File.ReadAllText(Path.Combine(dir, "launcher.log")));
		}
		finally
		{
			Directory.Delete(dir, true);
		}
	}

	[Fact]
	public void AFileBesideTheExeWithItsOwnRealms_ReplacesTheEmbeddedRealms()
	{
		string path = Path.Combine(Path.GetTempPath(), "ro3-realm-test-" + Guid.NewGuid().ToString("N") + ".json");
		File.WriteAllText(path, """{ "server": "http://127.0.0.1:6000", "realms": [ { "id": "local", "server": "http://127.0.0.1:6100" } ] }""");
		try
		{
			Assert.Equal("http://127.0.0.1:6100", Assert.Single(LauncherConfig.Load(path).GetRealms()).ServerBaseUrl);
		}
		finally
		{
			File.Delete(path);
		}
	}

	// The embedded brand/launcher.json must match the realm table of konsor.ps1.
	[Fact]
	public void TheEmbeddedConfigHasTheKonsorRealms()
	{
		LauncherConfig config = LauncherConfig.Load(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid().ToString("N") + ".json"));

		Assert.Equal("prontera", config.SelectRealm(null).Id);
		RealmSpec prontera = config.SelectRealm("prontera");
		RealmSpec payon = config.SelectRealm("payon");
		Assert.Equal("Prontera (5x)", prontera.Name);
		Assert.Equal("Payon (1x)", payon.Name);
		Assert.Equal("http://127.0.0.1:5380", prontera.ServerBaseUrl);
		Assert.Equal("http://127.0.0.1:5390", payon.ServerBaseUrl);
		Assert.Equal(
			new[] { ("ro3-http.konsor.com.ar", 5380), ("ro3-micro.konsor.com.ar", 20000), ("ro3-game.konsor.com.ar", 33100) },
			prontera.Tunnels.Select(t => (t.Hostname, t.Port)));
		Assert.Equal(
			new[] { ("ro3p-http.konsor.com.ar", 5390), ("ro3p-micro.konsor.com.ar", 20001), ("ro3p-game.konsor.com.ar", 33102) },
			payon.Tunnels.Select(t => (t.Hostname, t.Port)));
	}
}
