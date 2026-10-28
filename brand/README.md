# brand/ — branded fork overlay

This folder is empty in `main`. A server that wants its own look keeps its branch or fork of `main` and adds files here; the build uses them instead of the defaults. Because a fork only **adds** files under `brand/` and never edits files that `main` owns, merging `main` into the fork never conflicts.

## What you can override

Put a file here with the **same name** as the one in the repository root and it replaces it in the build:

| File in `brand/` | Replaces |
|---|---|
| `launcher.json` | `launcher.json` (server, serverName, language, links, tunnels...) |
| `news.json` | `news.json` |
| `status.json` | `status.json` |
| `ui.assets.logo-hero.png` | `ui.assets.logo-hero.png` |
| `ui.assets.hero-art.jpg` | `ui.assets.hero-art.jpg` |
| `ui.assets.discord-banner.jpg` | `ui.assets.discord-banner.jpg` |
| `app.ico` | `app.ico` (the executable icon) |
| `brand.props` | optional MSBuild properties, e.g. the executable name |

`brand.props` example, to keep an existing executable name:

```xml
<Project>
  <PropertyGroup>
    <AssemblyName>MyServer</AssemblyName>
  </PropertyGroup>
</Project>
```

`build.cmd`, `publish.cmd`, `deploy-dev.cmd`, `run-ui-dev.cmd` and `tools\package-patch.cmd` read the executable name from the build, so they keep working after a rename.

## Keeping a fork up to date

```bash
git checkout my-brand
git fetch origin
git merge origin/main
```

When `main` adds a new `launcher.json` field, copy it into `brand/launcher.json` if you want a value other than the default; a missing field simply uses the default.

## Creating a fork branch

```bash
git checkout -b my-brand origin/main
# add brand/launcher.json, brand/news.json, brand/ui.assets.logo-hero.png, brand/app.ico, ...
git add brand && git commit -m "feat: brand overlay for my server"
```
