using System;
using System.Collections.Generic;
using System.Globalization;

namespace Ro3Emu.Launcher;

/// <summary>
/// Player-facing text of the host (messages posted to the UI, dialogs, e-mails). English and Spanish,
/// selected by launcher.json "language" and switched at runtime when the player changes the UI language.
/// <c>{server}</c> expands to launcher.json "serverName"; <c>{0}</c>, <c>{1}</c> are the caller's arguments.
/// </summary>
public static class Strings
{
	public const string DefaultLanguage = "en";

	private static volatile string _language = DefaultLanguage;
	private static volatile string _serverName = "RO3 Server";

	public static string Language => _language;

	public static string ServerName => _serverName;

	/// <summary>Maps any language tag to a supported one: "es", "es-AR" -> "es"; everything else -> "en".</summary>
	public static string Normalize(string? language)
	{
		return language != null && language.Trim().StartsWith("es", StringComparison.OrdinalIgnoreCase) ? "es" : DefaultLanguage;
	}

	public static void Configure(string? language, string? serverName)
	{
		_language = Normalize(language);
		if (!string.IsNullOrWhiteSpace(serverName))
		{
			_serverName = serverName.Trim();
		}
	}

	public static void SetLanguage(string? language)
	{
		_language = Normalize(language);
	}

	public static string Get(string key, params object[] args)
	{
		if (!Table.TryGetValue(key, out var pair))
		{
			return key;
		}
		string text = (_language == "es" ? pair.Es : pair.En).Replace("{server}", _serverName);
		return args.Length == 0 ? text : string.Format(CultureInfo.InvariantCulture, text, args);
	}

	public static IReadOnlyDictionary<string, (string En, string Es)> All => Table;

	private static readonly Dictionary<string, (string En, string Es)> Table = new()
	{
		// configuration
		["config.server.invalid"] = ("launcher.json \"server\" must be an absolute http(s) URL such as https://game.example.com:5380 (current value: \"{0}\").", "El campo \"server\" de launcher.json debe ser una URL http(s) absoluta, por ejemplo https://game.example.com:5380 (valor actual: \"{0}\")."),
		["config.purchaseUrl.invalid"] = ("launcher.json \"purchaseUrl\" must be an absolute http(s) URL such as https://example.com/donate, or be left out (current value: \"{0}\").", "El campo \"purchaseUrl\" de launcher.json debe ser una URL http(s) absoluta, por ejemplo https://example.com/donate, o no estar (valor actual: \"{0}\")."),
		["config.repository.invalid"] = ("launcher.json \"repository\" must be an absolute http(s) URL such as https://patch.example.com/ (current value: \"{0}\").", "El campo \"repository\" de launcher.json debe ser una URL http(s) absoluta, por ejemplo https://patch.example.com/ (valor actual: \"{0}\")."),
		["config.tunnel.invalid"] = ("launcher.json \"tunnels\" has an invalid entry (hostname \"{0}\", port {1}).", "launcher.json \"tunnels\" tiene una entrada inválida (hostname \"{0}\", puerto {1})."),

		// launcher state messages
		["state.ready"] = ("Ready to play!", "¡Listo para jugar!"),
		["state.noclient"] = ("ro3.exe not found. Click Play to choose the game folder.", "No se encontró ro3.exe. Haz clic en Jugar para elegir la carpeta del juego."),
		["state.pickdir"] = ("ro3.exe not found. Choose the game folder in Settings.", "No se encontró ro3.exe. Elige la carpeta del juego en Configuración."),
		["state.update"] = ("Version {0} is available.", "La versión {0} está disponible."),
		["state.repair"] = ("{0} game files differ from the release.", "{0} archivos del juego difieren de la versión oficial."),
		["state.tunnels.check"] = ("Checking the tunnels to the {server} server...", "Verificando túneles con el servidor {server}..."),
		["state.tunnels.fail"] = ("Could not connect to the {server} server. Check your internet connection.", "No se pudo conectar con el servidor {server}. Revisa tu conexión a internet."),
		["state.repair.retry"] = ("The game files were changed by an earlier patch. Restoring the official files, then patching again...", "Los archivos del juego fueron modificados por un parche anterior. Restaurando los archivos oficiales y volviendo a parchear..."),
		["state.patching"] = ("Patching and starting the game...", "Parcheando e iniciando el juego..."),
		["state.running"] = ("The game is running.", "El juego está en ejecución."),
		// start-up update from the signed manifest (RemoteUpdater)
		["update.launcher"] = ("Updating the launcher to {0}…", "Actualizando el lanzador a {0}…"),
		["update.files"] = ("Updating the server files ({0}/{1})…", "Actualizando los archivos del servidor ({0}/{1})…"),
		["update.readonly"] = ("The server files could not be updated: the launcher folder is read-only. Move the launcher to a folder you can write to.", "No se pudieron actualizar los archivos del servidor: la carpeta del lanzador es de solo lectura. Mueve el lanzador a una carpeta con permisos de escritura."),
		["realm.locked"] = ("Close the game before changing realm (one realm at a time).", "Cierra el juego antes de cambiar de reino (se juega un reino a la vez)."),
		["realm.offline"] = ("The {0} realm is offline. Choose another realm.", "El reino {0} está apagado. Elige otro reino."),
		["state.closed"] = ("The game has closed. Ready to play again!", "El juego se ha cerrado. ¡Listo para volver a jugar!"),
		["state.restoring"] = ("Restoring the client to the official servers...", "Restaurando cliente a servidores oficiales..."),
		["state.restored"] = ("Client restored to the official version.", "Cliente restaurado correctamente a la versión oficial."),
		["state.restore.error"] = ("Restore failed: {0}", "Error al restaurar: {0}"),
		["state.install"] = ("The game is not installed. Click Install to download the official client from Genesis' servers, or choose an existing game folder in Settings.", "El juego no está instalado. Haz clic en Instalar para descargar el cliente oficial desde los servidores de Genesis, o elige una carpeta del juego existente en Configuración."),
		["state.checking.game"] = ("Checking for a game update...", "Buscando una actualización del juego..."),
		["state.paused"] = ("Paused. Downloaded parts are kept; continue any time.", "En pausa. Las partes descargadas se conservan; continúa cuando quieras."),

		// game install from Genesis' update servers
		["install.unreachable"] = ("Could not reach Genesis' download servers. Check your internet connection and try again. Some internet providers block Genesis' mirror: connecting through Cloudflare WARP (or another VPN) helps, or install the game with the Genesis Launcher and choose its folder in Settings.", "No se pudo conectar con los servidores de descarga de Genesis. Revisa tu conexión a internet e inténtalo de nuevo. Algunos proveedores de internet bloquean el mirror de Genesis: conectarte con Cloudflare WARP (u otra VPN) ayuda, o instala el juego con el Genesis Launcher y elige su carpeta en Configuración."),
		["install.download"] = ("A game package could not be downloaded. Check your internet connection and try again: the download resumes where it stopped.", "No se pudo descargar un paquete del juego. Revisa tu conexión a internet e inténtalo de nuevo: la descarga continúa donde se detuvo."),
		["install.space"] = ("Drive {0} has {1} free; {2} are needed. Free some space or choose a folder on another drive.", "La unidad {0} tiene {1} libres; se necesitan {2}. Libera espacio o elige una carpeta en otra unidad."),
		["install.older"] = ("Genesis' servers offer release {0}, older than the {1} already installed here. Try again later.", "Los servidores de Genesis ofrecen la versión {0}, anterior a la {1} ya instalada aquí. Inténtalo más tarde."),
		["install.failed"] = ("Could not install the game: {0} {1}", "No se pudo instalar el juego: {0} {1}"),

		// game launch
		["game.noclient"] = ("ro3.exe was not found in the selected folder.", "No se encontró ro3.exe en la carpeta seleccionada."),
		["game.noro3emu"] = ("RO3Emu.exe was not found. Make sure you extracted every file of the patch.", "No se encontró RO3Emu.exe. Asegúrate de extraer todos los archivos del parche."),
		["game.noro3emu.short"] = ("RO3Emu.exe was not found.", "No se encontró RO3Emu.exe."),
		["game.patching"] = ("Patching and starting the game with RO3Emu...", "Parcheando e iniciando el juego con RO3Emu..."),
		["game.exit"] = ("RO3Emu exited with error code {0}{1}", "RO3Emu terminó con código de error {0}{1}"),
		["game.error"] = ("Could not run RO3Emu: {0}", "Error al ejecutar RO3Emu: {0}"),
		["game.restoring"] = ("Restoring the official client files...", "Restaurando archivos oficiales del cliente..."),
		["game.restore.exit"] = ("Restore exited with code {0}", "Restore terminó con código {0}"),

		// tunnels
		["tunnel.nocloudflared"] = ("cloudflared.exe was not found.", "No se encontró cloudflared.exe."),
		["tunnel.ports"] = ("Checking connection ports...", "Verificando puertos de conexión..."),
		["tunnel.opening"] = ("Opening the tunnels to the {server} server...", "Abriendo túneles con el servidor {server}..."),
		["tunnel.error"] = ("Could not start the tunnel for {0}: {1}", "Error iniciando túnel para {0}: {1}"),
		["tunnel.connected"] = ("Connected to the {server} server.", "Conexión con el servidor {server} establecida."),
		["tunnel.timeout"] = ("Timed out while connecting to the server.", "Tiempo de espera agotado al conectar con el servidor."),

		// window
		["webview2.missing"] = ("The launcher needs the Microsoft Edge WebView2 Runtime, which is not installed on this computer.\n\nOpen the download page?", "El launcher necesita Microsoft Edge WebView2 Runtime, que no está instalado en este equipo.\n\n¿Deseas abrir la página de descarga?"),
		["webview2.error"] = ("Could not start the launcher interface: {0}", "Error al inicializar la interfaz del launcher: {0}"),
	};
}
