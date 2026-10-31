# Automatic updates (signed manifest)

The launcher can update itself, the server files that sit beside it, and parts of its configuration, without the player doing anything. Everything it applies comes from one **signed manifest**. A file that is not listed in a manifest signed with your key never reaches a player.

The feature is off unless `launcher.json` sets both `manifestUrl` and `manifestPublicKey`. The white-label default in `main` leaves them out.

## What happens at start

Before the tunnels start, the launcher:

1. Downloads `manifestUrl` and `manifestUrl + ".sig"` over HTTPS, with a 5-second limit.
2. Accepts the manifest only if:
   - the signature verifies with the pinned `manifestPublicKey`,
   - `schema` is `1`,
   - `sequence` is not lower than the cached manifest's (rollback protection).
3. Saves an accepted manifest to `%LOCALAPPDATA%\RO3Emu\manifest\<hash of the url>\`. If the download fails or the manifest is refused, the launcher uses the last cached manifest. With no cache, it keeps its own `launcher.json`.
4. Updates the launcher first if `launcher.version` is newer than the running version. It downloads the exe, checks its size and SHA-256, swaps it in (the old exe becomes `*.old`) and restarts.
5. Otherwise it updates the server files, then applies the manifest's `config`.

Errors only go to `launcher.log`. The player keeps whatever files and configuration they had. The only player-facing notice is when the launcher folder is read-only.

## Manifest format

```json
{
  "schema": 1,
  "sequence": 1767225600,
  "launcher": { "version": "1.4.0", "url": "KonsorRO.<sha12>.exe", "sha256": "<hex>", "size": 123 },
  "files": [
    { "path": "client/Ro3Emu.Loc.dll", "url": "Ro3Emu.Loc.<sha12>.dll", "sha256": "<hex>", "size": 456 }
  ],
  "config": { "realms": [], "defaultRealm": "prontera", "newsUrl": "https://example.com/news.json" }
}
```

- **url**: relative to the manifest url, or absolute.
- **signature**: `<manifest>.sig` holds one line of base64. It is a DER (RFC 3279) ECDSA P-256 signature, using SHA-256, over the exact bytes of the manifest file. If you edit the manifest after signing it, the signature no longer verifies.
- **files[].path**: the launcher only accepts these paths, relative to its own folder:

  | Path | Accepted |
  |---|---|
  | `RO3Emu.exe`, `cloudflared.exe` | Exact names, in the launcher folder |
  | `client/<name>.dll` | One level only |
  | `data/<name>.tsv` | One level only |

  It refuses everything else, including the launcher exe, `launcher.json`, subfolders, `..`, rooted paths and drive letters. A refused entry is logged and skipped. Leave `data/loc_en.tsv` and `data/en_overrides.tsv` out: they are rewritten at each Play. Publish their sources instead (`loc_es.tsv`, `loc_en_us.tsv`, `en_overrides_es.tsv`, `en_overrides_en.tsv`).
- **config**: may set only `realms`, `defaultRealm`, `tunnels`, `server`, `statusUrl`, `newsUrl`, `website`, `discord` and `purchaseUrl`. These values win over the embedded `launcher.json` and the `launcher.json` beside the exe. A manifest can never change `manifestUrl`, `manifestPublicKey` or `repository`.

## Version stamping

The launcher compares `launcher.version` with the version stamped at publish:

- **Release tags**: `release.yml` passes `-p:Version=<tag without prefix>`. For example, `konsor-v1.4.0` becomes `1.4.0`.
- **Local releases**: `publish.cmd 1.4.0` does the same by hand.
- **Builds without a version**: the version is `0.0.0-dev`. This includes `dotnet build`, `publish.cmd` with no argument and pull-request builds.

A version with a prerelease suffix (`-dev`, `-rc.1`), or `0.0.0`, counts as a **development build**. A development build never replaces itself with a published launcher. It still updates the server files and the configuration.

## Release tool

`tools/ManifestTool` is a .NET console. You only need the .NET 10 SDK. It reuses the launcher's own parsing, path rules and signature check.

```bat
dotnet run --project tools\ManifestTool -c Release -- keygen --out C:\keys\konsor-manifest.pem
dotnet run --project tools\ManifestTool -c Release -- build --dist <folder> --out <folder> --version 1.4.0 --launcher KonsorRO.exe --config <config.json> --name konsor.json
dotnet run --project tools\ManifestTool -c Release -- sign --manifest <out>\konsor.json --key C:\keys\konsor-manifest.pem
dotnet run --project tools\ManifestTool -c Release -- verify --manifest <out>\konsor.json --public-key brand\launcher.json
```

- **keygen**: writes the private key and `<name>.pub.pem` beside it (the public key, for `verify`), and prints the `manifestPublicKey` value. It refuses to write the key inside a git repository or over an existing file. `.gitignore` also ignores `*.pem` and `*.key`. Back the private key up offline: anyone who has it can update every launcher.
- **build**: copies each file flat beside the manifest under a content-addressed name, `<name>.<first 12 hex of its sha256>.<ext>` (for example `RO3Emu.3f2a9c01b7de.exe`), and points `url` at it. Different bytes always get a different name, so a published name never changes content. The `/launcher/` route serves these names with `Cache-Control: public, max-age=31536000, immutable`. It only serves top-level names matching `^[A-Za-z0-9._-]+\.(exe|dll|tsv|txt|zip)$`, with no subfolders, and the tool refuses any other name. Only the manifest and its `.sig` are served with `no-cache`. `--sequence` defaults to the current Unix time.

## First release (operator steps)

1. **Create the key**, outside every repository. Paste the printed `"manifestPublicKey"` value into `brand/launcher.json` and remove the placeholder `manifestPublicKeyNote`. Commit that change. The public key is not secret.

   ```bat
   dotnet run --project tools\ManifestTool -c Release -- keygen --out C:\keys\konsor-manifest.pem
   ```

2. **Build the launcher with a version**: tag `konsor-v1.4.0` and use the CI artifact, or run `publish.cmd 1.4.0`. Players need this signed-key build once, through the usual zip. After that, updates arrive automatically.
3. **Assemble a dist folder.** Put the launcher exe in it, plus the Konsor files: `RO3Emu.exe`, `cloudflared.exe`, `client\Ro3Emu.Loc.dll`, and a `client\Ro3Emu.ClientHooks.dll` rebuilt with `pack.ps1 -UpdatePrebuiltClientDlls`. Add the `data\*.tsv` sources.
4. **Write `config.json`** with the remote configuration, such as realms, tunnels and newsUrl. It may only contain the fields listed above.
5. **Build, sign and verify:**

   ```bat
   dotnet run --project tools\ManifestTool -c Release -- build --dist dist-konsor --out out-konsor --version 1.4.0 --launcher KonsorRO.exe --config config.json --name konsor.json
   dotnet run --project tools\ManifestTool -c Release -- sign --manifest out-konsor\konsor.json --key C:\keys\konsor-manifest.pem
   dotnet run --project tools\ManifestTool -c Release -- verify --manifest out-konsor\konsor.json --public-key brand\launcher.json
   ```

6. **Upload by hand.** Copy the *contents* of `out-konsor\` to the site's static root, at `launcher\` (`D:\ro3emu\ro3web\site\launcher\`). Upload the content-addressed files first and `konsor.json` + `konsor.json.sig` last, so a player never gets a manifest whose files are missing. The web server must serve `/launcher/konsor.json` and `.sig` with `Cache-Control: no-cache`.
7. **Check**: start a launcher of the previous version. `launcher.log` should show `Update: manifest sequence N verified.`

## Later releases

Repeat steps 3 to 6. Each build gets a higher sequence. Older content-addressed files can stay: they cost nothing and keep older manifests valid while caches refresh.
