using System;
using System.Security.Cryptography;

namespace Ro3Emu.Launcher;

/// <summary>
/// Detached manifest signature: ECDSA P-256 with SHA-256 over the exact bytes of the manifest file, DER-encoded
/// (RFC 3279 Ecdsa-Sig-Value) and stored as one line of base64 in "&lt;manifest&gt;.sig". The public key is a PEM
/// "BEGIN PUBLIC KEY" (SubjectPublicKeyInfo) pinned in launcher.json "manifestPublicKey".
/// </summary>
public static class ManifestSignature
{
	private const string P256Oid = "1.2.840.10045.3.1.7";

	public static bool IsKeyConfigured(string? publicKeyPem)
		=> !string.IsNullOrWhiteSpace(publicKeyPem) && publicKeyPem.Contains("BEGIN PUBLIC KEY", StringComparison.Ordinal);

	/// <summary>True only when <paramref name="signatureBase64"/> is a valid P-256 signature of <paramref name="data"/>.</summary>
	public static bool Verify(ReadOnlySpan<byte> data, string? signatureBase64, string? publicKeyPem)
	{
		if (string.IsNullOrWhiteSpace(signatureBase64) || !IsKeyConfigured(publicKeyPem))
		{
			return false;
		}
		try
		{
			byte[] signature = Convert.FromBase64String(signatureBase64.Trim());
			using ECDsa key = ECDsa.Create();
			key.ImportFromPem(publicKeyPem);
			if (key.ExportParameters(includePrivateParameters: false).Curve.Oid?.Value != P256Oid)
			{
				return false;
			}
			return key.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
		}
		catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
		{
			return false;
		}
	}
}
