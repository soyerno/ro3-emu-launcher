<p align="center">
  <img src="docs/assets/hero.svg" alt="RO3 Launcher: Cliente de Escritorio y Parcheador para RO3Emu" width="100%">
</p>

<p align="center">
  <a href="#inicio-rápido-en-tres-pasos"><img src="docs/assets/button-quickstart.svg" alt="Inicio Rápido" height="44"></a>
  &nbsp;
  <a href="#por-qué-ro3-launcher"><img src="docs/assets/button-features.svg" alt="Características" height="44"></a>
  &nbsp;
  <a href="https://github.com/aethai/ro3emu-server"><img src="docs/assets/button-ro3emu.svg" alt="Servidor RO3Emu" height="44"></a>
</p>

<p align="center">
  <a href="https://github.com/soyerno/ro3-emu-launcher/actions/workflows/ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/soyerno/ro3-emu-launcher/ci.yml?branch=main&label=compilaci%C3%B3n&color=d8b46a&labelColor=1b2740" alt="Estado de Compilación"></a>
  <img src="https://img.shields.io/badge/.NET-10.0-3fb950?labelColor=1b2740" alt=".NET 10">
  <img src="https://img.shields.io/badge/webview2-WPF-0078d4?labelColor=1b2740" alt="WebView2">
  <a href="https://github.com/aethai/ro3emu-server"><img src="https://img.shields.io/badge/companion-RO3Emu-d8b46a?labelColor=1b2740" alt="Companion para RO3Emu"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-8b949e?labelColor=1b2740" alt="Licencia MIT"></a>
</p>

Lanzador de escritorio moderno, liviano y personalizable (marca blanca) con auto-parcheo integrado para servidores privados de **Ragnarok Online 3 / Ragnarok Origin PC (`RO3Emu`)** ([aethai/ro3emu-server](https://github.com/aethai/ro3emu-server)).

Diseñado con una estética de fantasía nórdica oscura, arquitectura desacoplada **WPF + WebView2** (HTML5/CSS3/JavaScript), modo de desarrollo con recarga en caliente en vivo, gestión invisible de túneles Cloudflare y cifrado de credenciales con Windows DPAPI.

> 📖 **To read the documentation in English, see [README.md](README.md).**

---

## Por qué RO3 Launcher

<table>
  <tr>
    <td width="50%" valign="top"><b>Marca blanca por defecto</b><br>Personaliza todo el lanzador para tu servidor privado en segundos. Nombre del servidor, títulos, endpoints, noticias y estado de registros se configuran desde <code>launcher.json</code> sin tocar código C#.</td>
    <td width="50%" valign="top"><b>Modo de Desarrollo UI en Vivo</b><br>Modifica HTML5, CSS3 y JavaScript con recarga en caliente instantánea (<code>run-ui-dev.cmd</code>). Presiona <code>F5</code> en la ventana del launcher para probar cambios al vuelo sin esperar recompilaciones.</td>
  </tr>
  <tr>
    <td width="50%" valign="top"><b>Integración 1:1 con RO3Emu y Túneles</b><br>Detecta automáticamente la carpeta del juego (<code>ro3.exe</code>), inyecta los hooks Cecil necesarios (<code>Ro3Emu.ClientHooks.dll</code> / <code>Ro3Emu.Loc.dll</code>) e inicia túneles seguros Cloudflare en segundo plano sin consolas de CMD invasivas.</td>
    <td width="50%" valign="top"><b>Autenticación y Sesiones Seguras</b><br>Credenciales y sesiones persistentes cifradas de forma segura mediante Windows DPAPI y base de datos local SQLite. Soporte para recordar cuenta, revocación de tokens y control remoto de registros.</td>
  </tr>
  <tr>
    <td colspan="2" valign="top"><b>Distribución en un único archivo autocontenido</b><br>Compila a un único ejecutable de Windows (<code>dist\Ro3Launcher.exe</code>) con todas sus dependencias embebidas (.NET 10 win-x64). Los jugadores descargan un solo archivo ejecutable, hacen doble clic y juegan sin necesidad de instalar .NET previamente.</td>
  </tr>
</table>

---

## Inicio rápido en tres pasos

| Paso | Acción |
|:---:|---|
| **1** | **Clona el repositorio:**<br><code>git clone https://github.com/soyerno/ro3-emu-launcher.git</code><br><code>cd ro3-emu-launcher</code> |
| **2** | **Configura los endpoints de tu servidor:**<br>Edita <code>launcher.json</code> y apunta <code>server</code>, <code>repository</code> y <code>serverName</code> a tu servidor RO3Emu o túnel Cloudflare. |
| **3** | **Ejecuta o Publica:**<br>Desarrollo en vivo: <code>run-ui-dev.cmd</code> &nbsp;·&nbsp; Compilación: <code>build.cmd</code> &nbsp;·&nbsp; Publicar ejecutable único: <code>publish.cmd</code><br>El binario listo para distribuir se genera en <code>dist/</code>. |

---

## Arquitectura e Integración

RO3 Launcher opera como un cliente autónomo de escritorio que conecta a los jugadores con tu servidor `RO3Emu`:

```
[Jugador / Interfaz WebView2 (HTML5 + CSS3 + JS)]
       │
       │  (Puente IPC Bidireccional JSON vía WebView2)
       ▼
[Núcleo RO3 Launcher (C# / .NET 10)]
       ├── Assets Embebidos y Motor Live Reload
       ├── Motor de Auth (Windows DPAPI + SQLite Cifrado)
       ├── Gestor de Túneles Oculto ──► [Cloudflared (Access TCP)]
       │                                     └── un puerto local por entrada de "tunnels"
       │                                         (opcional; ninguno si el servidor es accesible directamente)
       └── Orquestador de Juego
               ├── Detección de Ruta de Cliente (ro3.exe)
               ├── Inyección de Hooks (Ro3Emu.ClientHooks y Loc)
               └── Ejecución de RO3Emu Join
```

---

## Configuración (`launcher.json`)

El lanzador lee la configuración desde `launcher.json` embebida durante la compilación o sobreescrita en el directorio de la aplicación:

```json
{
  "repository": "http://127.0.0.1:5380/",
  "channel": "live",
  "server": "http://127.0.0.1:5380",
  "serverName": "Mi Servidor RO3",
  "language": "es",
  "website": "https://miservidor.com",
  "discord": "https://discord.gg/tu-invitacion",
  "statusUrl": "https://miservidor.com/status.json",
  "tunnels": [{ "hostname": "ro3-http.miservidor.com", "port": 5380 }]
}
```

| Campo | Valor por defecto | Para qué sirve |
|---|---|---|
| `server` | `http://127.0.0.1:5380` | URL base del servidor RO3Emu (`http(s)` absoluta): inicio de sesión y registro (`/sdk/login`), ping de estado, y el servidor al que `RO3Emu.exe join` parchea el cliente. Un valor inválido detiene el lanzamiento y el inicio de sesión con un error que nombra el campo. |
| `repository` | `http://127.0.0.1:5380/` | Repositorio de releases: `channels/*.json` y `news.json`. |
| `channel` | `live` | Rama de parches. |
| `serverName` | `My RO3 Server` | Nombre que se muestra en el título de la ventana, los textos, los diálogos y los correos. |
| `language` | `en` | Idioma por defecto de la interfaz y los mensajes: `en` o `es`. Los jugadores pueden cambiarlo en el launcher. |
| `website`, `discord` | ninguno | Enlaces opcionales; los botones se ocultan si el campo está vacío. |
| `statusUrl` | ninguno | Control de registros opcional: una URL pública, sin login (ver abajo). |
| `tunnels` | `[]` | Túneles de Cloudflare a abrir, como `{ "hostname", "port" }`. Vacío: sin `cloudflared`. |

Cada campo está explicado en la [Guía de Marca Blanca](docs/guide/white-label.md).

### Control de Registros

Si `statusUrl` está definido, el launcher lo consulta para saber si la creación de cuentas está habilitada. Lee `{"enabled": true|false}` o `{"registration_enabled": true|false}`.

Cuando informa `false`, el launcher muestra una notificación de aviso, deshabilita los campos de entrada y rechaza intentos de registro antes de enviarlos. Sin `statusUrl`, se asume que el registro está abierto.

---

## Personalización Visual (HTML / CSS / JS)

La capa visual se encuentra en [`ui.index.html`](ui.index.html), [`ui.app.css`](ui.app.css) y [`ui.app.js`](ui.app.js).

1. Ejecuta **`run-ui-dev.cmd`** para iniciar en modo live reload.
2. Modifica estilos, paletas de colores o banners.
3. Haz clic derecho o presiona `F5` en la ventana del lanzador para previsualizar cambios al instante.
4. Ejecuta **`tools\update-logo.cmd`** con cualquier imagen PNG/JPG para recortar transparencias y generar automáticamente los assets y el icono multirresolución `app.ico` (16x16 a 256x256).
5. Ejecuta **`publish.cmd`** cuando estés listo para empaquetar el diseño final en el ejecutable para los jugadores.

---

## Prueba de Humo (Windows)

`tools\smoke-test.cmd` revisa un build de punta a punta antes de distribuirlo: requisitos (.NET y WebView2), `publish`, el `launcher.json` efectivo (`brand\launcher.json` si existe: URLs, DNS de cada túnel, `statusUrl`), los túneles en sí (los abre unos segundos y los cierra) y el estado del servidor. Solo lee de la red y nunca inicia sesión ni toca un cliente de juego. Con `-Launch` abre el launcher al final y muestra `launcher.log`; también imprime la lista corta de chequeos manuales.

---

## Guías de Documentación

- [Guía de Marca Blanca y Personalización](docs/guide/white-label.md) (Todos los campos de `launcher.json`, temas, textos e idiomas, forks con marca)
- [Guía de Arquitectura e IPC](docs/guide/architecture.md) (Host WPF, puente WebView2, orquestador)
- [Guía de Compilación y Distribución](docs/guide/publishing.md) (Compilación single-file, empaquetado)

---

## Forks con Marca

`main` es el launcher de la comunidad y no lleva la marca de ningún servidor. Para usar tu propia configuración, noticias, logo, icono o nombre de ejecutable, mantén una rama de `main` y agrega archivos en [`brand/`](brand/README.md) con los mismos nombres que los de la raíz; el build los usa en lugar de los valores por defecto. Los archivos que son tuyos pero no se compilan (un `data\loc_en.tsv`, un `LEEME.txt`) van en `brand/patch/` y `tools\package-patch.cmd` los agrega al ZIP del parche. Un fork solo agrega archivos, así que actualizarlo es un simple `git merge origin/main` sin conflictos. La rama [`konsor`](https://github.com/soyerno/ro3-emu-launcher/tree/konsor) es un ejemplo de fork con marca.

---

## Proyectos Relacionados

- **[ro3emu-server](https://github.com/aethai/ro3emu-server)** — Emulador de servidor open-source para Ragnarok Online 3.
- **[ro3-cpanel](https://github.com/soyerno/ro3-cpanel)** — Panel de control y administración web para RO3Emu.

---

## Contribuciones

¡Las contribuciones son bienvenidas! Por favor lee [CONTRIBUTING.md](CONTRIBUTING.md) y [AGENTS.md](AGENTS.md) para conocer las pautas de código y el flujo de trabajo de pull requests.

---

## Licencia

Publicado bajo la [Licencia MIT](LICENSE).
