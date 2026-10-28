# White-Label Customization Guide

This guide covers rebranding RO3 Launcher for any private server without touching C# source code.

---

## 1. Server Configuration (`launcher.json`)

Configure your server connection in `launcher.json`:

```json
{
  "repository": "https://patch.myserver.com/",
  "channel": "live",
  "server": "https://game.myserver.com:5380",
  "serverName": "My Ragnarok Server"
}
```

- `server`: The HTTP endpoint of the RO3Emu server or Cloudflare tunnel.
- `repository`: The URL where auto-update chunk manifests are hosted.
- `channel`: The patch branch (`live`, `beta`, `test`).
- `serverName`: Display name shown in dialogs and notifications.

---

## 2. Visual Identity & Assets

- **Main Logo:** Replace `ui.assets.logo-hero.png` with your transparent server logo.
- **Windows Executable Icon:** Run `tools\update-logo.cmd <your-image.png>` to automatically produce `app.ico` and `ui.assets.logo-hero.png`.
- **Background & Artwork:**
  - `ui.assets.bg.jpg`: Main launcher background image.
  - `ui.assets.hero-art.jpg`: Character / key visual illustration.
  - `ui.assets.discord-banner.jpg`: Community link banner.

---

## 3. News & Announcements (`news.json`)

Customize the embedded announcements displayed on the launcher feed:

```json
[
  {
    "id": 1,
    "title": "Welcome to the Server!",
    "date": "2026-10-28",
    "tag": "Notice",
    "summary": "Join our community Discord and download the latest patch.",
    "url": "https://myserver.com/news/1"
  }
]
```
