using System.Security.Cryptography;

namespace ManifestTool;

/// <summary>
/// The private half of the manifest signature (the launcher only verifies, see Ro3Emu.Launcher.ManifestSignature):
/// ECDSA P-256, SHA-256 over the exact manifest bytes, DER signature written as one line of base64.
/// </summary>
public static class ManifestSigner
{
	/// <summary>A new key pair: PKCS#8 private key PEM and SubjectPublicKeyInfo public key PEM.</summary>
	public static (string PrivateKeyPem, string PublicKeyPem) GenerateKey()
	{
		using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
		return (key.ExportPkcs8PrivateKeyPem(), key.ExportSubjectPublicKeyInfoPem());
	}

	public static string PublicKeyOf(string privateKeyPem)
	{
		using ECDsa key = ECDsa.Create();
		key.ImportFromPem(privateKeyPem);
		return key.ExportSubjectPublicKeyInfoPem();
	}

	public static string Sign(byte[] manifest, string privateKeyPem)
	{
		using ECDsa key = ECDsa.Create();
		key.ImportFromPem(privateKeyPem);
		if (key.KeySize != 256)
		{
			throw new ToolException("the private key is not an ECDSA P-256 key");
		}
		return Convert.ToBase64String(key.SignData(manifest, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
	}

	/// <summary>True when <paramref name="path"/> is inside a git working tree (a ".git" folder or file in it or above it).</summary>
	public static bool IsInsideRepository(string path)
	{
		for (DirectoryInfo? dir = new DirectoryInfo(Path.GetFullPath(path)); dir != null; dir = dir.Parent)
		{
			string git = Path.Combine(dir.FullName, ".git");
			if (Directory.Exists(git) || File.Exists(git))
			{
				return true;
			}
		}
		return false;
	}
}

public sealed class ToolException(string message) : Exception(message);
