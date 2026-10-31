using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Ro3Emu.Launcher;

/// <summary>
/// Replaces the running launcher with the manifest's release, the same way Ro3Emu.Patcher's <c>SelfUpdate</c> does
/// (the running exe is renamed to "*.old", the verified download takes its name, <c>SelfUpdate.DeleteLeftovers</c>
/// removes "*.old" on the next start and <c>SelfUpdate.Restart</c> starts it). <c>SelfUpdate.ApplyAsync</c> itself
/// is not reused because it only downloads paths relative to the patch repository, does not check the size and
/// compares versions with <see cref="Version"/>, which cannot tell a development build (<c>0.0.0-dev</c>).
/// </summary>
public static class LauncherSelfUpdater
{
	/// <summary>True when <paramref name="release"/> is newer than a published (non-development) running launcher.</summary>
	public static bool IsUpdateAvailable(ManifestLauncher? release, string runningVersion)
		=> release != null && LauncherVersion.CanSelfUpdate(runningVersion) && LauncherVersion.IsNewer(release.Version, runningVersion);

	/// <summary>Downloads, verifies and swaps in the release; true when the caller must restart into it.</summary>
	public static async Task<bool> ApplyAsync(HttpClient http, Uri baseUri, ManifestLauncher release, string exePath, CancellationToken ct)
	{
		if (!Uri.TryCreate(baseUri, release.Url, out Uri? url) || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp)
			|| release.Size <= 0 || release.Size > ManagedFiles.MaxFileSize)
		{
			return false;
		}
		string staged = exePath + ".new";
		try
		{
			if (!await ManagedFiles.DownloadVerifiedAsync(http, url, staged, release.Size, release.Sha256, ct))
			{
				return false; // a broken release must never replace a working launcher
			}
			ManagedFiles.Swap(exePath, staged);
			return true;
		}
		catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException
			|| (ex is OperationCanceledException && !ct.IsCancellationRequested))
		{
			try
			{
				File.Delete(staged);
			}
			catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
			{
			}
			return false;
		}
	}
}
