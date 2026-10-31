using System;

namespace Ro3Emu.Launcher;

/// <summary>
/// Launcher versions as stamped at publish (<c>dotnet publish -p:Version=1.2.0</c>, see <see cref="BuildInfo"/>):
/// <c>major.minor[.patch][-prerelease][+build]</c>, an optional leading "v" allowed. A version with a prerelease
/// suffix (the default <c>0.0.0-dev</c> of a local build, or <c>1.2.0-rc.1</c>), <c>0.0.0</c> or an unparsable one is
/// a development build: it never replaces itself with a published launcher.
/// </summary>
public static class LauncherVersion
{
	public static bool TryParse(string? text, out Version core, out string? prerelease)
	{
		core = new Version(0, 0, 0);
		prerelease = null;
		string value = (text ?? "").Trim();
		if (value.StartsWith('v') || value.StartsWith('V'))
		{
			value = value[1..];
		}
		int plus = value.IndexOf('+');
		if (plus >= 0)
		{
			value = value[..plus];
		}
		int dash = value.IndexOf('-');
		if (dash >= 0)
		{
			prerelease = value[(dash + 1)..];
			value = value[..dash];
			if (prerelease.Length == 0)
			{
				return false;
			}
		}
		if (!Version.TryParse(value, out Version? parsed) || value.Split('.').Length > 3)
		{
			return false;
		}
		core = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0));
		return true;
	}

	/// <summary>True when both versions parse and <paramref name="candidate"/> is higher; a release beats its prereleases.</summary>
	public static bool IsNewer(string? candidate, string? running)
	{
		if (!TryParse(candidate, out Version a, out string? preA) || !TryParse(running, out Version b, out string? preB))
		{
			return false;
		}
		int core = a.CompareTo(b);
		if (core != 0)
		{
			return core > 0;
		}
		if (preA == null || preB == null)
		{
			return preA == null && preB != null;
		}
		return string.CompareOrdinal(preA, preB) > 0;
	}

	public static bool IsDevelopment(string? version)
		=> !TryParse(version, out Version core, out string? prerelease) || prerelease != null || core == new Version(0, 0, 0);

	public static bool CanSelfUpdate(string? runningVersion) => !IsDevelopment(runningVersion);
}
