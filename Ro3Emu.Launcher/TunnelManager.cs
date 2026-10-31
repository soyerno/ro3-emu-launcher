using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Ro3Emu.Launcher;

public sealed class TunnelManager : IDisposable
{
    private readonly IReadOnlyList<TunnelSpec> _tunnels;
    private readonly List<Process> _processes = new();
    private readonly Lock _gate = new();
    private bool _disposed;

    public TunnelManager(IReadOnlyList<TunnelSpec> tunnels)
    {
        _tunnels = tunnels;
    }

    /// <summary>False when launcher.json lists no tunnels: the server is reached directly.</summary>
    public bool HasTunnels => _tunnels.Count > 0;

    public bool IsConnected { get; private set; }

    public static string? FindCloudflared(string? baseDir = null)
    {
        string baseDirectory = baseDir ?? AppContext.BaseDirectory;
        string[] candidates =
        {
            Path.Combine(baseDirectory, "cloudflared.exe"),
            Path.Combine(AppContext.BaseDirectory, "cloudflared.exe"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cloudflared.exe"),
            Path.Combine(baseDirectory, "..", "cloudflared.exe")
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c)) return Path.GetFullPath(c);
        }
        return null;
    }

    public async Task<bool> StartTunnelsAsync(string? baseDir = null, CancellationToken ct = default, Action<string>? onStatus = null)
    {
        if (!HasTunnels)
        {
            IsConnected = true;
            return true;
        }

        foreach (var t in _tunnels)
        {
            string? invalid = Validate(t);
            if (invalid != null)
            {
                onStatus?.Invoke(invalid);
                return false;
            }
        }

        string? cloudflared = FindCloudflared(baseDir);
        if (string.IsNullOrEmpty(cloudflared))
        {
            onStatus?.Invoke(Strings.Get("tunnel.nocloudflared"));
            return false;
        }

        onStatus?.Invoke(Strings.Get("tunnel.ports"));

        foreach (var t in _tunnels)
        {
            if (TestLocalPort(t.Port))
            {
                // Kill stale cloudflared if holding this port
                KillCloudflaredOnPort(t.Port);
                await Task.Delay(500, ct);
            }
        }

        onStatus?.Invoke(Strings.Get("tunnel.opening"));

        lock (_gate)
        {
            // A realm switch disposes this manager while a start may still be running: start nothing then.
            if (_disposed || ct.IsCancellationRequested) return false;
            StopTunnelsInternal();

            foreach (var t in _tunnels)
            {
                var psi = new ProcessStartInfo
                {
                    FileName = cloudflared,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                foreach (string arg in new[] { "access", "tcp", "--hostname", t.Hostname, "--url", $"127.0.0.1:{t.Port}" })
                {
                    psi.ArgumentList.Add(arg);
                }

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
                    onStatus?.Invoke(Strings.Get("tunnel.error", t.Hostname, ex.Message));
                    return false;
                }
            }
        }

        // Wait for every tunnel port to be listening
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            bool allOpen = true;
            foreach (var t in _tunnels)
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
                onStatus?.Invoke(Strings.Get("tunnel.connected"));
                return true;
            }

            await Task.Delay(300, ct);
        }

        onStatus?.Invoke(Strings.Get("tunnel.timeout"));
        return false;
    }

    private static string? Validate(TunnelSpec t)
    {
        if (Uri.CheckHostName(t.Hostname) != UriHostNameType.Dns || t.Port is < 1 or > 65535)
        {
            return Strings.Get("config.tunnel.invalid", t.Hostname, t.Port);
        }
        return null;
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

public sealed record TunnelSpec(
    [property: JsonPropertyName("hostname")] string Hostname,
    [property: JsonPropertyName("port")] int Port);
