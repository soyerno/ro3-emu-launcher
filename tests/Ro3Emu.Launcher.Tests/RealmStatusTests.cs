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

	/// <summary>
	/// Awaits the probe before the client is disposed: disposing it mid-request cancels the request, so a test could pass
	/// through that cancellation instead of the status, error or timeout it means to exercise. Also checks that the
	/// request reached the handler, so a result never comes from failing before the send.
	/// </summary>
	private static async Task<bool> Probe(Func<CancellationToken, Task<HttpResponseMessage>> respond, TimeSpan? timeout = null)
	{
		int calls = 0;
		using HttpClient http = new HttpClient(new FakeHandler(ct =>
		{
			Interlocked.Increment(ref calls);
			return respond(ct);
		})) { Timeout = timeout ?? TimeSpan.FromSeconds(5) };
		bool online = await RealmStatus.ProbeAsync(http, new Uri("https://konsor.example/status"), CancellationToken.None);
		Assert.Equal(1, calls);
		return online;
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
		System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
		Assert.False(await Probe(async ct =>
		{
			await Task.Delay(Timeout.Infinite, ct);
			return new HttpResponseMessage(HttpStatusCode.OK);
		}, TimeSpan.FromMilliseconds(100)));
		// Offline because the client timed out, not because something cancelled the request earlier.
		Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(90), $"Probe returned after {clock.Elapsed.TotalMilliseconds} ms, before the timeout.");
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

	private static readonly RealmSpec Watched = new() { Id = "payon", Name = "Payon", StatusUrl = "https://konsor.example/status" };

	[Fact]
	public void IsKnownOnline_IsFalseOnlyWhenTheLastProbeOfAWatchedRealmFailed()
	{
		Dictionary<string, bool> down = new(StringComparer.OrdinalIgnoreCase) { ["PAYON"] = false };
		Dictionary<string, bool> up = new(StringComparer.OrdinalIgnoreCase) { ["payon"] = true };

		Assert.False(RealmStatus.IsKnownOnline(Watched, down));
		Assert.True(RealmStatus.IsKnownOnline(Watched, up));
		// Not probed yet: unknown counts as online.
		Assert.True(RealmStatus.IsKnownOnline(Watched, new Dictionary<string, bool>()));
		// No status URL: always online, whatever a stale entry says.
		Assert.True(RealmStatus.IsKnownOnline(Payon, down));
	}

	[Fact]
	public void OfflineRefusal_RefusesAnOfflineRealmWithTheRealmOfflineMessage()
	{
		string? refusal = RealmStatus.OfflineRefusal(Payon, _ => false);

		Assert.Equal(Strings.Get("realm.offline", "Payon"), refusal);
		Assert.Contains("Payon", refusal);
	}

	[Fact]
	public void OfflineRefusal_AllowsAnOnlineOrUnknownRealm()
	{
		Assert.Null(RealmStatus.OfflineRefusal(Payon, _ => true));
		Assert.Null(RealmStatus.OfflineRefusal(Watched, r => RealmStatus.IsKnownOnline(r, new Dictionary<string, bool>())));
	}

	[Fact]
	public void OfflineRefusal_AsksAboutTheGivenRealm()
	{
		// Play asks about the selected realm, a switch about the target realm: only that one decides.
		Assert.NotNull(RealmStatus.OfflineRefusal(Geffen, r => r != Geffen));
		Assert.Null(RealmStatus.OfflineRefusal(Prontera, r => r != Geffen));
	}
}
