using System;

namespace Ro3Emu.Launcher.GenesisInstall;

/// <summary>
/// A failure while installing or updating the game from the publisher, in plain words: what went wrong and what to do.
/// <see cref="Key"/> names the player-facing text in <see cref="Strings"/> when there is one; otherwise the launcher
/// shows <see cref="Exception.Message"/> and <see cref="Fix"/> (English) inside a localized frame.
/// </summary>
public sealed class InstallException(string message, string fix, string? key = null, params object[] args) : Exception(message)
{
	public const string Unreachable = "install.unreachable";
	public const string DownloadFailed = "install.download";
	public const string NoSpace = "install.space";
	public const string OlderRelease = "install.older";

	public string Fix { get; } = fix;

	public string? Key { get; } = key;

	/// <summary>The arguments of <see cref="Key"/>'s text.</summary>
	public object[] Args { get; } = args;
}
