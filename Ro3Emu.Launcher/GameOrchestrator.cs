using System;
using System.Collections.Generic;
using System.ComponentModel;
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

    public static async Task<string?> JoinAndLaunchAsync(string gameDir, string? serverUrl, string? baseDir = null, string? loginAccount = null, Action<string>? onLog = null, string? purchaseUrl = null, string? language = null)
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

        string baseDirectory = baseDir ?? Path.GetDirectoryName(ro3emu) ?? AppContext.BaseDirectory;
        string dataDir = Path.Combine(baseDirectory, "data");
        string ro3emuTarget = Path.Combine(gameDir, "ro3emu");
        string clientLocDll = Path.Combine(baseDirectory, "client", "Ro3Emu.Loc.dll");
        string managedLocDll = Path.Combine(gameDir, "ro3_Data", "Managed", "Ro3Emu.Loc.dll");
        string activeLang = (language ?? Strings.Language ?? "es").Trim().ToLowerInvariant();

        void LogLauncher(string message) => AppendLauncherLog(baseDir ?? AppContext.BaseDirectory, message);

        PrepareGameLanguage(gameDir, dataDir, ro3emuTarget, activeLang, clientLocDll, managedLocDll, LogLauncher);

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
            LogLauncher("[RO3Emu] " + line);
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

        // The login account travels in the environment and is never logged; the arguments carry no secret.
        LogLauncher("Starting " + LauncherLog.FormatCommandLine(ro3emu, psi.ArgumentList));
        var clock = Stopwatch.StartNew();
        int pid = 0;
        try
        {
            process.Start();
            pid = process.Id;
            LogLauncher($"RO3Emu.exe started (pid {pid}).");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();
            LogLauncher(LauncherLog.FormatProcessExit("RO3Emu.exe", pid, process.ExitCode, clock.Elapsed));

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

            // Ensure our latest Ro3Emu.Loc.dll and en_overrides.tsv remain active even after RO3Emu join
            if (File.Exists(clientLocDll) && Directory.Exists(Path.GetDirectoryName(managedLocDll)))
            {
                try
                {
                    File.Copy(clientLocDll, managedLocDll, overwrite: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    LogLauncher($"Could not reinstall Ro3Emu.Loc.dll after join: {ex.Message}");
                }
            }
            try
            {
                RestageOverridesAfterJoin(dataDir, ro3emuTarget, activeLang);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogLauncher($"Could not restage the {activeLang} overrides after join: {ex.Message}");
            }

            return null; // Success
        }
        catch (Exception ex)
        {
            LogLauncher($"RO3Emu.exe join failed (pid {pid}, after {LauncherLog.FormatDuration(clock.Elapsed)}): {ex}");
            return Strings.Get("game.error", ex.Message);
        }
    }

    /// <summary>
    /// Before RO3Emu.exe join: stages the language files, sets the client's UI language and installs Ro3Emu.Loc.dll.
    /// Each step runs even when an earlier one fails; failures are reported through <paramref name="log"/>.
    /// </summary>
    public static void PrepareGameLanguage(string gameDir, string dataDir, string ro3emuTarget, string activeLang,
        string clientLocDll, string managedLocDll, Action<string> log, Action<string>? setClientLanguage = null)
    {
        try
        {
            StageLanguageFiles(gameDir, dataDir, ro3emuTarget, activeLang, log);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log($"Could not stage the {activeLang} language files: {ex.Message}");
        }

        // The client's own UI language (Unity PlayerPrefs, HKCU\Software\gravity\ro3).
        // Always set "english" so that all Monsters and NPCs use the official English tables natively.
        // Spanish UI, login screen, menus, and quest titles are applied on top via en_overrides.tsv and Ro3Loc.
        (setClientLanguage ?? (language => SetClientLanguagePref(language, log)))("english");

        if (File.Exists(clientLocDll) && Directory.Exists(Path.GetDirectoryName(managedLocDll)))
        {
            try
            {
                File.Copy(clientLocDll, managedLocDll, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log($"Could not install Ro3Emu.Loc.dll: {ex.Message}");
            }
        }
    }

    private static void AppendLauncherLog(string dir, string message) => LauncherLog.Write(dir, message);

    /// <summary>
    /// Before RO3Emu.exe join: writes language.txt (in ro3emu\ and next to the game; Ro3Loc and the client hooks read it)
    /// and stages the text maps and English overrides of <paramref name="activeLang"/> from the launcher's data\ folder.
    /// </summary>
    public static void StageLanguageFiles(string gameDir, string dataDir, string ro3emuTarget, string activeLang, Action<string>? log = null)
    {
        Directory.CreateDirectory(ro3emuTarget);

        File.WriteAllText(Path.Combine(ro3emuTarget, "language.txt"), activeLang);
        File.WriteAllText(Path.Combine(gameDir, "language.txt"), activeLang);

        if (activeLang.StartsWith("es"))
        {
            // Spanish uses loc_en.tsv and en_overrides.tsv; data\ may ship only those, without the _es copies.
            StageSpanishFile(dataDir, ro3emuTarget, "loc_es.tsv", "loc_en.tsv", log);
            StageSpanishFile(dataDir, ro3emuTarget, "en_overrides_es.tsv", "en_overrides.tsv", log);
        }
        else if (IsPortuguese(activeLang))
        {
            // Ro3Loc reads ro3emu\loc_pt-br.tsv and loc_sid_pt-br.tsv, which RO3Emu.exe join builds from the player's
            // client and copies there, plus its embedded en_overrides_pt-br.tsv and ro3emu\en_overrides_pt-br.tsv. It
            // reads neither loc_en.tsv nor en_overrides.tsv in Portuguese, and a hand-made ro3emu\loc_en.tsv would stop
            // join from refreshing it, so only the published Portuguese overrides are staged.
            CopyPortugueseOverrides(dataDir, ro3emuTarget);
        }
        else
        {
            string locEn = Path.Combine(dataDir, "loc_en_us.tsv");
            if (File.Exists(locEn))
            {
                File.Copy(locEn, Path.Combine(dataDir, "loc_en.tsv"), overwrite: true);
                File.Copy(locEn, Path.Combine(ro3emuTarget, "loc_en.tsv"), overwrite: true);
            }

            string overEn = Path.Combine(dataDir, "en_overrides_en.tsv");
            if (File.Exists(overEn))
            {
                File.Copy(overEn, Path.Combine(dataDir, "en_overrides.tsv"), overwrite: true);
                File.Copy(overEn, Path.Combine(ro3emuTarget, "en_overrides.tsv"), overwrite: true);
            }
            else
            {
                File.WriteAllText(Path.Combine(dataDir, "en_overrides.tsv"), "# en\n");
                File.WriteAllText(Path.Combine(ro3emuTarget, "en_overrides.tsv"), "# en\n");
            }
        }
    }

    /// <summary>
    /// Stages data\<paramref name="spanishName"/> as <paramref name="activeName"/> in data\ and ro3emu\. Without the
    /// Spanish file, the existing data\<paramref name="activeName"/> stays in place (it is never copied onto itself)
    /// and is staged to ro3emu\ as is.
    /// </summary>
    private static void StageSpanishFile(string dataDir, string ro3emuTarget, string spanishName, string activeName, Action<string>? log)
    {
        string spanish = Path.Combine(dataDir, spanishName);
        string active = Path.Combine(dataDir, activeName);
        try
        {
            string source = spanish;
            if (File.Exists(spanish))
            {
                File.Copy(spanish, active, overwrite: true);
            }
            else
            {
                source = active;
                log?.Invoke(File.Exists(active)
                    ? $"{spanishName} not found; keeping {activeName} for Spanish."
                    : $"Neither {spanishName} nor {activeName} found; nothing staged for Spanish.");
            }

            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(ro3emuTarget, activeName), overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"Could not stage {activeName} for Spanish: {ex.Message}");
        }
    }

    /// <summary>After RO3Emu.exe join: puts the launcher's English overrides back in ro3emu\ in case join replaced them.</summary>
    public static void RestageOverridesAfterJoin(string dataDir, string ro3emuTarget, string activeLang)
    {
        if (IsPortuguese(activeLang))
        {
            CopyPortugueseOverrides(dataDir, ro3emuTarget);
            return;
        }

        string overEs = Path.Combine(dataDir, "en_overrides_es.tsv");
        if (!File.Exists(overEs)) overEs = Path.Combine(dataDir, "en_overrides.tsv");
        if (File.Exists(overEs))
        {
            File.Copy(overEs, Path.Combine(ro3emuTarget, "en_overrides.tsv"), overwrite: true);
        }
    }

    /// <summary>language.txt selects Brazilian Portuguese when it starts with "pt" (Ro3Loc.GetConfiguredLanguage).</summary>
    private static bool IsPortuguese(string activeLang) => activeLang.StartsWith("pt", StringComparison.Ordinal);

    /// <summary>Copies the published data\en_overrides_pt-br.tsv, when there is one, to ro3emu\ for Ro3Loc.</summary>
    private static void CopyPortugueseOverrides(string dataDir, string ro3emuTarget)
    {
        string name = "en_overrides_" + Strings.Portuguese + ".tsv";
        string overPt = Path.Combine(dataDir, name);
        if (File.Exists(overPt))
        {
            File.Copy(overPt, Path.Combine(ro3emuTarget, name), overwrite: true);
        }
    }

    private static void SetClientLanguagePref(string clientLanguage, Action<string> log)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\gravity\ro3");
            if (key == null) return;
            byte[] value = System.Text.Encoding.UTF8.GetBytes(clientLanguage + "\0");
            key.SetValue("CurrentLanguage_h413354046", value, Microsoft.Win32.RegistryValueKind.Binary);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            log($"Could not set the client language to {clientLanguage}: {ex.Message}");
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

        string logDir = baseDir ?? AppContext.BaseDirectory;
        AppendLauncherLog(logDir, $"Starting {ro3emu} {psi.Arguments}");
        var clock = Stopwatch.StartNew();
        try
        {
            process.Start();
            int pid = process.Id;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();
            AppendLauncherLog(logDir, LauncherLog.FormatProcessExit("RO3Emu.exe restore", pid, process.ExitCode, clock.Elapsed));

            return process.ExitCode == 0 ? null : Strings.Get("game.restore.exit", process.ExitCode);
        }
        catch (Exception ex)
        {
            AppendLauncherLog(logDir, "RO3Emu.exe restore failed: " + ex);
            return ex.Message;
        }
    }

    /// <summary>True while a game client (ro3.exe) runs, whichever realm it was started for.</summary>
    public static bool IsGameRunning()
    {
        Process[] procs = Process.GetProcessesByName("ro3");
        foreach (Process p in procs) p.Dispose();
        return procs.Length > 0;
    }

    public static async Task MonitorGameProcessAsync(Action onGameExited, CancellationToken ct = default, Action<string>? log = null)
    {
        var watched = new Dictionary<int, WatchedProcess>();
        try
        {
            // Wait a few seconds for ro3 process to appear
            for (int i = 0; i < 20; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (WatchNewGameProcesses(watched, log) > 0) break;
                await Task.Delay(500, ct);
            }
            if (watched.Count == 0)
            {
                log?.Invoke("ro3.exe was not seen within 10 s of the join: the game did not start, or closed at once.");
            }

            // Now monitor while ro3 is running
            while (true)
            {
                WatchNewGameProcesses(watched, log);
                ReportExitedGameProcesses(watched, log);
                if (watched.Count == 0) break;
                await Task.Delay(2000, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            log?.Invoke($"Stopped watching ro3.exe: the launcher is closing ({watched.Count} game process(es) still running).");
            return;
        }
        finally
        {
            foreach (WatchedProcess w in watched.Values) w.Process.Dispose();
        }

        onGameExited();
    }

    private sealed record WatchedProcess(Process Process, DateTime StartedAt);

    /// <summary>Adds the ro3.exe processes not watched yet, holding a handle on each so its exit code stays readable.</summary>
    private static int WatchNewGameProcesses(Dictionary<int, WatchedProcess> watched, Action<string>? log)
    {
        int added = 0;
        foreach (Process p in Process.GetProcessesByName("ro3"))
        {
            if (watched.ContainsKey(p.Id))
            {
                p.Dispose();
                continue;
            }
            DateTime started = DateTime.Now;
            try
            {
                _ = p.SafeHandle;
                started = p.StartTime;
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
            {
                // Not ours to open (another user or elevated): the exit is still noticed, without its code.
            }
            watched[p.Id] = new WatchedProcess(p, started);
            added++;
            log?.Invoke($"ro3.exe detected (pid {p.Id}, started {started:HH:mm:ss}).");
        }
        return added;
    }

    private static void ReportExitedGameProcesses(Dictionary<int, WatchedProcess> watched, Action<string>? log)
    {
        foreach (KeyValuePair<int, WatchedProcess> entry in watched.ToList())
        {
            bool exited;
            try
            {
                exited = entry.Value.Process.HasExited;
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
            {
                exited = !IsGameProcessAlive(entry.Key);
            }
            if (!exited) continue;

            int? code = null;
            try
            {
                code = entry.Value.Process.ExitCode;
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
            {
            }
            log?.Invoke(LauncherLog.FormatProcessExit("ro3.exe", entry.Key, code, DateTime.Now - entry.Value.StartedAt));
            entry.Value.Process.Dispose();
            watched.Remove(entry.Key);
        }
    }

    private static bool IsGameProcessAlive(int pid)
    {
        try
        {
            using Process p = Process.GetProcessById(pid);
            return string.Equals(p.ProcessName, "ro3", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }
}
