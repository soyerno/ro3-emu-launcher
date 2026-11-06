using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
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

    /// <summary>Why the last <see cref="StartTunnelsAsync"/> failed, in the player's language; null when it did not fail.</summary>
    public string? LastError { get; private set; }

    /// <summary>True when the last start failed because another program listens on a tunnel port.</summary>
    public bool PortInUse { get; private set; }

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
        LastError = null;
        PortInUse = false;
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
                return Fail(invalid);
            }
        }

        string? cloudflared = FindCloudflared(baseDir);
        if (string.IsNullOrEmpty(cloudflared))
        {
            return Fail(Strings.Get("tunnel.nocloudflared"));
        }

        onStatus?.Invoke(Strings.Get("tunnel.ports"));

        foreach (var t in _tunnels)
        {
            if (!TestLocalPort(t.Port))
            {
                continue;
            }

            // A stale cloudflared (an earlier launcher run) is ours to replace. Anything else, such as a local test
            // server, keeps the port: the game would then reach that program instead of the realm, so stop here.
            PortOwner? owner = FindPortOwner(t.Port);
            if (owner == null || owner.IsCloudflared)
            {
                KillCloudflaredOnPort(t.Port);
                await Task.Delay(500, ct);
                if (!TestLocalPort(t.Port))
                {
                    continue;
                }
                owner = FindPortOwner(t.Port);
            }

            string holder = owner == null ? Strings.Get("tunnel.portbusy.unknown") : $"{owner.ProcessName} (PID {owner.ProcessId})";
            PortInUse = true;
            return Fail(Strings.Get("tunnel.portbusy", t.Port, holder));
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
                    return Fail(Strings.Get("tunnel.error", t.Hostname, ex.Message));
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

        return Fail(Strings.Get("tunnel.timeout"));

        bool Fail(string message)
        {
            LastError = message;
            onStatus?.Invoke(message);
            return false;
        }
    }

    /// <summary>The process listening on <paramref name="port"/> at 127.x.x.x or any IPv4 address; null when none or unknown.</summary>
    public static PortOwner? FindPortOwner(int port)
    {
        const int AfInet = 2;
        const int TcpTableOwnerPidListener = 3;
        const uint ErrorInsufficientBuffer = 122;
        const int RowSize = 24; // MIB_TCPROW_OWNER_PID: state, local addr, local port, remote addr, remote port, pid
        try
        {
            int size = 0;
            GetExtendedTcpTable(IntPtr.Zero, ref size, false, AfInet, TcpTableOwnerPidListener, 0);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                IntPtr table = Marshal.AllocHGlobal(size);
                try
                {
                    uint result = GetExtendedTcpTable(table, ref size, false, AfInet, TcpTableOwnerPidListener, 0);
                    if (result == ErrorInsufficientBuffer)
                    {
                        continue; // the table grew between the two calls; size now holds the new length
                    }
                    if (result != 0)
                    {
                        return null;
                    }

                    int count = Marshal.ReadInt32(table);
                    for (int i = 0; i < count; i++)
                    {
                        IntPtr row = table + 4 + i * RowSize;
                        uint localAddr = (uint)Marshal.ReadInt32(row, 4);
                        int rawPort = Marshal.ReadInt32(row, 8);
                        int localPort = ((rawPort & 0xFF) << 8) | ((rawPort >> 8) & 0xFF); // network byte order
                        bool loopbackReachable = localAddr == 0 || (localAddr & 0xFF) == 127;
                        if (localPort == port && loopbackReachable)
                        {
                            int pid = Marshal.ReadInt32(row, 20);
                            return new PortOwner(pid, ProcessNameOf(pid));
                        }
                    }
                    return null;
                }
                finally
                {
                    Marshal.FreeHGlobal(table);
                }
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or OutOfMemoryException)
        {
        }
        return null;
    }

    private static string ProcessNameOf(int pid)
    {
        try
        {
            using Process p = Process.GetProcessById(pid);
            return p.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return "?";
        }
    }

    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int addressFamily, int tableClass, uint reserved);

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

/// <summary>A process listening on a local TCP port.</summary>
public sealed record PortOwner(int ProcessId, string ProcessName)
{
    public bool IsCloudflared => string.Equals(ProcessName, "cloudflared", StringComparison.OrdinalIgnoreCase);
}

public sealed record TunnelSpec(
    [property: JsonPropertyName("hostname")] string Hostname,
    [property: JsonPropertyName("port")] int Port);
