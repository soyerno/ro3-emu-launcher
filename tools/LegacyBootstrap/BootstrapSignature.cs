#nullable disable
using System;
using System.Security.Cryptography;
using System.Text;

namespace Konsor.LegacyBootstrap
{
	/// <summary>
	/// The launcher's manifest signature check (Ro3Emu.Launcher.ManifestSignature) rebuilt for .NET Framework 4.8:
	/// ECDSA P-256 with SHA-256 over the exact manifest bytes, DER (RFC 3279 Ecdsa-Sig-Value) signature stored as one
	/// line of base64, public key as a PEM "BEGIN PUBLIC KEY" (SubjectPublicKeyInfo). .NET Framework has no SPKI or
	/// DER-signature import, so the key is loaded as a CNG ECCPublicBlob and the signature converted to IEEE P1363.
	/// This file is also compiled into the launcher tests (net10) so the same code is tested there.
	/// </summary>
	internal static class BootstrapSignature
	{
		/// <summary>The pinned key of brand/launcher.json "manifestPublicKey" (a test keeps them equal).</summary>
		public const string EmbeddedPublicKeyPem =
			"-----BEGIN PUBLIC KEY-----\nMFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEm59Fv/tQYa3jhZzP4684SsIx+qc5\nD3c9hU4DvtI7OfiGoK9M0kf3vLES7xkULK28vYjSjFQbWcdwuCadP+gFWA==\n-----END PUBLIC KEY-----";

		private const int FieldSize = 32;

		// SEQUENCE { SEQUENCE { OID ecPublicKey, OID prime256v1 }, BIT STRING (0 unused bits) } followed by 0x04||X||Y.
		private static readonly byte[] P256SpkiPrefix =
		{
			0x30, 0x59, 0x30, 0x13, 0x06, 0x07, 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x02, 0x01, 0x06, 0x08, 0x2A,
			0x86, 0x48, 0xCE, 0x3D, 0x03, 0x01, 0x07, 0x03, 0x42, 0x00, 0x04,
		};

		/// <summary>True only when <paramref name="signatureBase64"/> is a valid P-256 signature of <paramref name="data"/>.</summary>
		public static bool Verify(byte[] data, string signatureBase64, string publicKeyPem)
		{
			if (data == null || string.IsNullOrWhiteSpace(signatureBase64) || string.IsNullOrWhiteSpace(publicKeyPem))
			{
				return false;
			}
			try
			{
				byte[] point = ParseP256PublicKey(publicKeyPem);
				byte[] signature = DerToP1363(Convert.FromBase64String(signatureBase64.Trim()));
				if (point == null || signature == null)
				{
					return false;
				}
				using (CngKey key = CngKey.Import(EccPublicBlob(point), CngKeyBlobFormat.EccPublicBlob))
				using (ECDsaCng ecdsa = new ECDsaCng(key))
				{
					return ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256);
				}
			}
			catch (Exception ex) when (ex is FormatException || ex is CryptographicException || ex is ArgumentException)
			{
				return false;
			}
		}

		/// <summary>X||Y (64 bytes) of a P-256 SubjectPublicKeyInfo PEM, or null when the PEM is anything else.</summary>
		public static byte[] ParseP256PublicKey(string pem)
		{
			const string Begin = "-----BEGIN PUBLIC KEY-----";
			const string End = "-----END PUBLIC KEY-----";
			int start = pem.IndexOf(Begin, StringComparison.Ordinal);
			int end = pem.IndexOf(End, StringComparison.Ordinal);
			if (start < 0 || end < start)
			{
				return null;
			}
			StringBuilder body = new StringBuilder();
			foreach (char c in pem.Substring(start + Begin.Length, end - start - Begin.Length))
			{
				if (!char.IsWhiteSpace(c))
				{
					body.Append(c);
				}
			}
			byte[] der = Convert.FromBase64String(body.ToString());
			if (der.Length != P256SpkiPrefix.Length + 2 * FieldSize)
			{
				return null;
			}
			for (int i = 0; i < P256SpkiPrefix.Length; i++)
			{
				if (der[i] != P256SpkiPrefix[i])
				{
					return null;
				}
			}
			byte[] point = new byte[2 * FieldSize];
			Buffer.BlockCopy(der, P256SpkiPrefix.Length, point, 0, point.Length);
			return point;
		}

		/// <summary>BCRYPT_ECCKEY_BLOB for a P-256 public key: magic "ECS1", cbKey, X, Y (little-endian header).</summary>
		public static byte[] EccPublicBlob(byte[] point)
		{
			byte[] blob = new byte[8 + point.Length];
			BitConverter.GetBytes(0x31534345).CopyTo(blob, 0);
			BitConverter.GetBytes(FieldSize).CopyTo(blob, 4);
			Buffer.BlockCopy(point, 0, blob, 8, point.Length);
			return blob;
		}

		/// <summary>
		/// DER SEQUENCE { INTEGER r, INTEGER s } to the fixed-size r||s form (64 bytes), or null when it is not a
		/// well-formed P-256 signature (wrong tags, lengths, trailing bytes, or an integer wider than 32 bytes).
		/// </summary>
		public static byte[] DerToP1363(byte[] der)
		{
			int offset = 0;
			if (der == null || !ReadTag(der, ref offset, 0x30, out int sequenceLength) || offset + sequenceLength != der.Length)
			{
				return null;
			}
			byte[] result = new byte[2 * FieldSize];
			for (int part = 0; part < 2; part++)
			{
				if (!ReadTag(der, ref offset, 0x02, out int length) || length == 0 || offset + length > der.Length)
				{
					return null;
				}
				int start = offset;
				int count = length;
				while (count > 1 && der[start] == 0)
				{
					start++;
					count--;
				}
				if (count > FieldSize)
				{
					return null;
				}
				Buffer.BlockCopy(der, start, result, part * FieldSize + FieldSize - count, count);
				offset += length;
			}
			return offset == der.Length ? result : null;
		}

		private static bool ReadTag(byte[] der, ref int offset, byte tag, out int length)
		{
			length = 0;
			if (offset + 2 > der.Length || der[offset] != tag)
			{
				return false;
			}
			int first = der[offset + 1];
			offset += 2;
			if (first < 0x80)
			{
				length = first;
				return true;
			}
			if (first != 0x81 || offset >= der.Length)
			{
				return false;
			}
			length = der[offset++];
			return true;
		}
	}
}
