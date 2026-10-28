# AGENTS.md — AI Agent & Automation Guidelines

This document outlines architectural conventions, guardrails, and project layout for AI coding agents (Antigravity, Claude Code, Cursor, Copilot) working on `ro3-emu-launcher`.

---

## Architectural Principles

1. **Separation of Concerns:**
   - **Frontend:** WebView2 host running standard HTML5, CSS3, and modern vanilla JavaScript. No heavy frontend build pipelines (React/Vue/Webpack) are required.
   - **Backend:** C# / .NET 10 WPF host providing the Windows native runtime, DPAPI session storage, Cloudflare tunnel management, and RO3Emu join invocation.
   - **IPC Bridge:** Communication between WebView2 and C# uses `chrome.webview.postMessage` / `WebMessageReceived` with structured JSON payloads.

2. **White-Label Integrity:**
   - Server endpoints, channel names, news feeds, tunnels, language, links, and branding must remain dynamically configurable via `launcher.json`. Never hardcode a host, port, server name or brand in C#, JavaScript, scripts or CI; read the URL from `LauncherConfig` (`ServerBaseUrl`), which validates it.
   - Player-facing text has an English and a Spanish entry: `Ro3Emu.Launcher/Strings.cs` for the host (`Strings.Get("key")`, never an inline literal), `I18N` / `UI_TEXT` and the `data-i18n*` attributes for the UI. Developer logs (`launcher.log`) are plain English.
   - Embedded resources are declared in `Ro3Launcher.csproj`.
   - `main` carries no server brand; branded forks live in their own branch (for example `konsor`).

3. **Packaging:**
   - Single-file publish command:
     ```powershell
     dotnet publish Ro3Launcher.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o dist
     ```
   - Build outputs and intermediate staging folders must never be checked into Git.

4. **Security:**
   - Never commit API keys, master passwords, or database seeds with real credentials.
   - Credentials stored on the player machine must always use `ProtectedData.Protect` (Windows DPAPI) with `DataProtectionScope.CurrentUser`.
