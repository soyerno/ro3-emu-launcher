using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Ro3Emu.Launcher;

/// <summary>
/// Whether a realm is up, from its optional <see cref="RealmSpec.StatusUrl"/>: a 200 answer means online; any other
/// status, a network error or a timeout means offline. A realm without a status URL is always online.
/// </summary>
public static class RealmStatus
{
	/// <summary>Online only for a 200 answer; <paramref name="statusCode"/> is null when no answer came (error or timeout).</summary>
	public static bool IsOnline(int? statusCode) => statusCode == 200;

	/// <summary>Asks <paramref name="statusUri"/> once, within <paramref name="http"/>'s timeout. Never throws.</summary>
	public static async Task<bool> ProbeAsync(HttpClient http, Uri statusUri, CancellationToken ct)
	{
		int? statusCode = null;
		try
		{
			using HttpResponseMessage response = await http.GetAsync(statusUri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
			statusCode = (int)response.StatusCode;
		}
		catch (Exception)
		{
			// Network error, timeout or cancellation: no answer, so offline.
		}
		return IsOnline(statusCode);
	}

	/// <summary>
	/// The realm to play on: <paramref name="selected"/> while it is online; otherwise the first online realm; otherwise
	/// <paramref name="selected"/> (every realm is down, so there is nothing better to offer).
	/// </summary>
	public static RealmSpec PickPlayable(IReadOnlyList<RealmSpec> realms, RealmSpec selected, Func<RealmSpec, bool> isOnline)
	{
		if (isOnline(selected))
		{
			return selected;
		}
		foreach (RealmSpec realm in realms)
		{
			if (isOnline(realm))
			{
				return realm;
			}
		}
		return selected;
	}
}
