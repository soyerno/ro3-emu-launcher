# Publishing & Distribution Guide

RO3 Launcher compiles to a single, self-contained executable for Windows x64. Players do not need to install the .NET SDK or runtime on their systems.

---

## Prerequisites

- Windows 10/11 x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Visual Studio 2026 or .NET CLI

---

## One-Command Publish

Execute the root publishing script:

```cmd
publish.cmd
```

This runs the .NET publication pipeline with the following parameters:

```powershell
dotnet publish Ro3Launcher.csproj \
  -c Release \
  -r win-x64 \
  --self-contained \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o dist
```

### Result Artifacts
The finished binary is generated at:
```text
dist\Ro3Launcher.exe
```

This executable embeds:
- The entire .NET 10 runtime
- WebView2 bindings and SQLite native libraries
- All compiled HTML, CSS, JS, and image assets
- Default configuration (`launcher.json`) and news (`news.json`)

Players simply download the executable and launch it directly.

The executable is named after the build's `AssemblyName` (`Ro3Launcher` by default). A branded fork can rename it in `brand/brand.props`; the `.cmd` scripts and `tools\package-patch.cmd` pick the name up automatically (see [`brand/README.md`](../../brand/README.md)).
