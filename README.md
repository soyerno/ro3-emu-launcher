# KonsorRO Launcher — DevKit Oficial

Kit de desarrollo, compilación y despliegue del lanzador oficial de **KonsorRO** (Ragnarok Origin Private Server).

---

## 🚀 Guía Rápida: Flujo de Trabajo

| Script | Descripción |
|---|---|
| **`publish.cmd`** | Compila y empaqueta el ejecutable final en **`dist\KonsorRO.exe`** (.NET 10 self-contained, 1 archivo único). Cierra instancias previas si están bloqueadas. |
| **`deploy-dev.cmd`** | Compila la versión Release y la copia automáticamente a la carpeta del juego de desarrollo (`E:\Genesis Launcher - DEV\KonsorRO.exe`). |
| **`run-ui-dev.cmd`** | **Modo Desarrollo UI en Vivo**: Inicia el launcher leyendo los archivos HTML, CSS y JS directamente desde este directorio en tiempo real sin tener que recompilar cada vez. |
| **`build.cmd`** | Compilación rápida incremental para verificar errores de código en C#. |
| **`tools\update-logo.cmd`** | Actualizador de Logo: arrastra cualquier imagen (JPG o PNG) para remover el fondo blanco y generar automáticamente `ui.assets.logo-hero.png` y `app.ico` multirresolución. |
| **`tools\test-server.cmd`** | Ejecuta un diagnóstico rápido de los endpoints de la web (`konsor.com.ar`), estado del registro y puertos de los túneles locales. |

---

## 📁 Estructura del DevKit

```text
E:\KonsorRO_DevKit\
├── KonsorRO.slnx                 # Solución oficial .NET 10 (abrir en VS / Rider / VS Code)
├── KonsorRO.csproj               # Configuración del proyecto y manifiesto de recursos
├── build.cmd                     # Script de compilación rápida
├── publish.cmd                   # Script de publicación a dist/KonsorRO.exe
├── deploy-dev.cmd                # Compila y copia a la carpeta de juego
├── run-ui-dev.cmd                # Ejecuta en modo desarrollo de UI (Live Reload)
├── launcher.json                 # Configuración de URLs del servidor y repositorio
├── news.json                     # Noticias por defecto embebidas
├── status.json                   # Estado de registro por defecto
├── app.ico                       # Icono oficial de Windows (16 a 256px)
├── ui.index.html                 # Interfaz visual del launcher
├── ui.app.css                    # Estilos visuales y diseño responsivo
├── ui.app.js                     # Controlador JavaScript y puente WebView2
├── ui.assets.logo-hero.png       # Logo transparente del dragón Konsor
├── Ro3Emu.Launcher\              # Código C# del lanzador:
│   ├── LauncherController.cs     # Controlador principal (Auth, Noticias, Estado)
│   ├── KonsorGameOrchestrator.cs # Detección e inicio de ro3.exe y parcheo RO3Emu
│   ├── TunnelManager.cs          # Gestión en segundo plano de cloudflared
│   ├── MainWindow.xaml / .cs     # Ventana WPF y WebView2
├── Ro3Emu.Launcher.Auth\         # Sistema de autenticación:
│   ├── AuthDb.cs                 # Base de datos SQLite local cifrada
│   ├── SessionStore.cs           # Almacén de sesión persistente con Windows DPAPI
└── tools\
    ├── process_logo.py           # Herramienta Python de recorte y conversión de logos
    ├── update-logo.cmd           # Lanzador rápido de actualización de logos
    ├── test_server.py            # Diagnóstico de endpoints y túneles
    └── test-server.cmd           # Lanzador rápido de diagnóstico
```

---

## 🎨 Desarrollo Visual (HTML / CSS / JS)

Para modificar la interfaz sin tener que recompilar el ejecutable cada vez:

1. Ejecuta **`run-ui-dev.cmd`**.
2. Modifica [`ui.index.html`](file:///E:/KonsorRO_DevKit/ui.index.html), [`ui.app.css`](file:///E:/KonsorRO_DevKit/ui.app.css) o [`ui.app.js`](file:///E:/KonsorRO_DevKit/ui.app.js).
3. Haz clic derecho en el launcher o presiona `F5` / reinicia la ventana para ver los cambios reflejados al instante.
4. Cuando el diseño esté listo, ejecuta **`publish.cmd`** o **`deploy-dev.cmd`** para empaquetarlo en el ejecutable final para los jugadores.

---

## 🔒 Control de Creación de Cuentas

El launcher valida dinámicamente si la creación de cuentas está permitida mediante dos vías:
- Endpoint del panel admin: `https://konsor.com.ar/api/admin/registration`
- Archivo de estado: `https://konsor.com.ar/status.json`

Si el administrador desactiva el registro (`{"enabled": false}`):
- En la interfaz se muestra el aviso: *⚠️ "La creación de cuentas está deshabilitada temporalmente por el administrador."*
- Los campos de texto y el botón de crear cuenta quedan deshabilitados.
- El servidor y el backend del launcher rechazan cualquier intento de registro automáticamente.

---

## 🛡️ Túneles Cloudflare y Parcheo del Cliente

El launcher inicia automáticamente y de forma oculta (sin ventanas negras de CMD) los túneles a:
- `ro3-http.konsor.com.ar` -> `127.0.0.1:5380` (HTTP / Login / Patcher)
- `ro3-micro.konsor.com.ar` -> `127.0.0.1:20000` (Microservicios)
- `ro3-game.konsor.com.ar` -> `127.0.0.1:33100` (Juego TCP)

Al hacer clic en **JUGAR**, invoca a `RO3Emu.exe join http://127.0.0.1:5380` pasando la cuenta activa en la variable `RO3EMU_LOGIN_ACCOUNT`.
