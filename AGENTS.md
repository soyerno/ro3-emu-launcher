# AGENTS.md — AI Agent & Automation Guidelines

This document outlines architectural conventions, guardrails, and project layout for AI coding agents (Antigravity, Claude Code, Cursor, Copilot) working on `ro3-emu-launcher`.

---

## Architectural Principles

1. **Separation of Concerns:**
   - **Frontend:** WebView2 host running standard HTML5, CSS3, and modern vanilla JavaScript. No heavy frontend build pipelines (React/Vue/Webpack) are required.
   - **Backend:** C# / .NET 10 WPF host providing the Windows native runtime, DPAPI session storage, Cloudflare tunnel management, and RO3Emu join invocation.
   - **IPC Bridge:** Communication between WebView2 and C# uses `chrome.webview.postMessage` / `WebMessageReceived` with structured JSON payloads.

2. **White-Label Integrity:**
   - Server endpoints, channel names, news feeds, and branding must remain dynamically configurable via `launcher.json`.
   - Embedded resources are declared in `Ro3Launcher.csproj`.

3. **Packaging:**
   - Single-file publish command:
     ```powershell
     dotnet publish Ro3Launcher.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o dist
     ```
   - Build outputs and intermediate staging folders must never be checked into Git.

4. **Security:**
   - Never commit API keys, master passwords, or database seeds with real credentials.
   - Credentials stored on the player machine must always use `ProtectedData.Protect` (Windows DPAPI) with `DataProtectionScope.CurrentUser`.
