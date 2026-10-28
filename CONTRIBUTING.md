# Contributing to RO3 Launcher

Thank you for contributing to RO3 Launcher! We appreciate bug reports, feature requests, and code contributions that keep this desktop client modern, secure, and easy to deploy for any RO3 private server.

---

## Code of Conduct

Please maintain a respectful, constructive, and collaborative tone in all discussions and pull requests.

---

## Development Setup

1. **Prerequisites:**
   - Windows 10/11 x64
   - [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
   - WebView2 Runtime (installed by default on Windows 10/11)

2. **Clone the Repository:**
   ```bash
   git clone https://github.com/soyerno/ro3-emu-launcher.git
   cd ro3-emu-launcher
   ```

3. **Live UI Development:**
   Run `run-ui-dev.cmd` to start the launcher in live reload mode. Changes made to `ui.index.html`, `ui.app.css`, or `ui.app.js` can be refreshed instantly by pressing `F5` in the window.

4. **Building:**
   ```bash
   dotnet build Ro3Launcher.csproj
   ```

---

## Guidelines for Contributions

- **Preserve White-Label Capabilities:** Never hardcode private server branding, URLs, or credentials in C# logic. Use `launcher.json` and configurable properties.
- **Maintain Windows DPAPI & Encryption Standards:** Sensitive player tokens must remain encrypted.
- **Keep Build Dependencies Minimal:** The project should remain straightforward to compile without third-party proprietary frameworks.

---

## Pull Request Process

1. Create a feature branch: `git checkout -b feat/my-feature`.
2. Verify compilation with `dotnet build Ro3Launcher.csproj -c Release`.
3. Ensure no temporary files or binary artifacts (`bin/`, `obj/`, `dist/`) are staged.
4. Submit a clear, well-described Pull Request referencing any relevant issues.
