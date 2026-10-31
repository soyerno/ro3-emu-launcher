using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ManifestTool;
using Ro3Emu.Launcher;

// Release tool for the launcher's signed update manifest. See docs/AUTO_UPDATE.md for the release procedure.
const string Usage = """
	Usage: dotnet run --project tools/ManifestTool -- <command> [options]

	  keygen --out <private-key.pem>
	      New ECDSA P-256 key pair. Writes the private key (refused inside a git repository or over an existing file)
	      and <name>.pub.pem beside it, and prints the public key for launcher.json "manifestPublicKey".
	  build --dist <folder> --out <folder> --version <x.y.z> [--launcher <exe name>] [--config <config.json>]
	        [--sequence <n>] [--name <manifest file name>] [--base-url <url>]
	      Writes <out>/<name> (default manifest.json) and copies each file flat beside it under a content-addressed
	      name (RO3Emu.exe -> RO3Emu.<first 12 hex of sha256>.exe); a published name never changes content.
	      --sequence defaults to the current Unix time, so every build is higher than the previous one.
	  sign --manifest <file> --key <private-key.pem>
	      Writes <file>.sig (base64 DER ECDSA P-256 / SHA-256 signature of the exact manifest bytes).
	  verify --manifest <file> --public-key <public-key.pem | launcher.json>
	      Checks the signature, the schema and the hash of every file published next to the manifest.
	""";

try
{
	if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
	{
		Console.WriteLine(Usage);
		return args.Length == 0 ? 1 : 0;
	}
	Dictionary<string, string> options = ParseOptions(args[1..]);
	switch (args[0])
	{
	case "keygen":
		return Keygen(Required(options, "out"));
	case "build":
		return Build(options);
	case "sign":
		return Sign(Required(options, "manifest"), Required(options, "key"));
	case "verify":
		return Verify(Required(options, "manifest"), Required(options, "public-key"));
	default:
		Console.Error.WriteLine($"Unknown command '{args[0]}'.");
		Console.Error.WriteLine(Usage);
		return 1;
	}
}
catch (Exception ex) when (ex is ToolException or IOException or UnauthorizedAccessException or JsonException or CryptographicException or ManifestException or ArgumentException)
{
	Console.Error.WriteLine("[ERROR] " + ex.Message);
	return 1;
}

static int Keygen(string output)
{
	string path = Path.GetFullPath(output);
	if (ManifestSigner.IsInsideRepository(path))
	{
		throw new ToolException($"refusing to write a private key inside a git repository ({path}); keep it outside every repository");
	}
	if (File.Exists(path))
	{
		throw new ToolException($"{path} already exists; refusing to overwrite a key");
	}
	(string privateKey, string publicKey) = ManifestSigner.GenerateKey();
	Directory.CreateDirectory(Path.GetDirectoryName(path)!);
	File.WriteAllText(path, privateKey);
	string publicPath = Path.ChangeExtension(path, ".pub.pem");
	File.WriteAllText(publicPath, publicKey);
	Console.WriteLine($"Private key written to {path}. Back it up offline; whoever has it can update every launcher.");
	Console.WriteLine($"Public key written to {publicPath} (for verify; not secret).");
	Console.WriteLine();
	Console.WriteLine("Public key (PEM):");
	Console.WriteLine(publicKey);
	Console.WriteLine("launcher.json value:");
	Console.WriteLine($"  \"manifestPublicKey\": {JsonSerializer.Serialize(publicKey.ReplaceLineEndings("\n"), new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })}");
	return 0;
}

static int Build(Dictionary<string, string> options)
{
	JsonObject? config = null;
	if (options.TryGetValue("config", out string? configPath))
	{
		config = JsonNode.Parse(File.ReadAllBytes(configPath), null, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
			?? throw new ToolException($"{configPath} is not a JSON object");
	}
	long sequence = options.TryGetValue("sequence", out string? text)
		? (long.TryParse(text, out long parsed) ? parsed : throw new ToolException($"--sequence '{text}' is not a number"))
		: DateTimeOffset.UtcNow.ToUnixTimeSeconds();
	BuildOptions build = new BuildOptions(
		Required(options, "dist"),
		Required(options, "out"),
		Required(options, "version"),
		sequence,
		options.GetValueOrDefault("launcher"),
		config,
		options.GetValueOrDefault("base-url"),
		options.GetValueOrDefault("name") ?? "manifest.json");
	BuildResult result = ManifestBuilder.Build(build);
	Console.WriteLine($"Manifest {result.ManifestPath} (sequence {sequence}):");
	foreach (string file in result.Included)
	{
		Console.WriteLine("  + " + file);
	}
	foreach (string file in result.Skipped)
	{
		Console.WriteLine("  - " + file + " (not updated by the launcher; left out)");
	}
	Console.WriteLine("Next: sign it with the private key, then verify.");
	return 0;
}

static int Sign(string manifestPath, string keyPath)
{
	byte[] manifest = File.ReadAllBytes(manifestPath);
	RemoteManifest.Parse(manifest);
	string signature = ManifestSigner.Sign(manifest, File.ReadAllText(keyPath));
	File.WriteAllText(manifestPath + ".sig", signature + "\n", new UTF8Encoding(false));
	Console.WriteLine($"Signature written to {manifestPath}.sig");
	return 0;
}

static int Verify(string manifestPath, string publicKeyPath)
{
	string publicKey = File.ReadAllText(publicKeyPath);
	if (publicKeyPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
	{
		publicKey = LauncherConfig.Parse(publicKey).ManifestPublicKey ?? "";
	}
	if (!ManifestSignature.IsKeyConfigured(publicKey))
	{
		throw new ToolException($"{publicKeyPath} holds no PEM public key");
	}
	byte[] manifestBytes = File.ReadAllBytes(manifestPath);
	if (!ManifestSignature.Verify(manifestBytes, File.ReadAllText(manifestPath + ".sig"), publicKey))
	{
		throw new ToolException("the signature does NOT verify with this public key");
	}
	RemoteManifest manifest = RemoteManifest.Parse(manifestBytes);
	string root = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
	int problems = 0;
	List<(string Name, string Url, long Size, string Sha256)> entries = manifest.Files.Select(f => (f.Path, f.Url, f.Size, f.Sha256)).ToList();
	if (manifest.Launcher is { } launcher)
	{
		entries.Add(("launcher " + launcher.Version, launcher.Url, launcher.Size, launcher.Sha256));
	}
	foreach ((string name, string url, long size, string sha256) in entries)
	{
		if (!name.StartsWith("launcher ", StringComparison.Ordinal) && !ManagedFiles.TryNormalizePath(name, out _))
		{
			Console.WriteLine($"  ! {name}: path refused by the launcher");
			problems++;
			continue;
		}
		if (Uri.IsWellFormedUriString(url, UriKind.Absolute))
		{
			Console.WriteLine($"  ? {name}: absolute url {url} not checked");
			continue;
		}
		string local = Path.Combine(root, Uri.UnescapeDataString(url).Replace('/', Path.DirectorySeparatorChar));
		bool ok = ManagedFiles.Matches(local, size, sha256);
		Console.WriteLine($"  {(ok ? "ok" : "! MISMATCH or missing")} {name}");
		problems += ok ? 0 : 1;
	}
	Console.WriteLine(problems == 0
		? $"Verified: signature, schema {manifest.Schema}, sequence {manifest.Sequence}, {entries.Count} file(s)."
		: $"{problems} problem(s) found.");
	return problems == 0 ? 0 : 1;
}

static Dictionary<string, string> ParseOptions(string[] args)
{
	Dictionary<string, string> options = new(StringComparer.OrdinalIgnoreCase);
	for (int i = 0; i < args.Length; i++)
	{
		if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
		{
			throw new ToolException($"expected '--option value', got '{args[i]}'");
		}
		options[args[i][2..]] = args[++i];
	}
	return options;
}

static string Required(Dictionary<string, string> options, string name)
	=> options.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value) ? value : throw new ToolException($"--{name} is required");
