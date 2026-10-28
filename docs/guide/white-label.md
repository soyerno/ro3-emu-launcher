# White-Label Customization Guide

This guide covers rebranding RO3 Launcher for any private server without touching C# source code. Everything below is driven by `launcher.json` (embedded at build time; a `launcher.json` next to the executable overrides it) plus a few assets.

---

## 1. Configuration (`launcher.json`)

```json
{
  "repository": "https://patch.myserver.com/",
  "channel": "live",
  "server": "https://game.myserver.com:5380",
  "serverName": "My Ragnarok Server",
  "language": "en",
  "website": "https://myserver.com",
  "discord": "https://discord.gg/your-invite",
  "statusUrl": "https://myserver.com/status.json",
  "tunnels": [
    { "hostname": "ro3-http.myserver.com", "port": 5380 },
    { "hostname": "ro3-micro.myserver.com", "port": 20000 },
    { "hostname": "ro3-game.myserver.com", "port": 33100 }
  ]
}
```

| Field | Default | What it does |
|---|---|---|
| `server` | `http://127.0.0.1:5380` | Base URL of the RO3Emu server (or its Cloudflare tunnel). The launcher signs in and registers accounts at `<server>/sdk/login`, pings `<server>/ro3emu/status`, and passes this URL to `RO3Emu.exe join`, which patches the client (login, gift codes) to talk to this server. It must be an absolute `http(s)` URL; otherwise sign in, registration and launch stop with an error that names the field. |
| `repository` | `http://127.0.0.1:5380/` | Release repository: `channels/*.json` for auto-updates and `news.json` for announcements. Must be an absolute URL. |
| `channel` | `live` | Patch branch (`live`, `beta`, `test`). |
| `serverName` | `My RO3 Server` | Name shown in the window title, the logo caption, dialogs and e-mails. |
| `language` | `en` | Default language of the interface and of host messages: `en` or `es`. Players can switch it in the launcher (the UI also offers `pt-br`, which falls back to English for texts it does not translate); their choice is remembered and wins over this default. |
| `website`, `discord` | none | Optional `http(s)` URLs. The matching buttons (top bar and news card) are hidden when the field is empty. |
| `statusUrl` | none | Optional public URL (no login required) that returns `{ "enabled": true }` or `{ "registration_enabled": true }`. When it reports `false` the launcher disables the registration form. Without it, registration is assumed open. `status.json` in this repository is a sample of the format. |
| `tunnels` | `[]` | Cloudflare TCP tunnels opened with `cloudflared.exe` before launching, one per `{ hostname, port }`. Leave it empty if players reach the server directly; `cloudflared.exe` is then not needed. |

---

## 2. Visual Identity & Assets

- **Main Logo:** Replace `ui.assets.logo-hero.png` with your transparent server logo. The shipped logo and `app.ico` are a neutral "RO3" placeholder.
- **Windows Executable Icon:** Run `tools\update-logo.cmd <your-image.png>` to automatically produce `app.ico` and `ui.assets.logo-hero.png`.
- **Background & Artwork:**
  - `ui.assets.bg.jpg`: Main launcher background image.
  - `ui.assets.hero-art.jpg`: Character / key visual illustration.
  - `ui.assets.discord-banner.jpg`: Community link banner.

---

## 3. News & Announcements (`news.json`)

The launcher shows, in this order of preference:

1. `news.json` at the root of `repository` (the one you publish with your patches).
2. A `news.json` next to the executable.
3. The `news.json` embedded in the build.

```json
{
  "items": [
    {
      "tag": "WELCOME",
      "color": "blue",
      "title": "Welcome to the Server!",
      "text": "Join our community Discord and download the latest patch.",
      "date": "2026-10-28",
      "link": "https://myserver.com/news/1",
      "linkText": "Read more"
    }
  ]
}
```

`color` is `blue`, `green`, `orange` or `red`. `link` and `linkText` are optional; an item only gets a link button when it has a `link`.

---

## 4. Texts and Languages

Player-facing text lives in two places, each with an English and a Spanish entry:

- **Host messages** (sign-in and registration results, launch status, dialogs, e-mails): `Ro3Emu.Launcher/Strings.cs`. `{server}` expands to `serverName`; `{0}`, `{1}` are arguments. Call `Strings.Get("key", args)`; never write a literal in the code.
- **Interface**: `I18N` and `UI_TEXT` in `ui.app.js`, plus the `data-i18n*` attributes in `ui.index.html`.

To change a wording, edit the entry for each language. To add a language, add its code to `Strings.Normalize` and a column to the table, and add a block to `I18N` and `UI_TEXT`.

---

## 5. Branded Forks (`brand/` overlay)

`main` is the community launcher and stays free of any server's brand. A server that wants its own configuration, news, artwork or executable name keeps a branch or fork of `main` and adds files under [`brand/`](../../brand/README.md) with the same names as the root ones (`launcher.json`, `news.json`, `status.json`, `ui.assets.logo-hero.png`, `app.ico`, ...); the build embeds those instead of the defaults, and an optional `brand/brand.props` sets MSBuild properties such as `AssemblyName`.

A fork only adds files, so it takes the community updates with a plain merge and no conflicts:

```bash
git checkout my-brand
git fetch origin
git merge origin/main
```

The `konsor` branch is an example of such a branded fork.
