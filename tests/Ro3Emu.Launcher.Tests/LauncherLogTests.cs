using System.Text.Json;
using Xunit;

namespace Ro3Emu.Launcher.Tests;

public class LauncherLogTests
{
	[Fact]
	public void FormatLine_StampsTheDateAndTimeAndEndsTheLine()
	{
		string line = LauncherLog.FormatLine(new DateTime(2026, 11, 2, 13, 5, 9), "Game started.");

		Assert.Equal("[2026-11-02 13:05:09] Game started.\n", line);
	}

	[Fact]
	public void FormatLine_IndentsTheContinuationLinesOfAMultiLineMessage()
	{
		string line = LauncherLog.FormatLine(new DateTime(2026, 11, 2, 13, 5, 9), "first\r\nsecond\nthird\n");

		Assert.Equal("[2026-11-02 13:05:09] first\n    second\n    third\n", line);
	}

	[Fact]
	public void Write_AppendsOneLinePerCallToLauncherLog()
	{
		using TempDir dir = new TempDir();

		LauncherLog.Write(dir.Path, "one");
		LauncherLog.Write(dir.Path, "two");

		string[] lines = File.ReadAllLines(dir.File(LauncherLog.FileName));
		Assert.Equal(2, lines.Length);
		Assert.EndsWith("] one", lines[0]);
		Assert.EndsWith("] two", lines[1]);
	}

	[Fact]
	public void Write_NeverThrowsWhenTheFolderDoesNotExist()
	{
		using TempDir dir = new TempDir();

		LauncherLog.Write(dir.File("missing/folder"), "lost");

		Assert.False(File.Exists(dir.File("missing/folder/" + LauncherLog.FileName)));
	}

	[Fact]
	public void FormatException_NamesTheSourceTypeMessageAndStack()
	{
		Exception caught;
		try
		{
			throw new InvalidOperationException("boom", new IOException("disk"));
		}
		catch (Exception ex)
		{
			caught = ex;
		}

		string text = LauncherLog.FormatException("AppDomain.UnhandledException", caught, terminating: true);

		Assert.StartsWith("CRASH [AppDomain.UnhandledException, terminating]: System.InvalidOperationException: boom", text);
		Assert.Contains("System.IO.IOException: disk", text);
		Assert.Contains(nameof(FormatException_NamesTheSourceTypeMessageAndStack), text);
	}

	[Fact]
	public void FormatException_ToleratesANonExceptionObject()
	{
		Assert.Equal("CRASH [AppDomain.UnhandledException]: non-exception object: 42", LauncherLog.FormatException("AppDomain.UnhandledException", 42));
	}

	[Theory]
	[InlineData(0, "0")]
	[InlineData(1, "1")]
	[InlineData(-1073741819, "-1073741819 (0xC0000005)")]
	[InlineData(-1, "-1 (0xFFFFFFFF)")]
	public void FormatExitCode_AddsTheHexOfNtStatusCodes(int code, string expected)
	{
		Assert.Equal(expected, LauncherLog.FormatExitCode(code));
	}

	[Fact]
	public void FormatProcessExit_GivesTheExitCodeAndTheRunTime()
	{
		string text = LauncherLog.FormatProcessExit("ro3.exe", 4242, -1073741819, TimeSpan.FromSeconds(3725.4));

		Assert.Equal("ro3.exe (pid 4242) exited with code -1073741819 (0xC0000005) after 01:02:05.", text);
	}

	[Fact]
	public void FormatProcessExit_SaysWhenTheExitCodeCannotBeRead()
	{
		string text = LauncherLog.FormatProcessExit("ro3.exe", 7, null, TimeSpan.FromSeconds(12));

		Assert.Equal("ro3.exe (pid 7) exited (exit code unavailable) after 00:00:12.", text);
	}

	[Fact]
	public void FormatCommandLine_QuotesArgumentsWithSpaces()
	{
		string text = LauncherLog.FormatCommandLine(@"C:\Konsor\RO3Emu.exe", ["join", "https://play.example", "--client", @"C:\Genesis Launcher\ROO_PC", "--yes"]);

		Assert.Equal(@"C:\Konsor\RO3Emu.exe join https://play.example --client ""C:\Genesis Launcher\ROO_PC"" --yes", text);
	}

	[Fact]
	public void FormatJsError_KeepsTheFieldsTheUiSent()
	{
		using JsonDocument doc = JsonDocument.Parse("""{"cmd":"jsError","kind":"error","message":"x is not defined","source":"https://launcher.ro3emu/ui.app.js","line":12,"col":5,"stack":"ReferenceError: x is not defined\n    at f"}""");

		string text = LauncherLog.FormatJsError(doc.RootElement);

		Assert.Equal("UI script error (error): x is not defined at https://launcher.ro3emu/ui.app.js:12:5\nReferenceError: x is not defined\n    at f", text);
	}

	[Fact]
	public void FormatJsError_TruncatesLongFields()
	{
		string longMessage = new string('a', 5000);
		using JsonDocument doc = JsonDocument.Parse(JsonSerializer.Serialize(new { cmd = "jsError", kind = "unhandledrejection", message = longMessage }));

		string text = LauncherLog.FormatJsError(doc.RootElement);

		Assert.StartsWith("UI script error (unhandledrejection): aaa", text);
		Assert.True(text.Length < 1100, $"length {text.Length}");
		Assert.EndsWith("...", text);
	}

	[Fact]
	public void ExitReason_KeepsTheFirstReasonGiven()
	{
		LauncherLog.ExitReasonTracker tracker = new LauncherLog.ExitReasonTracker();

		tracker.Set("self-update restart");
		tracker.Set("window closed");

		Assert.Equal("self-update restart", tracker.Reason);
	}

	[Fact]
	public void ExitReason_IsUnknownWhenNoneWasGiven()
	{
		Assert.Equal("unknown", new LauncherLog.ExitReasonTracker().Reason);
	}
}
