using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ro3Emu.Launcher;

public enum ManifestSource
{
	/// <summary>No usable manifest: the launcher runs with its embedded and local launcher.json.</summary>
	None,
	/// <summary>Fetched now, signature verified, and cached.</summary>
	Remote,
	/// <summary>The last verified manifest, because the fetch failed or was refused.</summary>
	Cache
}

/// <param name="Error">Why the remote manifest was not used (offline, bad signature, rollback...), or null.</param>
public sealed record ManifestFetchResult(RemoteManifest? Manifest, ManifestSource Source, string? Error);

/// <summary>
/// The last verified manifest and its signature, kept in %LOCALAPPDATA%\RO3Emu\manifest\&lt;hash of the manifest url&gt;.
/// The signature is checked again on every read, so a file edited on disk is ignored.
/// </summary>
public sealed class ManifestCache(string directory)
{
	public string Directory { get; } = directory;

	public string ManifestPath => Path.Combine(Directory, "manifest.json");

	public string SignaturePath => Path.Combine(Directory, "manifest.json.sig");

	public static string DefaultDirectory(Uri manifestUrl)
	{
		string key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(manifestUrl.AbsoluteUri)))[..16];
		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RO3Emu", "manifest", key);
	}

	/// <summary>The cached manifest when its signature verifies with <paramref name="publicKeyPem"/>; otherwise null.</summary>
	public RemoteManifest? TryLoad(string? publicKeyPem)
	{
		try
		{
			if (!File.Exists(ManifestPath) || !File.Exists(SignaturePath))
			{
				return null;
			}
			byte[] json = File.ReadAllBytes(ManifestPath);
			string signature = File.ReadAllText(SignaturePath);
			return ManifestSignature.Verify(json, signature, publicKeyPem) ? RemoteManifest.Parse(json) : null;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ManifestException)
		{
			return null;
		}
	}

	public void Save(byte[] json, string signature)
	{
		System.IO.Directory.CreateDirectory(Directory);
		WriteReplacing(SignaturePath, Encoding.ASCII.GetBytes(signature.Trim()));
		WriteReplacing(ManifestPath, json);
	}

	private static void WriteReplacing(string path, byte[] data)
	{
		string temp = path + ".tmp";
		File.WriteAllBytes(temp, data);
		File.Move(temp, path, overwrite: true);
	}
}

/// <summary>
/// Fetches the manifest and its signature, verifies them against the pinned public key and refuses a rollback (a
/// lower sequence than the cached manifest). Any failure falls back to the cached manifest.
/// </summary>
public sealed class ManifestClient(HttpClient http, ManifestCache cache, string? publicKeyPem)
{
	public static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(5);

	public async Task<ManifestFetchResult> GetAsync(Uri manifestUrl, CancellationToken ct)
	{
		RemoteManifest? cached = cache.TryLoad(publicKeyPem);
		string error;
		try
		{
			using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
			timeout.CancelAfter(FetchTimeout);
			byte[] json = await GetBytesAsync(manifestUrl, timeout.Token);
			string signature = Encoding.ASCII.GetString(await GetBytesAsync(new Uri(manifestUrl.AbsoluteUri + ".sig"), timeout.Token));
			if (!ManifestSignature.Verify(json, signature, publicKeyPem))
			{
				error = "the manifest signature does not verify with the pinned public key";
			}
			else
			{
				RemoteManifest remote = RemoteManifest.Parse(json);
				if (cached != null && remote.Sequence < cached.Sequence)
				{
					error = $"the manifest sequence {remote.Sequence} is lower than the cached {cached.Sequence} (rollback refused)";
				}
				else
				{
					TrySave(json, signature);
					return new ManifestFetchResult(remote, ManifestSource.Remote, null);
				}
			}
		}
		catch (OperationCanceledException) when (!ct.IsCancellationRequested)
		{
			error = $"the manifest did not arrive within {FetchTimeout.TotalSeconds:0} s";
		}
		catch (Exception ex) when (ex is HttpRequestException or IOException or ManifestException)
		{
			error = ex.Message;
		}
		return cached != null
			? new ManifestFetchResult(cached, ManifestSource.Cache, error)
			: new ManifestFetchResult(null, ManifestSource.None, error);
	}

	private void TrySave(byte[] json, string signature)
	{
		try
		{
			cache.Save(json, signature);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
		}
	}

	private async Task<byte[]> GetBytesAsync(Uri url, CancellationToken ct)
	{
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
		request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
		using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
		if (!response.IsSuccessStatusCode)
		{
			throw new HttpRequestException($"{url.AbsoluteUri} answered {(int)response.StatusCode}");
		}
		if (response.Content.Headers.ContentLength > RemoteManifest.MaxBytes)
		{
			throw new IOException($"{url.AbsoluteUri} is too large to be a manifest");
		}
		await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
		using MemoryStream buffer = new MemoryStream();
		byte[] chunk = new byte[16384];
		int read;
		while ((read = await stream.ReadAsync(chunk, ct)) > 0)
		{
			buffer.Write(chunk, 0, read);
			if (buffer.Length > RemoteManifest.MaxBytes)
			{
				throw new IOException($"{url.AbsoluteUri} is too large to be a manifest");
			}
		}
		return buffer.ToArray();
	}
}
