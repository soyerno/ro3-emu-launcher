<#
.SYNOPSIS
  Windows smoke test for the launcher: prerequisites, build, configuration, network and tunnels.

.DESCRIPTION
  Works for any brand: it reads brand\launcher.json when the folder exists, otherwise launcher.json,
  and takes the executable name from the build (AssemblyName).
  It only reads from the network (GET requests, DNS, TCP) and, unless -SkipTunnels is given, opens the
  configured cloudflared tunnels for a few seconds and closes them again. It never signs in, registers,
  or touches a game client.

.PARAMETER SkipBuild    Reuse dist\ instead of publishing again.
.PARAMETER SkipTunnels  Do not start cloudflared; the server check then runs against the configured URL as is.
.PARAMETER Launch       Start the launcher when the automated part ends, wait for you, then show launcher.log.
.PARAMETER GameDir      Game folder (the one with ro3.exe), only used to print the `join` command the launcher will run.
#>
param(
    [switch]$SkipBuild,
    [switch]$SkipTunnels,
    [switch]$Launch,
    [string]$GameDir
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$script:results = New-Object System.Collections.Generic.List[object]
function Add-Result([string]$Status, [string]$Name, [string]$Detail) {
    $script:results.Add([pscustomobject]@{ Status = $Status; Name = $Name; Detail = $Detail })
    $color = @{ PASS = 'Green'; FAIL = 'Red'; WARN = 'Yellow'; INFO = 'Cyan' }[$Status]
    Write-Host ("[{0}] {1}  {2}" -f $Status, $Name, $Detail) -ForegroundColor $color
}

function Test-HttpUrl([string]$Value) {
    $uri = $null
    if ([string]::IsNullOrWhiteSpace($Value)) { return $null }
    if ([Uri]::TryCreate($Value.Trim(), [UriKind]::Absolute, [ref]$uri) -and ($uri.Scheme -eq 'http' -or $uri.Scheme -eq 'https')) { return $uri }
    return $null
}

function Get-Http([string]$Url, [int]$TimeoutSec = 10) {
    try {
        $r = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec $TimeoutSec -Headers @{ 'User-Agent' = 'Ro3Launcher-smoke-test' } -ErrorAction Stop
        return [pscustomobject]@{ Ok = $true; Status = [int]$r.StatusCode; Body = [string]$r.Content; Error = $null }
    } catch {
        $code = 0
        if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode }
        return [pscustomobject]@{ Ok = $false; Status = $code; Body = $null; Error = $_.Exception.Message }
    }
}

function Test-TcpPort([int]$Port, [int]$TimeoutMs = 400) {
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $ar = $client.BeginConnect('127.0.0.1', $Port, $null, $null)
        return ($ar.AsyncWaitHandle.WaitOne($TimeoutMs) -and $client.Connected)
    } catch { return $false } finally { $client.Close() }
}

Write-Host "`n=== 1. Prerequisites ===" -ForegroundColor White
$dotnetVersion = $null
try { $dotnetVersion = (& dotnet --version 2>$null) } catch { }
if ($dotnetVersion -and [int]($dotnetVersion.Split('.')[0]) -ge 10) { Add-Result PASS '.NET SDK' $dotnetVersion }
else { Add-Result FAIL '.NET SDK' 'version 10 or newer is required (dotnet --version)' }

$wv2 = $null
foreach ($key in @('HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'HKCU:\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}')) {
    if (-not $wv2 -and (Test-Path $key)) { $wv2 = (Get-ItemProperty $key).pv }
}
if ($wv2) { Add-Result PASS 'WebView2 Runtime' $wv2 } else { Add-Result FAIL 'WebView2 Runtime' 'not installed; the launcher cannot open its window' }

$appName = $null
try { $appName = (& dotnet msbuild Ro3Launcher.csproj -getProperty:AssemblyName 2>$null | Select-Object -First 1).Trim() } catch { }
if (-not $appName) { $appName = 'Ro3Launcher'; Add-Result WARN 'AssemblyName' 'could not be read from the build, assuming Ro3Launcher' }
else { Add-Result INFO 'AssemblyName' "$appName  (executable: dist\$appName.exe)" }

Write-Host "`n=== 2. Build ===" -ForegroundColor White
$exe = Join-Path $root "dist\$appName.exe"
if ($SkipBuild) {
    Add-Result INFO 'Build' 'skipped (-SkipBuild)'
} else {
    $ErrorActionPreference = 'Continue'   # Windows PowerShell 5.1 turns native stderr lines into errors otherwise
    & dotnet publish Ro3Launcher.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o dist | Out-Host
    $ErrorActionPreference = 'Stop'
    if ($LASTEXITCODE -ne 0) { Add-Result FAIL 'Build' "dotnet publish exited with $LASTEXITCODE" }
}
if (Test-Path $exe) {
    $mb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Add-Result PASS 'Executable' "dist\$appName.exe ($mb MB)"
} else {
    Add-Result FAIL 'Executable' "dist\$appName.exe was not produced"
}

Write-Host "`n=== 3. Configuration ===" -ForegroundColor White
$cfgPath = if (Test-Path (Join-Path $root 'brand\launcher.json')) { Join-Path $root 'brand\launcher.json' } else { Join-Path $root 'launcher.json' }
Add-Result INFO 'launcher.json' $cfgPath
$cfg = $null
try { $cfg = Get-Content $cfgPath -Raw | ConvertFrom-Json } catch { Add-Result FAIL 'launcher.json' "is not valid JSON: $($_.Exception.Message)" }

if ($cfg) {
    $serverUri = Test-HttpUrl $cfg.server
    if ($serverUri) { Add-Result PASS 'server' $serverUri.AbsoluteUri.TrimEnd('/') } else { Add-Result FAIL 'server' "'$($cfg.server)' is not an absolute http(s) URL" }
    $repoUri = Test-HttpUrl $cfg.repository
    if ($repoUri) { Add-Result PASS 'repository' $repoUri.AbsoluteUri } else { Add-Result FAIL 'repository' "'$($cfg.repository)' is not an absolute http(s) URL" }
    Add-Result INFO 'serverName' "$($cfg.serverName)"
    $lang = "$($cfg.language)"
    if ($lang -eq '' -or $lang -match '^(en|es)') { Add-Result PASS 'language' ($(if ($lang) { $lang } else { 'en (default)' })) } else { Add-Result WARN 'language' "'$lang' is not en or es; the launcher falls back to English" }

    foreach ($field in @('website', 'discord', 'statusUrl')) {
        $value = $cfg.$field
        if ([string]::IsNullOrWhiteSpace($value)) { Add-Result INFO $field 'not set'; continue }
        if (-not (Test-HttpUrl $value)) { Add-Result FAIL $field "'$value' is not an absolute http(s) URL"; continue }
        $r = Get-Http $value
        if ($r.Ok) { Add-Result PASS $field "$value -> HTTP $($r.Status)" }
        elseif ($field -eq 'statusUrl') { Add-Result FAIL $field "$value -> HTTP $($r.Status); it must be public (no login) or the launcher ignores it" }
        else { Add-Result WARN $field "$value -> HTTP $($r.Status) $($r.Error)" }
    }
    if ($cfg.statusUrl -and (Test-HttpUrl $cfg.statusUrl)) {
        $r = Get-Http $cfg.statusUrl
        if ($r.Ok) {
            try {
                $j = $r.Body | ConvertFrom-Json
                if ($null -ne $j.enabled -or $null -ne $j.registration_enabled) { Add-Result PASS 'statusUrl body' "enabled=$($j.enabled) registration_enabled=$($j.registration_enabled)" }
                else { Add-Result FAIL 'statusUrl body' 'has neither "enabled" nor "registration_enabled"' }
            } catch { Add-Result FAIL 'statusUrl body' 'is not JSON' }
        }
    }

    $tunnels = @($cfg.tunnels)
    if ($tunnels.Count -eq 0 -or $null -eq $tunnels[0]) {
        $tunnels = @()
        Add-Result INFO 'tunnels' 'none configured: the server is reached directly'
    }
    foreach ($t in $tunnels) {
        $portOk = ($t.port -is [int] -or $t.port -is [long]) -and $t.port -ge 1 -and $t.port -le 65535
        if (-not $portOk) { Add-Result FAIL "tunnel $($t.hostname)" "port '$($t.port)' is not 1-65535"; continue }
        try {
            $dns = Resolve-DnsName -Name $t.hostname -ErrorAction Stop | Where-Object { $_.IPAddress } | Select-Object -First 1
            Add-Result PASS "tunnel $($t.hostname):$($t.port)" "DNS -> $($dns.IPAddress)"
        } catch { Add-Result FAIL "tunnel $($t.hostname):$($t.port)" 'does not resolve in DNS' }
    }
}

Write-Host "`n=== 4. Tunnels and server ===" -ForegroundColor White
$started = New-Object System.Collections.Generic.List[object]
try {
    if ($cfg -and $serverUri) {
        if ($tunnels.Count -gt 0 -and -not $SkipTunnels) {
            $cf = @((Join-Path $root "dist\cloudflared.exe"), (Join-Path $root 'cloudflared.exe')) | Where-Object { Test-Path $_ } | Select-Object -First 1
            if (-not $cf) { $cmd = Get-Command cloudflared.exe -ErrorAction SilentlyContinue; if ($cmd) { $cf = $cmd.Source } }
            if (-not $cf) {
                Add-Result FAIL 'cloudflared.exe' 'not found in dist\, the repository root or PATH; the launcher needs it next to the executable'
            } else {
                Add-Result PASS 'cloudflared.exe' $cf
                foreach ($t in $tunnels) {
                    if (Test-TcpPort $t.port) { Add-Result WARN "port $($t.port)" 'already in use before starting the tunnel (another cloudflared or the server itself?)' }
                    $p = Start-Process -FilePath $cf -ArgumentList @('access', 'tcp', '--hostname', $t.hostname, '--url', "127.0.0.1:$($t.port)") -WindowStyle Hidden -PassThru
                    $started.Add($p)
                }
                $deadline = (Get-Date).AddSeconds(20)
                $pending = @($tunnels | ForEach-Object { $_.port })
                while ($pending.Count -gt 0 -and (Get-Date) -lt $deadline) {
                    $pending = @($pending | Where-Object { -not (Test-TcpPort $_) })
                    if ($pending.Count -gt 0) { Start-Sleep -Milliseconds 400 }
                }
                if ($pending.Count -eq 0) { Add-Result PASS 'tunnels' "all $($tunnels.Count) local ports are listening" }
                else { Add-Result FAIL 'tunnels' "ports still closed after 20 s: $($pending -join ', ')" }
            }
        } elseif ($tunnels.Count -gt 0) {
            Add-Result INFO 'tunnels' 'skipped (-SkipTunnels)'
        }

        $base = $serverUri.AbsoluteUri.TrimEnd('/')
        $r = Get-Http "$base/ro3emu/status"
        if ($r.Ok) { Add-Result PASS 'server status' "$base/ro3emu/status -> HTTP $($r.Status) $($r.Body)" }
        else { Add-Result FAIL 'server status' "$base/ro3emu/status -> HTTP $($r.Status) $($r.Error)" }
        $r = Get-Http "$base/ro3emu/gate-key.pem"
        if ($r.Ok -and $r.Body -match 'BEGIN') { Add-Result PASS 'gate key' "$base/ro3emu/gate-key.pem" } else { Add-Result WARN 'gate key' "$base/ro3emu/gate-key.pem -> HTTP $($r.Status)" }
        if ($repoUri) {
            $news = (New-Object System.Uri -ArgumentList $repoUri, 'news.json').AbsoluteUri
            $r = Get-Http $news
            if ($r.Ok) { Add-Result PASS 'news' $news } else { Add-Result WARN 'news' "$news -> HTTP $($r.Status); the launcher falls back to the local and embedded copies" }
        }
    } else {
        Add-Result INFO 'tunnels and server' 'skipped: the configuration above is invalid'
    }
} finally {
    foreach ($p in $started) { try { if (-not $p.HasExited) { $p.Kill() } } catch { } }
    if ($started.Count -gt 0) { Add-Result INFO 'tunnels' 'closed' }
}

Write-Host "`n=== 5. What the launcher will run ===" -ForegroundColor White
if ($cfg -and $serverUri) {
    $dir = if ($GameDir) { $GameDir } else { '<game folder>' }
    Add-Result INFO 'join' ("RO3Emu.exe join {0} --client `"{1}`" --yes" -f $serverUri.AbsoluteUri.TrimEnd('/'), $dir)
}
$ro3emu = @((Join-Path $root 'dist\RO3Emu.exe'), (Join-Path $root 'RO3Emu.exe')) | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($ro3emu) { Add-Result PASS 'RO3Emu.exe' $ro3emu } else { Add-Result WARN 'RO3Emu.exe' 'not in dist\ or the repository root; copy it next to the launcher before pressing Play' }

if ($Launch) {
    if (Test-Path $exe) {
        Write-Host "`nStarting $exe ..." -ForegroundColor White
        $app = Start-Process -FilePath $exe -PassThru
        Read-Host 'Do the manual checks below, close the launcher, then press Enter'
        try { if (-not $app.HasExited) { $app.CloseMainWindow() | Out-Null } } catch { }
        $log = Join-Path (Split-Path $exe) 'launcher.log'
        if (Test-Path $log) {
            Write-Host "`n--- last lines of $log ---" -ForegroundColor White
            Get-Content $log -Tail 40
        } else {
            Add-Result WARN 'launcher.log' "not found next to $exe"
        }
    } else {
        Add-Result FAIL 'Launch' "$exe does not exist"
    }
}

Write-Host @"

=== Manual checks (about 5 minutes; run again with -Launch to open the launcher) ===
 1. The window title and logo caption show serverName; the website and Discord buttons appear only if configured.
 2. Language: a fresh install follows "language"; changing it in the dropdown also changes sign-in and status messages.
 3. Press Play with a wrong "server" (edit a copy of launcher.json next to the exe): an error names the field.
 4. Press Play with the real server: the tunnel messages name the server, then the game starts.
 5. In launcher.log look for "Calling JoinAndLaunchAsync" and the [RO3Emu] lines: no error, and the URL is yours.
 6. Redeem a gift code in the game: it reaches your server (check the server log).
 7. Create an account and sign in; try a wrong password; the messages are in the selected language.
 8. Account tab: "I forgot my password" and the verification-code field still appear.
 9. Repair button restores the client; closing the game returns the launcher to "ready".
10. Windows Explorer shows the executable icon (brand\app.ico in a branded build).
"@

Write-Host "`n=== Summary ===" -ForegroundColor White
$fail = @($script:results | Where-Object { $_.Status -eq 'FAIL' }).Count
$warn = @($script:results | Where-Object { $_.Status -eq 'WARN' }).Count
$pass = @($script:results | Where-Object { $_.Status -eq 'PASS' }).Count
Write-Host ("PASS {0}   WARN {1}   FAIL {2}" -f $pass, $warn, $fail) -ForegroundColor $(if ($fail -gt 0) { 'Red' } elseif ($warn -gt 0) { 'Yellow' } else { 'Green' })
if ($fail -gt 0) { exit 1 }
