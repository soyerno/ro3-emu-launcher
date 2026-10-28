# Architecture & IPC Guide

RO3 Launcher pairs a lightweight native Windows host (.NET 10 WPF) with a modern HTML5/CSS3/JavaScript frontend rendered via Microsoft Edge WebView2.

---

## High-Level Architecture

```
┌────────────────────────────────────────────────────────┐
│                   WebView2 UI Layer                    │
│   (HTML5 + CSS3 + Vanilla JavaScript / Event Loop)     │
└───────────────────────────┬────────────────────────────┘
                            │
               Two-Way JSON Message Bridge
        window.chrome.webview.postMessage(json)
        window.chrome.webview.addEventListener(...)
                            │
┌───────────────────────────▼────────────────────────────┐
│                    C# Host Services                    │
│                                                        │
│  ├── LauncherController (Message Router & State)       │
│  ├── TunnelManager (Cloudflared Daemon Supervision)    │
│  ├── AuthDb & SessionStore (Windows DPAPI + SQLite)    │
│  ├── Strings (EN/ES host messages, launcher.json lang) │
│  └── GameOrchestrator (ro3.exe & Cecil Hooks)          │
└────────────────────────────────────────────────────────┘
```

---

## Two-Way IPC Bridge

Messages sent between the UI and C# host are structured JSON objects containing an `action` and associated payload:

### UI to Host Actions
- `launch`: Requests the orchestrator to launch `ro3.exe` using the current active login account.
- `login`: Sends credentials to authenticate against the server or local session store.
- `register`: Submits account registration details.
- `browse_folder`: Opens a native Windows folder browser dialog to select the game directory.
- `open_url`: Opens external URLs (Discord, Website, Forums) in the default system browser.
- `language`: Tells the host which language the player picked (`{ "cmd": "language", "lang": "es" }`) so host messages follow the UI.

### Host to UI Notifications
- `state_change`: Updates UI state (`checking`, `ready`, `patching`, `playing`, `error`).
- `progress`: Broadcasts download, chunk verification, and patch percentages.
- `news_loaded`: Feeds remote or cached server announcements to the news carousel.

---

## Configuration Hand-off

Before the page loads, the host injects `window.LAUNCHER_CONFIG = { serverName, language, website, discord }` (from `launcher.json`) with `AddScriptToExecuteOnDocumentCreatedAsync`. The UI reads it for the title, logo caption, default language and link buttons; when it is absent (the page opened in a plain browser for UI work) it falls back to neutral defaults.
