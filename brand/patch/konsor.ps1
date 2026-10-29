# Konsor RO3 launcher: opens the Cloudflare tunnels to the Konsor server, then patches and
# starts the player's own Ragnarok Origin client through RO3Emu's `join`.
#
# The server advertises 127.0.0.1 for its HTTP/gate/scene endpoints, so every player reaches
# them through these local tunnel listeners (cloudflared access tcp) instead of open ports.
$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $here
$cloudflared = Join-Path $here 'cloudflared.exe'
$ro3emu = Join-Path $here 'RO3Emu.exe'
$settingsFile = Join-Path $here 'RO3Emu.settings.json'

$tunnels = @(
    @{ Hostname = 'ro3-http.konsor.com.ar';  Port = 5380 },
    @{ Hostname = 'ro3-micro.konsor.com.ar'; Port = 20000 },
    @{ Hostname = 'ro3-game.konsor.com.ar';  Port = 33100 }
)

function Write-Step([string]$text) { Write-Host "> $text" -ForegroundColor Yellow }
function Write-Fail([string]$text) { Write-Host "  $text" -ForegroundColor Red }

function Test-LocalPort([int]$port) {
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $connect = $client.BeginConnect('127.0.0.1', $port, $null, $null)
        return ($connect.AsyncWaitHandle.WaitOne(300) -and $client.Connected)
    } catch {
        return $false
    } finally {
        $client.Close()
    }
}

function Get-PortOwner([int]$port) {
    # Returns the process listening on 127.0.0.1:<port>, or $null.
    try {
        $conn = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue |
                Select-Object -First 1
        if (-not $conn) { return $null }
        return Get-Process -Id $conn.OwningProcess -ErrorAction SilentlyContinue
    } catch {
        return $null
    }
}

function Test-GameFolder([string]$dir) {
    return $dir -and (Test-Path (Join-Path $dir 'ro3.exe'))
}

# Remembered folder first, then common install locations, then ask the player.
function Find-GameFolder {
    if (Test-Path $settingsFile) {
        try {
            $saved = (Get-Content $settingsFile -Raw | ConvertFrom-Json).ClientDir
            if (Test-GameFolder $saved) { return $saved }
        } catch { }
    }
    $direct = @(
        (Join-Path $here 'ROO_PC'),
        (Join-Path (Split-Path -Parent $here) 'ROO_PC'),
        $here,
        (Split-Path -Parent $here)
    )
    foreach ($d in $direct) {
        if (Test-GameFolder $d) { return $d }
    }
    $relative = @(
        'Genesis Launcher\ROO_PC',
        'Program Files\Genesis Launcher\ROO_PC',
        'Program Files (x86)\Genesis Launcher\ROO_PC',
        'Games\Genesis Launcher\ROO_PC'
    )
    foreach ($drive in Get-PSDrive -PSProvider FileSystem) {
        foreach ($rel in $relative) {
            $candidate = Join-Path $drive.Root $rel
            if (Test-GameFolder $candidate) { return $candidate }
        }
    }
    Add-Type -AssemblyName System.Windows.Forms
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.Description = 'Elige la carpeta del juego (la que contiene ro3.exe)'
    while ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
        if (Test-GameFolder $dialog.SelectedPath) { return $dialog.SelectedPath }
        [System.Windows.Forms.MessageBox]::Show('Esa carpeta no contiene ro3.exe. Prueba de nuevo.') | Out-Null
    }
    return $null
}

$started = @()
try {
    Write-Host ''
    Write-Host 'Konsor - Ragnarok Origin (servidor privado no oficial)' -ForegroundColor Cyan
    Write-Host ''

    foreach ($file in @($cloudflared, $ro3emu)) {
        if (-not (Test-Path $file)) {
            Write-Fail "Falta $(Split-Path -Leaf $file). Vuelve a extraer el zip completo."
            exit 1
        }
    }

    Write-Step 'Buscando la carpeta del juego'
    $gameDir = Find-GameFolder
    if (-not $gameDir) {
        Write-Fail 'No se eligio ninguna carpeta del juego.'
        exit 1
    }
    Write-Host "  $gameDir"

    # If a tunnel's local port is already in use, find who holds it. A stale or
    # duplicate Konsor tunnel (cloudflared) left over from a previous run is killed
    # and restarted fresh; a foreign process is reported (never killed blindly) so we
    # don't take down something unrelated on the player's PC.
    Write-Step 'Conectando con el servidor Konsor'
    foreach ($t in $tunnels) {
        if (Test-LocalPort $t.Port) {
            $owner = Get-PortOwner $t.Port
            if ($owner -and $owner.ProcessName -ieq 'cloudflared') {
                Write-Host "  puerto $($t.Port) ocupado por un tunel anterior (cloudflared PID $($owner.Id)); lo reinicio"
                Stop-Process -Id $owner.Id -Force -ErrorAction SilentlyContinue
                $freeBy = (Get-Date).AddSeconds(8)
                while ((Test-LocalPort $t.Port) -and (Get-Date) -lt $freeBy) { Start-Sleep -Milliseconds 300 }
                if (Test-LocalPort $t.Port) {
                    Write-Fail "No se pudo liberar el puerto $($t.Port). Cierra cloudflared.exe a mano y prueba de nuevo."
                    exit 1
                }
            } else {
                $who = if ($owner) { "$($owner.ProcessName) (PID $($owner.Id))" } else { 'un proceso desconocido' }
                Write-Fail "El puerto $($t.Port) lo esta usando $who, que no es de Konsor. Cierralo y vuelve a intentar."
                exit 1
            }
        }
        $started += Start-Process -FilePath $cloudflared -WindowStyle Hidden -PassThru -ArgumentList @(
            'access', 'tcp', '--hostname', $t.Hostname, '--url', "127.0.0.1:$($t.Port)"
        )
    }
    $deadline = (Get-Date).AddSeconds(20)
    foreach ($t in $tunnels) {
        while (-not (Test-LocalPort $t.Port)) {
            if ((Get-Date) -gt $deadline) {
                Write-Fail 'No se pudo abrir la conexion con el servidor. Revisa tu internet o el antivirus.'
                exit 1
            }
            Start-Sleep -Milliseconds 300
        }
    }
    Write-Host '  conectado'

    Write-Step 'Parcheando e iniciando el juego'
    $ro3emuDir = Join-Path $gameDir 'ro3emu'
    if (-not (Test-Path $ro3emuDir)) { New-Item -ItemType Directory -Path $ro3emuDir -Force | Out-Null }
    if (Test-Path (Join-Path $here 'data\loc_en.tsv')) {
        Copy-Item (Join-Path $here 'data\loc_en.tsv') (Join-Path $ro3emuDir 'loc_en.tsv') -Force
    }
    if (Test-Path (Join-Path $here 'data\en_overrides.tsv')) {
        Copy-Item (Join-Path $here 'data\en_overrides.tsv') (Join-Path $ro3emuDir 'en_overrides.tsv') -Force
    }
    # Spanish map is keyed by Chinese: the client must draw Chinese (PlayerPrefs CurrentLanguage).
    try { if (-not (Test-Path 'HKCU:\Software\gravity\ro3')) { New-Item -Path 'HKCU:\Software\gravity\ro3' | Out-Null }; Set-ItemProperty -Path 'HKCU:\Software\gravity\ro3' -Name 'CurrentLanguage_h413354046' -Value ([Text.Encoding]::UTF8.GetBytes('english' + [char]0)) -Type Binary } catch { }
    & $ro3emu join 'http://127.0.0.1:5380' --client $gameDir --yes
    if ($LASTEXITCODE -ne 0) {
        Write-Fail "RO3Emu termino con error ($LASTEXITCODE)."
        exit $LASTEXITCODE
    }
     = Join-Path  'client\Ro3Emu.Loc.dll'
     = Join-Path  'ro3_Data\Managed\Ro3Emu.Loc.dll'
    if ((Test-Path ) -and (Test-Path (Split-Path -Parent ))) {
        Copy-Item   -Force
    }

    # Keep the tunnels up for as long as the game runs.
    Start-Sleep -Seconds 5
    if (Get-Process -Name 'ro3' -ErrorAction SilentlyContinue) {
        Write-Host '  juego en marcha: no cierres esta ventana mientras juegas'
        while (Get-Process -Name 'ro3' -ErrorAction SilentlyContinue) { Start-Sleep -Seconds 5 }
    }
    Write-Host '  juego cerrado'
} finally {
    foreach ($p in $started) {
        if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    }
}



