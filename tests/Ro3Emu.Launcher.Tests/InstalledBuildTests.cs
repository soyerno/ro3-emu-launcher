using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Ro3Emu.Launcher.Tests;

public class InstalledBuildTests
{
	[Fact]
	public void HooksHash_IsTheFirstEightLowercaseHexOfTheSha256()
	{
		using TempDir dir = new TempDir();
		dir.Write(InstalledBuild.HooksPath, "hooks build");
		string expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("hooks build")))[..8];

		Assert.Equal(expected, InstalledBuild.HooksHash(dir.Path));
	}

	[Fact]
	public void HooksHash_IsNullWhenTheDllIsMissing()
	{
		using TempDir dir = new TempDir();

		Assert.Null(InstalledBuild.HooksHash(dir.Path));
	}

	[Fact]
	public void AppliedSequence_IsTheManifestOnceItsFilesAreInPlace()
	{
		RemoteUpdateOutcome outcome = new RemoteUpdateOutcome { Manifest = Manifest(42), Files = new FileApplyResult() };

		Assert.Equal(42, InstalledBuild.AppliedSequence(outcome, previous: 7));
	}

	[Fact]
	public void AppliedSequence_KeepsThePreviousOneWhenAFileFailed()
	{
		FileApplyResult files = new FileApplyResult();
		files.Failed.Add("client/Ro3Emu.ClientHooks.dll");
		RemoteUpdateOutcome outcome = new RemoteUpdateOutcome { Manifest = Manifest(42), Files = files };

		Assert.Equal(7, InstalledBuild.AppliedSequence(outcome, previous: 7));
	}

	[Fact]
	public void AppliedSequence_KeepsThePreviousOneWhenTheFolderIsReadOnly()
	{
		RemoteUpdateOutcome outcome = new RemoteUpdateOutcome { Manifest = Manifest(42), Files = new FileApplyResult { NotWritable = true } };

		Assert.Null(InstalledBuild.AppliedSequence(outcome, previous: null));
	}

	[Fact]
	public void AppliedSequence_KeepsThePreviousOneWithoutAManifest()
	{
		RemoteUpdateOutcome outcome = new RemoteUpdateOutcome { Error = "offline" };

		Assert.Equal(7, InstalledBuild.AppliedSequence(outcome, previous: 7));
	}

	[Fact]
	public void AppliedSequence_NeverDropsBelowAHigherKnownSequence()
	{
		RemoteUpdateOutcome outcome = new RemoteUpdateOutcome { Manifest = Manifest(42), Files = new FileApplyResult() };

		Assert.Equal(50, InstalledBuild.AppliedSequence(outcome, previous: 50));
	}

	private static RemoteManifest Manifest(long sequence) => new RemoteManifest { Schema = RemoteManifest.SupportedSchema, Sequence = sequence };
}
