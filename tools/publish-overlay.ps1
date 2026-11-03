<#
.SYNOPSIS
  Builds a signed konsor.json that ADDS the patch overlay to the live manifest. Local only: nothing is uploaded.

.DESCRIPTION
  1. Checks the overlay's assetmeta.data / ro3.manifest are the shipped client's copies plus added records.
  2. Generates patch/base.json (sha256 of the shipped copies) for the join stale-index guard.
  3. Stages the overlay and runs `ManifestTool add` against the live manifest (existing entries untouched, only
     sequence/generated change), then `sign` and `verify`.
  4. Re-checks every staged file's hash against the new manifest and that the live entries are preserved.
  5. Writes UPLOAD.txt (what to upload where). Executes nothing remote.

  Runs on Windows PowerShell 5.1. See docs/AUTO_UPDATE.md.
#>
[CmdletBinding()]
param(
	[Parameter(Mandatory)][string]$OverlayDir,
	[Parameter(Mandatory)][string]$ClientDir,
	[Parameter(Mandatory)][string]$LiveManifestDir,
	[Parameter(Mandatory)][string]$OutDir,
	[string]$KeyPath = 'C:\keys\konsor-manifest.pem',
	[string]$BaseUrl = 'https://konsor.com.ar/launcher',
	[string]$RemoteDir = 'D:\ro3emu\ro3web\site\launcher\'
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function Fail([string]$message) { throw "publish-overlay: $message" }

function Get-Sha256([string]$path) {
	$stream = [System.IO.File]::OpenRead($path)
	try {
		$sha = [System.Security.Cryptography.SHA256]::Create()
		try { return ([BitConverter]::ToString($sha.ComputeHash($stream)) -replace '-', '').ToLowerInvariant() }
		finally { $sha.Dispose() }
	}
	finally { $stream.Dispose() }
}

Add-Type -TypeDefinition @'
public static class OverlayCheck
{
	public static bool RangeEquals(byte[] a, int aOffset, byte[] b, int bOffset, int count)
	{
		if (count < 0 || aOffset < 0 || bOffset < 0 || aOffset + count > a.Length || bOffset + count > b.Length) { return false; }
		for (int i = 0; i < count; i++) { if (a[aOffset + i] != b[bOffset + i]) { return false; } }
		return true;
	}
}
'@

# The overlay indexes are the shipped ones with records ADDED (tools/w/overlay.py: write_assetmeta / write_manifest in
# c01_index.py), not a byte prefix: a u32 record count leads each file and new bundle records go before the
# "bytes"/"raw" sections. Returns $null when the overlay extends the shipped bytes, else a description of the mismatch.
function Test-ExtendsShipped([string]$name, [string]$overlayPath, [string]$shippedPath) {
	$o = [System.IO.File]::ReadAllBytes($overlayPath)
	$s = [System.IO.File]::ReadAllBytes($shippedPath)
	if ($o.Length -lt $s.Length -or $s.Length -lt 4) { return "overlay ($($o.Length) bytes) is shorter than the shipped file ($($s.Length) bytes)" }
	$oCount = [BitConverter]::ToUInt32($o, 0)
	$sCount = [BitConverter]::ToUInt32($s, 0)
	if ($oCount -lt $sCount) { return "record count shrank ($sCount -> $oCount)" }
	if ($name -eq 'ro3.manifest') {
		# count, then entries appended at the end
		if (-not [OverlayCheck]::RangeEquals($o, 4, $s, 4, $s.Length - 4)) { return 'shipped entries are not preserved verbatim after the count' }
		return $null
	}
	# assetmeta.data: u32 N, N x 27-byte bundle records, then the bytes/raw sections (unchanged)
	$sBundleEnd = 4 + 27 * [int]$sCount
	$oBundleEnd = 4 + 27 * [int]$oCount
	if ($sBundleEnd -gt $s.Length -or $oBundleEnd -gt $o.Length) { return 'bundle section runs past the end of the file' }
	if (-not [OverlayCheck]::RangeEquals($o, 4, $s, 4, $sBundleEnd - 4)) { return 'shipped bundle records are not preserved verbatim' }
	if (($o.Length - $oBundleEnd) -ne ($s.Length - $sBundleEnd)) { return 'the bytes/raw sections changed length' }
	if (-not [OverlayCheck]::RangeEquals($o, $oBundleEnd, $s, $sBundleEnd, $s.Length - $sBundleEnd)) { return 'the bytes/raw sections differ from the shipped file' }
	return $null
}

$repo = Split-Path -Parent $PSScriptRoot
$OverlayDir = (Resolve-Path -LiteralPath $OverlayDir).Path
$ClientDir = (Resolve-Path -LiteralPath $ClientDir).Path
$LiveManifestDir = (Resolve-Path -LiteralPath $LiveManifestDir).Path
$liveManifest = Join-Path $LiveManifestDir 'konsor.json'
if (-not (Test-Path -LiteralPath $liveManifest)) { Fail "$liveManifest not found (download the live konsor.json and konsor.json.sig first)" }
if (-not (Test-Path -LiteralPath $KeyPath)) { Fail "signing key not found at $KeyPath" }
if (Test-Path -LiteralPath $OutDir) {
	if (@(Get-ChildItem -LiteralPath $OutDir -Force).Count -gt 0) { Fail "$OutDir is not empty; pass a new empty directory" }
}
else { New-Item -ItemType Directory -Path $OutDir | Out-Null }
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path

# 1. The overlay indexes must be the shipped files plus appended records.
$vfs = Join-Path $ClientDir 'ro3_Data\StreamingAssets\VFS'
$base = [ordered]@{}
foreach ($name in 'assetmeta.data', 'ro3.manifest') {
	$shipped = Join-Path $vfs $name
	$overlay = Join-Path $OverlayDir $name
	if (-not (Test-Path -LiteralPath $shipped)) { Fail "shipped file missing: $shipped" }
	if (-not (Test-Path -LiteralPath $overlay)) { Fail "overlay file missing: $overlay" }
	$problem = Test-ExtendsShipped $name $overlay $shipped
	if ($problem) {
		Fail "$name in the overlay does not extend the shipped $shipped : $problem (overlay $((Get-Item -LiteralPath $overlay).Length) bytes, shipped $((Get-Item -LiteralPath $shipped).Length) bytes). It was built against a different client version; rebuild the overlay against this client."
	}
	$base[$name] = Get-Sha256 $shipped
}
Write-Host "Overlay indexes extend the shipped client files (assetmeta.data $($base['assetmeta.data'].Substring(0, 12)), ro3.manifest $($base['ro3.manifest'].Substring(0, 12)))."

# 2 + 3. Stage the overlay as manifest paths under <work>\patch.
$work = Join-Path ([System.IO.Path]::GetTempPath()) ('publish-overlay-' + [Guid]::NewGuid().ToString('N'))
$stage = Join-Path $work 'patch'
New-Item -ItemType Directory -Path $stage | Out-Null
try {
	$bundleCount = 0
	foreach ($file in Get-ChildItem -LiteralPath $OverlayDir -Recurse -File) {
		$relative = $file.FullName.Substring($OverlayDir.Length).TrimStart('\', '/').Replace('\', '/')
		$isIndex = $relative -in 'assetmeta.data', 'ro3.manifest', 'patched.data'
		$isBundle = $relative -match '^[0-9a-f]{2}/[0-9a-f]{16}\.bundle$'
		if (-not ($isIndex -or $isBundle)) { Fail "unexpected overlay file '$relative' (allowed: patch/<hh>/<16hex>.bundle, assetmeta.data, ro3.manifest, patched.data)" }
		if ($isBundle) { $bundleCount++ }
		$target = Join-Path $stage $relative.Replace('/', '\')
		New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
		Copy-Item -LiteralPath $file.FullName -Destination $target
	}
	$baseJson = '{"assetmeta.data":"' + $base['assetmeta.data'] + '","ro3.manifest":"' + $base['ro3.manifest'] + '"}'
	[System.IO.File]::WriteAllText((Join-Path $stage 'base.json'), $baseJson, (New-Object System.Text.UTF8Encoding($false)))

	$tool = Join-Path $repo 'tools\ManifestTool'
	function Invoke-Tool([string[]]$toolArgs) {
		& dotnet run --project $tool -c Release --no-launch-profile -- @toolArgs
		if ($LASTEXITCODE -ne 0) { Fail "ManifestTool $($toolArgs[0]) failed (exit $LASTEXITCODE)" }
	}

	Invoke-Tool @('add', '--dist', $work, '--out', $OutDir, '--base-manifest', $liveManifest, '--base-url', $BaseUrl, '--name', 'konsor.json')
	$newManifest = Join-Path $OutDir 'konsor.json'

	# 4. Sign (key by path only), verify, then check hashes independently of the tool.
	Invoke-Tool @('sign', '--manifest', $newManifest, '--key', $KeyPath)
	$publicKey = [System.IO.Path]::ChangeExtension($KeyPath, '.pub.pem')
	if (-not (Test-Path -LiteralPath $publicKey)) { Fail "public key $publicKey not found; cannot verify the signature" }
	Invoke-Tool @('verify', '--manifest', $newManifest, '--public-key', $publicKey)

	$live = Get-Content -LiteralPath $liveManifest -Raw -Encoding UTF8 | ConvertFrom-Json
	$new = Get-Content -LiteralPath $newManifest -Raw -Encoding UTF8 | ConvertFrom-Json
	if ($new.version -ne $live.version) { Fail "version changed ($($live.version) -> $($new.version))" }
	if ($new.sequence -le $live.sequence) { Fail "sequence did not advance ($($live.sequence) -> $($new.sequence))" }
	if (($live.launcher | ConvertTo-Json -Compress) -ne ($new.launcher | ConvertTo-Json -Compress)) { Fail 'launcher entry changed' }
	if (($live.config | ConvertTo-Json -Compress) -ne ($new.config | ConvertTo-Json -Compress)) { Fail 'config changed' }
	$liveEntries = @($live.files)
	$newEntries = @($new.files)
	for ($i = 0; $i -lt $liveEntries.Count; $i++) {
		if (($liveEntries[$i] | ConvertTo-Json -Compress) -ne ($newEntries[$i] | ConvertTo-Json -Compress)) { Fail "existing entry '$($liveEntries[$i].path)' was not preserved" }
	}
	$added = @($newEntries | Select-Object -Skip $liveEntries.Count)
	$problems = 0
	foreach ($entry in $added) {
		$published = Join-Path $OutDir ($entry.url.Substring($entry.url.LastIndexOf('/') + 1))
		if (-not (Test-Path -LiteralPath $published)) { Write-Host "  ! missing $published"; $problems++; continue }
		$item = Get-Item -LiteralPath $published
		if ($item.Length -ne $entry.size -or (Get-Sha256 $published) -ne $entry.sha256) { Write-Host "  ! hash/size mismatch $($entry.path)"; $problems++ }
	}
	if ($problems -gt 0) { Fail "$problems staged file(s) do not match the new manifest" }
	Write-Host "Preserved $($liveEntries.Count) live entr(y/ies) byte-identical; $($added.Count) overlay entr(y/ies) added and hash-checked."

	# 5. UPLOAD.txt: instructions only; nothing is executed.
	$lines = New-Object System.Collections.Generic.List[string]
	$lines.Add('Upload plan (NOT executed). Destination host: prod Windows host, directory ' + $RemoteDir)
	$lines.Add('Order matters: upload every published file FIRST, then konsor.json.sig, then konsor.json LAST (launchers read the manifest first).')
	$lines.Add('Needs explicit OK from Erno for destination + operation before any ssh/scp.')
	$lines.Add('')
	$lines.Add("Manifest sequence: $($live.sequence) -> $($new.sequence), version $($new.version) (unchanged), files $($liveEntries.Count) -> $($newEntries.Count)")
	$lines.Add('')
	$lines.Add('1) Published files (new, immutable names):')
	foreach ($entry in $added) { $lines.Add('   ' + $entry.url.Substring($entry.url.LastIndexOf('/') + 1) + '  <- ' + $entry.path + '  (' + $entry.size + ' bytes)') }
	$lines.Add('2) konsor.json.sig')
	$lines.Add('3) konsor.json (last)')
	$lines.Add('')
	$lines.Add("Total: $($added.Count) published files + sig + manifest, into $RemoteDir")
	$lines.Add('Rollback: re-upload the previous konsor.json + konsor.json.sig (the live copies you downloaded); published names are never overwritten.')
	$lines.Add('Note: the web /launcher/ route serves only names ending in exe|dll|tsv|txt|zip; the .txt names above satisfy it.')
	[System.IO.File]::WriteAllLines((Join-Path $OutDir 'UPLOAD.txt'), $lines.ToArray(), (New-Object System.Text.UTF8Encoding($false)))
	Write-Host "Done. Output in $OutDir ($bundleCount bundles). Nothing was uploaded."
}
finally {
	if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
}
