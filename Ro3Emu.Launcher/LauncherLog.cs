using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Ro3Emu.Launcher;

/// <summary>
/// launcher.log beside the exe: developer diagnostics in plain English. Each line is appended with the file opened and
/// closed again, so the line is handed to Windows before the call returns and a crash right after it loses nothing.
/// Writing never throws; a line that cannot be written is dropped.
/// </summary>
public static class LauncherLog
{
	public const string FileName = "launcher.log";

	/// <summary>The longest text kept from one field of a UI script error.</summary>
	private const int MaxJsField = 1000;

	private static readonly object Gate = new object();

	/// <summary>Folder of launcher.log for <see cref="Write(string)"/>.</summary>
	public static string Directory { get; set; } = AppContext.BaseDirectory;

	/// <summary>Why the launcher is exiting, reported by <c>App.OnExit</c>.</summary>
	public static ExitReasonTracker Exit { get; } = new ExitReasonTracker();

	public static void Write(string message) => Write(Directory, message);

	public static void Write(string dir, string message)
	{
		string line = FormatLine(DateTime.Now, message);
		lock (Gate)
		{
			// Another launcher (the old one during a self-update restart) may hold the file for a moment.
			for (int attempt = 0; attempt < 3; attempt++)
			{
				try
				{
					using FileStream stream = new FileStream(Path.Combine(dir, FileName), FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
					byte[] bytes = Encoding.UTF8.GetBytes(line);
					stream.Write(bytes, 0, bytes.Length);
					return;
				}
				catch (DirectoryNotFoundException)
				{
					return;
				}
				catch (IOException)
				{
					Thread.Sleep(15);
				}
				catch (Exception ex) when (ex is UnauthorizedAccessException or ArgumentException or NotSupportedException)
				{
					return;
				}
			}
		}
	}

	/// <summary>One log line: local date and time, the message, continuation lines indented.</summary>
	public static string FormatLine(DateTime at, string message)
	{
		string body = (message ?? "").TrimEnd('\r', '\n').ReplaceLineEndings("\n    ");
		return "[" + at.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "] " + body + "\n";
	}

	/// <summary>An unhandled exception: where it was caught, then its type, message, stack and inner exceptions.</summary>
	public static string FormatException(string source, object? exception, bool terminating = false)
	{
		string where = terminating ? source + ", terminating" : source;
		string what = exception is Exception ex ? ex.ToString() : "non-exception object: " + (exception?.ToString() ?? "null");
		return $"CRASH [{where}]: {what}";
	}

	/// <summary>The exit code, with its hex form when it is an NTSTATUS (negative), as Windows reports crashes.</summary>
	public static string FormatExitCode(int code)
		=> code < 0 ? $"{code} (0x{unchecked((uint)code).ToString("X8", CultureInfo.InvariantCulture)})" : code.ToString(CultureInfo.InvariantCulture);

	public static string FormatProcessExit(string name, int pid, int? exitCode, TimeSpan runTime)
	{
		string how = exitCode is int code ? "with code " + FormatExitCode(code) : "(exit code unavailable)";
		return $"{name} (pid {pid}) exited {how} after {FormatDuration(runTime)}.";
	}

	public static string FormatDuration(TimeSpan time)
	{
		time = time < TimeSpan.Zero ? TimeSpan.Zero : time;
		return $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";
	}

	/// <summary>A command line for the log; arguments with spaces are quoted. Never pass secrets in <paramref name="args"/>.</summary>
	public static string FormatCommandLine(string fileName, IEnumerable<string> args)
		=> string.Join(" ", new[] { fileName }.Concat(args).Select(Quote));

	/// <summary>A <c>{cmd:"jsError"}</c> message of ui.app.js (window.onerror / unhandledrejection), every field shortened.</summary>
	public static string FormatJsError(JsonElement message)
	{
		string kind = Field(message, "kind");
		string text = $"UI script error ({(kind.Length > 0 ? kind : "error")}): {Field(message, "message")}";
		string source = Field(message, "source");
		if (source.Length > 0)
		{
			text += $" at {source}:{Number(message, "line")}:{Number(message, "col")}";
		}
		string stack = Field(message, "stack");
		return stack.Length > 0 ? text + "\n" + stack : text;
	}

	private static string Quote(string arg)
		=> arg.Length == 0 || arg.Any(char.IsWhiteSpace) ? "\"" + arg + "\"" : arg;

	private static string Field(JsonElement message, string name)
	{
		if (message.ValueKind != JsonValueKind.Object || !message.TryGetProperty(name, out JsonElement value))
		{
			return "";
		}
		string text = value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
		return text.Length > MaxJsField ? text[..MaxJsField] + "..." : text;
	}

	private static long Number(JsonElement message, string name)
		=> message.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long n) ? n : 0;

	/// <summary>The first reason given wins: a self-update restart closes the window too, and stays the reason.</summary>
	public sealed class ExitReasonTracker
	{
		private string? _reason;

		public string Reason => Volatile.Read(ref _reason) ?? "unknown";

		public void Set(string reason) => Interlocked.CompareExchange(ref _reason, reason, null);
	}
}
