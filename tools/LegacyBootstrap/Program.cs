using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Cache;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Konsor.LegacyBootstrap
{
	/// <summary>
	/// Tiny bootstrap published as "launcher 1.2.0" on the v1.0.0 update channel. v1.0.0 replaces its own exe with it
	/// and starts it; it then installs the real launcher from the signed manifest (same signature rules as the
	/// launcher), swaps it in and starts it. See docs/AUTO_UPDATE.md, "Legacy v1.0.0 upgrade".
	/// </summary>
	internal static class Program
	{
		[STAThread]
		private static int Main(string[] args)
		{
			ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
			Options options;
			try
			{
				options = Options.Parse(args);
			}
			catch (ArgumentException ex)
			{
				return Fail("invalid arguments: " + ex.Message, 2);
			}
			if (options.VerifyOnly)
			{
				try
				{
					Release release = Updater.FetchRelease(options);
					WriteLine("OK: signature verified; launcher " + release.Version + " " + release.Url.AbsoluteUri
						+ " size " + release.Size + " sha256 " + release.Sha256);
					return 0;
				}
				catch (BootstrapException ex)
				{
					return Fail(ex.Message, 1);
				}
			}
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			Application.Run(new BootstrapForm(options));
			return 0;
		}

		private static int Fail(string message, int code)
		{
			WriteLine("FAIL: " + message);
			return code;
		}

		// A WinExe has no console: write to redirected output, or attach to the parent's console when there is one.
		private static void WriteLine(string text)
		{
			const int StdOutput = -11;
			if (GetFileType(GetStdHandle(StdOutput)) == 0)
			{
				AttachConsole(-1);
			}
			Console.Out.WriteLine(text);
			Console.Out.Flush();
		}

		[DllImport("kernel32.dll")]
		private static extern bool AttachConsole(int processId);

		[DllImport("kernel32.dll")]
		private static extern IntPtr GetStdHandle(int handle);

		[DllImport("kernel32.dll")]
		private static extern int GetFileType(IntPtr handle);
	}

	/// <summary>Command line. The overrides are for tests only: another manifest URL needs an explicit public key.</summary>
	internal sealed class Options
	{
		public const string DefaultManifestUrl = "https://konsor.com.ar/launcher/konsor.json";
		public const string DefaultLauncherPrefix = "https://konsor.com.ar/launcher/";

		public Uri ManifestUrl = new Uri(DefaultManifestUrl);
		public string PublicKeyPem = BootstrapSignature.EmbeddedPublicKeyPem;
		public string LauncherPrefix = DefaultLauncherPrefix;
		public bool VerifyOnly;

		public static Options Parse(string[] args)
		{
			Options options = new Options();
			string manifestUrl = null;
			string keyFile = null;
			for (int i = 0; i < args.Length; i++)
			{
				switch (args[i])
				{
					case "--verify-only":
						options.VerifyOnly = true;
						break;
					case "--manifest-url":
						manifestUrl = Value(args, ref i);
						break;
					case "--public-key-file":
						keyFile = Value(args, ref i);
						break;
					default:
						throw new ArgumentException("unknown argument " + args[i]);
				}
			}
			if (keyFile != null)
			{
				try
				{
					options.PublicKeyPem = File.ReadAllText(keyFile);
				}
				catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
				{
					throw new ArgumentException("cannot read --public-key-file: " + ex.Message);
				}
			}
			if (manifestUrl != null)
			{
				if (keyFile == null)
				{
					throw new ArgumentException("--manifest-url needs --public-key-file");
				}
				if (!Uri.TryCreate(manifestUrl, UriKind.Absolute, out Uri url) || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp))
				{
					throw new ArgumentException("--manifest-url must be an absolute http(s) URL");
				}
				options.ManifestUrl = url;
				options.LauncherPrefix = new Uri(url, ".").AbsoluteUri;
			}
			return options;
		}

		private static string Value(string[] args, ref int i)
		{
			if (i + 1 >= args.Length)
			{
				throw new ArgumentException(args[i] + " needs a value");
			}
			return args[++i];
		}
	}

	internal sealed class Release
	{
		public Uri Url;
		public string Sha256;
		public long Size;
		public string Version;
	}

	/// <summary>A failure with a message for the player (Spanish).</summary>
	internal sealed class BootstrapException : Exception
	{
		public BootstrapException(string message) : base(message)
		{
		}
	}

	internal static class Updater
	{
		private const int MaxManifestBytes = 1024 * 1024;
		private const long MaxLauncherBytes = 1024L * 1024 * 1024;

		/// <summary>The launcher release of the manifest, after the signature and the release fields check out.</summary>
		public static Release FetchRelease(Options options)
		{
			byte[] manifest;
			byte[] signature;
			try
			{
				manifest = GetSmall(options.ManifestUrl);
				signature = GetSmall(new Uri(options.ManifestUrl.AbsoluteUri + ".sig"));
			}
			catch (Exception ex) when (ex is WebException || ex is IOException)
			{
				throw new BootstrapException("No se pudo descargar la información de la actualización (" + ex.Message + ").");
			}
			if (!BootstrapSignature.Verify(manifest, Encoding.ASCII.GetString(signature), options.PublicKeyPem))
			{
				throw new BootstrapException("La firma de la actualización no es válida.");
			}
			return ParseRelease(manifest, options);
		}

		private static Release ParseRelease(byte[] manifest, Options options)
		{
			try
			{
				string json = Encoding.UTF8.GetString(manifest).TrimStart('﻿');
				Dictionary<string, object> root = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(json);
				if (Convert.ToInt32(root["schema"], CultureInfo.InvariantCulture) != 1)
				{
					throw new BootstrapException("Formato de actualización desconocido.");
				}
				Dictionary<string, object> launcher = (Dictionary<string, object>)root["launcher"];
				string url = (string)launcher["url"];
				Release release = new Release
				{
					Sha256 = ((string)launcher["sha256"]).ToLowerInvariant(),
					Size = Convert.ToInt64(launcher["size"], CultureInfo.InvariantCulture),
					Version = (string)launcher["version"],
				};
				if (!Uri.TryCreate(options.ManifestUrl, url, out release.Url)
					|| !release.Url.AbsoluteUri.StartsWith(options.LauncherPrefix, StringComparison.Ordinal))
				{
					throw new BootstrapException("La dirección de descarga del launcher no es válida.");
				}
				if (release.Size <= 0 || release.Size > MaxLauncherBytes || !IsSha256(release.Sha256))
				{
					throw new BootstrapException("Los datos del launcher en la actualización no son válidos.");
				}
				return release;
			}
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidCastException || ex is KeyNotFoundException
				|| ex is FormatException || ex is OverflowException || ex is NullReferenceException || ex is InvalidOperationException)
			{
				throw new BootstrapException("La información de la actualización no es válida.");
			}
		}

		/// <summary>Downloads the release to <paramref name="path"/>; deletes it unless size and SHA-256 match.</summary>
		public static void Download(Release release, string path, Action<long, long> progress)
		{
			bool ok = false;
			try
			{
				using (WebResponse response = Request(release.Url).GetResponse())
				using (Stream input = response.GetResponseStream())
				using (FileStream output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
				using (SHA256 sha = SHA256.Create())
				{
					byte[] buffer = new byte[81920];
					long total = 0;
					int read;
					while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
					{
						total += read;
						if (total > release.Size)
						{
							throw new BootstrapException("El launcher descargado es más grande de lo esperado.");
						}
						sha.TransformBlock(buffer, 0, read, null, 0);
						output.Write(buffer, 0, read);
						progress(total, release.Size);
					}
					sha.TransformFinalBlock(buffer, 0, 0);
					if (total != release.Size)
					{
						throw new BootstrapException("La descarga del launcher quedó incompleta.");
					}
					if (!string.Equals(Hex(sha.Hash), release.Sha256, StringComparison.OrdinalIgnoreCase))
					{
						throw new BootstrapException("El launcher descargado está dañado (SHA-256 distinto).");
					}
				}
				ok = true;
			}
			catch (Exception ex) when (ex is WebException || ex is IOException)
			{
				throw new BootstrapException("No se pudo descargar el launcher (" + ex.Message + ").");
			}
			finally
			{
				if (!ok)
				{
					TryDelete(path);
				}
			}
		}

		public static void TryDelete(string path)
		{
			try
			{
				File.Delete(path);
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
			}
		}

		private static byte[] GetSmall(Uri url)
		{
			using (WebResponse response = Request(url).GetResponse())
			using (Stream input = response.GetResponseStream())
			using (MemoryStream output = new MemoryStream())
			{
				byte[] buffer = new byte[16384];
				int read;
				while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
				{
					output.Write(buffer, 0, read);
					if (output.Length > MaxManifestBytes)
					{
						throw new IOException("response too large");
					}
				}
				return output.ToArray();
			}
		}

		private static HttpWebRequest Request(Uri url)
		{
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
			request.CachePolicy = new HttpRequestCachePolicy(HttpRequestCacheLevel.NoCacheNoStore);
			request.Headers[HttpRequestHeader.CacheControl] = "no-cache";
			request.UserAgent = "KonsorBootstrap/" + Assembly.GetExecutingAssembly().GetName().Version;
			request.Timeout = 30000;
			request.ReadWriteTimeout = 60000;
			return request;
		}

		private static bool IsSha256(string hex)
		{
			if (hex.Length != 64)
			{
				return false;
			}
			foreach (char c in hex)
			{
				if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
				{
					return false;
				}
			}
			return true;
		}

		private static string Hex(byte[] bytes)
		{
			StringBuilder text = new StringBuilder(bytes.Length * 2);
			foreach (byte b in bytes)
			{
				text.Append(b.ToString("x2", CultureInfo.InvariantCulture));
			}
			return text.ToString();
		}
	}
}
