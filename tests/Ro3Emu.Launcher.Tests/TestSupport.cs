using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Ro3Emu.Launcher.Tests;

/// <summary>A throwaway P-256 key pair that signs manifests the way the release tool does.</summary>
internal sealed class TestKey : IDisposable
{
	private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

	public string PublicKeyPem => _key.ExportSubjectPublicKeyInfoPem();

	public string Sign(byte[] data) => Convert.ToBase64String(_key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));

	public void Dispose() => _key.Dispose();
}

/// <summary>Serves fixed bytes per absolute URL; any other URL answers 404. Can simulate a network failure.</summary>
internal sealed class FakeHttpHandler : HttpMessageHandler
{
	private readonly Dictionary<string, byte[]> _responses = new(StringComparer.Ordinal);

	public List<string> Requests { get; } = [];

	public bool Offline { get; set; }

	public void Serve(string url, byte[] body) => _responses[url] = body;

	public void Serve(string url, string body) => Serve(url, Encoding.UTF8.GetBytes(body));

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		string url = request.RequestUri!.AbsoluteUri;
		lock (Requests)
		{
			Requests.Add(url);
		}
		if (Offline)
		{
			throw new HttpRequestException("offline (test)");
		}
		return Task.FromResult(_responses.TryGetValue(url, out byte[]? body)
			? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
			: new HttpResponseMessage(HttpStatusCode.NotFound));
	}
}

/// <summary>A temporary directory deleted when the test ends.</summary>
internal sealed class TempDir : IDisposable
{
	public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ro3-launcher-tests", Guid.NewGuid().ToString("N"));

	public TempDir() => Directory.CreateDirectory(Path);

	public string File(string relative) => System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));

	public void Write(string relative, string content)
	{
		string full = File(relative);
		Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
		System.IO.File.WriteAllText(full, content);
	}

	public void Dispose()
	{
		try
		{
			Directory.Delete(Path, recursive: true);
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}
}

internal static class ManifestJson
{
	public const string ManifestUrl = "https://updates.example.test/launcher/manifest.json";

	public static string Sha256(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

	public static string Sha256(string text) => Sha256(Encoding.UTF8.GetBytes(text));

	public static JsonObject FileEntry(string path, string content, string url) => new()
	{
		["path"] = path,
		["size"] = Encoding.UTF8.GetByteCount(content),
		["sha256"] = Sha256(content),
		["url"] = url
	};

	public static byte[] Build(long sequence, JsonArray? files = null, JsonObject? config = null, JsonObject? launcher = null, int schema = 1)
	{
		JsonObject root = new()
		{
			["schema"] = schema,
			["sequence"] = sequence,
			["files"] = files ?? new JsonArray()
		};
		if (launcher != null)
		{
			root["launcher"] = launcher;
		}
		if (config != null)
		{
			root["config"] = config;
		}
		return Encoding.UTF8.GetBytes(root.ToJsonString());
	}
}
