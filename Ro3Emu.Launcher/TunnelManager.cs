using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Ro3Emu.Launcher;

public sealed class TunnelManager : IDisposable
{
    public record TunnelSpec(string Hostname, int Port);

    public static readonly IReadOnlyList<TunnelSpec> Tunnels = new[]
    {
        new TunnelSpec("ro3-http.konsor.com.ar", 5380),
        new TunnelSpec("ro3-micro.konsor.com.ar", 20000),
        new TunnelSpec("ro3-game.konsor.com.ar", 33100)
    };

    private readonly List<Process> _processes = new();
    private readonly Lock _gate = new();
    private bool _disposed;

    public bool IsConnected { get; private set; }

    public static string? FindCloudflared(string? baseDir = null)
    {
        string baseDirectory = baseDir ?? AppContext.BaseDirectory;
        string[] candidates =
        {
            Path.Combine(baseDirectory, "cloudflared.exe"),
            Path.Combine(AppContext.BaseDirectory, "cloudflared.exe"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cloudflared.exe"),
            Path.Combine(baseDirectory, "..", "cloudflared.exe"),
            @"E:\Genesis Launcher - DEV\cloudflared.exe",
            @"E:\Genesis Launcher\cloudflared.exe"
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c)) return Path.GetFullPath(c);
        }
        return null;
    }

    public async Task<bool> StartTunnelsAsync(string? baseDir = null, CancellationToken ct = default, Action<string>? onStatus = null)
    {
        string? cloudflared = FindCloudflared(baseDir);
        if (string.IsNullOrEmpty(cloudflared))
        {
            onStatus?.Invoke("No se encontró cloudflared.exe");
            return false;
        }

        onStatus?.Invoke("Verificando puertos de conexión...");

        foreach (var t in Tunnels)
        {
            if (TestLocalPort(t.Port))
            {
                // Kill stale cloudflared if holding this port
                KillCloudflaredOnPort(t.Port);
                await Task.Delay(500, ct);
            }
        }

        onStatus?.Invoke("Abriendo túneles con el servidor Konsor...");

        lock (_gate)
        {
            StopTunnelsInternal();

            foreach (var t in Tunnels)
            {
                var psi = new ProcessStartInfo
                {
                    FileName = cloudflared,
                    Arguments = $"access tcp --hostname {t.Hostname} --url 127.0.0.1:{t.Port}",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                try
                {
                    var p = Process.Start(psi);
                    if (p != null)
                    {
                        _processes.Add(p);
                    }
                }
                catch (Exception ex)
                {
                    onStatus?.Invoke($"Error iniciando túnel para {t.Hostname}: {ex.Message}");
                    return false;
                }
            }
        }

        // Wait for all 3 ports to be listening
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            bool allOpen = true;
            foreach (var t in Tunnels)
            {
                if (!TestLocalPort(t.Port))
                {
                    allOpen = false;
                    break;
                }
            }

            if (allOpen)
            {
                IsConnected = true;
                onStatus?.Invoke("Conexión con el servidor Konsor establecida.");
                return true;
            }

            await Task.Delay(300, ct);
        }

        onStatus?.Invoke("Tiempo de espera agotado al conectar con el servidor.");
        return false;
    }

    public static bool TestLocalPort(int port)
    {
        try
        {
            using var client = new TcpClient();
            var ar = client.BeginConnect("127.0.0.1", port, null, null);
            return ar.AsyncWaitHandle.WaitOne(300) && client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static void KillCloudflaredOnPort(int port)
    {
        try
        {
            // Terminate existing cloudflared processes if they are running in background
            foreach (var p in Process.GetProcessesByName("cloudflared"))
            {
                try
                {
                    p.Kill();
                    p.WaitForExit(1000);
                }
                catch { }
            }
        }
        catch { }
    }

    public void StopTunnels()
    {
        lock (_gate)
        {
            StopTunnelsInternal();
        }
    }

    private void StopTunnelsInternal()
    {
        foreach (var p in _processes)
        {
            try
            {
                if (!p.HasExited)
                {
                    p.Kill();
                    p.WaitForExit(1000);
                }
                p.Dispose();
            }
            catch { }
        }
        _processes.Clear();
        IsConnected = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopTunnels();
    }
}
