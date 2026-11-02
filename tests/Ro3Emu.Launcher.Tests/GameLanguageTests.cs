using Ro3Emu.Launcher;
using Xunit;

namespace Ro3Emu.Launcher.Tests;

/// <summary>
/// The files the launcher stages around RO3Emu.exe join for the player's game language. Ro3Loc (Ro3Emu.Loc.dll) reads
/// ro3emu\language.txt: Spanish uses loc_en.tsv and en_overrides.tsv; Brazilian Portuguese ("pt-br") uses the
/// loc_pt-br.tsv and loc_sid_pt-br.tsv that join writes, plus en_overrides_pt-br.tsv.
/// </summary>
public class GameLanguageTests
{
	private sealed class Layout : IDisposable
	{
		private readonly TempDir _dir = new();

		public string GameDir => _dir.File("game");
		public string DataDir => _dir.File("launcher/data");
		public string Ro3emu => _dir.File("game/ro3emu");

		public void Data(string name, string content) => _dir.Write("launcher/data/" + name, content);

		public void Client(string name, string content) => _dir.Write("game/ro3emu/" + name, content);

		public string ReadData(string name) => File.ReadAllText(Path.Combine(DataDir, name));

		public string ReadClient(string name) => File.ReadAllText(Path.Combine(Ro3emu, name));

		public bool ClientHas(string name) => File.Exists(Path.Combine(Ro3emu, name));

		public string PathOf(string relative) => _dir.File(relative);

		public void Stage(string lang, List<string>? log = null)
		{
			Directory.CreateDirectory(GameDir);
			Directory.CreateDirectory(DataDir);
			GameOrchestrator.StageLanguageFiles(GameDir, DataDir, Ro3emu, lang, log == null ? null : log.Add);
		}

		public void Dispose() => _dir.Dispose();
	}

	[Theory]
	[InlineData("es", "es")]
	[InlineData("es-AR", "es")]
	[InlineData("pt-br", "pt-br")]
	[InlineData("PT-BR", "pt-br")]
	[InlineData("pt", "pt-br")]
	[InlineData("en", "en")]
	[InlineData("fr", "en")]
	[InlineData(null, "en")]
	public void Game_language_keeps_portuguese_apart_from_host_messages(string? picked, string expected)
	{
		Assert.Equal(expected, Strings.NormalizeGame(picked));
	}

	[Fact]
	public void Host_messages_stay_english_for_portuguese()
	{
		Assert.Equal("en", Strings.Normalize("pt-br"));
		Assert.Equal("es", Strings.Normalize("es"));
	}

	[Fact]
	public void Spanish_writes_language_txt_and_stages_spanish_maps()
	{
		using var l = new Layout();
		l.Data("loc_es.tsv", "zh\tes");
		l.Data("en_overrides_es.tsv", "Hello\tHola");

		l.Stage("es");

		Assert.Equal("es", l.ReadClient("language.txt"));
		Assert.Equal("es", File.ReadAllText(Path.Combine(l.GameDir, "language.txt")));
		Assert.Equal("zh\tes", l.ReadClient("loc_en.tsv"));
		Assert.Equal("zh\tes", l.ReadData("loc_en.tsv"));
		Assert.Equal("Hello\tHola", l.ReadClient("en_overrides.tsv"));
		Assert.Equal("Hello\tHola", l.ReadData("en_overrides.tsv"));
	}

	[Fact]
	public void Spanish_without_loc_es_keeps_loc_en_and_still_stages_overrides()
	{
		// Konsor's brand/patch/data ships loc_en.tsv and en_overrides.tsv with no _es copies.
		using var l = new Layout();
		l.Data("loc_en.tsv", "zh	en");
		l.Data("en_overrides_es.tsv", "Hello	Hola");
		var log = new List<string>();

		l.Stage("es", log);

		Assert.Equal("zh	en", l.ReadData("loc_en.tsv"));
		Assert.Equal("zh	en", l.ReadClient("loc_en.tsv"));
		Assert.Equal("Hello	Hola", l.ReadClient("en_overrides.tsv"));
		Assert.Equal("Hello	Hola", l.ReadData("en_overrides.tsv"));
		Assert.Contains(log, m => m.Contains("loc_es.tsv"));
	}

	[Fact]
	public void Spanish_without_overrides_es_keeps_en_overrides()
	{
		using var l = new Layout();
		l.Data("loc_es.tsv", "zh	es");
		l.Data("en_overrides.tsv", "Hello	Hola");
		var log = new List<string>();

		l.Stage("es", log);

		Assert.Equal("Hello	Hola", l.ReadData("en_overrides.tsv"));
		Assert.Equal("Hello	Hola", l.ReadClient("en_overrides.tsv"));
		Assert.Equal("zh	es", l.ReadClient("loc_en.tsv"));
		Assert.Contains(log, m => m.Contains("en_overrides_es.tsv"));
	}

	[Fact]
	public void Spanish_without_loc_es_still_sets_the_client_language_and_installs_the_loc_dll()
	{
		using var l = new Layout();
		l.Data("loc_en.tsv", "zh	en");
		l.Data("en_overrides.tsv", "Hello	Hola");
		string clientDll = l.PathOf("launcher/client/Ro3Emu.Loc.dll");
		string managedDll = l.PathOf("game/ro3_Data/Managed/Ro3Emu.Loc.dll");
		Directory.CreateDirectory(Path.GetDirectoryName(clientDll)!);
		Directory.CreateDirectory(Path.GetDirectoryName(managedDll)!);
		File.WriteAllText(clientDll, "loc dll");
		Directory.CreateDirectory(l.GameDir);
		Directory.CreateDirectory(l.DataDir);
		string? clientLanguage = null;
		var log = new List<string>();

		GameOrchestrator.PrepareGameLanguage(l.GameDir, l.DataDir, l.Ro3emu, "es", clientDll, managedDll, log.Add,
			language => clientLanguage = language);

		Assert.Equal("es", l.ReadClient("language.txt"));
		Assert.Equal("english", clientLanguage);
		Assert.Equal("loc dll", File.ReadAllText(managedDll));
		Assert.Equal("zh	en", l.ReadClient("loc_en.tsv"));
		Assert.Equal("Hello	Hola", l.ReadClient("en_overrides.tsv"));
		Assert.DoesNotContain(log, m => m.StartsWith("Could not"));
	}

	[Fact]
	public void Portuguese_writes_pt_br_to_language_txt()
	{
		using var l = new Layout();

		l.Stage("pt-br");

		Assert.Equal("pt-br", l.ReadClient("language.txt"));
		Assert.Equal("pt-br", File.ReadAllText(Path.Combine(l.GameDir, "language.txt")));
	}

	[Fact]
	public void Portuguese_does_not_stage_english_maps()
	{
		using var l = new Layout();
		l.Data("loc_en.tsv", "# generated by RO3Emu\nzh\tes");
		l.Data("loc_en_us.tsv", "zh\ten");
		l.Data("en_overrides_en.tsv", "Hello\tHello");
		l.Client("loc_pt-br.tsv", "zh\tpt");
		l.Client("loc_sid_pt-br.tsv", "1\tpt");

		l.Stage("pt-br");

		// A hand-made ro3emu\loc_en.tsv would stop join from refreshing it, and Ro3Loc never reads it in Portuguese.
		Assert.False(l.ClientHas("loc_en.tsv"));
		Assert.False(l.ClientHas("en_overrides.tsv"));
		Assert.Equal("# generated by RO3Emu\nzh\tes", l.ReadData("loc_en.tsv"));
		Assert.Equal("zh\tpt", l.ReadClient("loc_pt-br.tsv"));
		Assert.Equal("1\tpt", l.ReadClient("loc_sid_pt-br.tsv"));
	}

	[Fact]
	public void Portuguese_stages_the_published_portuguese_overrides()
	{
		using var l = new Layout();
		l.Data("en_overrides_pt-br.tsv", "Hello\tOlá");

		l.Stage("pt-br");

		Assert.Equal("Hello\tOlá", l.ReadClient("en_overrides_pt-br.tsv"));
	}

	[Fact]
	public void After_join_spanish_overrides_are_put_back()
	{
		using var l = new Layout();
		l.Data("en_overrides_es.tsv", "Hello\tHola");
		l.Client("en_overrides.tsv", "stale");

		GameOrchestrator.RestageOverridesAfterJoin(l.DataDir, l.Ro3emu, "es");

		Assert.Equal("Hello\tHola", l.ReadClient("en_overrides.tsv"));
	}

	[Fact]
	public void After_join_portuguese_overrides_are_put_back()
	{
		using var l = new Layout();
		l.Data("en_overrides_pt-br.tsv", "Hello\tOlá");
		l.Client("en_overrides_pt-br.tsv", "stale");

		GameOrchestrator.RestageOverridesAfterJoin(l.DataDir, l.Ro3emu, "pt-br");

		Assert.Equal("Hello\tOlá", l.ReadClient("en_overrides_pt-br.tsv"));
	}
}
