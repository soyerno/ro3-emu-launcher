using System.Net;
using Ro3Emu.Launcher;
using Xunit;

namespace Ro3Emu.Launcher.Tests;

public class RealmStatusTests
{
	private static RealmSpec ParseRealm(string statusUrlJson)
	{
		LauncherConfig config = LauncherConfig.Parse("{ \"realms\": [ { \"id\": \"a\", \"name\": \"A\", \"server\": \"http://127.0.0.1:1\"" + statusUrlJson + " } ] }");
		return config.SelectRealm("a");
	}

	[Theory]
	[InlineData("https://konsor.example/api/realms/payon/status", "https://konsor.example/api/realms/payon/status")]
	[InlineData(" https://konsor.example/status ", "https://konsor.example/status")]
	[InlineData("http://127.0.0.1:8080/status", "http://127.0.0.1:8080/status")]
	[InlineData("http://localhost/status", "http://localhost/status")]
	public void AValidStatusUrlIsParsed(string value, string expected)
	{
		RealmSpec realm = ParseRealm(", \"statusUrl\": \"" + value + "\"");

		Assert.Equal(expected, realm.StatusUri?.AbsoluteUri);
	}

	[Theory]
	[InlineData("http://konsor.example/status")]
	[InlineData("not a url")]
	[InlineData("ftp://konsor.example/status")]
	[InlineData("/api/realms/payon/status")]
	[InlineData("")]
	public void AnInvalidStatusUrlIsIgnored(string value)
	{
		Assert.Null(ParseRealm(", \"statusUrl\": \"" + value + "\"").StatusUri);
	}

	[Fact]
	public void ARealmWithoutStatusUrlHasNoStatusUri()
	{
		Assert.Null(ParseRealm("").StatusUri);
	}

	[Theory]
	[InlineData(200, true)]
	[InlineData(204, false)]
	[InlineData(404, false)]
	[InlineData(502, false)]
	[InlineData(530, false)]
	[InlineData(null, false)]
	public void OnlyA200MeansOnline(int? statusCode, bool expected)
	{
		Assert.Equal(expected, RealmStatus.IsOnline(statusCode));
	}

	private sealed class FakeHandler(Func<CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(cancellationToken);
	}

	private static Task<bool> Probe(Func<CancellationToken, Task<HttpResponseMessage>> respond, TimeSpan? timeout = null)
	{
		using HttpClient http = new HttpClient(new FakeHandler(respond)) { Timeout = timeout ?? TimeSpan.FromSeconds(5) };
		return RealmStatus.ProbeAsync(http, new Uri("https://konsor.example/status"), CancellationToken.None);
	}

	[Fact]
	public async Task ProbeIsOnlineOnA200()
	{
		Assert.True(await Probe(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));
	}

	[Theory]
	[InlineData(404)]
	[InlineData(502)]
	[InlineData(530)]
	public async Task ProbeIsOfflineOnAnyOtherStatus(int code)
	{
		Assert.False(await Probe(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)code))));
	}

	[Fact]
	public async Task ProbeIsOfflineOnANetworkError()
	{
		Assert.False(await Probe(_ => throw new HttpRequestException("no route")));
	}

	[Fact]
	public async Task ProbeIsOfflineOnATimeout()
	{
		Assert.False(await Probe(async ct =>
		{
			await Task.Delay(Timeout.Infinite, ct);
			return new HttpResponseMessage(HttpStatusCode.OK);
		}, TimeSpan.FromMilliseconds(100)));
	}

	private static readonly RealmSpec Prontera = new() { Id = "prontera", Name = "Prontera" };
	private static readonly RealmSpec Payon = new() { Id = "payon", Name = "Payon" };
	private static readonly RealmSpec Geffen = new() { Id = "geffen", Name = "Geffen" };
	private static readonly RealmSpec[] All = [Prontera, Payon, Geffen];

	[Fact]
	public void PickPlayable_KeepsAnOnlineSelection()
	{
		Assert.Same(Payon, RealmStatus.PickPlayable(All, Payon, _ => true));
	}

	[Fact]
	public void PickPlayable_MovesAnOfflineSelectionToTheFirstOnlineRealm()
	{
		Assert.Same(Geffen, RealmStatus.PickPlayable(All, Prontera, r => r == Geffen));
		Assert.Same(Prontera, RealmStatus.PickPlayable(All, Payon, r => r != Payon));
	}

	[Fact]
	public void PickPlayable_KeepsTheSelectionWhenEveryRealmIsOffline()
	{
		Assert.Same(Payon, RealmStatus.PickPlayable(All, Payon, _ => false));
	}
}
