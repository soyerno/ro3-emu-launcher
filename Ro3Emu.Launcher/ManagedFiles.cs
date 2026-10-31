using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Ro3Emu.Launcher;

public sealed class FileApplyResult
{
	/// <summary>Files downloaded, verified and swapped in.</summary>
	public List<string> Updated { get; } = [];

	/// <summary>Files whose download or swap failed; the previous copy (if any) is kept.</summary>
	public List<string> Failed { get; } = [];

	/// <summary>Manifest paths outside <see cref="ManagedFiles.TryNormalizePath"/>'s allow-list; never downloaded.</summary>
	public List<string> Rejected { get; } = [];

	public int UpToDate { get; set; }

	/// <summary>The launcher folder cannot be written (for example under Program Files): nothing was changed.</summary>
	public bool NotWritable { get; set; }
}

/// <summary>
/// The server's files kept loose beside the launcher (RO3Emu.exe, cloudflared.exe, client\*.dll, data\*.tsv) and
/// updated from the manifest: each mismatched file is downloaded to "&lt;file&gt;.new", checked against the manifest's
/// size and SHA-256, then swapped in by renaming the current file to "&lt;file&gt;.old" (a running exe can be renamed).
/// A "*.old" still in use is deleted on the next start by <see cref="CleanLeftovers"/>.
/// </summary>
public static class ManagedFiles
{
	/// <summary>Largest single file accepted from a manifest.</summary>
	public const long MaxFileSize = 1L << 30;

	private static readonly string[] RootFiles = ["RO3Emu.exe", "cloudflared.exe"];

	private static readonly (string Directory, string Extension)[] Folders = [("client", ".dll"), ("data", ".tsv")];

	private static readonly Regex DeviceName = new Regex("^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	/// <summary>
	/// True when <paramref name="path"/> is one of the files the manifest may update: <c>RO3Emu.exe</c> or
	/// <c>cloudflared.exe</c> in the launcher folder, a <c>client/*.dll</c> or a <c>data/*.tsv</c> (one level, no
	/// subfolders). Rooted paths, drive letters, "..", "." and empty segments, streams and device names are refused.
	/// <paramref name="normalized"/> uses "/" separators.
	/// </summary>
	public static bool TryNormalizePath(string? path, out string normalized)
	{
		normalized = "";
		if (string.IsNullOrWhiteSpace(path))
		{
			return false;
		}
		string candidate = path.Replace('\\', '/');
		if (candidate.StartsWith('/') || candidate.Contains(':') || Path.IsPathRooted(candidate))
		{
			return false;
		}
		string[] segments = candidate.Split('/');
		foreach (string segment in segments)
		{
			if (!IsSafeName(segment))
			{
				return false;
			}
		}
		if (segments.Length == 1)
		{
			foreach (string root in RootFiles)
			{
				if (string.Equals(segments[0], root, StringComparison.OrdinalIgnoreCase))
				{
					normalized = segments[0];
					return true;
				}
			}
			return false;
		}
		if (segments.Length == 2)
		{
			foreach ((string directory, string extension) in Folders)
			{
				if (string.Equals(segments[0], directory, StringComparison.OrdinalIgnoreCase)
					&& segments[1].EndsWith(extension, StringComparison.OrdinalIgnoreCase)
					&& segments[1].Length > extension.Length)
				{
					normalized = directory + "/" + segments[1];
					return true;
				}
			}
		}
		return false;
	}

	private static bool IsSafeName(string name)
	{
		if (name.Length == 0 || name == "." || name == ".." || name.StartsWith('.') || name.EndsWith('.') || name.EndsWith(' '))
		{
			return false;
		}
		if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
		{
			return false;
		}
		return !DeviceName.IsMatch(Path.GetFileNameWithoutExtension(name));
	}

	/// <summary>Deletes the "*.old" and "*.new" files an earlier update left beside the managed files.</summary>
	public static void CleanLeftovers(string launcherDir)
	{
		List<string> candidates = [];
		foreach (string root in RootFiles)
		{
			candidates.Add(Path.Combine(launcherDir, root + ".old"));
			candidates.Add(Path.Combine(launcherDir, root + ".new"));
		}
		foreach ((string directory, string extension) in Folders)
		{
			string folder = Path.Combine(launcherDir, directory);
			if (!Directory.Exists(folder))
			{
				continue;
			}
			try
			{
				candidates.AddRange(Directory.GetFiles(folder, "*" + extension + ".old"));
				candidates.AddRange(Directory.GetFiles(folder, "*" + extension + ".new"));
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
			}
		}
		foreach (string file in candidates)
		{
			TryDelete(file);
		}
	}

	/// <summary>
	/// Brings every allowed file of <paramref name="files"/> in line with the manifest. <paramref name="progress"/> gets
	/// (n, total) before the n-th download of the files that need one.
	/// </summary>
	public static async Task<FileApplyResult> ApplyAsync(IReadOnlyList<ManifestFile> files, Uri baseUri, string launcherDir, HttpClient http,
		Action<int, int>? progress, Action<string>? log, CancellationToken ct)
	{
		FileApplyResult result = new FileApplyResult();
		string root = Path.GetFullPath(launcherDir);
		List<(ManifestFile Entry, string Relative, string Target, Uri Url)> pending = [];
		foreach (ManifestFile entry in files)
		{
			if (!TryNormalizePath(entry.Path, out string relative) || entry.Size > MaxFileSize
				|| !Uri.TryCreate(baseUri, entry.Url, out Uri? url) || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp))
			{
				log?.Invoke($"Update: refused manifest entry '{entry.Path}' (path not allowed, too large or bad url).");
				result.Rejected.Add(entry.Path);
				continue;
			}
			string target = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
			if (!target.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
			{
				result.Rejected.Add(entry.Path);
				continue;
			}
			if (Matches(target, entry.Size, entry.Sha256))
			{
				result.UpToDate++;
			}
			else
			{
				pending.Add((entry, relative, target, url));
			}
		}
		if (pending.Count == 0)
		{
			return result;
		}
		if (!IsWritable(root))
		{
			log?.Invoke($"Update: the launcher folder '{root}' is not writable; {pending.Count} file(s) stay outdated.");
			result.NotWritable = true;
			return result;
		}
		for (int i = 0; i < pending.Count; i++)
		{
			(ManifestFile entry, string relative, string target, Uri url) = pending[i];
			progress?.Invoke(i + 1, pending.Count);
			string staged = target + ".new";
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(target)!);
				if (!await DownloadVerifiedAsync(http, url, staged, entry.Size, entry.Sha256, ct))
				{
					log?.Invoke($"Update: {relative} from {url.AbsoluteUri} does not match the manifest size/SHA-256; kept the current file.");
					result.Failed.Add(relative);
					continue;
				}
				Swap(target, staged);
				result.Updated.Add(relative);
				log?.Invoke($"Update: {relative} updated.");
			}
			catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException
				|| (ex is OperationCanceledException && !ct.IsCancellationRequested))
			{
				TryDelete(staged);
				log?.Invoke($"Update: {relative} failed: {ex.Message}");
				result.Failed.Add(relative);
			}
		}
		return result;
	}

	/// <summary>
	/// Streams <paramref name="url"/> to <paramref name="destination"/> and keeps it only when its size and SHA-256 match;
	/// otherwise deletes it and returns false.
	/// </summary>
	public static async Task<bool> DownloadVerifiedAsync(HttpClient http, Uri url, string destination, long size, string sha256, CancellationToken ct)
	{
		bool ok = false;
		try
		{
			using HttpResponseMessage response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
			if (!response.IsSuccessStatusCode)
			{
				throw new HttpRequestException($"{url.AbsoluteUri} answered {(int)response.StatusCode}");
			}
			using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
			long total = 0;
			await using (Stream source = await response.Content.ReadAsStreamAsync(ct))
			await using (FileStream output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				byte[] buffer = new byte[81920];
				int read;
				while ((read = await source.ReadAsync(buffer, ct)) > 0)
				{
					total += read;
					if (total > size)
					{
						return false;
					}
					hash.AppendData(buffer, 0, read);
					await output.WriteAsync(buffer.AsMemory(0, read), ct);
				}
			}
			ok = total == size && string.Equals(Convert.ToHexString(hash.GetHashAndReset()), sha256, StringComparison.OrdinalIgnoreCase);
			return ok;
		}
		finally
		{
			if (!ok)
			{
				TryDelete(destination);
			}
		}
	}

	/// <summary>Moves <paramref name="staged"/> over <paramref name="target"/>, keeping the old copy as "*.old" until it can be deleted.</summary>
	public static void Swap(string target, string staged)
	{
		string old = target + ".old";
		if (File.Exists(old))
		{
			File.Delete(old);
		}
		bool hadTarget = File.Exists(target);
		if (hadTarget)
		{
			File.Move(target, old);
		}
		try
		{
			File.Move(staged, target);
		}
		catch
		{
			if (hadTarget)
			{
				File.Move(old, target);
			}
			throw;
		}
		if (hadTarget)
		{
			TryDelete(old);
		}
	}

	public static bool Matches(string path, long size, string sha256)
	{
		try
		{
			FileInfo info = new FileInfo(path);
			if (!info.Exists || info.Length != size)
			{
				return false;
			}
			using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			return string.Equals(Convert.ToHexString(SHA256.HashData(stream)), sha256, StringComparison.OrdinalIgnoreCase);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return false;
		}
	}

	private static bool IsWritable(string directory)
	{
		string probe = Path.Combine(directory, ".write-test-" + Guid.NewGuid().ToString("N"));
		try
		{
			File.WriteAllBytes(probe, []);
			File.Delete(probe);
			return true;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return false;
		}
	}

	private static void TryDelete(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
		}
	}
}
