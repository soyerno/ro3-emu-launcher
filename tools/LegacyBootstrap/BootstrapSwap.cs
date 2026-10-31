#nullable disable
using System;
using System.IO;
using System.Threading;

namespace Konsor.LegacyBootstrap
{
	/// <summary>
	/// Puts the verified launcher in place of the running bootstrap. The bootstrap's own exe becomes "&lt;exe&gt;.old",
	/// the name the real launcher deletes on start (SelfUpdate.DeleteLeftovers). A running exe can be renamed but not
	/// deleted, so an "&lt;exe&gt;.old" still held by the exiting v1.0.0 parent is retried, then moved aside.
	/// This file is also compiled into the launcher tests (net10).
	/// </summary>
	internal static class BootstrapSwap
	{
		/// <summary>Swaps <paramref name="staged"/> into <paramref name="exePath"/>; returns where the old exe went.</summary>
		public static string Apply(string exePath, string staged, TimeSpan leftoverWait)
		{
			string leftover = FreeLeftoverName(exePath + ".old", leftoverWait);
			File.Move(exePath, leftover);
			try
			{
				File.Move(staged, exePath);
			}
			catch
			{
				// Never leave the folder without a runnable exe: the bootstrap retries on the next start.
				File.Move(leftover, exePath);
				throw;
			}
			return leftover;
		}

		/// <summary>
		/// <paramref name="preferred"/> once nothing is there (deleting a stale file, retrying while it is locked);
		/// when it stays locked it is renamed to a unique "*.old.&lt;ticks&gt;" so the preferred name is free anyway.
		/// </summary>
		public static string FreeLeftoverName(string preferred, TimeSpan wait)
		{
			DateTime deadline = DateTime.UtcNow + wait;
			while (true)
			{
				try
				{
					if (File.Exists(preferred))
					{
						File.Delete(preferred);
					}
					return preferred;
				}
				catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
				{
					if (DateTime.UtcNow >= deadline)
					{
						break;
					}
					Thread.Sleep(250);
				}
			}
			string unique = preferred + "." + DateTime.UtcNow.Ticks;
			try
			{
				File.Move(preferred, unique);
				return preferred;
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
				return unique;
			}
		}
	}
}
