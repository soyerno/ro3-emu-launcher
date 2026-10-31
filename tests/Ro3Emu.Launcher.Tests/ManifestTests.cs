using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Ro3Emu.Launcher.Tests;

public class ManifestSignatureTests
{
	[Fact]
	public void ValidSignature_IsAccepted()
	{
		using TestKey key = new();
		byte[] manifest = ManifestJson.Build(1);

		Assert.True(ManifestSignature.Verify(manifest, key.Sign(manifest), key.PublicKeyPem));
	}

	[Fact]
	public void OneTamperedByte_IsRejected()
	{
		using TestKey key = new();
		byte[] manifest = ManifestJson.Build(1);
		string signature = key.Sign(manifest);
		manifest[manifest.Length / 2] ^= 0x01;

		Assert.False(ManifestSignature.Verify(manifest, signature, key.PublicKeyPem));
	}

	[Fact]
	public void SignatureOfAnotherKey_IsRejected()
	{
		using TestKey key = new();
		using TestKey other = new();
		byte[] manifest = ManifestJson.Build(1);

		Assert.False(ManifestSignature.Verify(manifest, other.Sign(manifest), key.PublicKeyPem));
	}

	[Theory]
	[InlineData("")]
	[InlineData("not base64 at all")]
	[InlineData("AAAA")]
	public void MalformedSignature_IsRejected(string signature)
	{
		using TestKey key = new();

		Assert.False(ManifestSignature.Verify(ManifestJson.Build(1), signature, key.PublicKeyPem));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("-----BEGIN PUBLIC KEY-----\nnot a key\n-----END PUBLIC KEY-----")]
	public void MissingOrBrokenPublicKey_RejectsEverything(string? publicKey)
	{
		using TestKey key = new();
		byte[] manifest = ManifestJson.Build(1);

		Assert.False(ManifestSignature.Verify(manifest, key.Sign(manifest), publicKey));
	}

	[Fact]
	public void KeyIsConfigured_OnlyForAPublicKeyPem()
	{
		using TestKey key = new();

		Assert.True(ManifestSignature.IsKeyConfigured(key.PublicKeyPem));
		Assert.False(ManifestSignature.IsKeyConfigured(""));
		Assert.False(ManifestSignature.IsKeyConfigured(null));
	}
}

public class RemoteManifestParseTests
{
	[Fact]
	public void UnknownSchema_IsRejected()
	{
		Assert.Throws<ManifestException>(() => RemoteManifest.Parse(ManifestJson.Build(1, schema: 2)));
	}

	[Fact]
	public void NotJson_IsRejected()
	{
		Assert.Throws<ManifestException>(() => RemoteManifest.Parse(Encoding.UTF8.GetBytes("<html>")));
	}

	[Fact]
	public void FileWithABadHash_IsRejected()
	{
		JsonArray files = [new JsonObject { ["path"] = "RO3Emu.exe", ["size"] = 1, ["sha256"] = "xyz", ["url"] = "a" }];

		Assert.Throws<ManifestException>(() => RemoteManifest.Parse(ManifestJson.Build(1, files)));
	}

	[Fact]
	public void ValidManifest_KeepsSequenceFilesLauncherAndConfig()
	{
		JsonArray files = [ManifestJson.FileEntry("client/Ro3Emu.Loc.dll", "loc", "files/loc.dll")];
		JsonObject launcher = new() { ["version"] = "1.2.0", ["url"] = "files/l.exe", ["sha256"] = ManifestJson.Sha256("exe"), ["size"] = 3 };
		JsonObject config = new() { ["defaultRealm"] = "payon" };

		RemoteManifest manifest = RemoteManifest.Parse(ManifestJson.Build(7, files, config, launcher));

		Assert.Equal(7, manifest.Sequence);
		Assert.Equal("client/Ro3Emu.Loc.dll", Assert.Single(manifest.Files).Path);
		Assert.Equal("1.2.0", manifest.Launcher!.Version);
		Assert.Equal("payon", (string?)manifest.Config!["defaultRealm"]);
	}
}

public class ManifestClientTests
{
	private static readonly Uri Url = new(ManifestJson.ManifestUrl);

	private static ManifestClient Client(FakeHttpHandler handler, TempDir cache, TestKey key)
		=> new(new HttpClient(handler), new ManifestCache(cache.Path), key.PublicKeyPem);

	private static void Publish(FakeHttpHandler handler, TestKey key, byte[] manifest)
	{
		handler.Serve(ManifestJson.ManifestUrl, manifest);
		handler.Serve(ManifestJson.ManifestUrl + ".sig", key.Sign(manifest));
	}

	[Fact]
	public async Task ValidRemoteManifest_IsUsedAndCached()
	{
		using TestKey key = new();
		using TempDir cache = new();
		FakeHttpHandler handler = new();
		Publish(handler, key, ManifestJson.Build(3));

		ManifestFetchResult result = await Client(handler, cache, key).GetAsync(Url, CancellationToken.None);

		Assert.Equal(ManifestSource.Remote, result.Source);
		Assert.Equal(3, result.Manifest!.Sequence);
		Assert.Equal(3, new ManifestCache(cache.Path).TryLoad(key.PublicKeyPem)!.Sequence);
	}

	[Fact]
	public async Task OlderSequence_IsRejectedAsRollback_AndTheCacheIsKept()
	{
		using TestKey key = new();
		using TempDir cache = new();
		FakeHttpHandler handler = new();
		Publish(handler, key, ManifestJson.Build(5));
		await Client(handler, cache, key).GetAsync(Url, CancellationToken.None);
		Publish(handler, key, ManifestJson.Build(4));

		ManifestFetchResult result = await Client(handler, cache, key).GetAsync(Url, CancellationToken.None);

		Assert.Equal(ManifestSource.Cache, result.Source);
		Assert.Equal(5, result.Manifest!.Sequence);
		Assert.Contains("sequence", result.Error);
		Assert.Equal(5, new ManifestCache(cache.Path).TryLoad(key.PublicKeyPem)!.Sequence);
	}

	[Fact]
	public async Task NetworkFailure_FallsBackToTheCachedManifest()
	{
		using TestKey key = new();
		using TempDir cache = new();
		FakeHttpHandler handler = new();
		Publish(handler, key, ManifestJson.Build(9));
		await Client(handler, cache, key).GetAsync(Url, CancellationToken.None);
		handler.Offline = true;

		ManifestFetchResult result = await Client(handler, cache, key).GetAsync(Url, CancellationToken.None);

		Assert.Equal(ManifestSource.Cache, result.Source);
		Assert.Equal(9, result.Manifest!.Sequence);
	}

	[Fact]
	public async Task NetworkFailure_WithoutCache_GivesNoManifest()
	{
		using TestKey key = new();
		using TempDir cache = new();
		FakeHttpHandler handler = new() { Offline = true };

		ManifestFetchResult result = await Client(handler, cache, key).GetAsync(Url, CancellationToken.None);

		Assert.Equal(ManifestSource.None, result.Source);
		Assert.Null(result.Manifest);
		Assert.NotNull(result.Error);
	}

	[Fact]
	public async Task BadRemoteSignature_FallsBackToTheCache_AndIsNotCached()
	{
		using TestKey key = new();
		using TestKey attacker = new();
		using TempDir cache = new();
		FakeHttpHandler handler = new();
		Publish(handler, key, ManifestJson.Build(2));
		await Client(handler, cache, key).GetAsync(Url, CancellationToken.None);
		Publish(handler, attacker, ManifestJson.Build(99));

		ManifestFetchResult result = await Client(handler, cache, key).GetAsync(Url, CancellationToken.None);

		Assert.Equal(ManifestSource.Cache, result.Source);
		Assert.Equal(2, result.Manifest!.Sequence);
		Assert.Equal(2, new ManifestCache(cache.Path).TryLoad(key.PublicKeyPem)!.Sequence);
	}

	[Fact]
	public void TamperedCacheOnDisk_IsIgnored()
	{
		using TestKey key = new();
		using TempDir dir = new();
		ManifestCache cache = new(dir.Path);
		byte[] manifest = ManifestJson.Build(4);
		cache.Save(manifest, key.Sign(manifest));
		byte[] tampered = ManifestJson.Build(40);
		File.WriteAllBytes(cache.ManifestPath, tampered);

		Assert.Null(cache.TryLoad(key.PublicKeyPem));
	}
}
