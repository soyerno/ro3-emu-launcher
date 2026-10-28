# RO3Emu Launcher — Modern Desktop Client for Ragnarok Online 3

Lanzador de escritorio moderno, personalizable y autocontenido para servidores privados de **Ragnarok Online 3** (RO3Emu) con interfaz visual acelerada mediante **WebView2** (HTML5/CSS3/JS), orquestador de juego, gestión integrada de túneles Cloudflare y soporte de auto-parcheo.

Repositorio oficial: [https://github.com/soyerno/ro3-emu-launcher](https://github.com/soyerno/ro3-emu-launcher)

---

## 🚀 Características Principales

- **Frontend Moderno:** Interfaz web fluida (WPF + WebView2) desacoplada de la lógica del juego.
- **Modo Desarrollo en Vivo (Live Reload):** Modifica HTML, CSS y JS en tiempo real con recarga en caliente (`run-ui-dev.cmd`) sin tener que recompilar C#.
- **Orquestador de Juego Inteligente:** Detección automática de la carpeta del cliente (`ro3.exe`), inyección de hooks y parcheo automático con RO3Emu.
- **Gestión de Túneles en Segundo Plano:** Inicio y supervisión transparente de túneles `cloudflared` (HTTP/Gate/Scene) sin consolas de CMD invasivas.
- **Autenticación y Sesión Segura:** Almacén de credenciales y sesiones persistentes cifradas con Windows DPAPI y SQLite.
- **Control Dinámico de Registros:** Soporte para verificación remota del estado de creación de cuentas (`/api/admin/registration` y `status.json`).
- **Publicación Single-File (.NET 10):** Empaqueta todo el launcher, dependencias y assets en un único ejecutable listo para distribuir a los jugadores.

---

## 🛠️ Guía Rápida: Flujo de Trabajo

| Script | Descripción |
|---|---|
| **`publish.cmd`** | Compila y empaqueta el ejecutable final en **`dist\KonsorRO.exe`** (.NET 10 self-contained, 1 archivo único). Cierra instancias previas si están bloqueadas. |
| **`deploy-dev.cmd`** | Compila la versión Release y la copia automáticamente a la carpeta de prueba del cliente local. |
| **`run-ui-dev.cmd`** | **Modo Desarrollo UI en Vivo**: Inicia el launcher leyendo los archivos HTML, CSS y JS directamente desde el disco sin recompilar. |
| **`build.cmd`** | Compilación rápida incremental para verificar sintaxis y tipos en C#. |
| **`tools\update-logo.cmd`** | Actualizador de Logo: procesa imágenes PNG/JPG para remover fondo blanco y genera `ui.assets.logo-hero.png` y `app.ico` multirresolución. |
| **`tools\test-server.cmd`** | Ejecuta un diagnóstico rápido de endpoints HTTP, estado de registro y puertos de túneles locales. |

---

## 📁 Estructura del Repositorio

```text
ro3-emu-launcher/
├── KonsorRO.slnx                 # Solución .NET 10 (VS 2026 / Rider / VS Code)
├── KonsorRO.csproj               # Configuración del proyecto y manifiesto de recursos embebidos
├── build.cmd                     # Script de compilación rápida
├── publish.cmd                   # Script de publicación a dist/
├── deploy-dev.cmd                # Compila y copia a la carpeta de juego
├── run-ui-dev.cmd                # Ejecuta en modo desarrollo de UI (Live Reload)
├── launcher.json                 # Configuración de URLs del servidor y repositorio
├── news.json                     # Noticias por defecto embebidas
├── status.json                   # Estado de registro por defecto
├── app.ico                       # Icono oficial de Windows (16 a 256px)
├── ui.index.html                 # Interfaz visual del launcher
├── ui.app.css                    # Estilos visuales y diseño responsivo
├── ui.app.js                     # Controlador JavaScript y puente WebView2
├── ui.assets.logo-hero.png       # Logo principal
├── lib/                          # Ensamblados binarios necesarios (Ro3Emu.Patcher.dll)
├── Ro3Emu.Launcher/              # Código C# del lanzador:
│   ├── LauncherController.cs     # Controlador principal (Auth, Noticias, Estado)
│   ├── KonsorGameOrchestrator.cs # Detección e inicio de ro3.exe y parcheo RO3Emu
│   ├── TunnelManager.cs          # Gestión en segundo plano de cloudflared
│   ├── MainWindow.xaml / .cs     # Ventana WPF host de WebView2
├── Ro3Emu.Launcher.Auth/         # Sistema de autenticación:
│   ├── AuthDb.cs                 # Base de datos SQLite local cifrada
│   ├── SessionStore.cs           # Almacén de sesión persistente con Windows DPAPI
└── tools/
    ├── process_logo.py           # Herramienta Python de recorte y conversión de logos
    ├── update-logo.cmd           # Lanzador rápido de actualización de logos
    ├── test_server.py            # Diagnóstico de endpoints y túneles
    └── test-server.cmd           # Lanzador rápido de diagnóstico
```

---

## 🎨 Desarrollo Visual (HTML / CSS / JS)

Para modificar la interfaz sin tener que recompilar el ejecutable cada vez:

1. Ejecuta **`run-ui-dev.cmd`**.
2. Modifica `ui.index.html`, `ui.app.css` o `ui.app.js`.
3. Haz clic derecho en el launcher o presiona `F5` para recargar los cambios al instante.
4. Cuando el diseño esté listo, ejecuta **`publish.cmd`** para empaquetarlo en el ejecutable final.

---

## ⚙️ Requisitos de Compilación

- Windows 10/11 x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- WebView2 Runtime (incluido por defecto en Windows 10/11)

Compilación mediante CLI:
```powershell
dotnet publish KonsorRO.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o dist
```
