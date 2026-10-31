using System.Security.Cryptography;
using System.Text;
using Konsor.LegacyBootstrap;
using Xunit;

namespace Ro3Emu.Launcher.Tests;

// The v1.0.0 upgrade bootstrap (tools/LegacyBootstrap, .NET Framework 4.8) re-implements the manifest signature check
// without ImportSubjectPublicKeyInfo or DER signatures; it must accept and refuse exactly what the launcher does.
public class LegacyBootstrapSignatureTests
{
	// Same shape as the live https://konsor.com.ar/launcher/konsor.json.
	private static byte[] LiveFormatManifest() => Encoding.UTF8.GetBytes("""
		{
		  "schema": 1,
		  "sequence": 1793468596,
		  "version": "1.2.0",
		  "generated": "2026-10-31T17:43:16Z",
		  "launcher": {
		    "version": "1.2.0",
		    "url": "https://konsor.com.ar/launcher/KonsorRO.3e45bb3344fe.exe",
		    "sha256": "3e45bb3344fef0f000e7d56c71e962deb4de23235cc5e55955dc7ab6e3f8b0ab",
		    "size": 137129851
		  },
		  "files": []
		}
		""");

	[Fact]
	public void ValidSignature_OverALiveFormatManifest_Verifies_LikeTheLauncher()
	{
		using TestKey key = new();
		byte[] manifest = LiveFormatManifest();
		string signature = key.Sign(manifest);

		Assert.True(BootstrapSignature.Verify(manifest, signature, key.PublicKeyPem));
		Assert.True(ManifestSignature.Verify(manifest, signature, key.PublicKeyPem));
	}

	[Fact]
	public void SignatureFromTheReleaseTool_Verifies()
	{
		(string privateKey, string publicKey) = ManifestTool.ManifestSigner.GenerateKey();
		byte[] manifest = LiveFormatManifest();

		Assert.True(BootstrapSignature.Verify(manifest, ManifestTool.ManifestSigner.Sign(manifest, privateKey), publicKey));
	}

	[Fact]
	public void TamperedManifest_OrAnotherKey_IsRejected()
	{
		using TestKey key = new();
		using TestKey other = new();
		byte[] manifest = LiveFormatManifest();
		string signature = key.Sign(manifest);
		byte[] tampered = (byte[])manifest.Clone();
		tampered[tampered.Length / 2] ^= 0x01;

		Assert.False(BootstrapSignature.Verify(tampered, signature, key.PublicKeyPem));
		Assert.False(BootstrapSignature.Verify(manifest, signature, other.PublicKeyPem));
	}

	[Theory]
	[InlineData("")]
	[InlineData("not base64 at all")]
	[InlineData("AAAA")]
	public void MalformedSignature_IsRejected(string signature)
	{
		using TestKey key = new();

		Assert.False(BootstrapSignature.Verify(LiveFormatManifest(), signature, key.PublicKeyPem));
	}

	[Fact]
	public void NonP256Key_IsRejected()
	{
		using ECDsa p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
		byte[] manifest = LiveFormatManifest();
		string signature = Convert.ToBase64String(p384.SignData(manifest, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));

		Assert.Null(BootstrapSignature.ParseP256PublicKey(p384.ExportSubjectPublicKeyInfoPem()));
		Assert.False(BootstrapSignature.Verify(manifest, signature, p384.ExportSubjectPublicKeyInfoPem()));
	}

	[Fact]
	public void DerSignature_ConvertsToTheP1363FormOfTheSameSignature()
	{
		using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
		byte[] data = LiveFormatManifest();
		for (int i = 0; i < 20; i++)
		{
			byte[] der = key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

			byte[] p1363 = BootstrapSignature.DerToP1363(der)!;

			Assert.Equal(64, p1363.Length);
			Assert.True(key.VerifyData(data, p1363, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
		}
	}

	[Fact]
	public void DerIntegers_ArePaddedAndStrippedToFixedFields()
	{
		// r = 0x0080 (sign byte stripped), s = 0x01.
		byte[] p1363 = BootstrapSignature.DerToP1363([0x30, 0x07, 0x02, 0x02, 0x00, 0x80, 0x02, 0x01, 0x01])!;

		Assert.Equal(0x80, p1363[31]);
		Assert.Equal(0x01, p1363[63]);
		Assert.Equal(62, p1363.Count(b => b == 0));
	}

	[Fact]
	public void MalformedDer_IsRejected()
	{
		byte[] wide = new byte[2 + 33 + 3];
		wide[0] = 0x02;
		wide[1] = 33;
		wide[2] = 0x01;
		wide[35] = 0x02;
		wide[36] = 0x01;
		wide[37] = 0x01;
		byte[] tooWide = [0x30, (byte)wide.Length, .. wide];

		Assert.Null(BootstrapSignature.DerToP1363([0x30, 0x06, 0x02, 0x01, 0x01, 0x02, 0x01, 0x01, 0x00]));
		Assert.Null(BootstrapSignature.DerToP1363([0x31, 0x06, 0x02, 0x01, 0x01, 0x02, 0x01, 0x01]));
		Assert.Null(BootstrapSignature.DerToP1363(tooWide));
		Assert.Null(BootstrapSignature.DerToP1363([]));
	}

	[Fact]
	public void EmbeddedKey_EqualsTheBrandManifestPublicKey()
	{
		LauncherConfig config = LauncherConfig.Load(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid().ToString("N") + ".json"));

		Assert.Equal(BootstrapSignature.EmbeddedPublicKeyPem, config.ManifestPublicKey);
		Assert.NotNull(BootstrapSignature.ParseP256PublicKey(BootstrapSignature.EmbeddedPublicKeyPem));
	}
}

public class LegacyBootstrapSwapTests
{
	[Fact]
	public void Swap_InstallsTheLauncher_AndLeavesTheBootstrapAsExeOld()
	{
		using TempDir dir = new();
		string exe = dir.File("KonsorRO.exe");
		File.WriteAllText(exe, "bootstrap");
		File.WriteAllText(exe + ".new", "launcher");
		File.WriteAllText(exe + ".old", "v1.0.0");

		string leftover = BootstrapSwap.Apply(exe, exe + ".new", TimeSpan.FromSeconds(1));

		Assert.Equal(exe + ".old", leftover);
		Assert.Equal("launcher", File.ReadAllText(exe));
		Assert.Equal("bootstrap", File.ReadAllText(exe + ".old"));
		Assert.False(File.Exists(exe + ".new"));
	}

	[Fact]
	public void LockedExeOld_FallsBackToAUniqueLeftoverName()
	{
		using TempDir dir = new();
		string exe = dir.File("KonsorRO.exe");
		File.WriteAllText(exe, "bootstrap");
		File.WriteAllText(exe + ".new", "launcher");
		File.WriteAllText(exe + ".old", "v1.0.0");
		string leftover;
		using (new FileStream(exe + ".old", FileMode.Open, FileAccess.Read, FileShare.None))
		{
			leftover = BootstrapSwap.Apply(exe, exe + ".new", TimeSpan.FromMilliseconds(300));
		}

		Assert.StartsWith(exe + ".old.", leftover);
		Assert.Equal("launcher", File.ReadAllText(exe));
		Assert.Equal("bootstrap", File.ReadAllText(leftover));
		Assert.Equal("v1.0.0", File.ReadAllText(exe + ".old"));
	}

	[Fact]
	public void FailedSwap_RestoresTheBootstrap()
	{
		using TempDir dir = new();
		string exe = dir.File("KonsorRO.exe");
		File.WriteAllText(exe, "bootstrap");

		Assert.Throws<FileNotFoundException>(() => BootstrapSwap.Apply(exe, exe + ".new", TimeSpan.Zero));

		Assert.Equal("bootstrap", File.ReadAllText(exe));
		Assert.False(File.Exists(exe + ".old"));
	}
}
