using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Ro3Emu.Launcher;

public sealed class GameOrchestrator
{
    // RO3Emu refuses to patch a Managed DLL that already looks patched when ro3emu-backup has no original to patch from.
    // Only the official files fix that, so the launcher repairs the client and patches again.
    internal const string MissingOriginalMarker = "there is no original in";

    internal static bool NeedsOfficialFiles(IEnumerable<string> lines) =>
        lines.Any(line => line.Contains(MissingOriginalMarker, StringComparison.Ordinal));

    /// <summary>Whether the last <see cref="JoinAndLaunchAsync"/> failed because RO3Emu found no original DLL to patch from.</summary>
    public static bool LastJoinNeedsOfficialFiles { get; private set; }

    public static string? FindRO3Emu(string? baseDir = null)
    {
        string baseDirectory = baseDir ?? AppContext.BaseDirectory;
        string[] candidates =
        {
            Path.Combine(baseDirectory, "RO3Emu.exe"),
            Path.Combine(AppContext.BaseDirectory, "RO3Emu.exe"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RO3Emu.exe"),
            Path.Combine(baseDirectory, "..", "RO3Emu.exe")
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c)) return Path.GetFullPath(c);
        }
        return null;
    }

    public static string? FindSettingsFile(string? baseDir = null)
    {
        string baseDirectory = baseDir ?? AppContext.BaseDirectory;
        string[] candidates =
        {
            Path.Combine(baseDirectory, "RO3Emu.settings.json"),
            Path.Combine(AppContext.BaseDirectory, "RO3Emu.settings.json"),
            Path.Combine(baseDirectory, "..", "RO3Emu.settings.json")
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c)) return Path.GetFullPath(c);
        }
        return Path.Combine(baseDirectory, "RO3Emu.settings.json");
    }

    public static bool IsValidGameDir(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return false;
        return File.Exists(Path.Combine(dir, "ro3.exe"));
    }

    public static string? DetectGameDirectory(string? baseDir = null)
    {
        string settingsFile = FindSettingsFile(baseDir) ?? "";
        if (File.Exists(settingsFile))
        {
            try
            {
                var json = JsonNode.Parse(File.ReadAllText(settingsFile));
                string? saved = json?["ClientDir"]?.GetValue<string>();
                if (IsValidGameDir(saved)) return Path.GetFullPath(saved!);
            }
            catch { }
        }

        string baseDirectory = baseDir ?? AppContext.BaseDirectory;
        string[] directCandidates =
        {
            Path.Combine(baseDirectory, "ROO_PC"),
            Path.Combine(baseDirectory, "..", "ROO_PC"),
            baseDirectory,
            Path.Combine(baseDirectory, "..")
        };

        foreach (var c in directCandidates)
        {
            try
            {
                if (IsValidGameDir(c)) return Path.GetFullPath(c);
            }
            catch { }
        }

        string[] relatives =
        {
            "ROO_PC",
            @"Genesis Launcher\ROO_PC",
            @"Genesis Launcher - DEV\ROO_PC",
            @"Program Files\Genesis Launcher\ROO_PC",
            @"Program Files (x86)\Genesis Launcher\ROO_PC",
            @"Games\Genesis Launcher\ROO_PC"
        };

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady) continue;
            foreach (var rel in relatives)
            {
                try
                {
                    string candidate = Path.Combine(drive.RootDirectory.FullName, rel);
                    if (IsValidGameDir(candidate)) return Path.GetFullPath(candidate);
                }
                catch { }
            }
        }

        return null;
    }

    public static void SaveGameDirectory(string gameDir, string? baseDir = null)
    {
        if (!IsValidGameDir(gameDir)) return;
        string settingsFile = FindSettingsFile(baseDir) ?? Path.Combine(AppContext.BaseDirectory, "RO3Emu.settings.json");

        try
        {
            JsonObject root;
            if (File.Exists(settingsFile))
            {
                try
                {
                    root = JsonNode.Parse(File.ReadAllText(settingsFile)) as JsonObject ?? new JsonObject();
                }
                catch
                {
                    root = new JsonObject();
                }
            }
            else
            {
                root = new JsonObject();
            }

            root["ClientDir"] = gameDir;
            if (!root.ContainsKey("InstalledSequence"))
            {
                root["InstalledSequence"] = new JsonObject();
            }

            File.WriteAllText(settingsFile, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public static async Task<string?> JoinAndLaunchAsync(string gameDir, string? serverUrl, string? baseDir = null, string? loginAccount = null, Action<string>? onLog = null, string? purchaseUrl = null)
    {
        LastJoinNeedsOfficialFiles = false;
        if (!IsValidGameDir(gameDir))
        {
            return Strings.Get("game.noclient");
        }

        if (!LauncherConfig.TryParseHttpUrl(serverUrl, out Uri? server))
        {
            return Strings.Get("config.server.invalid", serverUrl ?? "");
        }

        Uri? purchase = null;
        if (!string.IsNullOrWhiteSpace(purchaseUrl) && !LauncherConfig.TryParseHttpUrl(purchaseUrl, out purchase))
        {
            return Strings.Get("config.purchaseUrl.invalid", purchaseUrl);
        }

        string? ro3emu = FindRO3Emu(baseDir);
        if (string.IsNullOrEmpty(ro3emu))
        {
            return Strings.Get("game.noro3emu");
        }

        SaveGameDirectory(gameDir, baseDir);

        onLog?.Invoke(Strings.Get("game.patching"));

        var psi = new ProcessStartInfo
        {
            FileName = ro3emu,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(ro3emu) ?? gameDir
        };

        foreach (string arg in new[] { "join", server!.AbsoluteUri.TrimEnd('/'), "--client", gameDir, "--yes" })
        {
            psi.ArgumentList.Add(arg);
        }
        if (purchase != null)
        {
            psi.ArgumentList.Add("--purchase-url");
            psi.ArgumentList.Add(purchase.AbsoluteUri);
        }

        if (!string.IsNullOrWhiteSpace(loginAccount))
        {
            psi.Environment["RO3EMU_LOGIN_ACCOUNT"] = loginAccount.Trim();
        }

        var logBuffer = new System.Collections.Generic.List<string>();
        void AppendLog(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            lock (logBuffer)
            {
                if (logBuffer.Count > 50) logBuffer.RemoveAt(0);
                logBuffer.Add(line);
            }
            try
            {
                File.AppendAllText(Path.Combine(baseDir ?? AppContext.BaseDirectory, "launcher.log"), $"[{DateTime.Now:HH:mm:ss}] [RO3Emu] {line}\n");
            }
            catch { }
            onLog?.Invoke(line);
        }

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data)) AppendLog(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data)) AppendLog("[ERR] " + e.Data);
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                string details = "";
                lock (logBuffer)
                {
                    LastJoinNeedsOfficialFiles = NeedsOfficialFiles(logBuffer);
                    if (logBuffer.Count > 0)
                    {
                        var lastItems = logBuffer.FindAll(s => !s.Contains("[UnityMemory]"));
                        int count = Math.Min(3, lastItems.Count);
                        if (count > 0)
                        {
                            details = ": " + string.Join(" | ", lastItems.GetRange(lastItems.Count - count, count));
                        }
                    }
                }
                return Strings.Get("game.exit", process.ExitCode, details);
            }

            return null; // Success
        }
        catch (Exception ex)
        {
            return Strings.Get("game.error", ex.Message);
        }
    }

    public static async Task<string?> RestoreOfficialAsync(string gameDir, string? baseDir = null, Action<string>? onLog = null)
    {
        string? ro3emu = FindRO3Emu(baseDir);
        if (string.IsNullOrEmpty(ro3emu))
        {
            return Strings.Get("game.noro3emu.short");
        }

        onLog?.Invoke(Strings.Get("game.restoring"));

        var psi = new ProcessStartInfo
        {
            FileName = ro3emu,
            Arguments = $"restore --yes --client \"{gameDir}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(ro3emu) ?? gameDir
        };

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data)) onLog?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data)) onLog?.Invoke("[ERR] " + e.Data);
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            return process.ExitCode == 0 ? null : Strings.Get("game.restore.exit", process.ExitCode);
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public static async Task MonitorGameProcessAsync(Action onGameExited, CancellationToken ct = default)
    {
        // Wait a few seconds for ro3 process to appear
        for (int i = 0; i < 20; i++)
        {
            if (ct.IsCancellationRequested) return;
            if (Process.GetProcessesByName("ro3").Length > 0) break;
            await Task.Delay(500, ct);
        }

        // Now monitor while ro3 is running
        while (!ct.IsCancellationRequested)
        {
            var procs = Process.GetProcessesByName("ro3");
            if (procs.Length == 0)
            {
                break;
            }
            await Task.Delay(2000, ct);
        }

        onGameExited();
    }
}
