// Launcher controller script
// Manages internationalization (PT-BR, EN, ES), state, UI rendering, news, and WebView2 communications.

(function () {
  "use strict";

  var $ = function (id) { return document.getElementById(id); };
  var host;
  var state = { phase: "checking" };
  var busy = false;
  var stage = null;
  var serverData = { online: false, players: null };
  var remoteNews = null;

  // Injected by the host from launcher.json; absent when the page runs in a plain browser (mock mode).
  var CFG = window.LAUNCHER_CONFIG || {};
  var SERVER_NAME = CFG.serverName || "RO3 Server";

  function fmt(text) {
    return String(text).replace(/\{server\}/g, SERVER_NAME).replace(/\{SERVER\}/g, SERVER_NAME.toUpperCase());
  }

  var I18N = {
    "pt-br": {
      langCode: "PT",
      tabs: {
        home: "INÍCIO",
        news: "NOTÍCIAS",
        account: "CONTA",
        register: "Site Oficial",
        discord: "Discord",
        settings: "CONFIGURAÇÕES"
      },
      server: {
        online: "Online",
        offline: "Offline",
        players: "jogadores",
        player: "jogador",
        connecting: "Conectando…"
      },
      notices: {
        title: "AVISOS",
        badge: "CONTEÚDO ASSINADO",
        sub: "Anúncios oficiais da equipe {server}.",
        foot: "CANAL OFICIAL DE NOTÍCIAS · PT-BR",
        webBtn: "Site Oficial",
        discordBtn: "Discord"
      },
      defaultNews: [
        {
          tag: "BEM-VINDO",
          color: "blue",
          title: "Bem-vindo a {server}",
          text: "Tudo pronto para explorar o mundo de Rune-Midgard."
        }
      ],
      auth: {
        login: "Entrar",
        verifying: "Verificando…",
        register: "Criar Conta",
        creating: "Criando…",
        repair: "Reparar"
      },
      phases: {
        checking: { button: "Verificando…", text: "Verificando atualizações…" },
        install: { button: "Instalar", text: "O jogo ainda não está instalado. Baixar arquivos necessários." },
        update: { button: "Atualizar", text: "Uma nova versão está disponível para download." },
        repair: { button: "Reparar", text: "Alguns arquivos diferem da versão oficial." },
        ready: { button: "Jogar", text: "Tudo atualizado. Bom jogo!" },
        offline: { button: "Tentar novamente", text: "Erro: não foi possível conectar ao servidor." },
        error: { button: "Tentar novamente", text: "Ocorreu um erro no inicializador." },
        launching: { button: "Iniciando…", text: "Iniciando o cliente do jogo…" },
        "needs-auth": { button: "Iniciar sesión", text: "Inicia sesión con tu conta para jogar." },
        running: { button: "Jogar", text: "O jogo está em execução." },
        pause: { button: "Pausar" }
      },
      stages: {
        scanning: "Verificando arquivos",
        downloading: "Baixando",
        assembling: "Montando arquivos",
        verifying: "Verificando",
        committing: "Salvando"
      },
      progress: {
        left: "restante"
      },
      settings: {
        title: "Configurações",
        sub: "Onde o jogo está instalado neste computador.",
        folder: "Pasta do jogo",
        btnChange: "Alterar pasta",
        btnOpen: "Abrir pasta",
        hint: "Já possui o cliente instalado? Aponte para a pasta e apenas os arquivos atualizados serão baixados.",
        langLabel: "Idioma do Inicializador e Jogo",
        langHint: "Selecione o idioma da interface e das traduções dentro do jogo."
      }
    },
    "en": {
      langCode: "EN",
      tabs: {
        home: "HOME",
        news: "NEWS",
        account: "ACCOUNT",
        register: "Official Web",
        discord: "Discord",
        settings: "SETTINGS"
      },
      server: {
        online: "Online",
        offline: "Offline",
        players: "players",
        player: "player",
        connecting: "Connecting…"
      },
      notices: {
        title: "NOTICES",
        badge: "VERIFIED CONTENT",
        sub: "Official announcements from the {server} team.",
        foot: "OFFICIAL NEWS CHANNEL · EN",
        webBtn: "Official Web",
        discordBtn: "Discord"
      },
      defaultNews: [
        {
          tag: "WELCOME",
          color: "blue",
          title: "Welcome to {server}",
          text: "Everything is ready to explore the world of Rune-Midgard."
        }
      ],
      auth: {
        login: "Sign in",
        verifying: "Verifying…",
        register: "Create Account",
        creating: "Creating…",
        repair: "Repair"
      },
      phases: {
        checking: { button: "Checking…", text: "Checking for updates…" },
        install: { button: "Install", text: "The game is not installed yet. Download required files." },
        update: { button: "Update", text: "A new version is available for download." },
        repair: { button: "Repair", text: "Some files differ from the official release." },
        ready: { button: "Play", text: "Up to date. Have fun!" },
        offline: { button: "Try again", text: "Error: Could not connect to update server." },
        error: { button: "Try again", text: "Something went wrong in the launcher." },
        launching: { button: "Starting…", text: "Starting the game client…" },
        "needs-auth": { button: "Sign in", text: "Sign in with your account to play." },
        running: { button: "Play", text: "The game is currently running." },
        pause: { button: "Pause" }
      },
      stages: {
        scanning: "Checking files",
        downloading: "Downloading",
        assembling: "Assembling files",
        verifying: "Verifying",
        committing: "Saving"
      },
      progress: {
        left: "left"
      },
      settings: {
        title: "Settings",
        sub: "Where the game lives on this PC.",
        folder: "Game folder",
        btnChange: "Change folder",
        btnOpen: "Open folder",
        hint: "Already have the RO3 PC client? Point this at its folder and only changed files are downloaded.",
        langLabel: "Game & Launcher Language",
        langHint: "Select language for the launcher UI and in-game translations."
      }
    },
    "es": {
      langCode: "ES",
      tabs: {
        home: "INICIO",
        news: "NOTICIAS",
        account: "CUENTA",
        register: "Web Oficial",
        discord: "Discord",
        settings: "CONFIGURACIÓN"
      },
      server: {
        online: "Online",
        offline: "Offline",
        players: "jugadores",
        player: "jugador",
        connecting: "Conectando…"
      },
      notices: {
        title: "AVISOS",
        badge: "SERVIDOR {SERVER}",
        sub: "Anuncios oficiales del servidor privado {server}.",
        foot: "CANAL OFICIAL DE NOTICIAS · ES",
        webBtn: "Web Oficial",
        discordBtn: "Discord"
      },
      defaultNews: [
        {
          tag: "BIENVENIDO",
          color: "blue",
          title: "Bienvenido a {server}",
          text: "Todo listo para explorar el mundo de Rune-Midgard."
        }
      ],
      auth: {
        login: "Entrar",
        verifying: "Verificando…",
        register: "Crear Cuenta",
        creating: "Creando…",
        repair: "Reparar"
      },
      phases: {
        checking: { button: "Verificando…", text: "Buscando actualizaciones…" },
        install: { button: "Instalar", text: "El juego aún no está instalado. Descargar archivos." },
        update: { button: "Actualizar", text: "Una nueva versión está disponible para descargar." },
        repair: { button: "Reparar", text: "Algunos archivos difieren de la versión oficial." },
        ready: { button: "Jugar", text: "¡Todo actualizado! ¡Que disfrutes del juego!" },
        offline: { button: "Reintentar", text: "Error: No se pudo conectar al servidor." },
        error: { button: "Reintentar", text: "Ocurrió un error en el actualizador." },
        launching: { button: "Iniciando…", text: "Iniciando el cliente del juego…" },
        "needs-auth": { button: "Iniciar sesión", text: "Inicia sesión con tu cuenta para jugar." },
        running: { button: "Jugar", text: "El juego está en ejecución." },
        pause: { button: "Pausar" }
      },
      stages: {
        scanning: "Comprobando archivos",
        downloading: "Descargando",
        assembling: "Ensamblando archivos",
        verifying: "Verificando",
        committing: "Guardando"
      },
      progress: {
        left: "restante"
      },
      settings: {
        title: "Configuración",
        sub: "Dónde está instalado el juego en este PC.",
        folder: "Carpeta del juego",
        btnChange: "Cambiar carpeta",
        btnOpen: "Abrir carpeta",
        hint: "¿Ya tienes el cliente instalado? Apunta a la carpeta y solo se descargarán los archivos modificados.",
        langLabel: "Idioma del Launcher y Juego",
        langHint: "Selecciona el idioma del launcher y de las traducciones dentro del juego."
      }
    }
  };

  // The player's own choice (dropdown) wins over the launcher.json default.
  var currentLang = normalizeLang(localStorage.getItem("ro3_lang") || CFG.language || "en");

  // Texts of the static markup (data-i18n*) and of the form feedback. A missing key falls back to English.
  var UI_TEXT = {
    "pt-br": {
      minimize: "Minimizar",
      close: "Fechar",
      authTabLogin: "Entrar",
      authTabRegister: "Criar conta",
      userLabel: "Usuário ou e-mail",
      userPlaceholder: "Ex.: meunome ou voce@email.com",
      passLabel: "Senha",
      passPlaceholder: "Sua senha",
      rememberMe: "Lembrar de mim",
      forgot: "Esqueci minha senha",
      codeLabel: "Código de verificação",
      codePlaceholder: "Código de 6 dígitos",
      verifyBtn: "Verificar e-mail",
      resetCodeLabel: "Código de recuperação",
      newPassLabel: "Nova senha",
      newPassPlaceholder: "Mínimo de 6 caracteres",
      resetBtn: "Redefinir senha",
      regDisabled: "A criação de contas está temporariamente desativada pelo administrador.",
      regUserLabel: "Nome de usuário",
      regUserPlaceholder: "Ex.: meunome (3 a 20 caracteres)",
      emailLabel: "E-mail",
      emailPlaceholder: "voce@email.com (opcional)",
      confirmLabel: "Confirmar senha",
      confirmPlaceholder: "Repita a senha",
      loggedLabel: "Conta ativa neste PC:",
      switchAccount: "Usar outra conta",
      msgHelp: "Ajuda",
      repairTitle: "Reparar arquivos",
      webTitle: "Abrir o site",
      discordTitle: "Entrar na comunidade do Discord",
      regUnavailable: "Registro indisponível",
      regTabTitle: "Criar nova conta",
      regTabDisabled: "A criação de contas está temporariamente desativada",
      fbRequired: "Usuário, senha e confirmação são obrigatórios.",
      fbUserLength: "O usuário deve ter pelo menos 3 caracteres.",
      fbPassLength: "A senha deve ter pelo menos 6 caracteres.",
      fbPassMismatch: "As senhas não coincidem.",
      fbCreating: "Criando sua conta em {server}...",
      fbSigningIn: "Entrando em {server}...",
      fbLoginMissing: "Informe usuário e senha.",
      fbForgot: "Digite seu e-mail no campo de usuário (ou use a aba Criar conta) e tente novamente."
    },
    en: {
      minimize: "Minimize",
      close: "Close",
      authTabLogin: "Sign in",
      authTabRegister: "Create account",
      userLabel: "Username or e-mail",
      userPlaceholder: "e.g. myname or you@email.com",
      passLabel: "Password",
      passPlaceholder: "Your password",
      rememberMe: "Remember me",
      forgot: "I forgot my password",
      codeLabel: "Verification code",
      codePlaceholder: "6-digit code",
      verifyBtn: "Verify e-mail",
      resetCodeLabel: "Recovery code",
      newPassLabel: "New password",
      newPassPlaceholder: "At least 6 characters",
      resetBtn: "Reset password",
      regDisabled: "Account creation is temporarily disabled by the administrator.",
      regUserLabel: "Username",
      regUserPlaceholder: "e.g. myname (3-20 characters)",
      emailLabel: "E-mail",
      emailPlaceholder: "you@email.com (optional)",
      confirmLabel: "Confirm password",
      confirmPlaceholder: "Repeat the password",
      loggedLabel: "Account active on this PC:",
      switchAccount: "Use another account",
      msgHelp: "Help",
      repairTitle: "Repair files",
      webTitle: "Open the website",
      discordTitle: "Join the Discord community",
      regUnavailable: "Registration unavailable",
      regTabTitle: "Create a new account",
      regTabDisabled: "Account creation is temporarily disabled",
      fbRequired: "Username, password and confirmation are required.",
      fbUserLength: "The username must have at least 3 characters.",
      fbPassLength: "The password must have at least 6 characters.",
      fbPassMismatch: "The passwords do not match.",
      fbCreating: "Creating your account on {server}...",
      fbSigningIn: "Signing in to {server}...",
      fbLoginMissing: "Please enter your username and password.",
      fbForgot: "Type your e-mail in the username field (or use the Create account tab) and try again."
    },
    es: {
      minimize: "Minimizar",
      close: "Cerrar",
      authTabLogin: "Iniciar Sesión",
      authTabRegister: "Crear Cuenta",
      userLabel: "Usuario o correo electrónico",
      userPlaceholder: "Ej: minombre o tu@email.com",
      passLabel: "Contraseña",
      passPlaceholder: "Tu contraseña",
      rememberMe: "Recordarme",
      forgot: "Olvidé mi contraseña",
      codeLabel: "Código de verificación",
      codePlaceholder: "Código de 6 dígitos",
      verifyBtn: "Verificar correo",
      resetCodeLabel: "Código de recuperación",
      newPassLabel: "Nueva contraseña",
      newPassPlaceholder: "Mínimo 6 caracteres",
      resetBtn: "Restablecer contraseña",
      regDisabled: "La creación de cuentas está deshabilitada temporalmente por el administrador.",
      regUserLabel: "Nombre de usuario",
      regUserPlaceholder: "Ej: minombre (3-20 caracteres)",
      emailLabel: "Correo electrónico",
      emailPlaceholder: "tu@email.com (opcional)",
      confirmLabel: "Confirmar contraseña",
      confirmPlaceholder: "Repite la contraseña",
      loggedLabel: "Cuenta activa en este PC:",
      switchAccount: "Usar otra cuenta",
      msgHelp: "Ayuda",
      repairTitle: "Reparar archivos",
      webTitle: "Abrir el sitio web",
      discordTitle: "Unirse a la comunidad de Discord",
      regUnavailable: "Registro no disponible",
      regTabTitle: "Crear nueva cuenta",
      regTabDisabled: "La creación de cuentas está deshabilitada temporalmente",
      fbRequired: "Usuario, contraseña y confirmación son obligatorios.",
      fbUserLength: "El usuario debe tener al menos 3 caracteres.",
      fbPassLength: "La contraseña debe tener al menos 6 caracteres.",
      fbPassMismatch: "Las contraseñas no coinciden.",
      fbCreating: "Creando cuenta en {server}...",
      fbSigningIn: "Iniciando sesión en {server}...",
      fbLoginMissing: "Por favor ingresa usuario y contraseña.",
      fbForgot: "Escribe tu correo en el campo de usuario (o usa la pestaña Crear Cuenta) y vuelve a intentarlo."
    }
  };

  function ui(key) {
    var table = UI_TEXT[currentLang] || UI_TEXT.en;
    var text = table[key] != null ? table[key] : UI_TEXT.en[key];
    return fmt(text != null ? text : key);
  }

  function applyStaticTexts() {
    document.querySelectorAll("[data-i18n]").forEach(function (el) { el.textContent = ui(el.getAttribute("data-i18n")); });
    document.querySelectorAll("[data-i18n-placeholder]").forEach(function (el) { el.placeholder = ui(el.getAttribute("data-i18n-placeholder")); });
    document.querySelectorAll("[data-i18n-title]").forEach(function (el) { el.title = ui(el.getAttribute("data-i18n-title")); });
  }

  // Server name, logo caption and the website / Discord links, all from launcher.json.
  function applyConfig() {
    document.title = SERVER_NAME;
    var logo = document.querySelector(".logo");
    if (logo) logo.alt = SERVER_NAME;
    [["tab-register", CFG.website], ["news-btn-web", CFG.website], ["tab-discord", CFG.discord], ["news-btn-discord", CFG.discord]].forEach(function (l) {
      var el = $(l[0]);
      if (!el) return;
      el.href = l[1] || "#";
      el.style.display = l[1] ? "" : "none";
    });
    var bar = $("news-links-bar");
    if (bar) bar.style.display = (CFG.website || CFG.discord) ? "" : "none";
  }

  function normalizeLang(lang) {
    lang = String(lang || "").toLowerCase();
    if (I18N[lang]) return lang;
    if (lang.indexOf("es") === 0) return "es";
    if (lang.indexOf("pt") === 0) return "pt-br";
    return "en";
  }

  var COLORS = { blue: "var(--blue)", green: "var(--green)", orange: "var(--orange)", red: "var(--red)" };

  function send(o) { host.post(o); }

  function openExternalUrl(url) {
    if (!url) return;
    try { send({ cmd: "openUrl", url: url }); } catch (e) {}
    try { send({ cmd: "window", action: "openUrl", url: url }); } catch (e) {}
    try { window.open(url, "_blank"); } catch (e) {}
  }

  function size(b) {
    return b >= 1073741824 ? (b / 1073741824).toFixed(2) + " GB" : Math.round(b / 1048576) + " MB";
  }

  function eta(s) {
    if (!(s > 0) || !isFinite(s)) return "";
    s = Math.round(s);
    if (s >= 3600) return Math.floor(s / 3600) + " h " + Math.floor(s % 3600 / 60) + " min";
    if (s >= 60) return Math.floor(s / 60) + " min " + (s % 60) + " s";
    return s + " s";
  }

  // Icons SVG definitions
  var ICONS = {
    play: '<svg viewBox="0 0 24 24" class="icon-svg"><polygon points="8,5 19,12 8,19" fill="currentColor"/></svg>',
    download: '<svg viewBox="0 0 24 24" class="icon-svg"><path d="M12 4v11m-4-4l4 4 4-4M5 19h14" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" fill="none"/></svg>',
    wrench: '<svg viewBox="0 0 24 24" class="icon-svg"><path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6-4.6 4.6a2 2 0 0 1-1.3.6 2 2 0 0 1-1.4-.6l-.6-.6a2 2 0 0 1 0-2.8l4.6-4.6-1.6-1.6a1 1 0 0 0-1.4 0l-1.4 1.4a4 4 0 0 0 0 5.6l.6.6-4.3 4.3a1 1 0 0 0 0 1.4l2.8 2.8a1 1 0 0 0 1.4 0l4.3-4.3.6.6a4 4 0 0 0 5.6 0l1.4-1.4a1 1 0 0 0 0-1.4l-1.6-1.6 4.6-4.6a1 1 0 0 0-1.4-1.4l-4.6 4.6z" fill="currentColor"/></svg>',
    pause: '<svg viewBox="0 0 24 24" class="icon-svg"><rect x="7" y="5" width="3.2" height="14" fill="currentColor"/><rect x="13.8" y="5" width="3.2" height="14" fill="currentColor"/></svg>',
    spinner: '<svg viewBox="0 0 24 24" class="icon-svg"><polygon points="12,3 18,12 12,21 6,12" fill="currentColor"/></svg>'
  };

  var PHASES_CONFIG = {
    checking: { step: 0, icon: "spinner", wait: true },
    install: { step: 1, icon: "download" },
    update: { step: 1, icon: "download" },
    repair: { step: 2, icon: "wrench", warn: true, recovery: true },
    ready: { step: 3, icon: "play" },
    offline: { step: 0, icon: "wrench", warn: true, recovery: true },
    error: { step: 0, icon: "wrench", warn: true, recovery: true },
    launching: { step: 3, icon: "spinner", wait: true },
    "needs-auth": { step: 3, icon: "wrench", warn: true, recovery: true },
    running: { step: 3, icon: "play" }
  };

  function setLanguage(lang, persist) {
    if (!I18N[lang]) return;
    currentLang = lang;
    if (persist) localStorage.setItem("ro3_lang", lang);
    document.documentElement.lang = lang === "pt-br" ? "pt-BR" : lang;
    send({ cmd: "language", lang: lang });
    applyStaticTexts();

    var t = I18N[lang];
    $("current-lang").textContent = t.langCode;

    // Update active dropdown item
    document.querySelectorAll(".lang-opt").forEach(function (btn) {
      btn.classList.toggle("active", btn.dataset.lang === lang);
    });

    // Update active settings pill
    document.querySelectorAll(".lang-pill-opt").forEach(function (btn) {
      btn.classList.toggle("active", btn.dataset.lang === lang);
    });

    // Update tabs safely
    if ($("tab-home")) $("tab-home").textContent = t.tabs.home;
    if ($("tab-news")) $("tab-news").textContent = t.tabs.news;
    if ($("tab-account")) $("tab-account").textContent = t.tabs.account || "ACCOUNT";
    if ($("tab-register")) $("tab-register").title = t.tabs.register || ui("webTitle");
    if ($("tab-discord")) $("tab-discord").title = t.tabs.discord || ui("discordTitle");
    var tabSet = $("tab-settings");
    if (tabSet) tabSet.textContent = t.tabs.settings;

    // Update notices card safely
    if ($("notices-title")) $("notices-title").textContent = t.notices.title;
    if ($("notices-badge")) $("notices-badge").textContent = fmt(t.notices.badge);
    if ($("notices-sub")) $("notices-sub").textContent = fmt(t.notices.sub);
    if ($("notices-foot")) $("notices-foot").textContent = t.notices.foot;
    if ($("lbl-news-web") && t.notices.webBtn) $("lbl-news-web").textContent = t.notices.webBtn;
    if ($("lbl-news-discord") && t.notices.discordBtn) $("lbl-news-discord").textContent = t.notices.discordBtn;

    // Update auth button labels
    var btnReg = $("btn-do-register");
    if (btnReg && !btnReg.disabled) btnReg.textContent = t.auth.register;
    var btnLogin = $("btn-do-login");
    if (btnLogin && !btnLogin.disabled) btnLogin.textContent = t.auth.login;

    // Update settings card if present
    var sTitle = $("settings-title"); if (sTitle) sTitle.textContent = t.settings.title;
    var sSub = $("settings-sub"); if (sSub) sSub.textContent = t.settings.sub;
    var sLbl = $("settings-lbl-folder"); if (sLbl) sLbl.textContent = t.settings.folder;
    var sChg = $("set-dir-change"); if (sChg) sChg.textContent = t.settings.btnChange;
    var sOpn = $("set-open"); if (sOpn) sOpn.textContent = t.settings.btnOpen;
    var sHnt = $("settings-hint"); if (sHnt) sHnt.textContent = t.settings.hint;

    // Update server text with current data
    renderServerStatus();

    // Re-render news and main controls
    renderNews(remoteNews);
    render();
  }

  function renderServerStatus() {
    var t = I18N[currentLang].server;
    $("server").className = "server " + (serverData.online ? "on" : "off");
    if (serverData.online) {
      var pText = "";
      if (serverData.players != null) {
        var unit = serverData.players === 1 ? t.player : t.players;
        pText = " · " + serverData.players + " " + unit;
      }
      $("server-text").innerHTML = "<b>" + t.online + "</b>" + pText;
    } else {
      $("server-text").innerHTML = "<b>" + t.offline + "</b>";
    }
  }

  var KNOWN_MESSAGES = {
    offline_installed: {
      "pt-br": "Não foi possível conectar ao servidor de atualização; jogando a versão instalada.",
      "en": "Can't reach the update server; playing the installed version.",
      "es": "No se pudo conectar al servidor de actualización; jugando la versión instalada."
    },
    offline_uninstalled: {
      "pt-br": "Não foi possível conectar ao servidor de atualização.",
      "en": "Can't reach the update server.",
      "es": "No se pudo conectar al servidor de actualización."
    },
    found_client: {
      "pt-br": "Cliente encontrado nesta pasta: apenas os arquivos modificados serão baixados.",
      "en": "Found an RO3 client in this folder: only the changed files will be downloaded.",
      "es": "Cliente encontrado en esta carpeta: solo se descargarán los archivos modificados."
    },
    paused: {
      "pt-br": "Pausado. Os arquivos baixados foram mantidos; continue a qualquer momento.",
      "en": "Paused. Downloaded parts are kept; continue any time.",
      "es": "Pausado. Los archivos descargados se conservan; continúa en cualquier momento."
    },
    ready: {
      "pt-br": "Tudo atualizado. Bom jogo!",
      "en": "Up to date. Have fun!",
      "es": "¡Todo actualizado! ¡Que disfrutes del juego!"
    },
    checking: {
      "pt-br": "Verificando atualizações…",
      "en": "Checking for updates…",
      "es": "Buscando actualizaciones…"
    },
    running: {
      "pt-br": "O jogo está em execução.",
      "en": "The game is running.",
      "es": "El juego se está ejecutando."
    },
    starting: {
      "pt-br": "Iniciando o cliente do jogo…",
      "en": "Starting the game…",
      "es": "Iniciando el cliente del juego…"
    }
  };

  function translateMessage(rawMsg, phase) {
    if (!rawMsg) {
      var ph = I18N[currentLang].phases[phase];
      return ph ? ph.text : "";
    }

    var lang = currentLang;

    // Check offline installed message
    if (rawMsg.indexOf("Can't reach the update server; playing the installed version") !== -1 ||
        rawMsg.indexOf("Não foi possível conectar ao servidor de atualização; jogando a versão instalada") !== -1 ||
        rawMsg.indexOf("No se pudo conectar al servidor de actualización; jugando la versión instalada") !== -1) {
      return KNOWN_MESSAGES.offline_installed[lang];
    }

    // Check offline uninstalled
    if (rawMsg.indexOf("Can't reach the update server") !== -1 ||
        rawMsg.indexOf("Não foi possível conectar ao servidor de atualização") !== -1 ||
        rawMsg.indexOf("No se pudo conectar al servidor de actualización") !== -1) {
      return KNOWN_MESSAGES.offline_uninstalled[lang];
    }

    // Check found client
    if (rawMsg.indexOf("Found an RO3 client in this folder") !== -1 ||
        rawMsg.indexOf("Cliente encontrado nesta pasta") !== -1 ||
        rawMsg.indexOf("Cliente encontrado en esta carpeta") !== -1) {
      return KNOWN_MESSAGES.found_client[lang];
    }

    // Check paused
    if (rawMsg.indexOf("Paused. Downloaded parts are kept") !== -1 ||
        rawMsg.indexOf("Pausado. Os arquivos baixados foram mantidos") !== -1 ||
        rawMsg.indexOf("Pausado. Los arquivos descargados se conservan") !== -1) {
      return KNOWN_MESSAGES.paused[lang];
    }

    // Version X is available
    var mVer = rawMsg.match(/Version\s+([\w\.\-]+)\s+is available/i) ||
               rawMsg.match(/versão\s+([\w\.\-]+)\s+está disponível/i) ||
               rawMsg.match(/versión\s+([\w\.\-]+)\s+está disponible/i);
    if (mVer) {
      if (lang === "pt-br") return "A versão " + mVer[1] + " está disponível.";
      if (lang === "es") return "La versión " + mVer[1] + " está disponible.";
      return "Version " + mVer[1] + " is available.";
    }

    // X game files differ from the release
    var mDiff = rawMsg.match(/(\d+)\s+game files differ/i) ||
                rawMsg.match(/(\d+)\s+arquivos do jogo diferem/i) ||
                rawMsg.match(/(\d+)\s+archivos del juego difieren/i);
    if (mDiff) {
      if (lang === "pt-br") return mDiff[1] + " arquivos do jogo diferem da versão oficial.";
      if (lang === "es") return mDiff[1] + " archivos del juego difieren de la versión oficial.";
      return mDiff[1] + " game files differ from the release.";
    }

    // Updating the launcher to X
    var mUpLaunch = rawMsg.match(/Updating the launcher to\s+([\w\.\-]+)/i) ||
                    rawMsg.match(/Atualizando o inicializador para\s+([\w\.\-]+)/i);
    if (mUpLaunch) {
      if (lang === "pt-br") return "Atualizando o inicializador para " + mUpLaunch[1] + "…";
      if (lang === "es") return "Actualizando el lanzador a " + mUpLaunch[1] + "…";
      return "Updating the launcher to " + mUpLaunch[1] + "…";
    }

    // The game did not start: X
    var mFail = rawMsg.match(/The game did not start:\s*(.+)/i) ||
                rawMsg.match(/O jogo não iniciou:\s*(.+)/i);
    if (mFail) {
      if (lang === "pt-br") return "O jogo não iniciou: " + mFail[1];
      if (lang === "es") return "El juego no se inició: " + mFail[1];
      return "The game did not start: " + mFail[1];
    }

    // Ready message
    if (rawMsg.indexOf("Up to date") !== -1 ||
        rawMsg.indexOf("Tudo atualizado") !== -1 ||
        rawMsg.indexOf("Todo actualizado") !== -1) {
      return KNOWN_MESSAGES.ready[lang];
    }

    // Checking message
    if (rawMsg.indexOf("Checking for updates") !== -1 ||
        rawMsg.indexOf("Verificando atualizações") !== -1 ||
        rawMsg.indexOf("Buscando actualizaciones") !== -1) {
      return KNOWN_MESSAGES.checking[lang];
    }

    var phText = I18N[lang].phases[phase];
    return (phText && phText.text) ? phText.text : rawMsg;
  }

  function render() {
    var t = I18N[currentLang];
    var cfg = PHASES_CONFIG[state.phase] || PHASES_CONFIG.checking;
    var phaseTexts = t.phases[state.phase] || t.phases.checking;

    $("progress").hidden = !busy;
    $("message").hidden = busy;

    var msg = $("message");
    msg.className = "message" + (cfg.warn ? " warn" : "");
    $("message-text").textContent = translateMessage(state.message, state.phase);
    $("msg-help").hidden = !cfg.warn;

    // Primary button
    var btn = $("primary");
    var icoWrap = $("primary-icon-wrap");
    if (busy) {
      btn.disabled = false;
      $("primary-label").textContent = t.phases.pause.button;
      icoWrap.innerHTML = ICONS.pause;
    } else {
      btn.disabled = !!cfg.wait;
      $("primary-label").textContent = phaseTexts.button;
      icoWrap.innerHTML = ICONS[cfg.icon] || ICONS.spinner;
    }

    // Repair button
    $("repair-label").textContent = t.auth.repair;
    $("repair").disabled = busy || !state.installed || !!cfg.wait;

    var setDirChange = $("set-dir-change");
    if (setDirChange) setDirChange.disabled = busy;

    // Footer Version: e.g. "v1.0.1" or "v2.2.13"
    var vStr = state.installed || state.remote || state.launcher || "1.0.1";
    if (!vStr.startsWith("v")) vStr = "v" + vStr;
    $("ver-line").textContent = vStr;
  }

  function renderProgress(m) {
    stage = m.stage;
    var t = I18N[currentLang];
    $("p-stage").textContent = t.stages[m.stage] || m.stage;
    var f = m.total > 0 ? Math.min(1, m.done / m.total) : 0;
    $("p-pct").textContent = Math.floor(f * 100) + "%";
    $("p-fill").style.width = (f * 100).toFixed(2) + "%";
    $("p-bytes").textContent = m.total > 0 ? size(m.done) + " / " + size(m.total) : "";
    $("p-rate").textContent = (m.stage === "downloading" && m.rate > 0)
      ? (m.rate / 1048576).toFixed(1) + " MB/s · " + eta(m.eta) + " " + t.progress.left
      : "";
  }

  function renderNews(items) {
    var list = $("news-list");
    list.innerHTML = "";
    var currentNews = (items && items.length) ? items : I18N[currentLang].defaultNews.map(function (n) {
      return { tag: fmt(n.tag), color: n.color, title: fmt(n.title), text: fmt(n.text) };
    });
    currentNews.forEach(function (n) {
      var li = document.createElement("li");
      li.style.setProperty("--c", COLORS[n.color] || COLORS.blue);

      var topDiv = document.createElement("div");
      topDiv.className = "list-top";

      var tag = document.createElement("span");
      tag.className = "tag";
      tag.textContent = n.tag || "News";
      topDiv.appendChild(tag);

      if (n.date) {
        var dateSpan = document.createElement("span");
        dateSpan.className = "date";
        dateSpan.textContent = n.date;
        topDiv.appendChild(dateSpan);
      }

      var b = document.createElement("b");
      b.textContent = n.title;

      var p = document.createElement("p");
      p.textContent = n.text || "";

      li.appendChild(topDiv);
      li.appendChild(b);
      li.appendChild(p);

      var linkUrl = n.link || n.url;

      if (linkUrl) {
        var wrap = document.createElement("div");
        wrap.className = "news-item-link-wrap";
        var a = document.createElement("a");
        var isDiscord = linkUrl.indexOf("discord") !== -1;
        a.className = "news-item-link" + (isDiscord ? " discord-link" : "");
        a.href = linkUrl;
        a.target = "_blank";
        a.rel = "noopener noreferrer";

        var defLabel = isDiscord
          ? (currentLang === "es" ? "Unirse a Discord ↗" : (currentLang === "pt-br" ? "Entrar no Discord ↗" : "Join Discord ↗"))
          : (currentLang === "es" ? "Visitar Web ↗" : (currentLang === "pt-br" ? "Visitar Site ↗" : "Visit Web ↗"));
        a.textContent = n.linkText || n.link_text || defLabel;

        (function (targetUrl) {
          a.addEventListener("click", function (e) {
            e.preventDefault();
            openExternalUrl(targetUrl);
          });
        })(linkUrl);

        wrap.appendChild(a);
        li.appendChild(wrap);
      }

      list.appendChild(li);
    });
  }

  function showTab(name) {
    document.querySelectorAll(".tab").forEach(function (t) {
      t.classList.toggle("active", t.dataset.tab === name);
    });
    var settingsSec = $("settings");
    var accountSec = $("account");
    if (settingsSec) settingsSec.hidden = (name !== "settings");
    if (accountSec) accountSec.hidden = (name !== "account");
    $("notices").hidden = (name === "settings" || name === "account");
    $("notices").classList.toggle("wide", name === "news");
  }

  function setAuthFeedback(msg, type) {
    var fb = $("auth-status-msg");
    if (!fb) return;
    if (!msg) {
      fb.hidden = true;
      fb.textContent = "";
      fb.className = "auth-status-msg";
      return;
    }
    fb.hidden = false;
    fb.textContent = msg;
    fb.className = "auth-status-msg " + (type || "waiting");
  }

  function handle(m) {
    switch (m.type) {
      case "state":
        busy = (m.phase === "updating");
        var launcher = state.launcher;
        if (!busy) {
          state = m;
          state.launcher = launcher;
          stage = null;
        } else {
          state.installed = m.installed || state.installed;
          state.remote = m.remote || state.remote;
        }
        render();
        break;
      case "progress":
        if (!busy) busy = true;
        renderProgress(m);
        render();
        break;
      case "server":
        serverData.online = !!m.online;
        serverData.players = m.players != null ? m.players : null;
        renderServerStatus();
        break;
      case "settings":
        var dirEl = $("set-dir");
        if (dirEl) dirEl.textContent = m.dir;
        state.launcher = m.launcher;
        var setVer = $("set-version");
        if (setVer) setVer.textContent = "Launcher v" + m.launcher;
        render();
        break;
      case "news":
        remoteNews = m.items;
        renderNews(remoteNews);
        break;
      case "auth_result":
        var tAuth = I18N[currentLang].auth;
        if ($("btn-do-register")) {
          $("btn-do-register").disabled = !isRegistrationEnabled;
          $("btn-do-register").textContent = isRegistrationEnabled ? tAuth.register : ui("regUnavailable");
        }
        if ($("btn-do-login")) {
          $("btn-do-login").disabled = false;
          $("btn-do-login").textContent = tAuth.login;
        }
        setAuthFeedback(m.message, m.success ? "success" : "error");
        var msg = (m.message || "").toLowerCase();
        var needsCode = msg.indexOf("código") >= 0 || msg.indexOf("codigo") >= 0 || msg.indexOf("verificar") >= 0 ||
                       msg.indexOf("code") >= 0 || msg.indexOf("verify") >= 0;
        if (needsCode) {
          if ($("auth-code-group")) $("auth-code-group").hidden = false;
          if ($("btn-verify-email")) $("btn-verify-email").hidden = false;
          if ($("auth-verify-row")) $("auth-verify-row").hidden = false;
          // El registro pide verificar el correo: mantener el formulario abierto en modo login
          // para que el campo de codigo sea visible y el usuario pueda verificar.
          var formsOpen = $("account-forms");
          if (formsOpen) formsOpen.hidden = false;
          var loggedHide = $("account-logged");
          if (loggedHide) loggedHide.hidden = true;
          var modeLogin = $("auth-mode-login");
          var modeReg = $("auth-mode-register");
          if (modeLogin && modeReg && !modeReg.hidden) {
            modeReg.hidden = true;
            modeLogin.hidden = false;
            var tabL = $("auth-tab-login"), tabR = $("auth-tab-register");
            if (tabL) tabL.classList.add("active");
            if (tabR) tabR.classList.remove("active");
          }
        }
        if (m.success && m.username && !needsCode) {
          var loggedName = $("logged-username");
          if (loggedName) loggedName.textContent = m.username;
          var loggedBox = $("account-logged");
          var formsBox = $("account-forms");
          if (loggedBox) loggedBox.hidden = false;
          if (formsBox) formsBox.hidden = true;
          if ($("auth-input-pass")) $("auth-input-pass").value = "";
          if ($("auth-input-reg-pass")) $("auth-input-reg-pass").value = "";
          if ($("auth-input-pass-confirm")) $("auth-input-pass-confirm").value = "";
        }
        break;
      case "auth_status":
        setAuthFeedback(m.message, m.status || "waiting");
        break;
      case "auth_current":
        var loggedName = $("logged-username");
        var loggedBox = $("account-logged");
        var formsBox = $("account-forms");
        // Signed in only when the host says so; a remembered account just pre-fills the login form.
        if (m.username && m.loggedIn !== false) {
          if (loggedName) loggedName.textContent = m.username;
          if (loggedBox) loggedBox.hidden = false;
          if (formsBox) formsBox.hidden = true;
          if ($("auth-input-user")) $("auth-input-user").value = m.username;
        } else {
          if (loggedBox) loggedBox.hidden = true;
          if (formsBox) formsBox.hidden = false;
          if (m.remembered && $("auth-input-user") && !$("auth-input-user").value) {
            $("auth-input-user").value = m.remembered;
          }
          if ($("auth-remember")) $("auth-remember").checked = !!m.remembered;
        }
        break;
      case "registration_status":
        updateRegistrationState(m.enabled, m.message);
        break;
    }
  }

  var isRegistrationEnabled = true;

  function updateRegistrationState(enabled, message) {
    isRegistrationEnabled = !!enabled;
    var banner = $("auth-reg-disabled");
    var btnReg = $("btn-do-register");
    var userField = $("auth-input-reg-user");
    var emailField = $("auth-input-email");
    var passField = $("auth-input-reg-pass");
    var confirmField = $("auth-input-pass-confirm");
    var tabReg = $("auth-tab-register");

    if (isRegistrationEnabled) {
      if (banner) banner.hidden = true;
      if (btnReg) {
        btnReg.disabled = false;
        btnReg.textContent = (I18N[currentLang] && I18N[currentLang].auth) ? I18N[currentLang].auth.register : ui("authTabRegister");
      }
      if (userField) userField.disabled = false;
      if (emailField) emailField.disabled = false;
      if (passField) passField.disabled = false;
      if (confirmField) confirmField.disabled = false;
      if (tabReg) tabReg.title = ui("regTabTitle");
    } else {
      if (banner) {
        banner.hidden = false;
        var span = banner.querySelector("span:last-child");
        if (span) span.textContent = ui("regDisabled");
      }
      if (btnReg) {
        btnReg.disabled = true;
        btnReg.textContent = ui("regUnavailable");
      }
      if (userField) userField.disabled = true;
      if (emailField) emailField.disabled = true;
      if (passField) passField.disabled = true;
      if (confirmField) confirmField.disabled = true;
      if (tabReg) tabReg.title = ui("regTabDisabled");
    }
  }

  function bind() {
    $("drag").addEventListener("mousedown", function (e) {
      if (e.button === 0 && !e.target.closest("button") && !e.target.closest("a")) {
        send({ cmd: "window", action: "drag" });
      }
    });

    $("btn-min").onclick = function () { send({ cmd: "window", action: "minimize" }); };
    $("btn-close").onclick = function () { send({ cmd: "window", action: "close" }); };

    // Tabs
    document.querySelectorAll(".tab").forEach(function (t) {
      t.onclick = function () { showTab(t.dataset.tab); };
    });

    // Language dropdown toggle
    $("lang-btn").onclick = function (e) {
      e.stopPropagation();
      var drop = $("lang-dropdown");
      drop.hidden = !drop.hidden;
    };

    document.querySelectorAll(".lang-opt").forEach(function (opt) {
      opt.onclick = function (e) {
        e.stopPropagation();
        setLanguage(opt.dataset.lang, true);
        $("lang-dropdown").hidden = true;
      };
    });

    document.addEventListener("click", function () {
      var drop = $("lang-dropdown");
      if (drop) drop.hidden = true;
    });

    // Quick links bar & top icon links
    var btnNewsWeb = $("news-btn-web");
    if (btnNewsWeb) {
      btnNewsWeb.onclick = function (e) {
        e.preventDefault();
        if (CFG.website) openExternalUrl(CFG.website);
      };
    }
    var btnNewsDisc = $("news-btn-discord");
    if (btnNewsDisc) {
      btnNewsDisc.onclick = function (e) {
        e.preventDefault();
        if (CFG.discord) openExternalUrl(CFG.discord);
      };
    }
    var tabWebLink = $("tab-register");
    if (tabWebLink) {
      tabWebLink.onclick = function (e) {
        e.preventDefault();
        if (CFG.website) openExternalUrl(CFG.website);
      };
    }
    var tabDiscLink = $("tab-discord");
    if (tabDiscLink) {
      tabDiscLink.onclick = function (e) {
        e.preventDefault();
        if (CFG.discord) openExternalUrl(CFG.discord);
      };
    }

    // Buttons
    $("primary").onclick = function () {
      if (!busy && state.phase === "needs-auth") {
        var forms = $("account-forms");
        var logged = $("account-logged");
        if (forms) {
          forms.hidden = false;
          if (logged) logged.hidden = true;
        }
        return;
      }
      send({ cmd: busy ? "pause" : "primary" });
    };
    $("repair").onclick = function () { send({ cmd: "repair" }); };

    var setDirChange = $("set-dir-change");
    if (setDirChange) setDirChange.onclick = function () { send({ cmd: "changeDir" }); };

    var setOpen = $("set-open");
    if (setOpen) setOpen.onclick = function () { send({ cmd: "openDir" }); };

    // Auth mode tabs: login vs register
    var lastAuthUser = "";
    function setAuthMode(mode) {
      var tabLogin = $("auth-tab-login"), tabReg = $("auth-tab-register");
      var boxLogin = $("auth-mode-login"), boxReg = $("auth-mode-register");
      if (!boxLogin || !boxReg) return;
      boxLogin.hidden = mode !== "login";
      boxReg.hidden = mode !== "register";
      if (tabLogin) tabLogin.classList.toggle("active", mode === "login");
      if (tabReg) tabReg.classList.toggle("active", mode === "register");
      setAuthFeedback("");
    }
    var tabLoginBtn = $("auth-tab-login");
    if (tabLoginBtn) tabLoginBtn.onclick = function () { setAuthMode("login"); };
    var tabRegBtn = $("auth-tab-register");
    if (tabRegBtn) tabRegBtn.onclick = function () { setAuthMode("register"); };

    // Auth actions
    var btnReg = $("btn-do-register");
    if (btnReg) {
      btnReg.onclick = function () {
        if (!isRegistrationEnabled) {
          setAuthFeedback(ui("regDisabled"), "error");
          return;
        }
        var u = ($("auth-input-reg-user") ? $("auth-input-reg-user").value : "").trim();
        var e = ($("auth-input-email") ? $("auth-input-email").value : "").trim();
        var p = $("auth-input-reg-pass") ? $("auth-input-reg-pass").value : "";
        var p2 = $("auth-input-pass-confirm") ? $("auth-input-pass-confirm").value : "";
        if (!u || !p || !p2) {
          setAuthFeedback(ui("fbRequired"), "error");
          return;
        }
        if (u.length < 3) {
          setAuthFeedback(ui("fbUserLength"), "error");
          return;
        }
        if (p.length < 6) {
          setAuthFeedback(ui("fbPassLength"), "error");
          return;
        }
        if (p !== p2) {
          setAuthFeedback(ui("fbPassMismatch"), "error");
          return;
        }
        setAuthFeedback(ui("fbCreating"), "waiting");
        lastAuthUser = u;
        if ($("auth-input-user")) $("auth-input-user").value = u;
        btnReg.disabled = true;
        btnReg.textContent = I18N[currentLang].auth.creating;
        send({ cmd: "auth_register_local", username: u, email: e, password: p, confirmPassword: p2, remember: rememberChecked() });
      };
    }

    var btnLogin = $("btn-do-login");
    if (btnLogin) {
      btnLogin.onclick = function () {
        var u = ($("auth-input-user") ? $("auth-input-user").value : "").trim();
        var p = $("auth-input-pass") ? $("auth-input-pass").value : "";
        if (!u || !p) {
          setAuthFeedback(ui("fbLoginMissing"), "error");
          return;
        }
        setAuthFeedback(ui("fbSigningIn"), "waiting");
        btnLogin.disabled = true;
        btnLogin.textContent = I18N[currentLang].auth.verifying;
        send({ cmd: "auth_login_local", username: u, password: p, remember: rememberChecked() });
      };
    }

    // "Remember me" stores only the account name on this PC; the password is never stored.
    function rememberChecked() {
      var box = $("auth-remember");
      return !!(box && box.checked);
    }

    // Submit on Enter keypress
    var enterOn = function (id, btn) {
      var el = $(id);
      if (el && btn) {
        el.addEventListener("keydown", function (e) {
          if (e.key === "Enter") {
            e.preventDefault();
            btn.click();
          }
        });
      }
    };
    enterOn("auth-input-user", btnLogin);
    enterOn("auth-input-pass", btnLogin);
    enterOn("auth-input-reg-user", btnReg);
    enterOn("auth-input-email", btnReg);
    enterOn("auth-input-reg-pass", btnReg);
    enterOn("auth-input-pass-confirm", btnReg);



    var btnVerify = $("btn-verify-email");
    if (btnVerify) {
      btnVerify.onclick = function () {
        var u = ($("auth-input-user") ? $("auth-input-user").value : "").trim() || lastAuthUser;
        var code = $("auth-input-code") ? $("auth-input-code").value.trim() : "";
        send({ cmd: "auth_verify_email", username: u, code: code });
      };
    }

    var btnForgot = $("btn-forgot-password");
    if (btnForgot) {
      btnForgot.onclick = function () {
        var e = ($("auth-input-email") ? $("auth-input-email").value : "").trim();
        if (!e) {
          var u = ($("auth-input-user") ? $("auth-input-user").value : "").trim();
          e = u.indexOf("@") !== -1 ? u : "";
        }
        if (!e) {
          setAuthFeedback(ui("fbForgot"), "error");
          return;
        }
        send({ cmd: "auth_request_reset", email: e });
      };
    }

    var btnReset = $("btn-reset-password");
    if (btnReset) {
      btnReset.onclick = function () {
        var u = ($("auth-input-user") ? $("auth-input-user").value : "").trim();
        var code = $("auth-input-reset-code") ? $("auth-input-reset-code").value.trim() : "";
        var p = $("auth-input-reset-pass") ? $("auth-input-reset-pass").value : "";
        var p2 = p;
        send({ cmd: "auth_reset_password", username: u, code: code, password: p, confirmPassword: p2 });
      };
    }

    var btnSwitch = $("btn-account-switch");
    if (btnSwitch) {
      btnSwitch.onclick = function () {
        var loggedBox = $("account-logged");
        var formsBox = $("account-forms");
        if (loggedBox) loggedBox.hidden = true;
        if (formsBox) formsBox.hidden = false;
        setAuthFeedback("");
        send({ cmd: "auth_switch_account" });
      };
    }
  }

  // Mock host for testing in a plain web browser
  function createMockHost(scenario) {
    function emit(o) { setTimeout(function () { handle(o); }, 0); }
    var remote = "2.2.13";
    return {
      post: function (c) {
        if (c.cmd !== "ready") return;
        emit({ type: "settings", dir: "C:\\Games\\HeroSaga\\Client", launcher: "1.0.1" });
        emit({ type: "server", online: scenario !== "offline", players: 124 });
        if (scenario === "offline") return emit({ type: "state", phase: "offline" });
        if (scenario === "offline_installed") {
          return emit({
            type: "state",
            phase: "ready",
            installed: "2.2.13",
            remote: remote,
            message: "Can't reach the update server; playing the installed version."
          });
        }
        if (scenario === "downloading") {
          emit({ type: "state", phase: "install", remote: remote });
          emit({ type: "progress", stage: "downloading", done: 4.2e9, total: 11.5e9, rate: 28.5 * 1048576, eta: 256 });
          return;
        }
        var phase = { install: "install", ready: "ready", repair: "repair" }[scenario] || "ready";
        emit({
          type: "state",
          phase: phase,
          installed: phase === "install" ? null : phase === "update" ? "2.2.10" : remote,
          remote: remote
        });
      }
    };
  }

  function start() {
    var params = new URLSearchParams(location.search);
    if (params.get("lang")) {
      var l = params.get("lang").toLowerCase();
      if (I18N[l]) currentLang = l;
    }
    var wv = window.chrome && window.chrome.webview;
    if (wv) {
      host = { post: function (o) { wv.postMessage(o); } };
      wv.addEventListener("message", function (e) { handle(e.data); });
      document.addEventListener("contextmenu", function (e) { e.preventDefault(); });
    } else {
      host = createMockHost(params.get("mock") || "ready");
    }
    bind();
    applyConfig();
    setLanguage(currentLang);
    send({ cmd: "ready" });
  }

  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", start);
  else start();
})();
