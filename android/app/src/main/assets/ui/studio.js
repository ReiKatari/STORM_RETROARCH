/* ==========================================================================
   STORM RETROARCH 2.6.1 - MOBILE STUDIO FRONTEND ENGINE (studio.js)
   Hardware-Accelerated STORM AURORA FLUID Architecture
   12 Interactive Studio Modules + 120Hz Spring Choreography + Virtual Grid
   Zero Em-Dash, Zero Emojis, Single Vector Icon Mandate
   ========================================================================== */
(function () {
  "use strict";

  const APP_VERSION = "2.6.1";
  const hasBridge = typeof window.StormBridge !== "undefined";

  /* ------------------------------------------------------------------------
     1. UNIQUE GEOMETRIC INLINE SVG ICON REGISTRY (MAX 1 ICON PER ELEMENT)
     ------------------------------------------------------------------------ */
  const ICONS = {
    bolt: '<svg class="ico" viewBox="0 0 24 24"><polygon points="13 2 4 14 11 14 10 22 20 10 13 10 13 2"></polygon></svg>',
    home: '<svg class="ico" viewBox="0 0 24 24"><path d="M3 10.5L12 3l9 7.5V20a1 1 0 0 1-1 1h-5.5v-6h-5v6H4a1 1 0 0 1-1-1v-9.5z"></path></svg>',
    gamepad: '<svg class="ico" viewBox="0 0 24 24"><rect x="2" y="6" width="20" height="12" rx="5"></rect><line x1="6" y1="12" x2="10" y2="12"></line><line x1="8" y1="10" x2="8" y2="14"></line><circle cx="15.5" cy="10.5" r="1"></circle><circle cx="17.5" cy="13.5" r="1"></circle></svg>',
    cpu: '<svg class="ico" viewBox="0 0 24 24"><rect x="5" y="5" width="14" height="14" rx="2.5"></rect><rect x="9" y="9" width="6" height="6" rx="1"></rect><line x1="9" y1="2" x2="9" y2="5"></line><line x1="15" y1="2" x2="15" y2="5"></line><line x1="9" y1="19" x2="9" y2="22"></line><line x1="15" y1="19" x2="15" y2="22"></line><line x1="2" y1="9" x2="5" y2="9"></line><line x1="2" y1="15" x2="5" y2="15"></line><line x1="19" y1="9" x2="22" y2="9"></line><line x1="19" y1="15" x2="22" y2="15"></line></svg>',
    shield: '<svg class="ico" viewBox="0 0 24 24"><path d="M12 2l8 3.5v6c0 5.2-3.4 9.4-8 10.5-4.6-1.1-8-5.3-8-10.5v-6L12 2z"></path><polyline points="9 12 11 14 15.5 9.5"></polyline></svg>',
    display: '<svg class="ico" viewBox="0 0 24 24"><rect x="2" y="4" width="20" height="13" rx="2.2"></rect><line x1="8" y1="21" x2="16" y2="21"></line><line x1="12" y1="17" x2="12" y2="21"></line></svg>',
    wave: '<svg class="ico" viewBox="0 0 24 24"><line x1="4" y1="10" x2="4" y2="14"></line><line x1="8" y1="6" x2="8" y2="18"></line><line x1="12" y1="3" x2="12" y2="21"></line><line x1="16" y1="7" x2="16" y2="17"></line><line x1="20" y1="10" x2="20" y2="14"></line></svg>',
    dpad: '<svg class="ico" viewBox="0 0 24 24"><path d="M9 3h6v6h6v6h-6v6H9v-6H3V9h6V3z"></path></svg>',
    chip: '<svg class="ico" viewBox="0 0 24 24"><rect x="4" y="4" width="16" height="16" rx="3"></rect><path d="M8 12h8M12 8v8"></path></svg>',
    save: '<svg class="ico" viewBox="0 0 24 24"><path d="M5 3h11l4 4v13a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1z"></path><rect x="8" y="3" width="7" height="5"></rect><rect x="7" y="13" width="10" height="6" rx="1"></rect></svg>',
    wrench: '<svg class="ico" viewBox="0 0 24 24"><path d="M14.7 6.3a4.5 4.5 0 0 0-5.8 5.8L3 18l3 3 5.9-5.9a4.5 4.5 0 0 0 5.8-5.8l-2.6 2.6-2-2 2.6-2.6z"></path></svg>',
    trophy: '<svg class="ico" viewBox="0 0 24 24"><path d="M8 21h8M12 17v4M7 4h10v5a5 5 0 0 1-10 0V4z"></path><path d="M7 6H4v2a3 3 0 0 0 3 3M17 6h3v2a3 3 0 0 1-3 3"></path></svg>',
    sliders: '<svg class="ico" viewBox="0 0 24 24"><line x1="4" y1="6" x2="20" y2="6"></line><circle cx="9" cy="6" r="2.2"></circle><line x1="4" y1="12" x2="20" y2="12"></line><circle cx="15" cy="12" r="2.2"></circle><line x1="4" y1="18" x2="20" y2="18"></line><circle cx="10" cy="18" r="2.2"></circle></svg>',
    grid: '<svg class="ico" viewBox="0 0 24 24"><rect x="3" y="3" width="7" height="7" rx="1.6"></rect><rect x="14" y="3" width="7" height="7" rx="1.6"></rect><rect x="3" y="14" width="7" height="7" rx="1.6"></rect><rect x="14" y="14" width="7" height="7" rx="1.6"></rect></svg>',
    play: '<svg class="ico" viewBox="0 0 24 24"><polygon points="6 4 20 12 6 20 6 4"></polygon></svg>',
    folder: '<svg class="ico" viewBox="0 0 24 24"><path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7z"></path></svg>',
    file: '<svg class="ico" viewBox="0 0 24 24"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path><polyline points="14 2 14 8 20 8"></polyline></svg>',
    search: '<svg class="ico" viewBox="0 0 24 24"><circle cx="11" cy="11" r="7"></circle><line x1="20" y1="20" x2="16.2" y2="16.2"></line></svg>',
    star: '<svg class="ico" viewBox="0 0 24 24"><polygon points="12 2.5 14.9 8.4 21.4 9.3 16.7 13.9 17.8 20.4 12 17.3 6.2 20.4 7.3 13.9 2.6 9.3 9.1 8.4 12 2.5"></polygon></svg>',
    download: '<svg class="ico" viewBox="0 0 24 24"><path d="M12 3v12m0 0l-4.5-4.5M12 15l4.5-4.5"></path><path d="M4 17v2a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-2"></path></svg>',
    upload: '<svg class="ico" viewBox="0 0 24 24"><path d="M12 15V3m0 0L7.5 7.5M12 3l4.5 4.5"></path><path d="M4 17v2a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-2"></path></svg>',
    refresh: '<svg class="ico" viewBox="0 0 24 24"><path d="M21 12a9 9 0 1 1-2.64-6.36"></path><polyline points="21 3 21 9 15 9"></polyline></svg>',
    trash: '<svg class="ico" viewBox="0 0 24 24"><polyline points="3 6 5 6 21 6"></polyline><path d="M8 6V4a1 1 0 0 1 1-1h6a1 1 0 0 1 1 1v2m2 0v13a2 2 0 0 1-2 2H9a2 2 0 0 1-2-2V6h10z"></path></svg>',
    check: '<svg class="ico" viewBox="0 0 24 24"><polyline points="20 6 9 17 4 12"></polyline></svg>',
    plus: '<svg class="ico" viewBox="0 0 24 24"><line x1="12" y1="5" x2="12" y2="19"></line><line x1="5" y1="12" x2="19" y2="12"></line></svg>',
    close: '<svg class="ico" viewBox="0 0 24 24"><line x1="18" y1="6" x2="6" y2="18"></line><line x1="6" y1="6" x2="18" y2="18"></line></svg>',
    list: '<svg class="ico" viewBox="0 0 24 24"><line x1="8" y1="6" x2="21" y2="6"></line><line x1="8" y1="12" x2="21" y2="12"></line><line x1="8" y1="18" x2="21" y2="18"></line><circle cx="4" cy="6" r="1"></circle><circle cx="4" cy="12" r="1"></circle><circle cx="4" cy="18" r="1"></circle></svg>',
    disc: '<svg class="ico" viewBox="0 0 24 24"><circle cx="12" cy="12" r="9"></circle><circle cx="12" cy="12" r="2.5"></circle></svg>',
    hash: '<svg class="ico" viewBox="0 0 24 24"><line x1="4" y1="9" x2="20" y2="9"></line><line x1="4" y1="15" x2="20" y2="15"></line><line x1="10" y1="3" x2="8" y2="21"></line><line x1="16" y1="3" x2="14" y2="21"></line></svg>',
    wifi: '<svg class="ico" viewBox="0 0 24 24"><path d="M5 12.5a10 10 0 0 1 14 0"></path><path d="M8.5 16a5 5 0 0 1 7 0"></path><circle cx="12" cy="19.5" r="1"></circle><path d="M2 9a15 15 0 0 1 20 0"></path></svg>',
    shuffle: '<svg class="ico" viewBox="0 0 24 24"><polyline points="16 3 21 3 21 8"></polyline><line x1="4" y1="20" x2="21" y2="3"></line><polyline points="21 16 21 21 16 21"></polyline><line x1="15" y1="15" x2="21" y2="21"></line><line x1="4" y1="4" x2="9" y2="9"></line></svg>',
    chart: '<svg class="ico" viewBox="0 0 24 24"><line x1="18" y1="20" x2="18" y2="10"></line><line x1="12" y1="20" x2="12" y2="4"></line><line x1="6" y1="20" x2="6" y2="14"></line></svg>',
    command: '<svg class="ico" viewBox="0 0 24 24"><path d="M18 3a3 3 0 0 0-3 3v12a3 3 0 0 0 3 3 3 3 0 0 0 3-3 3 3 0 0 0-3-3H6a3 3 0 0 0-3 3 3 3 0 0 0 3 3 3 3 0 0 0 3-3V6a3 3 0 0 0-3-3 3 3 0 0 0-3 3 3 3 0 0 0 3 3h12a3 3 0 0 0 3-3 3 3 0 0 0-3-3z"></path></svg>',
    bookmark: '<svg class="ico" viewBox="0 0 24 24"><path d="M19 21l-7-5-7 5V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z"></path></svg>'
  };

  function ico(name) {
    return ICONS[name] || ICONS.bolt;
  }

  function esc(val) {
    return String(val == null ? "" : val)
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  /* ------------------------------------------------------------------------
     2. 9 THEMES, 13 ACCENTS, 10 FONTS & 12 LANGUAGES DICTIONARY
     ------------------------------------------------------------------------ */
  const THEMES = [
    { id: "MIDNIGHT", label: "MIDNIGHT", bg: "#0D0B18", surface: "#17132B" },
    { id: "OBSIDIAN", label: "OBSIDIAN", bg: "#06070B", surface: "#10131E" },
    { id: "CARBON", label: "CARBON", bg: "#0E1015", surface: "#181C25" },
    { id: "CYBERPUNK", label: "CYBERPUNK", bg: "#11061B", surface: "#200C34" },
    { id: "AURORA", label: "AURORA", bg: "#07131D", surface: "#0F2234" },
    { id: "NEBULA", label: "NEBULA", bg: "#140921", surface: "#24123A" },
    { id: "OCEAN", label: "OCEAN", bg: "#061322", surface: "#0D2440" },
    { id: "EMERALD DARK", label: "EMERALD DARK", bg: "#061511", surface: "#0D2922" },
    { id: "SOLARIZED DARK", label: "SOLARIZED DARK", bg: "#001A23", surface: "#072D3A" }
  ];

  const ACCENTS = [
    { id: "VIOLET", hex: "#8B5CF6" },
    { id: "CYAN", hex: "#06B6D4" },
    { id: "EMERALD", hex: "#10B981" },
    { id: "ROSE", hex: "#F43F5E" },
    { id: "AMBER", hex: "#F59E0B" },
    { id: "BLUE", hex: "#3B82F6" },
    { id: "INDIGO", hex: "#6366F1" },
    { id: "FUCHSIA", hex: "#D946EF" },
    { id: "CRIMSON", hex: "#EF4444" },
    { id: "TEAL", hex: "#14B8A6" },
    { id: "LIME", hex: "#84CC16" },
    { id: "ORANGE", hex: "#F97316" },
    { id: "GOLD", hex: "#EAB308" }
  ];

  const FONTS = [
    "Inter",
    "Plus Jakarta Sans",
    "Geist Sans",
    "Manrope",
    "Outfit",
    "Montserrat",
    "Rubik",
    "Rajdhani",
    "JetBrains Mono",
    "Fira Code"
  ];

  const LANGUAGES = [
    { code: "ru", label: "Русский" },
    { code: "en", label: "English" },
    { code: "de", label: "Deutsch" },
    { code: "fr", label: "Français" },
    { code: "es", label: "Español" },
    { code: "it", label: "Italiano" },
    { code: "pt", label: "Português" },
    { code: "pl", label: "Polski" },
    { code: "tr", label: "Türkçe" },
    { code: "ja", label: "日本語" },
    { code: "zh", label: "简体中文" },
    { code: "ko", label: "한국어" }
  ];

  const I18N = {
    ru: {
      nav_dashboard: "Главное",
      nav_library: "Игры",
      nav_cores: "Ядра",
      nav_cheats: "Читы",
      nav_graphics: "Графика",
      nav_audio: "Аудио DSP",
      nav_controls: "Управление",
      nav_bios: "BIOS",
      nav_saves: "Сейвы",
      nav_romtools: "ROM Патчи",
      nav_community: "Netplay",
      nav_settings: "Настройки",
      nav_studio: "Студия",
      ready_cores_short: "готово",
      hero_continue: "ПРОДОЛЖИТЬ ИГРУ",
      play: "Играть",
      quick_state: "Быстрый стейт",
      game_options: "Опции игры",
      add_folder: "Добавить папку",
      open_rom: "Открыть ROM",
      quick_scan: "Быстрое сканирование памяти",
      covers: "Обложки",
      stat_games: "Игр в библиотеке",
      stat_cores: "Готовых ядер",
      stat_cheats: "База читов",
      stat_bios: "Статус BIOS и GPU",
      recent_launches: "Недавние запуски",
      quick_studio: "Быстрые инструменты студии",
      all: "Все",
      favorites: "Избранное",
      search_games: "Поиск по названию, платформе или региону...",
      posters: "Постеры",
      tiles: "Плитки",
      list: "Список"
    },
    en: {
      nav_dashboard: "Home",
      nav_library: "Games",
      nav_cores: "Cores",
      nav_cheats: "Cheats",
      nav_graphics: "Graphics",
      nav_audio: "Audio DSP",
      nav_controls: "Controls",
      nav_bios: "BIOS",
      nav_saves: "Saves",
      nav_romtools: "ROM Tools",
      nav_community: "Netplay",
      nav_settings: "Settings",
      nav_studio: "Studio",
      ready_cores_short: "ready",
      hero_continue: "CONTINUE PLAYING",
      play: "Play",
      quick_state: "Quick State",
      game_options: "Game Options",
      add_folder: "Add Folder",
      open_rom: "Open ROM",
      quick_scan: "Quick Storage Scan",
      covers: "Covers",
      stat_games: "Games in Library",
      stat_cores: "Ready Cores",
      stat_cheats: "Cheats Vault",
      stat_bios: "BIOS & GPU Status",
      recent_launches: "Recent Launches",
      quick_studio: "Studio Quick Tools",
      all: "All",
      favorites: "Favorites",
      search_games: "Search by title, platform or region...",
      posters: "Posters",
      tiles: "Tiles",
      list: "List"
    },
    de: { nav_dashboard: "Start", nav_library: "Spiele", nav_cores: "Kerne", nav_cheats: "Cheats", nav_graphics: "Grafik", nav_audio: "Audio DSP", nav_controls: "Steuerung", nav_bios: "BIOS", nav_saves: "Speicher", nav_romtools: "ROM-Tools", nav_community: "Netplay", nav_settings: "Optionen", nav_studio: "Studio", play: "Spielen", quick_state: "Schnellstand", game_options: "Spieloptionen", all: "Alle", favorites: "Favoriten" },
    fr: { nav_dashboard: "Accueil", nav_library: "Jeux", nav_cores: "Coeurs", nav_cheats: "Codes", nav_graphics: "Graphismes", nav_audio: "Audio DSP", nav_controls: "Controles", nav_bios: "BIOS", nav_saves: "Sauvegardes", nav_romtools: "Outils ROM", nav_community: "Netplay", nav_settings: "Reglages", nav_studio: "Studio", play: "Jouer", quick_state: "Etat rapide", game_options: "Options", all: "Tous", favorites: "Favoris" },
    es: { nav_dashboard: "Inicio", nav_library: "Juegos", nav_cores: "Nucleos", nav_cheats: "Trucos", nav_graphics: "Graficos", nav_audio: "Audio DSP", nav_controls: "Controles", nav_bios: "BIOS", nav_saves: "Partidas", nav_romtools: "ROM Tools", nav_community: "Netplay", nav_settings: "Ajustes", nav_studio: "Estudio", play: "Jugar", quick_state: "Estado rapido", game_options: "Opciones", all: "Todos", favorites: "Favoritos" },
    it: { nav_dashboard: "Home", nav_library: "Giochi", nav_cores: "Core", nav_cheats: "Trucchi", nav_graphics: "Grafica", nav_audio: "Audio DSP", nav_controls: "Controlli", nav_bios: "BIOS", nav_saves: "Salvataggi", nav_romtools: "Strumenti ROM", nav_community: "Netplay", nav_settings: "Impostazioni", nav_studio: "Studio", play: "Gioca", quick_state: "Stato rapido", game_options: "Opzioni", all: "Tutti", favorites: "Preferiti" },
    pt: { nav_dashboard: "Inicio", nav_library: "Jogos", nav_cores: "Nucleos", nav_cheats: "Cheats", nav_graphics: "Graficos", nav_audio: "Audio DSP", nav_controls: "Controles", nav_bios: "BIOS", nav_saves: "Saves", nav_romtools: "ROM Tools", nav_community: "Netplay", nav_settings: "Ajustes", nav_studio: "Estudio", play: "Jogar", quick_state: "Save rapido", game_options: "Opcoes", all: "Todos", favorites: "Favoritos" },
    pl: { nav_dashboard: "Start", nav_library: "Gry", nav_cores: "Rdzenie", nav_cheats: "Kody", nav_graphics: "Grafika", nav_audio: "Audio DSP", nav_controls: "Sterowanie", nav_bios: "BIOS", nav_saves: "Zapisy", nav_romtools: "Narzedzia ROM", nav_community: "Netplay", nav_settings: "Ustawienia", nav_studio: "Studio", play: "Graj", quick_state: "Szybki zapis", game_options: "Opcje gry", all: "Wszystkie", favorites: "Ulubione" },
    tr: { nav_dashboard: "Ana Sayfa", nav_library: "Oyunlar", nav_cores: "Cekirdek", nav_cheats: "Hileler", nav_graphics: "Grafik", nav_audio: "Ses DSP", nav_controls: "Kontroller", nav_bios: "BIOS", nav_saves: "Kayitlar", nav_romtools: "ROM Araclari", nav_community: "Netplay", nav_settings: "Ayarlar", nav_studio: "Studyo", play: "Oyna", quick_state: "Hizli Kayit", game_options: "Secenekler", all: "Tumu", favorites: "Favoriler" },
    ja: { nav_dashboard: "ホーム", nav_library: "ゲーム", nav_cores: "コア", nav_cheats: "チート", nav_graphics: "映像", nav_audio: "音声 DSP", nav_controls: "操作", nav_bios: "BIOS", nav_saves: "セーブ", nav_romtools: "ROMツール", nav_community: "通信", nav_settings: "設定", nav_studio: "スタジオ", play: "起動", quick_state: "クイックロード", game_options: "設定", all: "すべて", favorites: "お気に入り" },
    zh: { nav_dashboard: "主页", nav_library: "游戏库", nav_cores: "核心", nav_cheats: "金手指", nav_graphics: "画面", nav_audio: "音频 DSP", nav_controls: "操控", nav_bios: "BIOS", nav_saves: "存档", nav_romtools: "ROM工具", nav_community: "联机", nav_settings: "设置", nav_studio: "工作室", play: "启动", quick_state: "快速读档", game_options: "游戏设置", all: "全部", favorites: "收藏" },
    ko: { nav_dashboard: "홈", nav_library: "게임", nav_cores: "코어", nav_cheats: "치트", nav_graphics: "그래픽", nav_audio: "오디오 DSP", nav_controls: "컨트롤", nav_bios: "BIOS", nav_saves: "세이브", nav_romtools: "ROM 도구", nav_community: "넷플레이", nav_settings: "설정", nav_studio: "스튜디오", play: "실행", quick_state: "빠른 슬롯", game_options: "게임 옵션", all: "전체", favorites: "즐겨찾기" }
  };

  const STUDIO_MODULES = [
    { id: "dashboard", key: "nav_dashboard", icon: "home", group: "core", descRu: "Спотлайт, телеметрия и быстрый старт", descEn: "Spotlight, telemetry & quick launch" },
    { id: "library", key: "nav_library", icon: "gamepad", group: "core", descRu: "Постеры, платформы и инспектор ROM", descEn: "Boxart posters, filters & ROM inspector" },
    { id: "cores", key: "nav_cores", icon: "cpu", group: "core", descRu: "20 вшитых + 252 ядра Libretro CDN", descEn: "20 pre-bundled + 252 Libretro cores" },
    { id: "cheats", key: "nav_cheats", icon: "shield", group: "core", descRu: "26 797 игр, конструктор и сканер ОЗУ", descEn: "26,797 games, cheat builder & RAM scan" },
    { id: "graphics", key: "nav_graphics", icon: "display", group: "av", descRu: "GLES3/Vulkan, 1x-8x, CRT шейдеры и рамки", descEn: "GLES3/Vulkan, 1x-8x, CRT shaders & bezels" },
    { id: "audio", key: "nav_audio", icon: "wave", group: "av", descRu: "Эквалайзер DSP, усиление дБ и буфер", descEn: "DSP Equalizer, pre-amp dB & low latency" },
    { id: "controls", key: "nav_controls", icon: "dpad", group: "input", descRu: "3D сенсорный геймпад, гироскоп и хоткеи", descEn: "3D touch pad, gyro stick & pad remapper" },
    { id: "bios", key: "nav_bios", icon: "chip", group: "input", descRu: "Проверка MD5 прошивок для 13 систем", descEn: "MD5 firmware checklist across 13 consoles" },
    { id: "saves", key: "nav_saves", icon: "save", group: "input", descRu: "Слоты 0-9, батарейки .srm и .ZIP для ПК", descEn: "Slots 0-9, .srm battery & PC .ZIP sync" },
    { id: "romtools", key: "nav_romtools", icon: "wrench", group: "sys", descRu: "Патчер IPS/BPS/UPS, .M3U и CRC32/SHA-1", descEn: "IPS/BPS/UPS patcher, .M3U & ROM hashes" },
    { id: "community", key: "nav_community", icon: "trophy", group: "sys", descRu: "RetroAchievements, Wi-Fi Netplay и гид", descEn: "RetroAchievements, Wi-Fi Netplay & guide" },
    { id: "settings", key: "nav_settings", icon: "sliders", group: "sys", descRu: "9 тем, 13 акцентов, 11+ языков и память", descEn: "9 themes, 13 accents, languages & storage" }
  ];

  const NAV_ORDER = STUDIO_MODULES.map(function (m) { return m.id; });

  const COMPLETION_STATUSES = [
    { id: "Не начато", en: "Not Started", cls: "status-none", color: "#64748B" },
    { id: "Играю сейчас", en: "Playing", cls: "status-playing", color: "#06B6D4" },
    { id: "Пройдено", en: "Completed", cls: "status-done", color: "#10B981" },
    { id: "Пройдено на 100%", en: "100% Mastered", cls: "status-100", color: "#EAB308" },
    { id: "В планах", en: "Plan to Play", cls: "status-planned", color: "#8B5CF6" },
    { id: "Отложено", en: "On Hold", cls: "status-hold", color: "#F43F5E" }
  ];

  /* ------------------------------------------------------------------------
     3. PROCEDURAL CONSOLE COVER POSTER GENERATOR (CRISP DATA-URI SVG)
     ------------------------------------------------------------------------ */
  function makePosterSvg(title, subtitle, platform, region, c1, c2, accentHex) {
    const svg =
      '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 300 400">' +
      '<defs>' +
      '<linearGradient id="bg" x1="0" y1="0" x2="1" y2="1">' +
      '<stop offset="0%" stop-color="' + c1 + '"/>' +
      '<stop offset="55%" stop-color="' + c2 + '"/>' +
      '<stop offset="100%" stop-color="#07060E"/>' +
      '</linearGradient>' +
      '<linearGradient id="foil" x1="0" y1="0" x2="1" y2="0">' +
      '<stop offset="0%" stop-color="' + accentHex + '"/>' +
      '<stop offset="100%" stop-color="#38BDF8"/>' +
      '</linearGradient>' +
      '</defs>' +
      '<rect width="300" height="400" fill="url(#bg)"/>' +
      '<circle cx="235" cy="95" r="95" fill="' + accentHex + '" fill-opacity="0.22"/>' +
      '<circle cx="65" cy="300" r="110" fill="#38BDF8" fill-opacity="0.14"/>' +
      '<polygon points="150,78 228,156 150,234 72,156" fill="none" stroke="url(#foil)" stroke-width="3.5" opacity="0.85"/>' +
      '<polygon points="150,104 202,156 150,208 98,156" fill="' + accentHex + '" fill-opacity="0.24" stroke="#F8FAFC" stroke-width="1.5"/>' +
      '<rect x="0" y="0" width="300" height="36" fill="#05040A" fill-opacity="0.78"/>' +
      '<rect x="0" y="36" width="300" height="3" fill="url(#foil)"/>' +
      '<text x="16" y="24" fill="#F8FAFC" font-family="Segoe UI, Arial, sans-serif" font-size="13" font-weight="900" letter-spacing="1.6">' + esc(platform) + '</text>' +
      '<text x="284" y="24" text-anchor="end" fill="' + accentHex + '" font-family="Segoe UI, Arial, sans-serif" font-size="12" font-weight="800">' + esc(region) + '</text>' +
      '<rect x="14" y="266" width="272" height="118" rx="14" fill="#080612" fill-opacity="0.82" stroke="#FFFFFF" stroke-opacity="0.18"/>' +
      '<text x="28" y="304" fill="#F8FAFC" font-family="Segoe UI, Arial, sans-serif" font-size="22" font-weight="900">' + esc(title) + '</text>' +
      '<text x="28" y="330" fill="#CBD5E1" font-family="Segoe UI, Arial, sans-serif" font-size="13" font-weight="700">' + esc(subtitle) + '</text>' +
      '<rect x="28" y="348" width="96" height="22" rx="6" fill="' + accentHex + '" fill-opacity="0.32"/>' +
      '<text x="76" y="363" text-anchor="middle" fill="#F8FAFC" font-family="Segoe UI, Arial, sans-serif" font-size="10.5" font-weight="800">STORM STUDIO</text>' +
      '</svg>';
    return "data:image/svg+xml;utf8," + encodeURIComponent(svg);
  }

  /* ------------------------------------------------------------------------
     4. REALISTIC 252 CORES + PREVIEW DATA FACTORY (WHEN BRIDGE IS ABSENT)
     ------------------------------------------------------------------------ */
  function buildDefaultCores252() {
    const flagship20 = [
      { id: "snes9x", displayName: "Super Nintendo (Snes9x)", platformKey: "snes", platform: "SNES", bundled: true, installed: true, recommended: true },
      { id: "pcsx_rearmed", displayName: "Sony PlayStation (PCSX ReARMed)", platformKey: "psx", platform: "PS1", bundled: true, installed: true, recommended: true },
      { id: "swanstation", displayName: "Sony PlayStation HD (SwanStation)", platformKey: "psx", platform: "PS1", bundled: true, installed: true, recommended: true },
      { id: "ppsspp", displayName: "Sony PSP (PPSSPP)", platformKey: "psp", platform: "PSP", bundled: true, installed: true, recommended: true },
      { id: "mupen64plus_next_gles3", displayName: "Nintendo 64 (Mupen64Plus-Next GLES3)", platformKey: "n64", platform: "N64", bundled: true, installed: true, recommended: true },
      { id: "genesis_plus_gx", displayName: "Sega Genesis / CD / MS (Genesis Plus GX)", platformKey: "md", platform: "Sega Genesis", bundled: true, installed: true, recommended: true },
      { id: "picodrive", displayName: "Sega 32X / Mega Drive (PicoDrive)", platformKey: "md", platform: "Sega Genesis", bundled: true, installed: true, recommended: false },
      { id: "mgba", displayName: "Game Boy Advance (mGBA)", platformKey: "gba", platform: "GBA", bundled: true, installed: true, recommended: true },
      { id: "gambatte", displayName: "Game Boy / Color (Gambatte)", platformKey: "gbc", platform: "GBC", bundled: true, installed: true, recommended: true },
      { id: "melonds", displayName: "Nintendo DS (melonDS)", platformKey: "nds", platform: "NDS", bundled: true, installed: true, recommended: true },
      { id: "fceumm", displayName: "NES / Famicom (FCEUmm)", platformKey: "nes", platform: "NES", bundled: true, installed: true, recommended: true },
      { id: "flycast", displayName: "Sega Dreamcast / NAOMI (Flycast)", platformKey: "dc", platform: "Dreamcast", bundled: true, installed: true, recommended: true },
      { id: "fbneo", displayName: "Arcade CPS / Neo Geo (FinalBurn Neo)", platformKey: "arcade", platform: "Arcade", bundled: true, installed: true, recommended: true },
      { id: "dosbox_pure", displayName: "MS-DOS / Windows 9x (DOSBox-Pure)", platformKey: "dos", platform: "MS-DOS", bundled: true, installed: true, recommended: true },
      { id: "opera", displayName: "3DO Interactive Multiplayer (Opera)", platformKey: "3do", platform: "3DO", bundled: true, installed: true, recommended: true },
      { id: "mednafen_pce_fast", displayName: "PC Engine / TurboGrafx-CD (Beetle PCE)", platformKey: "pce", platform: "PC Engine", bundled: true, installed: true, recommended: true },
      { id: "mednafen_ngp", displayName: "Neo Geo Pocket Color (Beetle NGP)", platformKey: "ngp", platform: "NGPC", bundled: true, installed: true, recommended: true },
      { id: "mednafen_wswan", displayName: "Bandai WonderSwan Color (Beetle Cygne)", platformKey: "ws", platform: "WonderSwan", bundled: true, installed: true, recommended: true },
      { id: "stella2014", displayName: "Atari 2600 (Stella)", platformKey: "atari2600", platform: "Atari 2600", bundled: true, installed: true, recommended: true },
      { id: "prboom", displayName: "DOOM Engine (PrBoom)", platformKey: "ports", platform: "Ports", bundled: true, installed: true, recommended: false }
    ];

    const cloudNamed = [
      ["beetle_psx_hw", "Sony PlayStation Vulkan (Beetle PSX HW)", "psx", "PS1", true],
      ["yabasanshiro", "Sega Saturn (YabaSanshiro)", "saturn", "Saturn", true],
      ["beetle_saturn", "Sega Saturn Accurate (Beetle Saturn)", "saturn", "Saturn", true],
      ["citra", "Nintendo 3DS (Citra)", "3ds", "3DS", true],
      ["desmume", "Nintendo DS (DeSmuME)", "nds", "NDS", false],
      ["dolphin", "GameCube / Wii (Dolphin)", "gc", "GameCube", true],
      ["pcsx2_play", "PlayStation 2 (Play!)", "ps2", "PS2", false],
      ["bsnes_hd", "Super Nintendo Widescreen (bsnes-hd)", "snes", "SNES", true],
      ["mesen", "NES / Famicom Cycle-Accurate (Mesen)", "nes", "NES", true],
      ["nestopia", "NES / FDS (Nestopia UE)", "nes", "NES", false],
      ["mame2003_plus", "Arcade Classic (MAME 2003-Plus)", "arcade", "Arcade", true],
      ["mame2010", "Arcade Modern (MAME 2010)", "arcade", "Arcade", false],
      ["puae", "Commodore Amiga 500/1200/CD32 (PUAE)", "amiga", "Amiga", true],
      ["vice_x64", "Commodore 64 (VICE x64)", "c64", "C64", true],
      ["fuse", "ZX Spectrum 128K / Pentagon (Fuse)", "zx", "ZX Spectrum", true],
      ["np2kai", "NEC PC-9801 (Neko Project II Kai)", "pc98", "PC-98", true],
      ["px68k", "Sharp X68000 (PX68K)", "x68k", "X68000", true],
      ["scummvm", "LucasArts / Sierra Adventures (ScummVM)", "scummvm", "ScummVM", true],
      ["neocd", "SNK Neo Geo CD (NeoCD)", "neogeocd", "Neo Geo CD", true],
      ["parallel_n64", "Nintendo 64 (ParaLLEl N64)", "n64", "N64", false],
      ["vba_m", "Game Boy Advance (VBA-M)", "gba", "GBA", false],
      ["sameboy", "Game Boy Color Accurate (SameBoy)", "gbc", "GBC", true],
      ["handy", "Atari Lynx (Handy)", "lynx", "Atari Lynx", true],
      ["virtualjaguar", "Atari Jaguar (Virtual Jaguar)", "jaguar", "Jaguar", false]
    ];

    const list = flagship20.map(function (c) {
      return Object.assign({ soName: "lib" + c.id + "_libretro_android.so", progress: 0 }, c);
    });

    cloudNamed.forEach(function (row) {
      list.push({
        id: row[0],
        displayName: row[1],
        platformKey: row[2],
        platform: row[3],
        bundled: false,
        installed: false,
        recommended: row[4],
        soName: "lib" + row[0] + "_libretro_android.so",
        progress: 0
      });
    });

    const extraSystems = [
      ["arcade", "Arcade"], ["snes", "SNES"], ["nes", "NES"], ["md", "Sega Genesis"],
      ["psx", "PS1"], ["gba", "GBA"], ["dos", "MS-DOS"], ["amiga", "Amiga"],
      ["zx", "ZX Spectrum"], ["c64", "C64"], ["pce", "PC Engine"], ["ports", "Ports"]
    ];
    let idx = 1;
    while (list.length < 252) {
      const sys = extraSystems[idx % extraSystems.length];
      const coreId = sys[0] + "_core_" + idx;
      list.push({
        id: coreId,
        displayName: sys[1] + " Libretro Core #" + idx,
        platformKey: sys[0],
        platform: sys[1],
        bundled: false,
        installed: false,
        recommended: false,
        soName: "lib" + coreId + "_libretro_android.so",
        progress: 0
      });
      idx++;
    }
    return list;
  }

  function buildPreviewGames() {
    return [
      {
        id: "chrono_trigger_ru",
        title: "Chrono Trigger",
        subtitle: "MOD - RUS • Хроно Триггер",
        platform: "SNES",
        platformKey: "snes",
        region: "RUS",
        coreId: "snes9x",
        favorite: true,
        playtimeMin: 860,
        totalPlaySeconds: 51600,
        launchCount: 14,
        lastPlayedIso: "2026-11-12T18:20:00Z",
        completionStatus: "Пройдено на 100%",
        starRating: 5,
        userNotes: "Все 13 концовок открыты в версии MOD RUS.",
        customCollection: "JRPG Шедевры",
        sizeMb: "4.0 MB",
        filePath: "/storage/emulated/0/STORM RETROARCH/roms/snes/Chrono Trigger [MOD - RUS].sfc",
        cover: makePosterSvg("CHRONO TRIGGER", "RPG ШЕДЕВР • MOD RUS", "SUPER NINTENDO", "RUS", "#311058", "#0F2647", "#8B5CF6")
      },
      {
        id: "re2_dualshock",
        title: "Resident Evil 2 DualShock",
        subtitle: "Disc 1 - Leon S. Kennedy",
        platform: "PS1",
        platformKey: "psx",
        region: "USA",
        coreId: "swanstation",
        favorite: true,
        playtimeMin: 515,
        totalPlaySeconds: 30900,
        launchCount: 9,
        lastPlayedIso: "2026-11-11T21:10:00Z",
        completionStatus: "Играю сейчас",
        starRating: 5,
        userNotes: "Leon A / Claire B - 2x PGXP SwanStation.",
        customCollection: "Survival Horror",
        sizeMb: "618 MB",
        filePath: "/storage/emulated/0/STORM RETROARCH/roms/psx/Resident Evil 2.chd",
        cover: makePosterSvg("RESIDENT EVIL 2", "DUALSHOCK VER • CHD", "PLAYSTATION", "USA", "#4C0519", "#17132B", "#F43F5E")
      },
      {
        id: "sonic3_knuckles",
        title: "Sonic 3 & Knuckles",
        subtitle: "Lock-On Complete Edition",
        platform: "Sega Genesis",
        platformKey: "md",
        region: "EUR",
        coreId: "genesis_plus_gx",
        favorite: true,
        playtimeMin: 340,
        totalPlaySeconds: 20400,
        launchCount: 11,
        lastPlayedIso: "2026-11-10T16:40:00Z",
        completionStatus: "Пройдено",
        starRating: 5,
        userNotes: "Собраны все 7 Гипер-Изумрудов.",
        customCollection: "Sega 16-bit",
        sizeMb: "4.0 MB",
        filePath: "/storage/emulated/0/STORM RETROARCH/roms/md/Sonic 3 & Knuckles.md",
        cover: makePosterSvg("SONIC 3 & KNUCKLES", "LOCK-ON TECHNOLOGY", "SEGA GENESIS", "EUR", "#0C2D6B", "#091834", "#06B6D4")
      },
      {
        id: "castlevania_aria",
        title: "Castlevania: Aria of Sorrow",
        subtitle: "RUS VWF • Ария Печали",
        platform: "GBA",
        platformKey: "gba",
        region: "RUS",
        coreId: "mgba",
        favorite: false,
        playtimeMin: 275,
        totalPlaySeconds: 16500,
        launchCount: 6,
        lastPlayedIso: "2026-11-08T14:05:00Z",
        completionStatus: "Играю сейчас",
        starRating: 4,
        userNotes: "",
        customCollection: "Metroidvania",
        sizeMb: "8.0 MB",
        filePath: "/storage/emulated/0/STORM RETROARCH/roms/gba/Castlevania Aria [RUS].gba",
        cover: makePosterSvg("ARIA OF SORROW", "CASTLEVANIA • VWF RUS", "GAME BOY ADVANCE", "RUS", "#3B0764", "#1E1B4B", "#D946EF")
      },
      {
        id: "gow_chains",
        title: "God of War: Chains of Olympus",
        subtitle: "60 FPS Vulkan / GLES3",
        platform: "PSP",
        platformKey: "psp",
        region: "EUR",
        coreId: "ppsspp",
        favorite: false,
        playtimeMin: 420,
        totalPlaySeconds: 25200,
        launchCount: 5,
        lastPlayedIso: "2026-11-05T19:30:00Z",
        completionStatus: "В планах",
        starRating: 4,
        userNotes: "",
        customCollection: "",
        sizeMb: "1.18 GB",
        filePath: "/storage/emulated/0/STORM RETROARCH/roms/psp/God of War Chains.iso",
        cover: makePosterSvg("GOD OF WAR", "CHAINS OF OLYMPUS", "SONY PSP", "EUR", "#451A03", "#1C1917", "#F59E0B")
      },
      {
        id: "soulcalibur_dc",
        title: "SoulCalibur",
        subtitle: "Arcade Perfect GDI / CHD",
        platform: "Dreamcast",
        platformKey: "dc",
        region: "JPN",
        coreId: "flycast",
        favorite: false,
        playtimeMin: 195,
        totalPlaySeconds: 11700,
        launchCount: 4,
        lastPlayedIso: "2026-11-02T12:00:00Z",
        completionStatus: "Не начато",
        starRating: 0,
        userNotes: "",
        customCollection: "Arcade Fighting",
        sizeMb: "742 MB",
        filePath: "/storage/emulated/0/STORM RETROARCH/roms/dc/SoulCalibur.chd",
        cover: makePosterSvg("SOULCALIBUR", "WEAPON FIGHTING 60HZ", "DREAMCAST", "JPN", "#064E3B", "#0F172A", "#10B981")
      }
    ];
  }

  function buildPreviewBios() {
    return [
      { id: "ps1_5501", platform: "PS1", relativePath: "system/scph5501.bin", expectedMd5: "490f666e1afb15b7362b406ed1cea246", status: "verified" },
      { id: "ps2_bios", platform: "PS2", relativePath: "system/pcsx2/bios/scph39001.bin", expectedMd5: "d5ce2c7d119f563ce04bc04dbc3a323e", status: "present" },
      { id: "segacd_us", platform: "Sega CD", relativePath: "system/bios_CD_U.bin", expectedMd5: "2efd74e3232ff260e371b99f84024f7f", status: "verified" },
      { id: "saturn_jp", platform: "Saturn", relativePath: "system/sega_101.bin", expectedMd5: "85ec9ca47d8f6807718151cbcca8b964", status: "verified" },
      { id: "dc_boot", platform: "Dreamcast", relativePath: "system/dc/dc_boot.bin", expectedMd5: "e10c53c2f8b90bab96ead2d368858623", status: "verified" },
      { id: "nds_bios7", platform: "Nintendo DS", relativePath: "system/bios7.bin", expectedMd5: "df692a80a5b1bc90728bc3dfc76cd948", status: "verified" },
      { id: "gba_bios", platform: "GBA", relativePath: "system/gba_bios.bin", expectedMd5: "a860e8c0b6d573d191e4ec7db1b1e4f6", status: "verified" },
      { id: "pce_syscard", platform: "PC Engine CD", relativePath: "system/syscard3.pce", expectedMd5: "38179df8f4ac870017db21ebcbf53114", status: "verified" },
      { id: "3do_fz10", platform: "3DO", relativePath: "system/panafz10.bin", expectedMd5: "51f2f43ae2f3508a14d9f56597e2d3ce", status: "verified" },
      { id: "neogeo_zip", platform: "Neo Geo", relativePath: "system/neogeo.zip", expectedMd5: "dfb996f9874469a12df8282902821e6a", status: "verified" },
      { id: "amiga_kick", platform: "Amiga", relativePath: "system/kick34005.A500", expectedMd5: "82a21c1890cae844b3df741f2762d48d", status: "missing" },
      { id: "pc98_ipl", platform: "PC-98", relativePath: "system/np2kai/itf.rom", expectedMd5: "eb85b9a24f56b4d2b453b6e1d3607f55", status: "missing" },
      { id: "x68k_ipl", platform: "Sharp X68000", relativePath: "system/keropi/iplrom.dat", expectedMd5: "7fd4caabac1d9169e289f0f7bbf71d8e", status: "missing" }
    ];
  }

  const PREVIEW_CHEAT_GAMES = [
    { id: "chrono_trigger_ru", title: "Chrono Trigger (SNES)", system: "SNES", inLibrary: true, count: 6 },
    { id: "re2_dualshock", title: "Resident Evil 2 DualShock (PS1)", system: "PS1", inLibrary: true, count: 5 },
    { id: "sonic3_knuckles", title: "Sonic 3 & Knuckles (Genesis)", system: "Sega Genesis", inLibrary: true, count: 4 },
    { id: "contra_hard_corps", title: "Contra: Hard Corps (Genesis)", system: "Sega Genesis", inLibrary: false, count: 5 },
    { id: "castlevania_sotn", title: "Castlevania: Symphony of the Night (PS1)", system: "PS1", inLibrary: false, count: 5 },
    { id: "metal_slug_3", title: "Metal Slug 3 (Arcade FBNeo)", system: "Arcade", inLibrary: false, count: 4 }
  ];

  const PREVIEW_CHEAT_CODES = {
    chrono_trigger_ru: [
      { index: 0, description: "Бесконечное здоровье (999 ОЗ всей группы)", code: "7E2603E7+7E260403", enabled: true },
      { index: 1, description: "Бесконечная магия (99 ОМ в бою)", code: "7E260763", enabled: true },
      { index: 2, description: "Максимум золота (9 999 999 G)", code: "7E2C537F+7E2C5496+7E2C5598", enabled: false },
      { index: 3, description: "Мгновенная шкала ATB в бою", code: "7E2623FF", enabled: false },
      { index: 4, description: "Удвоенный опыт и очки техник TP", code: "C28914EA", enabled: false },
      { index: 5, description: "Прохождение сквозь преграды (L+R)", code: "7E009401", enabled: false }
    ],
    re2_dualshock: [
      { index: 0, description: "Бесконечное здоровье Леона / Клэр", code: "8009C796 00C8", enabled: true },
      { index: 1, description: "Бесконечные патроны без перезарядки", code: "8006953C 2400", enabled: true },
      { index: 2, description: "Все слоты инвентаря открыты (10 ячеек)", code: "3009CE94 000A", enabled: false },
      { index: 3, description: "Ранг S - Секретный сценарий HUNK/Tofu", code: "8009C6E0 0000", enabled: false },
      { index: 4, description: "Бесконечные чернильные ленты", code: "8009CCB6 0063", enabled: false }
    ],
    sonic3_knuckles: [
      { index: 0, description: "Бесконечные жизни (99 Lives)", code: "FFFE12:0063", enabled: true },
      { index: 1, description: "Неуязвимость и защита от шипов", code: "FFB02B:0002", enabled: false },
      { index: 2, description: "Все 7 Изумрудов Хаоса и Супер-Изумрудов", code: "FFFFB0:0707", enabled: true },
      { index: 3, description: "Супер-прыжок (Moon Jump)", code: "FFB01A:FA00", enabled: false }
    ]
  };

  /* ------------------------------------------------------------------------
     5. CENTRAL REACTIVE APPLICATION STATE
     ------------------------------------------------------------------------ */
  const state = {
    activeTab: "dashboard",
    prevTab: "dashboard",
    pendingViewDirection: "",
    settings: {
      theme: "MIDNIGHT",
      accent: "VIOLET",
      font: "Inter",
      language: "ru",
      uiScale: 100,
      videoBackend: "GLES3",
      internalScale: "2x",
      shaderPreset: "crt_royale",
      aspectRatio: "auto",
      highRefresh120: true,
      runAheadFrames: "1",
      rewindEnabled: true,
      masterVolume: 92,
      preAmpDb: 2,
      dspPreset: "balanced",
      eqBands: [65, 58, 62, 70, 68],
      audioLatencyMs: "24",
      dynamicRateControl: true,
      muteOnFocusLoss: true,
      touchLayout: "Console Pro",
      touchColorTheme: "STORM Violet",
      touchOpacity: 85,
      touchScale: 100,
      hapticStrength: "medium",
      gyroStick: false,
      turboHz: "15",
      comboMacro: "L1 + R1 (Спецприём)",
      autoSaveExit: true,
      autoLoadLaunch: true,
      raUsername: "STORM_PLAYER",
      raHardcore: false,
      netplayMode: "host",
      netplayPort: "55435",
      netplayRelay: true,
      netplaySpectator: false,
      autoUpdateCheck: true
    },
    storageGranted: true,
    storagePath: "/storage/emulated/0/STORM RETROARCH",
    updateInfo: {
      currentVersion: APP_VERSION,
      latestVersion: APP_VERSION,
      updateAvailable: false,
      statusMessage: "Установлена актуальная версия 2.6.1"
    },
    games: buildPreviewGames(),
    cores: buildDefaultCores252(),
    bios: buildPreviewBios(),
    bezelPacks: [
      { id: "hsm_mega_bezel", title: "HSM Mega Bezel Reflection", size: "148 MB", installed: true, progress: 0 },
      { id: "duimon_mega", title: "Duimon Console Bezels HD", size: "192 MB", installed: false, progress: 0 },
      { id: "duimon_vintage", title: "Duimon Vintage CRT TVs", size: "110 MB", installed: false, progress: 0 },
      { id: "storm_handheld", title: "STORM Handheld Skins (Portrait/Landscape)", size: "44 MB", installed: true, progress: 0 }
    ],
    saves: [
      { gameId: "chrono_trigger_ru", gameTitle: "Chrono Trigger", platform: "SNES", coreId: "snes9x", slot: 1, type: "State #1 + .SRM", date: "Сегодня, 14:32", size: "268 KB" },
      { gameId: "re2_dualshock", gameTitle: "Resident Evil 2 DualShock", platform: "PS1", coreId: "swanstation", slot: 0, type: "State #0 + MemCard", date: "Вчера, 22:15", size: "1.8 MB" },
      { gameId: "sonic3_knuckles", gameTitle: "Sonic 3 & Knuckles", platform: "Sega Genesis", coreId: "genesis_plus_gx", slot: 2, type: "State #2 + .SRM", date: "10 ноя, 19:04", size: "192 KB" }
    ],
    controllers: [
      { descriptor: "054C:0CE6_DualSense", name: "Sony DualSense Wireless Controller", port: 1, type: "Bluetooth HID" },
      { descriptor: "28DE:11FF_Handheld", name: "Gamesir / Backbone USB-C Pad", port: 2, type: "USB Direct" }
    ],
    controllerMap: {
      A: "Крест / B", B: "Круг / A", X: "Квадрат / Y", Y: "Треугольник / X",
      L1: "L1 Бампер", R1: "R1 Бампер", L2: "L2 Курок", R2: "R2 Курок",
      Start: "Options / Start", Select: "Create / Select"
    },
    listeningBindKey: null,
    // View UI Filters
    libQuery: "",
    libPlatform: "all",
    libStatusFilter: "all",
    libAlphaFilter: "all",
    libCoreFilter: "all",
    libViewMode: "posters",
    libSort: "recent",
    libZoom: 100,
    coresQuery: "",
    coresFilter: "all",
    coresPlatform: "all",
    coresShowAll: false,
    cheatsQuery: "",
    cheatsLibraryOnly: false,
    cheatsSystem: "all",
    selectedCheatGameId: "chrono_trigger_ru",
    cheatResults: PREVIEW_CHEAT_GAMES.slice(),
    activeCheatEntries: PREVIEW_CHEAT_CODES.chrono_trigger_ru.map(function (c) { return Object.assign({}, c); }),
    showCustomCheatForm: false,
    ramBits: "16",
    ramCompare: "exact",
    biosPlatform: "all",
    romPatchGameId: "chrono_trigger_ru",
    romHashGameId: "chrono_trigger_ru",
    romHashResult: { crc32: "EC58574D", md5: "a2bc447961e52fd2227baed164f729dc", sha1: "e870391f81c2e3be9a2f094f8f53f64f8a1e8f92", verified: "No-Intro Verified" },
    m3uTitle: "Resident Evil 2 (Multi-Disc)",
    m3uDiscs: [
      "/storage/emulated/0/STORM RETROARCH/roms/psx/Resident Evil 2 (Disc 1).chd",
      "/storage/emulated/0/STORM RETROARCH/roms/psx/Resident Evil 2 (Disc 2).chd"
    ],
    guidePlatform: "PS1",
    inspectorGameId: null,
    coreModalId: null,
    rouletteSpinTimer: null,
    rouletteGameId: null,
    omniboxQuery: "",
    omniboxSelectedIdx: 0,
    omniboxResults: [],
    coreOptionsDraft: {
      resolution: "2x (720p HD)",
      cpuOverclock: "100% (Штатная)",
      pgxp: true,
      frameSkip: "0 (60 FPS)",
      biosMode: "Оригинальный BIOS / Авто",
      widescreenHack: false,
      fastCdLoad: true
    }
  };

  function tr(key) {
    const lang = state.settings.language || "ru";
    return (I18N[lang] && I18N[lang][key]) || (I18N.ru[key]) || (I18N.en[key]) || key;
  }

  function isRu() {
    return (state.settings.language || "ru") === "ru";
  }

  /* ------------------------------------------------------------------------
     6. BRIDGE HELPERS, HAPTICS & TOASTS
     ------------------------------------------------------------------------ */
  function haptic(kind) {
    try {
      if (hasBridge && typeof window.StormBridge.haptic === "function") {
        window.StormBridge.haptic(kind || "tap");
      }
    } catch (_) {}
  }

  function safeJsonParse(raw, fallback) {
    if (!raw) return fallback;
    if (typeof raw === "object") return raw;
    try {
      return JSON.parse(raw);
    } catch (_) {
      return fallback;
    }
  }

  function showToast(msg) {
    const stack = document.getElementById("toastStack");
    if (!stack) return;
    const el = document.createElement("div");
    el.className = "toast-item";
    el.innerHTML = ico("bolt") + "<span>" + esc(msg) + "</span>";
    stack.appendChild(el);
    setTimeout(function () {
      if (el.parentNode) el.parentNode.removeChild(el);
    }, 3100);
  }

  function saveSettingsPatch(patch) {
    Object.assign(state.settings, patch);
    applyVisualTokens();
    try {
      if (hasBridge && typeof window.StormBridge.saveSettingsJson === "function") {
        window.StormBridge.saveSettingsJson(JSON.stringify(patch));
      }
    } catch (_) {}
  }

  function applyVisualTokens() {
    const root = document.documentElement;
    root.setAttribute("data-storm-theme", state.settings.theme || "MIDNIGHT");
    root.setAttribute("data-storm-accent", state.settings.accent || "VIOLET");
    root.setAttribute("data-storm-font", state.settings.font || "Inter");
    root.setAttribute("lang", state.settings.language || "ru");
    root.style.setProperty("--ui-scale", String((Number(state.settings.uiScale) || 100) / 100));
    root.style.setProperty("--card-zoom", String((Number(state.libZoom) || 100) / 100));
  }

  /* ------------------------------------------------------------------------
     7. BOOTSTRAP SYNC & LIVE NATIVE EVENT HANDLER (window.onStormEvent)
     ------------------------------------------------------------------------ */
  function ingestBootstrap(data) {
    if (!data || typeof data !== "object") return;
    if (data.settings && typeof data.settings === "object") {
      Object.assign(state.settings, data.settings);
    }
    if (typeof data.storageGranted === "boolean") state.storageGranted = data.storageGranted;
    if (data.storagePath) state.storagePath = data.storagePath;

    const rawGames = data.games || data.library || data.roms;
    if (Array.isArray(rawGames)) {
      state.games = rawGames.map(function (g, i) {
        const plat = g.platform || g.platformDisplay || "ROM";
        const pKey = (g.platformKey || plat).toLowerCase();
        const totalSec = Number(g.totalPlaySeconds || 0);
        const playMin = Number(g.playtimeMin || Math.floor(totalSec / 60));
        return {
          id: g.id || g.filePath || ("rom_" + i),
          title: g.title || g.name || "Game",
          subtitle: g.subtitle || g.rawNoIntroName || plat,
          platform: plat,
          platformKey: pKey,
          region: g.region || g.regionCode || (g.isRussianMod ? "RUS" : "WORLD"),
          coreId: g.coreId || g.assignedCoreId || "snes9x",
          favorite: Boolean(g.favorite || g.isFavorite),
          playtimeMin: playMin,
          totalPlaySeconds: totalSec || playMin * 60,
          launchCount: Number(g.launchCount || 0),
          lastPlayedIso: g.lastPlayedIso || "",
          completionStatus: g.completionStatus || "Не начато",
          starRating: Math.max(0, Math.min(5, Number(g.starRating || 0))),
          userNotes: g.userNotes || "",
          customCollection: g.customCollection || "",
          sizeMb: g.sizeMb || (g.sizeBytes ? (g.sizeBytes / 1048576).toFixed(1) + " MB" : ""),
          filePath: g.filePath || "",
          cover: g.cover || g.coverUrl || g.coverPath || makePosterSvg((g.title || g.name || "GAME").slice(0, 18).toUpperCase(), plat, plat.toUpperCase(), g.regionCode || "ROM", "#24123A", "#0D0B18", "#8B5CF6")
        };
      });
    }

    const rawCores = data.cores || data.coreCatalog;
    if (Array.isArray(rawCores) && rawCores.length > 0) {
      state.cores = rawCores.map(function (c) {
        return {
          id: c.id,
          displayName: c.displayName || c.name || c.id,
          platformKey: (c.platformId || c.platformKey || (Array.isArray(c.platformKeys) ? c.platformKeys[0] : "") || "misc").toLowerCase(),
          platform: c.platform || c.platformDisplay || (c.platformId || "CORE").toUpperCase(),
          bundled: Boolean(c.bundled || c.prebundledArm64 || c.isBundled),
          installed: Boolean(c.installed || c.isInstalled || c.bundled || c.prebundledArm64),
          recommended: Boolean(c.recommended),
          soName: c.jniLibName || c.soInsideZip || ("lib" + c.id + "_libretro_android.so"),
          progress: Number(c.progress || 0)
        };
      });
    }

    const rawBios = data.bios || data.biosItems;
    if (Array.isArray(rawBios) && rawBios.length > 0) {
      state.bios = rawBios.map(function (b) {
        const st = String(b.status || b.state || "").toLowerCase();
        return {
          id: b.id || b.fileName,
          platform: b.platform || "System",
          relativePath: b.relativePath || ("system/" + (b.fileName || "")),
          expectedMd5: b.expectedMd5 || "",
          status: st.indexOf("verif") >= 0 ? "verified" : (st.indexOf("present") >= 0 ? "present" : "missing")
        };
      });
    }

    if (Array.isArray(data.saves)) state.saves = data.saves;
    if (Array.isArray(data.controllers)) state.controllers = data.controllers;
    if (data.updateInfo) Object.assign(state.updateInfo, data.updateInfo);
    applyVisualTokens();
  }

  window.onStormEvent = function (eventType, payloadRaw) {
    const p = safeJsonParse(payloadRaw, payloadRaw || {});
    if (eventType === "bootstrapUpdated") {
      ingestBootstrap(p);
      renderShellAndView();
    } else if (eventType === "libraryUpdated") {
      if (Array.isArray(p)) ingestBootstrap({ games: p });
      else if (p && Array.isArray(p.games)) ingestBootstrap(p);
      renderShellAndView();
    } else if (eventType === "scanProgress") {
      showToast((isRu() ? "Сканирование ROM: " : "Scanning ROMs: ") + (p.message || p.count || ""));
    } else if (eventType === "coreProgress") {
      const target = state.cores.find(function (c) { return c.id === p.coreId; });
      if (target) target.progress = Number(p.percent || p.progress || 50);
      if (state.activeTab === "cores") {
        if (document.getElementById("coresResultsMount")) updateCoresResults();
        else renderViewport();
      }
    } else if (eventType === "coreInstalled") {
      const target = state.cores.find(function (c) { return c.id === (p.coreId || p.id); });
      if (target) { target.installed = true; target.progress = 0; }
      showToast((isRu() ? "Ядро готово к запуску: " : "Core installed: ") + (p.coreId || ""));
      renderShellAndView();
    } else if (eventType === "coreError") {
      showToast((isRu() ? "Ошибка загрузки ядра: " : "Core error: ") + (p.message || p.coreId || ""));
    } else if (eventType === "biosUpdated") {
      if (Array.isArray(p)) ingestBootstrap({ bios: p });
      else if (p && Array.isArray(p.bios)) ingestBootstrap(p);
      if (state.activeTab === "bios" || state.activeTab === "dashboard") renderViewport();
    } else if (eventType === "bezelProgress") {
      const pack = state.bezelPacks.find(function (b) { return b.id === p.packId; });
      if (pack) {
        pack.progress = Number(p.percent || 100);
        if (pack.progress >= 100) pack.installed = true;
      }
      if (state.activeTab === "graphics") renderViewport();
    } else if (eventType === "toast") {
      showToast(typeof p === "string" ? p : (p.message || "OK"));
    } else if (eventType === "updateStatus") {
      Object.assign(state.updateInfo, p || {});
      if (state.updateInfo.latestVersion === APP_VERSION) {
        state.updateInfo.updateAvailable = false;
      }
      if (state.activeTab === "settings") renderViewport();
    } else if (eventType === "storagePermission") {
      state.storageGranted = Boolean(p && (p.granted || p === true));
      renderViewport();
    }
  };

  /* ------------------------------------------------------------------------
     8. NAVIGATION CHROME RENDERERS (RAIL + QUICK STRIP + BOTTOM DOCK + GLIDERS)
     ------------------------------------------------------------------------ */
  let navChromeLang = null;
  let activeChunkObservers = [];
  const debounceTimers = {};

  function scheduleDebounced(key, fn, delayMs) {
    if (debounceTimers[key]) clearTimeout(debounceTimers[key]);
    debounceTimers[key] = setTimeout(function () {
      debounceTimers[key] = null;
      fn();
    }, delayMs == null ? 25 : delayMs);
  }

  function disconnectChunkObservers() {
    while (activeChunkObservers.length > 0) {
      try {
        activeChunkObservers.pop().disconnect();
      } catch (_) {}
    }
  }

  function mountChunkedGrid(containerEl, items, renderItemHtmlFn, emptyHtml) {
    if (!containerEl) return;
    if (!items || items.length === 0) {
      containerEl.innerHTML = emptyHtml || "";
      return;
    }
    const BATCH = 48;
    let offset = Math.min(BATCH, items.length);
    const firstHtml = items.slice(0, offset).map(renderItemHtmlFn).join("");
    const hasMore = offset < items.length;
    containerEl.innerHTML = firstHtml + (hasMore ? '<div id="chunkSentinel" class="chunk-sentinel"></div>' : "");

    if (!hasMore || typeof IntersectionObserver !== "function") {
      if (hasMore) {
        containerEl.innerHTML = items.map(renderItemHtmlFn).join("");
      }
      return;
    }

    const sentinel = containerEl.querySelector("#chunkSentinel");
    if (!sentinel) return;

    const obs = new IntersectionObserver(function (entries) {
      for (let i = 0; i < entries.length; i++) {
        if (!entries[i].isIntersecting) continue;
        const nextEnd = Math.min(offset + BATCH, items.length);
        const batchHtml = items.slice(offset, nextEnd).map(renderItemHtmlFn).join("");
        offset = nextEnd;
        sentinel.insertAdjacentHTML("beforebegin", batchHtml);
        if (offset >= items.length) {
          obs.disconnect();
          if (sentinel.parentNode) sentinel.parentNode.removeChild(sentinel);
        }
        break;
      }
    }, { rootMargin: "360px 0px" });

    obs.observe(sentinel);
    activeChunkObservers.push(obs);
  }

  function countInstalledCores() {
    return state.cores.filter(function (c) { return c.installed || c.bundled; }).length;
  }

  function positionNavGliders() {
    requestAnimationFrame(function () {
      const dock = document.getElementById("bottomGlassDock");
      const dockGlider = document.getElementById("dockGlider");
      if (dock && dockGlider) {
        const activeDock = dock.querySelector(".dock-tab-btn.active");
        if (activeDock && activeDock.offsetWidth > 0) {
          dockGlider.style.width = activeDock.offsetWidth + "px";
          dockGlider.style.transform = "translate3d(" + (activeDock.offsetLeft - 6) + "px, 0, 0)";
          dockGlider.style.opacity = "1";
        } else {
          dockGlider.style.opacity = "0";
        }
      }

      const railList = document.getElementById("railNavList");
      const railGlider = document.getElementById("railGlider");
      if (railList && railGlider) {
        const activeRail = railList.querySelector(".nav-pill-3d.active");
        if (activeRail && activeRail.offsetHeight > 0) {
          railGlider.style.height = activeRail.offsetHeight + "px";
          railGlider.style.transform = "translate3d(0, " + activeRail.offsetTop + "px, 0)";
          railGlider.style.opacity = "1";
        } else {
          railGlider.style.opacity = "0";
        }
      }
    });
  }

  function renderNavigationChrome() {
    const readyCount = countInstalledCores();
    const headerReady = document.getElementById("headerReadyLabel");
    const railReady = document.getElementById("railReadyLabel");
    if (headerReady) {
      headerReady.textContent = readyCount + " " + (isRu() ? "ядер готово" : tr("ready_cores_short"));
    }
    if (railReady) {
      railReady.textContent = readyCount + " " + (isRu() ? "готово" : "Ready");
    }

    const curLang = state.settings.language || "ru";
    const needRebuild = navChromeLang !== curLang;
    navChromeLang = curLang;

    // 1. Landscape Console Rail (All 12 Modules) with sliding GPU glider
    const railList = document.getElementById("railNavList");
    if (railList) {
      if (needRebuild || !railList.querySelector("#railGlider")) {
        railList.classList.add("has-glider");
        railList.innerHTML =
          '<div class="rail-glider" id="railGlider" aria-hidden="true"></div>' +
          STUDIO_MODULES.map(function (m) {
            const active = state.activeTab === m.id ? " active" : "";
            return (
              '<button type="button" class="nav-pill-3d rail-mode' + active + '" data-nav="' + m.id + '">' +
              ico(m.icon) +
              '<span>' + esc(tr(m.key)) + '</span>' +
              '</button>'
            );
          }).join("");
      } else {
        Array.prototype.forEach.call(railList.querySelectorAll("[data-nav]"), function (btn) {
          btn.classList.toggle("active", btn.getAttribute("data-nav") === state.activeTab);
        });
      }
    }

    // 2. Portrait Quick-Module Horizontal Strip (All 12 Modules 1 Tap Away!)
    const strip = document.getElementById("quickModuleStrip");
    if (strip) {
      if (needRebuild || !strip.children.length) {
        strip.innerHTML = STUDIO_MODULES.map(function (m) {
          const active = state.activeTab === m.id ? " active" : "";
          return (
            '<button type="button" role="tab" aria-selected="' + (state.activeTab === m.id) + '" class="nav-pill-3d' + active + '" data-nav="' + m.id + '">' +
            ico(m.icon) +
            '<span>' + esc(tr(m.key)) + '</span>' +
            '</button>'
          );
        }).join("");
      } else {
        Array.prototype.forEach.call(strip.querySelectorAll("[data-nav]"), function (btn) {
          const isAct = btn.getAttribute("data-nav") === state.activeTab;
          btn.classList.toggle("active", isAct);
          btn.setAttribute("aria-selected", String(isAct));
        });
      }
    }

    // 3. Portrait Floating Sculpted Glass 5-Tab Bottom Dock with sliding GPU glider
    const dock = document.getElementById("bottomGlassDock");
    if (dock) {
      const dockItems = [
        { id: "dashboard", label: tr("nav_dashboard"), icon: "home", action: "nav" },
        { id: "library", label: tr("nav_library"), icon: "gamepad", action: "nav" },
        { id: "cores", label: tr("nav_cores"), icon: "cpu", action: "nav" },
        { id: "cheats", label: tr("nav_cheats"), icon: "shield", action: "nav" },
        { id: "studio_hub", label: tr("nav_studio"), icon: "grid", action: "hub" }
      ];
      const inExtraStudio = ["dashboard", "library", "cores", "cheats"].indexOf(state.activeTab) === -1;
      if (needRebuild || !dock.querySelector("#dockGlider")) {
        dock.classList.add("has-glider");
        dock.innerHTML =
          '<div class="dock-glider" id="dockGlider" aria-hidden="true"></div>' +
          dockItems.map(function (d) {
            const isAct = d.action === "hub" ? inExtraStudio : state.activeTab === d.id;
            return (
              '<button type="button" class="dock-tab-btn' + (isAct ? " active" : "") + '" ' +
              (d.action === "hub" ? 'data-action="open-studio-hub"' : 'data-nav="' + d.id + '"') + '>' +
              ico(d.icon) +
              '<span>' + esc(d.label) + '</span>' +
              '</button>'
            );
          }).join("");
      } else {
        Array.prototype.forEach.call(dock.querySelectorAll(".dock-tab-btn"), function (btn) {
          const navId = btn.getAttribute("data-nav");
          const isAct = navId ? (state.activeTab === navId) : inExtraStudio;
          btn.classList.toggle("active", isAct);
        });
      }
    }

    positionNavGliders();
  }

  window.addEventListener("resize", positionNavGliders);

  function formatPlaytime(totalSec) {
    const mins = Math.max(0, Math.floor((Number(totalSec) || 0) / 60));
    const h = Math.floor(mins / 60);
    const m = mins % 60;
    if (isRu()) return h > 0 ? (h + " ч " + m + " м") : (m + " м");
    return h > 0 ? (h + "h " + m + "m") : (m + "m");
  }

  function statusMetaFor(statusStr) {
    for (let i = 0; i < COMPLETION_STATUSES.length; i++) {
      if (COMPLETION_STATUSES[i].id === statusStr) return COMPLETION_STATUSES[i];
    }
    return COMPLETION_STATUSES[0];
  }

  function statusLabel(statusObj) {
    return isRu() ? statusObj.id : statusObj.en;
  }

  function persistGameMetadata(gameId, patch) {
    const gm = state.games.find(function (x) { return x.id === gameId; });
    if (gm) {
      if (typeof patch.completionStatus === "string") gm.completionStatus = patch.completionStatus;
      if (typeof patch.starRating === "number") gm.starRating = Math.max(0, Math.min(5, patch.starRating));
      if (typeof patch.userNotes === "string") gm.userNotes = patch.userNotes;
      if (typeof patch.customCollection === "string") gm.customCollection = patch.customCollection;
    }
    try {
      if (hasBridge && typeof window.StormBridge.saveGameMetadataJson === "function") {
        const resRaw = window.StormBridge.saveGameMetadataJson(gameId, JSON.stringify(patch));
        const res = safeJsonParse(resRaw, null);
        if (res && res.ok && gm) {
          gm.completionStatus = res.completionStatus || gm.completionStatus;
          if (typeof res.starRating === "number") gm.starRating = res.starRating;
          if (typeof res.userNotes === "string") gm.userNotes = res.userNotes;
          if (typeof res.customCollection === "string") gm.customCollection = res.customCollection;
        }
      }
    } catch (_) {}
  }

  /* ------------------------------------------------------------------------
     9. MODULE VIEW RENDERERS (1..6)
     ------------------------------------------------------------------------ */
  function coresForPlatform(platformKey, platformLabel) {
    if (!platformKey || platformKey === "all" || platformKey === "fav") return state.cores;
    const pk = String(platformKey).toLowerCase();
    const pl = String(platformLabel || "").toLowerCase();
    const matched = state.cores.filter(function (c) {
      return c.platformKey === pk || String(c.platform).toLowerCase() === pl;
    });
    return matched.length > 0 ? matched : state.cores.slice(0, 5);
  }

  function renderDashboard() {
    const hero = state.games[0];
    const readyCores = countInstalledCores();
    const verifiedBios = state.bios.filter(function (b) { return b.status !== "missing"; }).length;

    // Player Statistics & Backlog Funnel Calculations
    const totalPlaySec = state.games.reduce(function (acc, g) {
      return acc + (Number(g.totalPlaySeconds) || (Number(g.playtimeMin) || 0) * 60);
    }, 0);
    const ratedGames = state.games.filter(function (g) { return Number(g.starRating) > 0; });
    const avgRating = ratedGames.length > 0
      ? (ratedGames.reduce(function (acc, g) { return acc + Number(g.starRating); }, 0) / ratedGames.length).toFixed(1)
      : "0.0";

    const statusCounts = {};
    COMPLETION_STATUSES.forEach(function (st) { statusCounts[st.id] = 0; });
    const platTotals = {};
    state.games.forEach(function (g) {
      const st = statusCounts.hasOwnProperty(g.completionStatus) ? g.completionStatus : "Не начато";
      statusCounts[st]++;
      platTotals[g.platform] = (platTotals[g.platform] || 0) + 1;
    });
    const topPlatforms = Object.keys(platTotals).sort(function (a, b) {
      return platTotals[b] - platTotals[a];
    }).slice(0, 3);
    const totalGamesCount = Math.max(1, state.games.length);

    const funnelSegmentsHtml = COMPLETION_STATUSES.map(function (st) {
      const cnt = statusCounts[st.id] || 0;
      if (cnt <= 0) return "";
      const pct = ((cnt / totalGamesCount) * 100).toFixed(2);
      return '<div class="funnel-segment" style="width:' + pct + '%;background:' + st.color + ';" title="' + esc(statusLabel(st) + ": " + cnt) + '"></div>';
    }).join("");

    const funnelLegendHtml = COMPLETION_STATUSES.map(function (st) {
      const cnt = statusCounts[st.id] || 0;
      return '<span class="funnel-legend-item"><span class="funnel-dot" style="background:' + st.color + ';"></span><span>' + esc(statusLabel(st)) + ': <strong class="tabular" style="color:#F8FAFC;">' + cnt + '</strong></span></span>';
    }).join("");

    const heroBlock = hero ? (
      '<div class="hero-spotlight-card stagger-card" style="--stagger-i:0;">' +
        '<div class="hero-main-flex">' +
          '<div class="hero-poster-frame">' +
            '<img src="' + esc(hero.cover) + '" alt="' + esc(hero.title) + '">' +
          '</div>' +
          '<div class="hero-info-col">' +
            '<span class="hero-eyebrow">' + esc(tr("hero_continue")) + '</span>' +
            '<h2 class="hero-game-title">' + esc(hero.title) + '</h2>' +
            '<div class="hero-badges-row">' +
              '<span class="badge-pill badge-accent">' + esc(hero.platform) + '</span>' +
              '<span class="badge-pill badge-emerald">' + esc(hero.region) + '</span>' +
              '<span class="badge-pill tabular">' + esc(hero.coreId) + ' • ' + formatPlaytime(hero.totalPlaySeconds || hero.playtimeMin * 60) + '</span>' +
            '</div>' +
          '</div>' +
        '</div>' +
        '<div class="action-trio-grid" style="margin-bottom:0;">' +
          '<button type="button" class="btn-3d btn-3d-primary btn-trio-hero" data-action="launch-game" data-game="' + esc(hero.id) + '" data-slot="-1">' +
            ico("play") + '<span>' + esc(tr("play")) + '</span>' +
          '</button>' +
          '<button type="button" class="btn-3d btn-3d-emerald" data-action="open-roulette">' +
            ico("shuffle") + '<span>' + (isRu() ? "Случайная игра" : "Random Game") + '</span>' +
          '</button>' +
          '<button type="button" class="btn-3d btn-3d-secondary" data-action="inspect-game" data-game="' + esc(hero.id) + '">' +
            ico("sliders") + '<span>' + esc(tr("game_options")) + '</span>' +
          '</button>' +
        '</div>' +
      '</div>'
    ) : (
      '<div class="hero-spotlight-card stagger-card" style="--stagger-i:0;">' +
        '<span class="hero-eyebrow">STORM RETROARCH 2.6.1</span>' +
        '<h2 class="hero-game-title">' + (isRu() ? "Добро пожаловать в консоль-студию" : "Welcome to STORM Console Studio") + '</h2>' +
        '<p class="section-subtitle">' + (isRu() ? "20 флагманских ядер уже вшиты в APK и готовы к мгновенному офлайн-запуску. Укажите папку с играми или откройте файл ROM:" : "20 flagship cores are pre-bundled inside APK for instant offline play. Pick your ROM folder or open a file:") + '</p>' +
        '<div class="hero-actions-row">' +
          '<button type="button" class="btn-3d btn-3d-primary" data-action="pick-rom-folder">' + ico("folder") + '<span>' + (isRu() ? "Выбрать папку с играми" : "Select ROM Folder") + '</span></button>' +
          '<button type="button" class="btn-3d btn-3d-emerald" data-action="pick-single-rom">' + ico("file") + '<span>' + (isRu() ? "Открыть файл ROM" : "Open ROM File") + '</span></button>' +
          '<button type="button" class="btn-3d btn-3d-secondary" data-action="quick-scan-storage">' + ico("refresh") + '<span>' + esc(tr("quick_scan")) + '</span></button>' +
        '</div>' +
      '</div>'
    );

    const recentCards = state.games.slice(0, 18).map(function (g) {
      return (
        '<div class="recent-poster-card" data-action="inspect-game" data-game="' + esc(g.id) + '" tabindex="0">' +
          '<div class="recent-poster-art"><img src="' + esc(g.cover) + '" alt="' + esc(g.title) + '"></div>' +
          '<div class="recent-poster-title">' + esc(g.title) + '</div>' +
          '<span class="badge-pill badge-accent">' + esc(g.platform) + '</span>' +
        '</div>'
      );
    }).join("");

    return (
      '<div class="dashboard-top-bento">' +
        heroBlock +
        '<div class="telemetry-grid">' +
          '<button type="button" class="telemetry-card stagger-card" style="--stagger-i:1;" data-nav="library">' +
            '<div class="telemetry-top">' + ico("gamepad") + '<span class="badge-pill badge-accent">ROM</span></div>' +
            '<div class="telemetry-val">' + state.games.length + '</div>' +
            '<div class="telemetry-label">' + esc(tr("stat_games")) + '</div>' +
            '<div class="telemetry-sub">' + (isRu() ? "Постеры, фильтры и слоты" : "Boxart, filters & slots") + '</div>' +
          '</button>' +
          '<button type="button" class="telemetry-card stagger-card" style="--stagger-i:2;" data-nav="cores">' +
            '<div class="telemetry-top">' + ico("cpu") + '<span class="badge-pill badge-emerald">APK + CDN</span></div>' +
            '<div class="telemetry-val">' + readyCores + ' / ' + state.cores.length + '</div>' +
            '<div class="telemetry-label">' + esc(tr("stat_cores")) + '</div>' +
            '<div class="telemetry-sub">' + (isRu() ? "20 вшито • " + state.cores.length + " доступно" : "20 bundled • " + state.cores.length + " online") + '</div>' +
          '</button>' +
          '<button type="button" class="telemetry-card stagger-card" style="--stagger-i:3;" data-nav="cheats">' +
            '<div class="telemetry-top">' + ico("shield") + '<span class="badge-pill badge-amber">76 SYS</span></div>' +
            '<div class="telemetry-val">26 797</div>' +
            '<div class="telemetry-label">' + esc(tr("stat_cheats")) + '</div>' +
            '<div class="telemetry-sub">' + (isRu() ? "База кодов + ОЗУ сканер" : "Libretro DB + RAM scanner") + '</div>' +
          '</button>' +
          '<button type="button" class="telemetry-card stagger-card" style="--stagger-i:4;" data-nav="bios">' +
            '<div class="telemetry-top">' + ico("chip") + '<span class="badge-pill badge-emerald">120 Hz</span></div>' +
            '<div class="telemetry-val">' + verifiedBios + ' / ' + state.bios.length + '</div>' +
            '<div class="telemetry-label">' + esc(tr("stat_bios")) + '</div>' +
            '<div class="telemetry-sub">' + esc(state.settings.videoBackend) + ' FBO • MD5 OK</div>' +
          '</button>' +
        '</div>' +
      '</div>' +

      '<div class="glass-card stats-funnel-card stagger-card" style="--stagger-i:5;">' +
        '<div class="section-head">' +
          '<div class="section-title-cluster">' +
            '<div class="section-icon-badge">' + ico("chart") + '</div>' +
            '<div>' +
              '<h3 class="section-title">' + (isRu() ? "Статистика игрока и воронка бэклога" : "Player Statistics & Backlog Funnel") + '</h3>' +
              '<p class="section-subtitle">' + (isRu() ? "Топ платформ: " : "Top platforms: ") + esc(topPlatforms.join(", ") || "-") + '</p>' +
            '</div>' +
          '</div>' +
          '<button type="button" class="btn-3d btn-3d-emerald btn-3d-sm" data-action="open-roulette">' +
            ico("shuffle") + '<span>' + (isRu() ? "Рулетка игр" : "Game Roulette") + '</span>' +
          '</button>' +
        '</div>' +
        '<div class="stats-mini-grid">' +
          '<div class="stats-mini-box"><div class="stats-mini-val">' + esc(formatPlaytime(totalPlaySec)) + '</div><div class="stats-mini-lbl">' + (isRu() ? "Общее время в играх" : "Total Playtime") + '</div></div>' +
          '<div class="stats-mini-box"><div class="stats-mini-val">' + (statusCounts["Пройдено"] + statusCounts["Пройдено на 100%"]) + ' / ' + state.games.length + '</div><div class="stats-mini-lbl">' + (isRu() ? "Пройдено игр" : "Completed Games") + '</div></div>' +
          '<div class="stats-mini-box"><div class="stats-mini-val">' + statusCounts["Играю сейчас"] + '</div><div class="stats-mini-lbl">' + (isRu() ? "Играю сейчас" : "Playing Now") + '</div></div>' +
          '<div class="stats-mini-box"><div class="stats-mini-val">' + avgRating + ' / 5.0</div><div class="stats-mini-lbl">' + (isRu() ? "Средний рейтинг (" + ratedGames.length + ")" : "Avg Rating (" + ratedGames.length + ")") + '</div></div>' +
        '</div>' +
        '<div class="funnel-bar-track">' + funnelSegmentsHtml + '</div>' +
        '<div class="funnel-legend-grid">' + funnelLegendHtml + '</div>' +
      '</div>' +

      '<div class="glass-card stagger-card" style="margin-bottom:12px;--stagger-i:6;">' +
        '<div class="section-head">' +
          '<div class="section-title-cluster">' +
            '<div class="section-icon-badge">' + ico("folder") + '</div>' +
            '<div>' +
              '<h3 class="section-title">' + (isRu() ? "Быстрое пополнение библиотеки" : "1-Tap Library Onboarding") + '</h3>' +
              '<p class="section-subtitle">' + esc(state.storagePath) + '</p>' +
            '</div>' +
          '</div>' +
          '<div class="toolbar-row">' +
            '<button type="button" class="btn-3d btn-3d-primary btn-3d-sm" data-action="pick-rom-folder">' + ico("folder") + '<span>' + (isRu() ? "Выбрать папку с играми" : "Pick ROM Folder") + '</span></button>' +
            '<button type="button" class="btn-3d btn-3d-secondary btn-3d-sm" data-action="pick-single-rom">' + ico("file") + '<span>' + esc(tr("open_rom")) + '</span></button>' +
            '<button type="button" class="btn-3d btn-3d-secondary btn-3d-sm" data-action="quick-scan-storage">' + ico("refresh") + '<span>' + esc(tr("quick_scan")) + '</span></button>' +
          '</div>' +
        '</div>' +
        (state.games.length > 0 ? (
          '<div class="section-title" style="font-size:0.88rem;margin-bottom:8px;">' + esc(tr("recent_launches")) + '</div>' +
          '<div class="recent-carousel">' + recentCards + '</div>'
        ) : "") +
      '</div>' +

      '<div class="glass-card stagger-card" style="--stagger-i:7;">' +
        '<div class="section-head">' +
          '<div class="section-title-cluster">' +
            '<div class="section-icon-badge">' + ico("bolt") + '</div>' +
            '<div>' +
              '<h3 class="section-title">' + esc(tr("quick_studio")) + '</h3>' +
              '<p class="section-subtitle">' + (isRu() ? "Прямой доступ ко всем профессиональным подсистемам STORM" : "Instant 1-tap access to professional STORM subsystems") + '</p>' +
            '</div>' +
          '</div>' +
        '</div>' +
        '<div class="quick-bento-grid">' +
          '<button type="button" class="bento-tool-tile stagger-card" style="--stagger-i:0;" data-nav="cores"><div class="bento-tile-head">' + ico("download") + '<span>' + (isRu() ? "Пакетная установка ядер" : "Batch Core Packs") + '</span></div><div class="bento-tile-desc">' + (isRu() ? "Базовый 15, 3D Консоли и Аркады в 1 касание" : "Install Essential 15, 3D Consoles & Arcade packs") + '</div></button>' +
          '<button type="button" class="bento-tool-tile stagger-card" style="--stagger-i:1;" data-nav="bios"><div class="bento-tile-head">' + ico("chip") + '<span>' + (isRu() ? "Автосканер BIOS и MD5" : "Auto BIOS MD5 Scanner") + '</span></div><div class="bento-tile-desc">' + (isRu() ? "Проверка прошивок PS1, PS2, DC, Saturn, NDS" : "Verify PS1, PS2, DC, Saturn & NDS firmware") + '</div></button>' +
          '<button type="button" class="bento-tool-tile stagger-card" style="--stagger-i:2;" data-nav="saves"><div class="bento-tile-head">' + ico("save") + '<span>' + (isRu() ? "Синхронизация сейвов с ПК" : "PC Save ZIP Sync") + '</span></div><div class="bento-tile-desc">' + (isRu() ? "Экспорт и импорт всех .srm и стейтов в .ZIP" : "1-tap .ZIP backup & restore of .srm and states") + '</div></button>' +
          '<button type="button" class="bento-tool-tile stagger-card" style="--stagger-i:3;" data-nav="cheats"><div class="bento-tile-head">' + ico("shield") + '<span>' + (isRu() ? "Конструктор читов и ОЗУ" : "Cheat Builder & RAM Scan") + '</span></div><div class="bento-tile-desc">' + (isRu() ? "GameGenie/ActionReplay + живой поиск 8/16/32 бит" : "Custom codes + live 8/16/32-bit RAM finder") + '</div></button>' +
          '<button type="button" class="bento-tool-tile stagger-card" style="--stagger-i:4;" data-nav="controls"><div class="bento-tile-head">' + ico("dpad") + '<span>' + (isRu() ? "3D Сенсорный геймпад" : "3D Touch Gamepad Studio") + '</span></div><div class="bento-tile-desc">' + (isRu() ? "6 раскладок, гироскоп, турбо-огонь и макросы" : "6 layouts, gyro stick, turbo fire & macros") + '</div></button>' +
          '<button type="button" class="bento-tool-tile stagger-card" style="--stagger-i:5;" data-nav="graphics"><div class="bento-tile-head">' + ico("display") + '<span>' + (isRu() ? "Рамки и ЭЛТ шейдеры" : "Bezels & CRT Shaders") + '</span></div><div class="bento-tile-desc">' + (isRu() ? "CRT Royale, Trinitron, 1x-8x HD и Mega Bezel" : "CRT Royale, Trinitron, 1x-8x upscale & Bezels") + '</div></button>' +
        '</div>' +
      '</div>'
    );
  }

  function getFilteredSortedGames() {
    const selectedPlatGames = state.games.filter(function (g) {
      if (state.libPlatform === "fav") return g.favorite;
      if (state.libPlatform !== "all") return g.platform === state.libPlatform;
      return true;
    });

    const q = state.libQuery.trim().toLowerCase();
    const alpha = state.libAlphaFilter || "all";
    return selectedPlatGames.filter(function (g) {
      if (state.libCoreFilter !== "all" && g.coreId !== state.libCoreFilter) return false;
      if (state.libStatusFilter !== "all" && g.completionStatus !== state.libStatusFilter) return false;
      if (alpha !== "all") {
        const ch = (g.title || "").trim().charAt(0).toUpperCase();
        if (alpha === "#") {
          if (/[A-ZА-ЯЁ]/.test(ch)) return false;
        } else if (ch !== alpha) {
          return false;
        }
      }
      if (!q) return true;
      return (g.title + " " + g.subtitle + " " + g.platform + " " + g.region + " " + (g.customCollection || "")).toLowerCase().indexOf(q) >= 0;
    }).slice().sort(function (a, b) {
      if (state.libSort === "az") return a.title.localeCompare(b.title);
      if (state.libSort === "platform") return a.platform.localeCompare(b.platform);
      if (state.libSort === "playtime") return (b.totalPlaySeconds || b.playtimeMin * 60) - (a.totalPlaySeconds || a.playtimeMin * 60);
      if (state.libSort === "rating") return (b.starRating || 0) - (a.starRating || 0);
      return 0;
    });
  }

  function renderGameCardHtml(g) {
    const coreObj = state.cores.find(function (c) { return c.id === g.coreId; });
    const coreReady = coreObj ? (coreObj.installed || coreObj.bundled) : true;
    const cardModeCls = state.libViewMode === "tiles" ? "tile-card" : "poster-card";
    const stMeta = statusMetaFor(g.completionStatus);
    const showStatus = g.completionStatus && g.completionStatus !== "Не начато";
    const statusShort = g.completionStatus === "Пройдено на 100%" ? "100%" : statusLabel(stMeta);
    const ratingVal = Number(g.starRating || 0);

    return (
      '<article class="game-card ' + cardModeCls + '">' +
        '<div class="game-art-box" data-action="inspect-game" data-game="' + esc(g.id) + '">' +
          '<img src="' + esc(g.cover) + '" alt="' + esc(g.title) + '" loading="lazy">' +
          '<div class="game-art-top-badges">' +
            '<span class="badge-pill ' + (g.region === "RUS" ? "badge-emerald" : "badge-accent") + '">' + esc(g.region) + '</span>' +
            '<div class="fav-corner-wrap">' +
              '<button type="button" class="icon-btn-3d' + (g.favorite ? " active-fav" : "") + '" data-action="toggle-fav" data-game="' + esc(g.id) + '" aria-label="Favorite">' +
                ico("star") +
              '</button>' +
            '</div>' +
          '</div>' +
        '</div>' +
        '<div class="game-card-body">' +
          '<h4 class="game-card-title" data-action="inspect-game" data-game="' + esc(g.id) + '">' + esc(g.title) + '</h4>' +
          '<div class="game-card-meta">' +
            '<span class="ready-dot" style="background:' + (coreReady ? "#34D399" : "#F59E0B") + '"></span>' +
            '<span>' + esc(g.platform) + ' • ' + esc(g.coreId) + '</span>' +
          '</div>' +
          ((showStatus || ratingVal > 0) ? (
            '<div class="game-status-row">' +
              (showStatus ? '<span class="game-status-badge ' + stMeta.cls + '">' + esc(statusShort) + '</span>' : "") +
              (ratingVal > 0 ? '<span class="game-status-badge">' + ico("star") + '<span>' + ratingVal + '</span></span>' : "") +
            '</div>'
          ) : "") +
          '<div class="toolbar-row" style="margin-top:auto;">' +
            '<button type="button" class="btn-3d btn-3d-primary btn-3d-sm" style="flex:1;" data-action="launch-game" data-game="' + esc(g.id) + '" data-slot="-1">' +
              ico("play") + '<span>' + esc(tr("play")) + '</span>' +
            '</button>' +
            '<button type="button" class="icon-btn-3d" data-action="inspect-game" data-game="' + esc(g.id) + '" aria-label="Inspector">' +
              ico("sliders") +
            '</button>' +
          '</div>' +
        '</div>' +
      '</article>'
    );
  }

  function updateLibraryResults() {
    const mount = document.getElementById("libraryResultsMount");
    if (!mount) return;
    mount.className = "library-grid view-" + esc(state.libViewMode);
    const filtered = getFilteredSortedGames();
    const emptyHtml = '<p class="section-subtitle">' + (isRu() ? "Игры по текущему фильтру не найдены." : "No games match the active filter.") + '</p>';
    mountChunkedGrid(mount, filtered, renderGameCardHtml, emptyHtml);
  }

  function renderLibrary() {
    // Dynamic Platform Counts (ONLY platforms present in library!)
    const platMap = {};
    let favCount = 0;
    const statusCounts = { all: state.games.length };
    COMPLETION_STATUSES.forEach(function (st) { statusCounts[st.id] = 0; });
    state.games.forEach(function (g) {
      if (g.favorite) favCount++;
      platMap[g.platform] = (platMap[g.platform] || 0) + 1;
      const stKey = statusCounts.hasOwnProperty(g.completionStatus) ? g.completionStatus : "Не начато";
      statusCounts[stKey]++;
    });

    const selectedPlatGames = state.games.filter(function (g) {
      if (state.libPlatform === "fav") return g.favorite;
      if (state.libPlatform !== "all") return g.platform === state.libPlatform;
      return true;
    });

    const sampleKey = selectedPlatGames[0] ? selectedPlatGames[0].platformKey : "all";
    const allowedCores = (state.libPlatform !== "all" && state.libPlatform !== "fav")
      ? coresForPlatform(sampleKey, state.libPlatform)
      : state.cores.filter(function (c) { return c.installed || c.bundled; });

    const chipsHtml =
      '<button type="button" class="chip-3d' + (state.libPlatform === "all" ? " active" : "") + '" data-lib-plat="all"><span>' + esc(tr("all")) + '</span><span class="chip-count">' + state.games.length + '</span></button>' +
      '<button type="button" class="chip-3d' + (state.libPlatform === "fav" ? " active" : "") + '" data-lib-plat="fav">' + ico("star") + '<span>' + esc(tr("favorites")) + '</span><span class="chip-count">' + favCount + '</span></button>' +
      Object.keys(platMap).map(function (plat) {
        const act = state.libPlatform === plat ? " active" : "";
        return '<button type="button" class="chip-3d' + act + '" data-lib-plat="' + esc(plat) + '"><span>' + esc(plat) + '</span><span class="chip-count">' + platMap[plat] + '</span></button>';
      }).join("");

    const statusChipsHtml =
      '<button type="button" class="chip-3d' + (state.libStatusFilter === "all" ? " active" : "") + '" data-lib-status="all"><span>' + (isRu() ? "Статус: Все" : "Status: All") + '</span><span class="chip-count">' + state.games.length + '</span></button>' +
      ["Играю сейчас", "Пройдено", "Пройдено на 100%", "В планах", "Отложено", "Не начато"].map(function (stId) {
        const stMeta = statusMetaFor(stId);
        const act = state.libStatusFilter === stId ? " active" : "";
        return '<button type="button" class="chip-3d' + act + '" data-lib-status="' + esc(stId) + '"><span>' + esc(statusLabel(stMeta)) + '</span><span class="chip-count">' + (statusCounts[stId] || 0) + '</span></button>';
      }).join("");

    const alphaChars = ["all", "#", "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z", "А", "Б", "В", "Г", "Д", "Е", "Ж", "З", "И", "К", "Л", "М", "Н", "О", "П", "Р", "С", "Т", "У", "Ф", "Х", "Ц", "Ч", "Ш", "Э", "Ю", "Я"];
    const alphaHtml = alphaChars.map(function (ch) {
      const act = state.libAlphaFilter === ch ? " active" : "";
      const lbl = ch === "all" ? (isRu() ? "Все" : "All") : ch;
      return '<button type="button" class="alpha-btn' + act + '" data-lib-alpha="' + esc(ch) + '">' + esc(lbl) + '</button>';
    }).join("");

    return (
      '<div class="glass-card">' +
        '<div class="toolbar-stack">' +
          '<div class="toolbar-row">' +
            '<div class="input-well-wrap">' +
              '<span class="input-lead-ico">' + ico("search") + '</span>' +
              '<input type="search" id="libSearchInput" class="input-well-3d" value="' + esc(state.libQuery) + '" placeholder="' + esc(tr("search_games")) + '">' +
            '</div>' +
            '<button type="button" class="btn-3d btn-3d-emerald btn-3d-sm" data-action="open-roulette">' + ico("shuffle") + '<span>' + (isRu() ? "Случайная игра" : "Random") + '</span></button>' +
            '<button type="button" class="btn-3d btn-3d-primary btn-3d-sm" data-action="pick-rom-folder">' + ico("folder") + '<span>' + esc(tr("add_folder")) + '</span></button>' +
            '<button type="button" class="btn-3d btn-3d-secondary btn-3d-sm" data-action="pick-single-rom">' + ico("file") + '<span>' + esc(tr("open_rom")) + '</span></button>' +
            '<button type="button" class="btn-3d btn-3d-secondary btn-3d-sm" data-action="refresh-covers">' + ico("refresh") + '<span>' + esc(tr("covers")) + '</span></button>' +
          '</div>' +

          '<div class="chips-scroll-row" id="libPlatformChips">' + chipsHtml + '</div>' +
          '<div class="chips-scroll-row" id="libStatusChips">' + statusChipsHtml + '</div>' +
          '<div class="alpha-strip" id="libAlphaStrip" aria-label="Alphabet Quick Jump">' + alphaHtml + '</div>' +

          '<div class="toolbar-row" style="justify-content:space-between;">' +
            '<div class="seg-group-3d">' +
              '<button type="button" class="seg-btn-3d' + (state.libViewMode === "posters" ? " active" : "") + '" data-lib-view="posters">' + ico("grid") + '<span>' + esc(tr("posters")) + '</span></button>' +
              '<button type="button" class="seg-btn-3d' + (state.libViewMode === "tiles" ? " active" : "") + '" data-lib-view="tiles">' + ico("gamepad") + '<span>' + esc(tr("tiles")) + '</span></button>' +
              '<button type="button" class="seg-btn-3d' + (state.libViewMode === "list" ? " active" : "") + '" data-lib-view="list">' + ico("list") + '<span>' + esc(tr("list")) + '</span></button>' +
            '</div>' +

            '<div class="toolbar-row" style="flex:1 1 280px;justify-content:flex-end;">' +
              '<select id="libCoreSelect" class="select-3d" style="flex:1 1 230px;">' +
                '<option value="all">' + (isRu() ? "Ядро: Все (" + allowedCores.length + ")" : "Core: All (" + allowedCores.length + ")") + '</option>' +
                allowedCores.map(function (c) {
                  return '<option value="' + esc(c.id) + '"' + (state.libCoreFilter === c.id ? " selected" : "") + '>' + esc(c.displayName) + '</option>';
                }).join("") +
              '</select>' +
              '<select id="libSortSelect" class="select-3d" style="flex:1 1 205px;">' +
                '<option value="recent"' + (state.libSort === "recent" ? " selected" : "") + '>' + (isRu() ? "Сортировка: Недавние" : "Sort: Recent") + '</option>' +
                '<option value="az"' + (state.libSort === "az" ? " selected" : "") + '>' + (isRu() ? "По алфавиту A-Z" : "Sort: A-Z") + '</option>' +
                '<option value="platform"' + (state.libSort === "platform" ? " selected" : "") + '>' + (isRu() ? "По платформе" : "Sort: Platform") + '</option>' +
                '<option value="playtime"' + (state.libSort === "playtime" ? " selected" : "") + '>' + (isRu() ? "По времени в игре" : "Sort: Playtime") + '</option>' +
                '<option value="rating"' + (state.libSort === "rating" ? " selected" : "") + '>' + (isRu() ? "По рейтингу" : "Sort: Rating") + '</option>' +
              '</select>' +
              '<div style="display:flex;align-items:center;gap:8px;min-width:155px;">' +
                '<span class="badge-pill tabular" id="libZoomLabel">' + state.libZoom + '%</span>' +
                '<input type="range" id="libZoomSlider" class="range-3d" min="75" max="140" step="5" value="' + state.libZoom + '">' +
              '</div>' +
            '</div>' +
          '</div>' +
        '</div>' +

        '<div id="libraryResultsMount" class="library-grid view-' + esc(state.libViewMode) + '"></div>' +
      '</div>'
    );
  }

  /* ------------------------------------------------------------------------
     10. MODULE VIEW RENDERERS (3..12)
     ------------------------------------------------------------------------ */
  function renderCoreCardHtml(c) {
    const statusBadge = c.bundled
      ? '<span class="badge-pill badge-emerald">' + (isRu() ? "Вшито в APK" : "Pre-Bundled") + '</span>'
      : (c.installed
          ? '<span class="badge-pill badge-accent">' + (isRu() ? "Установлено" : "Installed") + '</span>'
          : '<span class="badge-pill">' + (isRu() ? "Облако CDN" : "Cloud CDN") + '</span>');

    return (
      '<div class="core-card" data-core-card="' + esc(c.id) + '">' +
        '<div>' +
          '<div class="toolbar-row" style="justify-content:space-between;margin-bottom:6px;">' +
            '<span class="badge-pill badge-accent">' + esc(c.platform) + '</span>' +
            statusBadge +
          '</div>' +
          '<div style="font-weight:800;font-size:0.90rem;color:#F8FAFC;">' + esc(c.displayName) + '</div>' +
          '<div class="mono-code" style="font-size:0.70rem;color:var(--text-secondary);margin-top:3px;">' + esc(c.soName) + '</div>' +
        '</div>' +
        (c.progress > 0 && c.progress < 100 ? (
          '<div>' +
            '<div class="progress-track-3d"><div class="progress-fill-gpu" style="--p:' + (c.progress / 100).toFixed(2) + ';"></div></div>' +
            '<div class="tabular" style="font-size:0.70rem;color:var(--text-secondary);margin-top:3px;">' + c.progress + '% CDN</div>' +
          '</div>'
        ) : "") +
        '<div class="toolbar-row">' +
          (c.installed || c.bundled ? (
            '<button type="button" class="btn-3d btn-3d-secondary btn-3d-sm" style="flex:1;" data-action="open-core-options" data-core="' + esc(c.id) + '">' + ico("sliders") + '<span>' + (isRu() ? "Опции ядра" : "Core Options") + '</span></button>' +
            (!c.bundled ? '<button type="button" class="icon-btn-3d" data-action="delete-core" data-core="' + esc(c.id) + '" aria-label="Delete">' + ico("trash") + '</button>' : "")
          ) : (
            '<button type="button" class="btn-3d btn-3d-primary btn-3d-sm" style="flex:1;" data-action="download-core" data-core="' + esc(c.id) + '">' + ico("download") + '<span>' + (isRu() ? "Скачать ядро" : "Download") + '</span></button>' +
            '<button type="button" class="icon-btn-3d" data-action="open-core-options" data-core="' + esc(c.id) + '" aria-label="Options">' + ico("sliders") + '</button>'
          )) +
        '</div>' +
      '</div>'
    );
  }

  function updateCoresResults() {
    const mount = document.getElementById("coresResultsMount");
    if (!mount) return;
    const q = state.coresQuery.trim().toLowerCase();
    const filtered = state.cores.filter(function (c) {
      if (state.coresFilter === "installed" && !(c.installed || c.bundled)) return false;
      if (state.coresFilter === "recommended" && !c.recommended) return false;
      if (state.coresPlatform !== "all" && c.platform !== state.coresPlatform) return false;
      if (!q) return true;
      return (c.displayName + " " + c.id + " " + c.platform).toLowerCase().indexOf(q) >= 0;
    });
    const emptyHtml = '<p class="section-subtitle">' + (isRu() ? "Ядра по текущему запросу не найдены." : "No cores match the current filter.") + '</p>';
    mountChunkedGrid(mount, filtered, renderCoreCardHtml, emptyHtml);
  }

  function renderCores() {
    const platSet = {};
    state.cores.forEach(function (c) { platSet[c.platform] = true; });
    const installedCount = countInstalledCores();

    return (
      '<div class="glass-card" style="margin-bottom:12px;">' +
        '<div class="section-head">' +
          '<div class="section-title-cluster">' +
            '<div class="section-icon-badge">' + ico("cpu") + '</div>' +
            '<div>' +
              '<h3 class="section-title">' + (isRu() ? "20 флагманских ядер вшиты в APK + " + state.cores.length + " ядра в облаке" : "20 Pre-Bundled APK Cores + " + state.cores.length + " Official Android Cores") + '</h3>' +
              '<p class="section-subtitle">' + (isRu() ? "Мгновенный офлайн-старт и отказоустойчивая загрузка с двух зеркал: STORM CDN + Libretro Buildbot" : "Instant offline play + resilient Dual-Mirror Downloader: STORM CDN + Libretro Buildbot") + '</p>' +
            '</div>' +
          '</div>' +
          '<span class="badge-pill badge-emerald tabular">' + installedCount + ' / ' + state.cores.length + ' Ready</span>' +
        '</div>' +

        '<div class="pack-grid-4">' +
          '<button type="button" class="bento-tool-tile" data-action="download-core-pack" data-pack="essential_15"><div class="bento-tile-head">' + ico("download") + '<span>' + (isRu() ? "Базовый пакет (15 ядер)" : "Essential Pack (15 Cores)") + '</span></div><div class="bento-tile-desc">NES, SNES, Genesis, GBA, GBC, PS1</div></button>' +
          '<button type="button" class="bento-tool-tile" data-action="download-core-pack" data-pack="3d_consoles"><div class="bento-tile-head">' + ico("disc") + '<span>' + (isRu() ? "3D Консоли (PS1/PSP/N64/DC)" : "3D Consoles Pack") + '</span></div><div class="bento-tile-desc">PS1, PSP, N64, NDS, 3DS, Dreamcast, Saturn</div></button>' +
          '<button type="button" class="bento-tool-tile" data-action="download-core-pack" data-pack="arcade_pc"><div class="bento-tile-head">' + ico("gamepad") + '<span>' + (isRu() ? "Аркады и ПК (FBNeo/DOS)" : "Arcade & PC Pack") + '</span></div><div class="bento-tile-desc">FBNeo, MAME, DOSBox-Pure, Amiga, ZX</div></button>' +
          '<button type="button" class="bento-tool-tile" data-action="import-core-file"><div class="bento-tile-head">' + ico("upload") + '<span>' + (isRu() ? "Импорт .so / .zip с памяти" : "Import Local .so / .zip") + '</span></div><div class="bento-tile-desc">' + (isRu() ? "Установка своего ядра с устройства" : "Install custom Libretro core offline") + '</div></button>' +
        '</div>' +

        '<div class="toolbar-row">' +
          '<div class="input-well-wrap">' +
            '<span class="input-lead-ico">' + ico("search") + '</span>' +
            '<input type="search" id="coresSearchInput" class="input-well-3d" value="' + esc(state.coresQuery) + '" placeholder="' + (isRu() ? "Поиск среди 252 ядер (Snes9x, SwanStation, PPSSPP, Flycast)..." : "Search 252 cores...") + '">' +
          '</div>' +
          '<select id="coresPlatSelect" class="select-3d" style="max-width:190px;">' +
            '<option value="all">' + (isRu() ? "Все системы" : "All Systems") + '</option>' +
            Object.keys(platSet).sort().map(function (p) {
              return '<option value="' + esc(p) + '"' + (state.coresPlatform === p ? " selected" : "") + '>' + esc(p) + '</option>';
            }).join("") +
          '</select>' +
        '</div>' +

        '<div class="chips-scroll-row" id="coresFilterChips" style="margin-top:9px;">' +
          '<button type="button" class="chip-3d' + (state.coresFilter === "all" ? " active" : "") + '" data-core-filter="all"><span>' + (isRu() ? "Все " + state.cores.length : "All " + state.cores.length) + '</span><span class="chip-count">' + state.cores.length + '</span></button>' +
          '<button type="button" class="chip-3d' + (state.coresFilter === "installed" ? " active" : "") + '" data-core-filter="installed"><span>' + (isRu() ? "Вшитые и установленные" : "Bundled & Installed") + '</span><span class="chip-count">' + installedCount + '</span></button>' +
          '<button type="button" class="chip-3d' + (state.coresFilter === "recommended" ? " active" : "") + '" data-core-filter="recommended"><span>' + (isRu() ? "Рекомендуемые" : "Recommended") + '</span></button>' +
        '</div>' +
      '</div>' +

      '<div id="coresResultsMount" class="cores-grid"></div>'
    );
  }

  function updateCheatsResults() {
    const mount = document.getElementById("cheatsResultsMount");
    if (!mount) return;
    const selectedMeta = state.cheatResults.find(function (x) { return x.id === state.selectedCheatGameId; }) || state.cheatResults[0] || { id: "custom", title: "Chrono Trigger (SNES)", system: "SNES" };

    mount.innerHTML =
      '<div class="glass-card">' +
        '<div class="section-title" style="font-size:0.92rem;margin-bottom:9px;">' + (isRu() ? "Найденные профили читов" : "Matched Game Profiles") + '</div>' +
        '<div class="cheats-list-scroll" id="cheatsListMount"></div>' +
      '</div>' +

      '<div class="glass-card">' +
        '<div class="section-head">' +
          '<div>' +
            '<h4 class="section-title">' + esc(selectedMeta.title) + '</h4>' +
            '<p class="section-subtitle">' + esc(selectedMeta.system) + ' • GameGenie / ActionReplay / RAW</p>' +
          '</div>' +
          '<div class="toolbar-row">' +
            '<button type="button" class="btn-3d btn-3d-secondary btn-3d-sm" data-action="cheats-enable-all">' + ico("check") + '<span>' + (isRu() ? "Включить все" : "Enable All") + '</span></button>' +
            '<button type="button" class="btn-3d btn-3d-secondary btn-3d-sm" data-action="cheats-disable-all">' + ico("close") + '<span>' + (isRu() ? "Отключить все" : "Disable All") + '</span></button>' +
            '<button type="button" class="btn-3d btn-3d-secondary btn-3d-sm" data-action="toggle-custom-cheat">' + ico("plus") + '<span>' + (isRu() ? "Конструктор своего чита" : "Custom Cheat") + '</span></button>' +
            '<button type="button" class="btn-3d btn-3d-primary btn-3d-sm" data-action="save-game-cheats">' + ico("save") + '<span>' + (isRu() ? "Сохранить читы" : "Save Cheats") + '</span></button>' +
          '</div>' +
        '</div>' +

        (state.showCustomCheatForm ? (
          '<div class="glass-card" style="margin-bottom:10px;background:rgba(0,0,0,0.32);">' +
            '<div class="toolbar-row" style="margin-bottom:8px;">' +
              '<input type="text" id="newCheatDesc" class="input-well-3d" placeholder="' + (isRu() ? "Описание (напр. 99 жизней / Макс. золото)" : "Cheat description") + '">' +
            '</div>' +
            '<div class="toolbar-row">' +
              '<input type="text" id="newCheatCode" class="input-well-3d mono-code" placeholder="' + (isRu() ? "Код GameGenie / ActionReplay / RAW (напр. 8009C796 00C8)" : "RAW / ActionReplay code") + '">' +
              '<button type="button" class="btn-3d btn-3d-emerald btn-3d-sm" data-action="append-custom-cheat">' + ico("plus") + '<span>' + (isRu() ? "Добавить" : "Add") + '</span></button>' +
            '</div>' +
          '</div>'
        ) : "") +

        '<div style="display:flex;flex-direction:column;gap:8px;">' +
          state.activeCheatEntries.map(function (c, i) {
            return (
              '<div class="toggle-row-3d">' +
                '<div class="toggle-meta">' +
                  '<div class="toggle-title">' + esc(c.description) + '</div>' +
                  '<div class="toggle-desc mono-code">' + esc(c.code) + '</div>' +
                '</div>' +
                '<button type="button" role="switch" aria-checked="' + Boolean(c.enabled) + '" class="switch-3d" data-action="toggle-cheat-entry" data-idx="' + i + '"><span class="switch-track"><span class="switch-thumb"></span></span></button>' +
              '</div>'
            );
          }).join("") +
        '</div>' +

        '<div class="glass-card" style="margin-top:12px;background:rgba(139,92,246,0.09);">' +
          '<div class="section-head" style="margin-bottom:8px;">' +
            '<div class="section-title-cluster">' +
              '<div class="section-icon-badge">' + ico("cpu") + '</div>' +
              '<div>' +
                '<div class="toggle-title">' + (isRu() ? "Живой сканер ОЗУ (8 / 16 / 32-бит) в быстром меню игры" : "Live Gameplay RAM Scanner (8 / 16 / 32-bit)") + '</div>' +
                '<div class="toggle-desc">' + (isRu() ? "Ищите жизни, патроны и деньги прямо во время геймплея через Быстрое меню (L3+R3) и замораживайте адреса памяти" : "Search HP, ammo & gold directly inside gameplay Quick Menu (L3+R3) and freeze addresses") + '</div>' +
              '</div>' +
            '</div>' +
          '</div>' +
          '<div class="toolbar-row">' +
            '<div class="seg-group-3d">' +
              ["8", "16", "32"].map(function (b) {
                return '<button type="button" class="seg-btn-3d' + (state.ramBits === b ? " active" : "") + '" data-ram-bits="' + b + '"><span>' + b + '-bit</span></button>';
              }).join("") +
            '</div>' +
            '<select id="ramCompareSelect" class="select-3d" style="max-width:240px;">' +
              '<option value="exact"' + (state.ramCompare === "exact" ? " selected" : "") + '>' + (isRu() ? "Режим: Точное значение" : "Mode: Exact Value") + '</option>' +
              '<option value="decreased"' + (state.ramCompare === "decreased" ? " selected" : "") + '>' + (isRu() ? "Уменьшилось (урон/патроны)" : "Decreased Value") + '</option>' +
              '<option value="increased"' + (state.ramCompare === "increased" ? " selected" : "") + '>' + (isRu() ? "Увеличилось (опыт/золото)" : "Increased Value") + '</option>' +
            '</select>' +
          '</div>' +
        '</div>' +
      '</div>';

    const listMount = document.getElementById("cheatsListMount");
    mountChunkedGrid(listMount, state.cheatResults, function (item) {
      const act = item.id === selectedMeta.id ? " active" : "";
      return (
        '<button type="button" class="cheat-game-item cheat-game-row' + act + '" data-action="select-cheat-game" data-id="' + esc(item.id) + '" data-sys="' + esc(item.system) + '" data-title="' + esc(item.title) + '">' +
          '<div><div style="font-weight:800;font-size:0.83rem;">' + esc(item.title) + '</div><div style="font-size:0.70rem;color:var(--text-secondary);">' + esc(item.system) + '</div></div>' +
          '<span class="badge-pill badge-accent">' + (item.count || 5) + '</span>' +
        '</button>'
      );
    }, '<p class="section-subtitle">' + (isRu() ? "Профили читов не найдены." : "No cheat profiles found.") + '</p>');
  }

  function renderCheats() {
    return (
      '<div class="glass-card" style="margin-bottom:12px;">' +
        '<div class="section-head">' +
          '<div class="section-title-cluster">' +
            '<div class="section-icon-badge">' + ico("shield") + '</div>' +
            '<div>' +
              '<h3 class="section-title">' + (isRu() ? "База читов и тренеров: 26 797 игр (76 систем)" : "Cheats & Trainers Vault: 26,797 Games (76 Systems)") + '</h3>' +
              '<p class="section-subtitle">' + (isRu() ? "Мгновенный поиск по каталогу Libretro, конструктор своих кодов и живой сканер ОЗУ" : "Instant catalog search, custom code builder & live gameplay RAM scanner") + '</p>' +
            '</div>' +
          '</div>' +
          '<button type="button" class="btn-3d btn-3d-emerald btn-3d-sm" data-action="download-library-cheats">' + ico("download") + '<span>' + (isRu() ? "Скачать читы для всех игр библиотеки" : "Download Cheats for Library") + '</span></button>' +
        '</div>' +

        '<div class="toolbar-row">' +
          '<div class="input-well-wrap">' +
            '<span class="input-lead-ico">' + ico("search") + '</span>' +
            '<input type="search" id="cheatsSearchInput" class="input-well-3d" value="' + esc(state.cheatsQuery) + '" placeholder="' + (isRu() ? "Поиск среди 26 797 игр (Chrono Trigger, Resident Evil, Contra, Sonic)..." : "Search 26,797 games...") + '">' +
          '</div>' +
          '<div class="seg-group-3d">' +
            '<button type="button" class="seg-btn-3d' + (!state.cheatsLibraryOnly ? " active" : "") + '" data-cheat-scope="all"><span>' + (isRu() ? "Вся база 26 797" : "Full DB 26,797") + '</span></button>' +
            '<button type="button" class="seg-btn-3d' + (state.cheatsLibraryOnly ? " active" : "") + '" data-cheat-scope="lib"><span>' + (isRu() ? "Моя библиотека" : "My Library") + '</span></button>' +
          '</div>' +
          '<select id="cheatsSysSelect" class="select-3d" style="max-width:185px;">' +
            '<option value="all">' + (isRu() ? "Все 76 систем" : "All 76 Systems") + '</option>' +
            ["SNES", "PS1", "PSP", "Sega Genesis", "GBA", "NDS", "N64", "Dreamcast", "Arcade"].map(function (s) {
              return '<option value="' + s + '"' + (state.cheatsSystem === s ? " selected" : "") + '>' + s + '</option>';
            }).join("") +
          '</select>' +
        '</div>' +
      '</div>' +

      '<div id="cheatsResultsMount" class="two-col-studio"></div>'
    );
  }

  function renderGraphics() {
    const shaders = [
      { id: "none", title: isRu() ? "Без фильтра" : "Raw Pixels", cls: "" },
      { id: "crt_royale", title: "CRT Royale", cls: "shader-scanlines" },
      { id: "crt_geom", title: "CRT Geom Curvature", cls: "shader-scanlines" },
      { id: "trinitron", title: "Sony Trinitron Aperture", cls: "shader-scanlines" },
      { id: "lcd_grid", title: "LCD Grid Handheld", cls: "shader-lcd" },
      { id: "xbrz", title: "xBRZ Smooth HD", cls: "" },
      { id: "vhs", title: "VHS Composite", cls: "shader-scanlines" }
    ];

    return (
      '<div class="two-col-studio">' +
        '<div class="glass-card">' +
          '<div class="section-head">' +
            '<div class="section-title-cluster">' +
              '<div class="section-icon-badge">' + ico("display") + '</div>' +
              '<div><h3 class="section-title">' + (isRu() ? "Рендерер, Масштаб 1x-8x и 120 Гц" : "Video Backend, 1x-8x Upscale & 120 Hz") + '</h3><p class="section-subtitle">GLES3 Hardware FBO / Vulkan Ready</p></div>' +
            '</div>' +
          '</div>' +

          '<div style="display:flex;flex-direction:column;gap:10px;">' +
            '<div><div class="toggle-title" style="margin-bottom:5px;">' + (isRu() ? "Графический API" : "Graphics Backend") + '</div>' +
              '<select id="gfxBackendSelect" class="select-3d">' +
                '<option value="GLES3"' + (state.settings.videoBackend === "GLES3" ? " selected" : "") + '>OpenGL ES 3.2 Hardware FBO</option>' +
                '<option value="Vulkan"' + (state.settings.videoBackend === "Vulkan" ? " selected" : "") + '>Vulkan 1.3 Low-Overhead Ready</option>' +
              '</select>' +
            '</div>' +

            '<div><div class="toggle-title" style="margin-bottom:5px;">' + (isRu() ? "Внутреннее разрешение 3D ядер" : "Internal Upscaling") + '</div>' +
              '<select id="gfxScaleSelect" class="select-3d">' +
                ["1x Native", "2x (720p HD)", "3x (1080p FHD)", "4x (1440p QHD)", "6x", "8x (4K UHD)"].map(function (s) {
                  const key = s.split(" ")[0];
                  return '<option value="' + key + '"' + (state.settings.internalScale === key ? " selected" : "") + '>' + s + '</option>';
                }).join("") +
              '</select>' +
            '</div>' +

            '<div><div class="toggle-title" style="margin-bottom:5px;">' + (isRu() ? "Соотношение сторон экрана" : "Aspect Ratio") + '</div>' +
              '<select id="gfxAspectSelect" class="select-3d">' +
                [["auto", "Auto (Core Provided)"], ["4:3", "4:3 Classic CRT"], ["16:9", "16:9 Widescreen"], ["integer", "Pixel-Perfect Integer"], ["stretch", "Full Handheld Stretch"]].map(function (r) {
                  return '<option value="' + r[0] + '"' + (state.settings.aspectRatio === r[0] ? " selected" : "") + '>' + r[1] + '</option>';
                }).join("") +
              '</select>' +
            '</div>' +

            '<div><div class="toggle-title" style="margin-bottom:5px;">' + (isRu() ? "Опережение ввода Run-Ahead (Zero-Latency)" : "Run-Ahead Zero-Latency") + '</div>' +
              '<div class="seg-group-3d">' +
                ["0", "1", "2", "3", "4"].map(function (f) {
                  return '<button type="button" class="seg-btn-3d' + (String(state.settings.runAheadFrames) === f ? " active" : "") + '" data-runahead="' + f + '"><span>' + f + (isRu() ? " кадр." : " fr") + '</span></button>';
                }).join("") +
              '</div>' +
            '</div>' +

            '<div class="toggle-row-3d">' +
              '<div class="toggle-meta"><div class="toggle-title">' + (isRu() ? "Высокая герцовка 90 / 120 Гц" : "90 / 120 Hz High Refresh") + '</div><div class="toggle-desc">' + (isRu() ? "Синхронизация кадрового буфера без разрывов" : "Smooth low-persistence pacing") + '</div></div>' +
              '<button type="button" role="switch" aria-checked="' + Boolean(state.settings.highRefresh120) + '" class="switch-3d" data-toggle-setting="highRefresh120"><span class="switch-track"><span class="switch-thumb"></span></span></button>' +
            '</div>' +

            '<div class="toggle-row-3d">' +
              '<div class="toggle-meta"><div class="toggle-title">' + (isRu() ? "Перемотка времени (Rewind Buffer)" : "Gameplay Rewind Buffer") + '</div><div class="toggle-desc">' + (isRu() ? "Мгновенный откат ошибок в реальном времени" : "Real-time frame-by-frame rewind") + '</div></div>' +
              '<button type="button" role="switch" aria-checked="' + Boolean(state.settings.rewindEnabled) + '" class="switch-3d" data-toggle-setting="rewindEnabled"><span class="switch-track"><span class="switch-thumb"></span></span></button>' +
            '</div>' +
          '</div>' +
        '</div>' +

        '<div style="display:flex;flex-direction:column;gap:12px;">' +
          '<div class="glass-card">' +
            '<h4 class="section-title" style="margin-bottom:10px;">' + (isRu() ? "Шейдеры и ЭЛТ пресеты" : "CRT & Upscale Shader Presets") + '</h4>' +
            '<div class="shader-cards-grid">' +
              shaders.map(function (sh) {
                const act = state.settings.shaderPreset === sh.id ? " active" : "";
                return (
                  '<button type="button" class="shader-card-btn' + act + '" data-shader="' + sh.id + '">' +
                    '<div class="shader-preview-bar ' + sh.cls + '"></div>' +
                    '<div style="font-size:0.79rem;font-weight:800;">' + esc(sh.title) + '</div>' +
                  '</button>'
                );
              }).join("") +
            '</div>' +
          '</div>' +

          '<div class="glass-card">' +
            '<h4 class="section-title" style="margin-bottom:10px;">' + (isRu() ? "Онлайн-загрузка рамок и скинов консолей" : "Online Bezel & Console Skin Packs") + '</h4>' +
            '<div style="display:flex;flex-direction:column;gap:8px;">' +
              state.bezelPacks.map(function (bp) {
                return (
                  '<div class="toggle-row-3d">' +
                    '<div class="toggle-meta"><div class="toggle-title">' + esc(bp.title) + '</div><div class="toggle-desc tabular">' + esc(bp.size) + (bp.installed ? (isRu() ? " • Установлен" : " • Installed") : "") + '</div></div>' +
                    '<button type="button" class="btn-3d ' + (bp.installed ? "btn-3d-emerald" : "btn-3d-primary") + ' btn-3d-sm" data-action="download-bezel" data-pack="' + esc(bp.id) + '">' +
                      ico(bp.installed ? "check" : "download") + '<span>' + (bp.installed ? (isRu() ? "Обновить" : "Ready") : (isRu() ? "Скачать" : "Download")) + '</span>' +
                    '</button>' +
                  '</div>'
                );
              }).join("") +
            '</div>' +
          '</div>' +
        '</div>' +
      '</div>'
    );
  }

  function renderAudio() {
    const presets = [
      { id: "balanced", label: isRu() ? "Сбалансированный" : "Balanced Studio", bands: [60, 60, 62, 62, 60] },
      { id: "crt_bass", label: isRu() ? "Усиление басов CRT" : "CRT Bass Boost", bands: [88, 76, 58, 62, 66] },
      { id: "rpg_voice", label: isRu() ? "Чёткие голоса RPG" : "Clear RPG Voices", bands: [48, 56, 84, 82, 68] },
      { id: "arcade_cab", label: isRu() ? "Аркадный кабинет" : "Arcade Cabinet", bands: [82, 68, 64, 78, 86] },
      { id: "night", label: isRu() ? "Ночной режим" : "Night Limiter", bands: [44, 52, 66, 58, 48] }
    ];
    const eqLabels = ["60 Hz", "250 Hz", "1 kHz", "4 kHz", "12 kHz"];

    return (
      '<div class="two-col-studio">' +
        '<div class="glass-card">' +
          '<div class="section-head">' +
            '<div class="section-title-cluster">' +
              '<div class="section-icon-badge">' + ico("wave") + '</div>' +
              '<div><h3 class="section-title">' + (isRu() ? "Аудио DSP и Эквалайзер" : "Audio DSP & Parametric Equalizer") + '</h3><p class="section-subtitle">AAudio / OpenSL ES Low-Latency Ring Buffer</p></div>' +
            '</div>' +
          '</div>' +

          '<div class="dsp-spectrum-box" style="margin-bottom:12px;">' +
            [0, 1, 2, 3, 4, 4, 3, 2, 1, 0].map(function (bi, idx) {
              const lv = Math.max(0.18, Math.min(1, ((state.settings.eqBands[bi] || 60) / 100) * (idx % 2 === 0 ? 0.95 : 0.78)));
              return '<div class="dsp-bar" style="--lv:' + lv.toFixed(2) + ';"></div>';
            }).join("") +
          '</div>' +

          '<div class="chips-scroll-row" style="margin-bottom:10px;">' +
            presets.map(function (pr) {
              const act = state.settings.dspPreset === pr.id ? " active" : "";
              return '<button type="button" class="chip-3d' + act + '" data-dsp-preset="' + pr.id + '" data-bands="' + pr.bands.join(",") + '"><span>' + esc(pr.label) + '</span></button>';
            }).join("") +
          '</div>' +

          '<div style="display:flex;flex-direction:column;gap:8px;">' +
            eqLabels.map(function (lbl, idx) {
              const v = state.settings.eqBands[idx] || 60;
              return (
                '<div style="display:flex;align-items:center;gap:10px;">' +
                  '<span class="mono-code" style="width:62px;font-size:0.75rem;">' + lbl + '</span>' +
                  '<input type="range" class="range-3d" data-eq-band="' + idx + '" min="15" max="100" value="' + v + '">' +
                  '<span class="badge-pill tabular" style="min-width:48px;justify-content:center;">' + v + '%</span>' +
                '</div>'
              );
            }).join("") +
          '</div>' +
        '</div>' +

        '<div class="glass-card" style="display:flex;flex-direction:column;gap:11px;">' +
          '<div>' +
            '<div style="display:flex;justify-content:space-between;"><span class="toggle-title">' + (isRu() ? "Общая громкость (Master)" : "Master Volume") + '</span><span class="badge-pill badge-accent tabular">' + state.settings.masterVolume + '%</span></div>' +
            '<input type="range" id="audVolSlider" class="range-3d" min="0" max="100" value="' + state.settings.masterVolume + '">' +
          '</div>' +

          '<div>' +
            '<div style="display:flex;justify-content:space-between;"><span class="toggle-title">' + (isRu() ? "Предусилитель (Pre-Amp Gain)" : "Pre-Amp Gain") + '</span><span class="badge-pill tabular">' + (state.settings.preAmpDb >= 0 ? "+" : "") + state.settings.preAmpDb + ' dB</span></div>' +
            '<input type="range" id="audGainSlider" class="range-3d" min="-12" max="12" step="1" value="' + state.settings.preAmpDb + '">' +
          '</div>' +

          '<div>' +
            '<div class="toggle-title" style="margin-bottom:6px;">' + (isRu() ? "Кольцевой аудио-буфер низкой задержки" : "Low-Latency Ring Buffer") + '</div>' +
            '<div class="seg-group-3d">' +
              ["16", "24", "32", "48"].map(function (ms) {
                return '<button type="button" class="seg-btn-3d' + (String(state.settings.audioLatencyMs) === ms ? " active" : "") + '" data-audio-ms="' + ms + '"><span>' + ms + ' мс</span></button>';
              }).join("") +
            '</div>' +
          '</div>' +

          '<div class="toggle-row-3d">' +
            '<div class="toggle-meta"><div class="toggle-title">' + (isRu() ? "Динамический контроль частоты (DRC)" : "Dynamic Rate Control") + '</div><div class="toggle-desc">' + (isRu() ? "Идеальная синхронизация звука и видео без треска" : "Eliminates audio crackles & frame drops") + '</div></div>' +
            '<button type="button" role="switch" aria-checked="' + Boolean(state.settings.dynamicRateControl) + '" class="switch-3d" data-toggle-setting="dynamicRateControl"><span class="switch-track"><span class="switch-thumb"></span></span></button>' +
          '</div>' +

          '<div class="toggle-row-3d">' +
            '<div class="toggle-meta"><div class="toggle-title">' + (isRu() ? "Приглушать звук в фоне" : "Mute on Focus Loss") + '</div><div class="toggle-desc">' + (isRu() ? "Автоматическая пауза DSP при сворачивании" : "Auto-mute when switching apps") + '</div></div>' +
            '<button type="button" role="switch" aria-checked="' + Boolean(state.settings.muteOnFocusLoss) + '" class="switch-3d" data-toggle-setting="muteOnFocusLoss"><span class="switch-track"><span class="switch-thumb"></span></span></button>' +
          '</div>' +
        '</div>' +
      '</div>'
    );
  }

  function renderControls() {
    const padPalette = {
      "STORM Violet": ["#A78BFA", "#6D28D9"],
      "PlayStation": ["#60A5FA", "#1D4ED8"],
      "SNES": ["#FB7185", "#BE123C"],
      "Xbox": ["#34D399", "#047857"],
      "Minimal Glass": ["#94A3B8", "#334155"]
    }[state.settings.touchColorTheme] || ["#A78BFA", "#6D28D9"];

    return (
      '<div class="two-col-studio">' +
        '<div class="glass-card" style="display:flex;flex-direction:column;gap:11px;">' +
          '<div class="section-head" style="margin-bottom:2px;">' +
            '<div class="section-title-cluster">' +
              '<div class="section-icon-badge">' + ico("dpad") + '</div>' +
              '<div><h3 class="section-title">' + (isRu() ? "Студия 3D Сенсорного Геймпада" : "3D Touch Gamepad Studio") + '</h3><p class="section-subtitle">' + esc(state.settings.touchLayout) + ' • ' + esc(state.settings.touchColorTheme) + '</p></div>' +
            '</div>' +
          '</div>' +

          '<div class="touch-pad-stage" style="--pad-scale:' + (state.settings.touchScale / 100).toFixed(2) + ';--pad-opacity:' + (state.settings.touchOpacity / 100).toFixed(2) + ';--pad-c1:' + padPalette[0] + ';--pad-c2:' + padPalette[1] + ';">' +
            '<div class="dpad-mock"><div class="dpad-arm-h"></div><div class="dpad-arm-v"></div></div>' +
            '<div style="text-align:center;"><span class="badge-pill badge-accent tabular">' + state.settings.touchScale + '% • ' + state.settings.touchOpacity + '%</span><div style="font-size:0.72rem;color:var(--text-secondary);margin-top:4px;">Turbo ' + state.settings.turboHz + ' Hz</div></div>' +
            '<div class="abxy-cluster"><div class="pad-face-btn btn-n">X</div><div class="pad-face-btn btn-s">B</div><div class="pad-face-btn btn-w">Y</div><div class="pad-face-btn btn-e">A</div></div>' +
          '</div>' +

          '<div class="toolbar-row">' +
            '<select id="ctrlLayoutSelect" class="select-3d" style="flex:1;">' +
              ["Console Pro", "PlayStation Dual", "Arcade 6-Button", "SNES Classic", "RPG Minimal", "Hidden for Physical Pad"].map(function (l) {
                return '<option value="' + l + '"' + (state.settings.touchLayout === l ? " selected" : "") + '>' + (isRu() ? "Раскладка: " : "Layout: ") + l + '</option>';
              }).join("") +
            '</select>' +
            '<select id="ctrlThemeSelect" class="select-3d" style="flex:1;">' +
              ["STORM Violet", "PlayStation", "SNES", "Xbox", "Minimal Glass"].map(function (t) {
                return '<option value="' + t + '"' + (state.settings.touchColorTheme === t ? " selected" : "") + '>' + (isRu() ? "Стиль 3D: " : "Theme: ") + t + '</option>';
              }).join("") +
            '</select>' +
          '</div>' +

          '<div><div style="display:flex;justify-content:space-between;"><span class="toggle-title">' + (isRu() ? "Прозрачность оверлея" : "Overlay Opacity") + '</span><span class="badge-pill tabular">' + state.settings.touchOpacity + '%</span></div>' +
            '<input type="range" id="ctrlOpacitySlider" class="range-3d" min="25" max="100" value="' + state.settings.touchOpacity + '">' +
          '</div>' +

          '<div><div style="display:flex;justify-content:space-between;"><span class="toggle-title">' + (isRu() ? "Масштаб 3D кнопок" : "Button Scale") + '</span><span class="badge-pill tabular">' + state.settings.touchScale + '%</span></div>' +
            '<input type="range" id="ctrlScaleSlider" class="range-3d" min="70" max="140" value="' + state.settings.touchScale + '">' +
          '</div>' +

          '<div class="toggle-row-3d">' +
            '<div class="toggle-meta"><div class="toggle-title">' + (isRu() ? "Наклон гироскопа как левый стик" : "Gyroscope Tilt-to-Analog Stick") + '</div><div class="toggle-desc">' + (isRu() ? "Плавное руление в гонках и авиасимуляторах" : "Precision steering via phone gyroscope") + '</div></div>' +
            '<button type="button" role="switch" aria-checked="' + Boolean(state.settings.gyroStick) + '" class="switch-3d" data-toggle-setting="gyroStick"><span class="switch-track"><span class="switch-thumb"></span></span></button>' +
          '</div>' +

          '<div class="toolbar-row">' +
            '<select id="ctrlHapticSelect" class="select-3d" style="flex:1;">' +
              [["off", "Виброотклик: Выкл"], ["light", "Вибро: Лёгкий"], ["medium", "Вибро: Средний"], ["strong", "Вибро: Мощный"]].map(function (h) {
                return '<option value="' + h[0] + '"' + (state.settings.hapticStrength === h[0] ? " selected" : "") + '>' + h[1] + '</option>';
              }).join("") +
            '</select>' +
            '<select id="ctrlTurboSelect" class="select-3d" style="flex:1;">' +
              ["10", "15", "20", "30"].map(function (hz) {
                return '<option value="' + hz + '"' + (String(state.settings.turboHz) === hz ? " selected" : "") + '>Turbo Fire: ' + hz + ' Hz</option>';
              }).join("") +
            '</select>' +
          '</div>' +
        '</div>' +

        '<div class="glass-card" style="display:flex;flex-direction:column;gap:11px;">' +
          '<div class="section-head" style="margin-bottom:2px;">' +
            '<div><h3 class="section-title">' + (isRu() ? "Физические Bluetooth / USB Геймпады и Хоткеи" : "Physical Controllers & Hotkey Combos") + '</h3><p class="section-subtitle">Backbone, Razer Kishi, Gamesir, DualSense, Xbox</p></div>' +
            '<button type="button" class="btn-3d btn-3d-secondary btn-3d-sm" data-action="refresh-controllers">' + ico("refresh") + '<span>' + (isRu() ? "Обновить" : "Scan Pads") + '</span></button>' +
          '</div>' +

          '<div style="display:flex;flex-direction:column;gap:6px;">' +
            state.controllers.map(function (pad) {
              return '<div class="toggle-row-3d"><div class="toggle-meta"><div class="toggle-title">' + esc(pad.name) + '</div><div class="toggle-desc">' + esc(pad.type || "HID") + ' • Port #' + (pad.port || pad.playerPort || 1) + '</div></div><span class="badge-pill badge-emerald">Active</span></div>';
            }).join("") +
          '</div>' +

          '<div class="toggle-title">' + (isRu() ? "Переназначение кнопок (нажмите для записи)" : "Interactive Button Remapper") + '</div>' +
          '<div class="telemetry-grid">' +
            Object.keys(state.controllerMap).map(function (btnKey) {
              const listening = state.listeningBindKey === btnKey;
              return (
                '<button type="button" class="btn-3d ' + (listening ? "btn-3d-primary" : "btn-3d-secondary") + ' btn-3d-sm" data-action="remap-btn" data-key="' + btnKey + '">' +
                  '<span>' + btnKey + ': ' + (listening ? (isRu() ? "Нажмите..." : "Press...") : esc(state.controllerMap[btnKey])) + '</span>' +
                '</button>'
              );
            }).join("") +
          '</div>' +

          '<div class="toggle-title" style="margin-top:4px;">' + (isRu() ? "Глобальные комбинации хоткеев" : "Global Hotkey Combos") + '</div>' +
          '<div style="display:flex;flex-direction:column;gap:6px;font-size:0.78rem;">' +
            '<div class="toggle-row-3d"><span>' + (isRu() ? "Быстрое меню студии в игре" : "In-Game Quick Menu") + '</span><span class="badge-pill badge-accent mono-code">L3 + R3</span></div>' +
            '<div class="toggle-row-3d"><span>' + (isRu() ? "Быстрое сохранение / загрузка" : "Quick Save / Load") + '</span><span class="badge-pill mono-code">Select + R1 / L1</span></div>' +
            '<div class="toggle-row-3d"><span>' + (isRu() ? "Ускорение 2x / Перемотка назад" : "Fast-Forward 2x / Rewind") + '</span><span class="badge-pill mono-code">Select + R2 / L2</span></div>' +
          '</div>' +
        '</div>' +
      '</div>'
    );
  }

  function renderBios() {
    const okCount = state.bios.filter(function (b) { return b.status !== "missing"; }).length;
    const pct = Math.round((okCount / Math.max(1, state.bios.length)) * 100);
    const platforms = ["all"].concat(state.bios.map(function (b) { return b.platform; }).filter(function (v, i, a) { return a.indexOf(v) === i; }));
    const shown = state.bios.filter(function (b) {
      return state.biosPlatform === "all" || b.platform === state.biosPlatform;
    });

    return (
      '<div class="glass-card" style="margin-bottom:12px;">' +
        '<div class="section-head">' +
          '<div class="section-title-cluster">' +
            '<div class="section-icon-badge">' + ico("chip") + '</div>' +
            '<div>' +
              '<h3 class="section-title">' + (isRu() ? "Системные BIOS и Прошивки (13 платформ)" : "System BIOS & Firmware Checklist (13 Consoles)") + '</h3>' +
              '<p class="section-subtitle">' + (isRu() ? "Готовность системного каталога: " : "BIOS Readiness: ") + okCount + " / " + state.bios.length + " (" + pct + "%)</p>" +
            '</div>' +
          '</div>' +
          '<div class="toolbar-row">' +
            '<button type="button" class="btn-3d btn-3d-primary btn-3d-sm" data-action="pick-bios-folder">' + ico("folder") + '<span>' + (isRu() ? "Автопоиск BIOS в папке" : "Pick BIOS Folder") + '</span></button>' +
            '<button type="button" class="btn-3d btn-3d-emerald btn-3d-sm" data-action="import-bios-file">' + ico("upload") + '<span>' + (isRu() ? "Импорт файла BIOS" : "Import BIOS File") + '</span></button>' +
            '<button type="button" class="btn-3d btn-3d-secondary btn-3d-sm" data-action="scan-bios-md5">' + ico("refresh") + '<span>' + (isRu() ? "Проверить MD5" : "Verify MD5") + '</span></button>' +
          '</div>' +
        '</div>' +
        '<div class="progress-track-3d" style="margin-bottom:10px;"><div class="progress-fill-gpu" style="--p:' + (pct / 100).toFixed(2) + ';"></div></div>' +
        '<div class="chips-scroll-row">' +
          platforms.map(function (p) {
            return '<button type="button" class="chip-3d' + (state.biosPlatform === p ? " active" : "") + '" data-bios-plat="' + esc(p) + '"><span>' + (p === "all" ? esc(tr("all")) : esc(p)) + '</span></button>';
          }).join("") +
        '</div>' +
      '</div>' +

      '<div class="cores-grid">' +
        shown.map(function (b) {
          const pill = b.status === "verified"
            ? '<span class="badge-pill badge-emerald">' + (isRu() ? "Проверен MD5" : "Verified MD5") + '</span>'
            : (b.status === "present"
                ? '<span class="badge-pill badge-amber">' + (isRu() ? "Найден" : "Present") + '</span>'
                : '<span class="badge-pill badge-rose">' + (isRu() ? "Отсутствует" : "Missing") + '</span>');
          return (
            '<div class="core-card">' +
              '<div class="toolbar-row" style="justify-content:space-between;">' +
                '<span class="badge-pill badge-accent">' + esc(b.platform) + '</span>' +
                pill +
              '</div>' +
              '<div class="mono-code" style="font-size:0.82rem;font-weight:700;color:#F8FAFC;">' + esc(b.relativePath) + '</div>' +
              '<div class="mono-code" style="font-size:0.69rem;color:var(--text-secondary);word-break:break-all;">MD5: ' + esc(b.expectedMd5) + '</div>' +
            '</div>'
          );
        }).join("") +
      '</div>'
    );
  }

  function renderSaves() {
    return (
      '<div class="glass-card" style="margin-bottom:12px;">' +
        '<div class="section-head">' +
          '<div class="section-title-cluster">' +
            '<div class="section-icon-badge">' + ico("save") + '</div>' +
            '<div>' +
              '<h3 class="section-title">' + (isRu() ? "Сейвы .SRM, Быстрые стейты и Синхронизация с ПК" : "Battery .SRM, Save States & PC .ZIP Sync") + '</h3>' +
              '<p class="section-subtitle">' + (isRu() ? "Мгновенный перенос сохранений между Android и десктопным STORM RETROARCH в одном .ZIP архиве" : "Seamless 1-tap .ZIP backup & restore between Android and PC") + '</p>' +
            '</div>' +
          '</div>' +
          '<div class="toolbar-row">' +
            '<button type="button" class="btn-3d btn-3d-primary btn-3d-sm" data-action="export-saves-zip">' + ico("download") + '<span>' + (isRu() ? "Экспорт всех сейвов в .ZIP" : "Export All Saves .ZIP") + '</span></button>' +
            '<button type="button" class="btn-3d btn-3d-emerald btn-3d-sm" data-action="import-saves-zip">' + ico("upload") + '<span>' + (isRu() ? "Импорт .ZIP с ПК" : "Import PC .ZIP") + '</span></button>' +
          '</div>' +
        '</div>' +

        '<div class="two-col-studio">' +
          '<div class="toggle-row-3d">' +
            '<div class="toggle-meta"><div class="toggle-title">' + (isRu() ? "Автосохранение при выходе" : "Auto-Save on Exit") + '</div><div class="toggle-desc">' + (isRu() ? "Запись стейта и .srm при сворачивании" : "Auto-write state & battery on exit") + '</div></div>' +
            '<button type="button" role="switch" aria-checked="' + Boolean(state.settings.autoSaveExit) + '" class="switch-3d" data-toggle-setting="autoSaveExit"><span class="switch-track"><span class="switch-thumb"></span></span></button>' +
          '</div>' +
          '<div class="toggle-row-3d">' +
            '<div class="toggle-meta"><div class="toggle-title">' + (isRu() ? "Автозагрузка при старте игры" : "Auto-Load State on Launch") + '</div><div class="toggle-desc">' + (isRu() ? "Продолжать ровно с последнего кадра" : "Resume right from last frame") + '</div></div>' +
            '<button type="button" role="switch" aria-checked="' + Boolean(state.settings.autoLoadLaunch) + '" class="switch-3d" data-toggle-setting="autoLoadLaunch"><span class="switch-track"><span class="switch-thumb"></span></span></button>' +
          '</div>' +
        '</div>' +
      '</div>' +

      '<div class="cores-grid">' +
        state.saves.map(function (sv, idx) {
          return (
            '<div class="core-card">' +
              '<div class="toolbar-row" style="justify-content:space-between;">' +
                '<span class="badge-pill badge-accent">' + esc(sv.platform) + '</span>' +
                '<span class="badge-pill badge-emerald tabular">' + esc(sv.size) + '</span>' +
              '</div>' +
              '<div style="font-weight:800;font-size:0.92rem;">' + esc(sv.gameTitle) + '</div>' +
              '<div style="font-size:0.75rem;color:var(--text-secondary);">' + esc(sv.type) + ' • ' + esc(sv.date) + '</div>' +
              '<div class="toolbar-row">' +
                '<button type="button" class="btn-3d btn-3d-primary btn-3d-sm" style="flex:1;" data-action="launch-game" data-game="' + esc(sv.gameId) + '" data-slot="' + Number(sv.slot || 0) + '">' + ico("play") + '<span>' + (isRu() ? "Загрузить слот #" + sv.slot : "Load Slot #" + sv.slot) + '</span></button>' +
                '<button type="button" class="icon-btn-3d" data-action="delete-save-slot" data-idx="' + idx + '" data-game="' + esc(sv.gameId) + '" data-slot="' + Number(sv.slot || 0) + '" aria-label="Delete">' + ico("trash") + '</button>' +
              '</div>' +
            '</div>'
          );
        }).join("") +
      '</div>'
    );
  }

  function renderRomTools() {
    const gameOpts = state.games.map(function (g) {
      return '<option value="' + esc(g.id) + '">' + esc(g.title) + " (" + esc(g.platform) + ')</option>';
    }).join("");

    return (
      '<div class="cores-grid">' +
        '<div class="glass-card" style="display:flex;flex-direction:column;gap:10px;">' +
          '<div class="section-title-cluster"><div class="section-icon-badge">' + ico("wrench") + '</div><div><h3 class="section-title">' + (isRu() ? "Патчер ROM: IPS / BPS / UPS" : "IPS / BPS / UPS ROM Patcher") + '</h3><p class="section-subtitle">' + (isRu() ? "Установка русификаторов и модов прямо на телефоне" : "Apply translation & mod patches on Android") + '</p></div></div>' +
          '<select id="patchGameSelect" class="select-3d">' + gameOpts + '</select>' +
          '<button type="button" class="btn-3d btn-3d-primary btn-block" data-action="apply-rom-patch">' + ico("upload") + '<span>' + (isRu() ? "Выбрать патч (.IPS/.BPS/.UPS) и применить" : "Select Patch & Apply") + '</span></button>' +
        '</div>' +

        '<div class="glass-card" style="display:flex;flex-direction:column;gap:10px;">' +
          '<div class="section-title-cluster"><div class="section-icon-badge">' + ico("disc") + '</div><div><h3 class="section-title">' + (isRu() ? "Генератор многодисковых .M3U" : "Multi-Disc .M3U Generator") + '</h3><p class="section-subtitle">PS1, Saturn, Dreamcast, Sega CD, 3DO, DOS</p></div></div>' +
          '<input type="text" id="m3uTitleInput" class="input-well-3d" value="' + esc(state.m3uTitle) + '" placeholder="Название плейлиста">' +
          '<textarea id="m3uPathsArea" class="textarea-well-3d">' + esc(state.m3uDiscs.join("\n")) + '</textarea>' +
          '<button type="button" class="btn-3d btn-3d-emerald btn-block" data-action="create-m3u">' + ico("plus") + '<span>' + (isRu() ? "Создать плейлист .M3U" : "Generate .M3U Playlist") + '</span></button>' +
        '</div>' +

        '<div class="glass-card" style="display:flex;flex-direction:column;gap:10px;">' +
          '<div class="section-title-cluster"><div class="section-icon-badge">' + ico("hash") + '</div><div><h3 class="section-title">' + (isRu() ? "Верификатор CRC32 / MD5 / SHA-1" : "CRC32 / MD5 / SHA-1 Verifier") + '</h3><p class="section-subtitle">No-Intro & Redump Integrity Check</p></div></div>' +
          '<select id="hashGameSelect" class="select-3d">' + gameOpts + '</select>' +
          '<button type="button" class="btn-3d btn-3d-secondary btn-block" data-action="compute-rom-hash">' + ico("refresh") + '<span>' + (isRu() ? "Вычислить контрольные суммы" : "Compute Hashes") + '</span></button>' +
          '<div class="glass-card" style="background:var(--bg-input);padding:10px;font-size:0.72rem;" class="mono-code">' +
            '<div class="mono-code">CRC32: <strong>' + esc(state.romHashResult.crc32) + '</strong></div>' +
            '<div class="mono-code" style="word-break:break-all;margin-top:4px;">MD5: ' + esc(state.romHashResult.md5) + '</div>' +
            '<div class="mono-code" style="word-break:break-all;margin-top:4px;">SHA-1: ' + esc(state.romHashResult.sha1) + '</div>' +
          '</div>' +
        '</div>' +
      '</div>'
    );
  }

  function renderCommunity() {
    const guides = {
      PS1: isRu() ? "SwanStation для 3D апскейла 2x-4x + PGXP коррекции геометрии; PCSX ReARMed для бюджетных ARM чипов. BIOS: system/scph5501.bin." : "Use SwanStation for 2x-4x upscale + PGXP geometry; PCSX ReARMed for max speed.",
      PSP: isRu() ? "Ядро PPSSPP поддерживает Vulkan и GLES3, 2x-4x разрешение 1080p и 60 FPS патчи без внешнего BIOS." : "PPSSPP runs Vulkan/GLES3 at 2x-4x 1080p native speed with zero external BIOS needed.",
      SNES: isRu() ? "Snes9x вшит в APK: включите Run-Ahead 1 кадр и шейдер CRT Royale для нулевой задержки ввода." : "Snes9x pre-bundled: enable 1-frame Run-Ahead + CRT Royale shader.",
      Dreamcast: isRu() ? "Flycast поддерживает образы .CHD и широкоэкранный хак 16:9. BIOS: system/dc/dc_boot.bin." : "Flycast supports compressed .CHD + widescreen 16:9. BIOS: system/dc/dc_boot.bin."
    };

    return (
      '<div class="cores-grid">' +
        '<div class="glass-card" style="display:flex;flex-direction:column;gap:10px;">' +
          '<div class="section-title-cluster"><div class="section-icon-badge">' + ico("trophy") + '</div><div><h3 class="section-title">RetroAchievements</h3><p class="section-subtitle">' + (isRu() ? "Трофеи классических консолей онлайн" : "Global retro trophy tracking") + '</p></div></div>' +
          '<input type="text" id="raUserInput" class="input-well-3d" value="' + esc(state.settings.raUsername) + '" placeholder="Логин RetroAchievements">' +
          '<input type="password" class="input-well-3d" value="••••••••••••" placeholder="API Token / Пароль">' +
          '<div class="toggle-row-3d">' +
            '<div class="toggle-meta"><div class="toggle-title">Hardcore Mode</div><div class="toggle-desc">' + (isRu() ? "Удвоенный рейтинг без перемотки и читов" : "Competitive hardcore verification") + '</div></div>' +
            '<button type="button" role="switch" aria-checked="' + Boolean(state.settings.raHardcore) + '" class="switch-3d" data-toggle-setting="raHardcore"><span class="switch-track"><span class="switch-thumb"></span></span></button>' +
          '</div>' +
          '<button type="button" class="btn-3d btn-3d-primary btn-block" data-action="save-ra-login">' + ico("check") + '<span>' + (isRu() ? "Сохранить профиль достижений" : "Connect Profile") + '</span></button>' +
        '</div>' +

        '<div class="glass-card" style="display:flex;flex-direction:column;gap:10px;">' +
          '<div class="section-title-cluster"><div class="section-icon-badge">' + ico("wifi") + '</div><div><h3 class="section-title">' + (isRu() ? "Локальный Wi-Fi Netplay" : "Local Wi-Fi Netplay") + '</h3><p class="section-subtitle">' + (isRu() ? "Кооператив на 2-4 игроков (Порт 55435)" : "Rollback multiplayer over Wi-Fi (Port 55435)") + '</p></div></div>' +
          '<div class="seg-group-3d">' +
            '<button type="button" class="seg-btn-3d' + (state.settings.netplayMode === "host" ? " active" : "") + '" data-netplay-mode="host"><span>' + (isRu() ? "Создать комнату (Host)" : "Host Room") + '</span></button>' +
            '<button type="button" class="seg-btn-3d' + (state.settings.netplayMode === "join" ? " active" : "") + '" data-netplay-mode="join"><span>' + (isRu() ? "Подключиться (Join)" : "Join Room") + '</span></button>' +
          '</div>' +
          '<input type="text" class="input-well-3d mono-code" value="192.168.1.100:' + esc(state.settings.netplayPort) + '">' +
          '<div class="toggle-row-3d">' +
            '<div class="toggle-meta"><div class="toggle-title">Relay Server & Spectator</div><div class="toggle-desc">' + (isRu() ? "Обход NAT и режим зрителя" : "NAT traversal & spectator mode") + '</div></div>' +
            '<button type="button" role="switch" aria-checked="' + Boolean(state.settings.netplayRelay) + '" class="switch-3d" data-toggle-setting="netplayRelay"><span class="switch-track"><span class="switch-thumb"></span></span></button>' +
          '</div>' +
          '<button type="button" class="btn-3d btn-3d-emerald btn-block" data-action="start-netplay">' + ico("wifi") + '<span>' + (state.settings.netplayMode === "host" ? (isRu() ? "Запустить сервер 55435" : "Start Host 55435") : (isRu() ? "Подключиться к комнате" : "Connect")) + '</span></button>' +
        '</div>' +

        '<div class="glass-card" style="display:flex;flex-direction:column;gap:10px;">' +
          '<div class="section-title-cluster"><div class="section-icon-badge">' + ico("bolt") + '</div><div><h3 class="section-title">' + (isRu() ? "Тактический офлайн-гид платформ" : "Offline Tactical Emulation Guide") + '</h3><p class="section-subtitle">' + (isRu() ? "Лучшие ядра, BIOS и настройки FPS" : "Optimal cores, BIOS & FPS recipes") + '</p></div></div>' +
          '<div class="chips-scroll-row">' +
            Object.keys(guides).map(function (gp) {
              return '<button type="button" class="chip-3d' + (state.guidePlatform === gp ? " active" : "") + '" data-guide-plat="' + gp + '"><span>' + gp + '</span></button>';
            }).join("") +
          '</div>' +
          '<div class="glass-card" style="background:var(--bg-input);font-size:0.81rem;color:var(--text-secondary);">' + esc(guides[state.guidePlatform]) + '</div>' +
        '</div>' +
      '</div>'
    );
  }

  function renderSettings() {
    return (
      '<div style="display:flex;flex-direction:column;gap:12px;">' +
        '<div class="glass-card">' +
          '<h3 class="section-title" style="margin-bottom:10px;">' + (isRu() ? "9 Динамических тем STORM AURORA FLUID" : "9 Dynamic STORM AURORA FLUID Themes") + '</h3>' +
          '<div class="swatches-grid-themes">' +
            THEMES.map(function (t) {
              const act = state.settings.theme === t.id ? " active" : "";
              return (
                '<button type="button" class="theme-swatch-btn' + act + '" data-set-theme="' + t.id + '" style="background:linear-gradient(135deg, ' + t.bg + ' 0%, ' + t.surface + ' 100%);">' +
                  '<span style="font-size:0.76rem;font-weight:800;">' + t.label + '</span>' +
                  (act ? ico("check") : "") +
                '</button>'
              );
            }).join("") +
          '</div>' +
        '</div>' +

        '<div class="glass-card">' +
          '<h3 class="section-title" style="margin-bottom:10px;">' + (isRu() ? "13 Неоновых акцентов" : "13 Volumetric Neon Accents") + '</h3>' +
          '<div class="accents-grid">' +
            ACCENTS.map(function (a) {
              const act = state.settings.accent === a.id ? " active" : "";
              return (
                '<button type="button" class="accent-pill-btn' + act + '" data-set-accent="' + a.id + '">' +
                  '<span class="accent-orb" style="background:' + a.hex + ';box-shadow:0 0 10px ' + a.hex + ';"></span>' +
                  '<span>' + a.id + '</span>' +
                '</button>'
              );
            }).join("") +
          '</div>' +
        '</div>' +

        '<div class="two-col-studio">' +
          '<div class="glass-card">' +
            '<h3 class="section-title" style="margin-bottom:10px;">' + (isRu() ? "Язык интерфейса (12 локализаций)" : "Interface Language") + '</h3>' +
            '<div class="accents-grid">' +
              LANGUAGES.map(function (l) {
                const act = state.settings.language === l.code ? " active" : "";
                return '<button type="button" class="chip-3d' + act + '" data-set-lang="' + l.code + '"><span>' + esc(l.label) + '</span></button>';
              }).join("") +
            '</div>' +

            '<h4 class="section-title" style="margin:14px 0 8px;font-size:0.92rem;">' + (isRu() ? "Шрифтовая гарнитура (10 шрифтов) и Масштаб UI" : "Typography (10 Fonts) & UI Scale") + '</h4>' +
            '<select id="fontFamilySelect" class="select-3d" style="margin-bottom:10px;">' +
              FONTS.map(function (f) {
                return '<option value="' + f + '"' + (state.settings.font === f ? " selected" : "") + '>' + f + '</option>';
              }).join("") +
            '</select>' +
            '<div style="display:flex;justify-content:space-between;"><span class="toggle-title">' + (isRu() ? "Масштаб интерфейса (85% - 125%)" : "UI Scale (85% - 125%)") + '</span><span class="badge-pill badge-accent tabular">' + state.settings.uiScale + '%</span></div>' +
            '<input type="range" id="uiScaleSlider" class="range-3d" min="85" max="125" step="5" value="' + state.settings.uiScale + '">' +
          '</div>' +

          '<div class="glass-card" style="display:flex;flex-direction:column;gap:10px;">' +
            '<h3 class="section-title">' + (isRu() ? "Память Android, Game Booster и Обновления" : "Storage Root, Game Booster & Updates") + '</h3>' +
            '<div class="toggle-row-3d">' +
              '<div class="toggle-meta"><div class="toggle-title mono-code">/storage/emulated/0/STORM RETROARCH</div><div class="toggle-desc">' + (state.storageGranted ? (isRu() ? "Полный доступ MANAGE_EXTERNAL_STORAGE активен" : "Full storage access granted") : (isRu() ? "Требуется разрешение на доступ к памяти" : "Storage permission needed")) + '</div></div>' +
              '<button type="button" class="btn-3d btn-3d-secondary btn-3d-sm" data-action="request-storage">' + ico("folder") + '<span>' + (isRu() ? "Права" : "Grant") + '</span></button>' +
            '</div>' +

            '<div class="toggle-row-3d">' +
              '<div class="toggle-meta"><div class="toggle-title">Samsung Game Booster & Android Game Mode</div><div class="toggle-desc">' + (isRu() ? "Активирован игровой профиль Performance 120 Гц" : "Uninterruptible Performance Game Mode active") + '</div></div>' +
              '<span class="badge-pill badge-emerald">ACTIVE</span>' +
            '</div>' +

            '<div class="toggle-row-3d">' +
              '<div class="toggle-meta"><div class="toggle-title">' + (isRu() ? "Автопроверка обновлений при запуске" : "Auto-Update Check") + '</div><div class="toggle-desc">' + esc(state.updateInfo.statusMessage) + '</div></div>' +
              '<button type="button" role="switch" aria-checked="' + Boolean(state.settings.autoUpdateCheck) + '" class="switch-3d" data-toggle-setting="autoUpdateCheck"><span class="switch-track"><span class="switch-thumb"></span></span></button>' +
            '</div>' +

            '<div class="toolbar-row">' +
              '<button type="button" class="btn-3d btn-3d-primary" style="flex:1;" data-action="check-updates">' + ico("refresh") + '<span>' + (isRu() ? "Проверить обновления (2.6.1)" : "Check Updates (2.6.1)") + '</span></button>' +
              (state.updateInfo.updateAvailable ? (
                '<button type="button" class="btn-3d btn-3d-emerald" data-action="install-update">' + ico("download") + '<span>' + (isRu() ? "Обновить" : "Update") + '</span></button>'
              ) : "") +
            '</div>' +
          '</div>' +
        '</div>' +
      '</div>'
    );
  }

  function renderViewport() {
    const vp = document.getElementById("studioViewport");
    if (!vp) return;
    disconnectChunkObservers();

    const map = {
      dashboard: renderDashboard,
      library: renderLibrary,
      cores: renderCores,
      cheats: renderCheats,
      graphics: renderGraphics,
      audio: renderAudio,
      controls: renderControls,
      bios: renderBios,
      saves: renderSaves,
      romtools: renderRomTools,
      community: renderCommunity,
      settings: renderSettings
    };
    const fn = map[state.activeTab] || renderDashboard;

    const dir = state.pendingViewDirection;
    state.pendingViewDirection = null;
    vp.classList.remove("view-enter-up", "view-enter-down");
    vp.innerHTML = fn();

    if (dir === "up" || dir === "down") {
      void vp.offsetWidth;
      vp.classList.add(dir === "up" ? "view-enter-up" : "view-enter-down");
    }

    if (state.activeTab === "library") updateLibraryResults();
    else if (state.activeTab === "cores") updateCoresResults();
    else if (state.activeTab === "cheats") updateCheatsResults();
  }

  function renderShellAndView() {
    applyVisualTokens();
    renderNavigationChrome();
    renderViewport();
  }

  /* ------------------------------------------------------------------------
     11. MODALS: STUDIO 12-MODULE BENTO HUB, GAME INSPECTOR, ROULETTE, OMNIBOX
     ------------------------------------------------------------------------ */
  function openStudioHubModal() {
    const modal = document.getElementById("studioHubModal");
    const box = document.getElementById("studioHubContent");
    if (!modal || !box) return;
    box.innerHTML =
      '<div class="section-head">' +
        '<div><h3 class="section-title" id="studioHubTitle">' + (isRu() ? "Все 12 модулей STORM RETROARCH 2.6.1" : "All 12 STORM Studio Modules") + '</h3><p class="section-subtitle">' + (isRu() ? "Выберите нужный раздел консоли в одно касание" : "Jump to any console subsystem in 1 tap") + '</p></div>' +
        '<button type="button" class="icon-btn-3d" data-action="close-modals" aria-label="Close">' + ico("close") + '</button>' +
      '</div>' +
      '<div class="quick-bento-grid">' +
        STUDIO_MODULES.map(function (m, idx) {
          return (
            '<button type="button" class="bento-tool-tile stagger-card" style="--stagger-i:' + Math.min(idx, 8) + ';" data-nav="' + m.id + '">' +
              '<div class="bento-tile-head">' + ico(m.icon) + '<span>' + esc(tr(m.key)) + '</span></div>' +
              '<div class="bento-tile-desc">' + esc(isRu() ? m.descRu : m.descEn) + '</div>' +
            '</button>'
          );
        }).join("") +
      '</div>';
    modal.hidden = false;
  }

  function openGameInspector(gameId) {
    const g = state.games.find(function (x) { return x.id === gameId; }) || state.games[0];
    if (!g) return;
    state.inspectorGameId = g.id;
    const modal = document.getElementById("gameInspectorModal");
    const box = document.getElementById("gameInspectorContent");
    const validCores = coresForPlatform(g.platformKey, g.platform);
    const curStatus = g.completionStatus || "Не начато";
    const curRating = Number(g.starRating || 0);
    const playLabel = formatPlaytime(g.totalPlaySeconds || (g.playtimeMin || 0) * 60);

    box.innerHTML =
      '<div class="section-head">' +
        '<div style="display:flex;gap:12px;align-items:center;">' +
          '<img src="' + esc(g.cover) + '" alt="" style="width:62px;height:84px;border-radius:10px;object-fit:cover;">' +
          '<div>' +
            '<h3 class="section-title" id="inspectorGameTitle">' + esc(g.title) + '</h3>' +
            '<p class="section-subtitle">' + esc(g.platform) + ' | ' + esc(g.region) + (g.sizeMb ? ' | ' + esc(g.sizeMb) : '') + ' | ' + esc(playLabel) + '</p>' +
          '</div>' +
        '</div>' +
        '<button type="button" class="icon-btn-3d" data-action="close-modals" aria-label="Close">' + ico("close") + '</button>' +
      '</div>' +

      '<div class="action-trio-grid">' +
        '<button type="button" class="btn-3d btn-3d-primary btn-trio-hero" data-action="launch-game" data-game="' + esc(g.id) + '" data-slot="-1">' + ico("play") + '<span>' + esc(tr("play")) + '</span></button>' +
        '<button type="button" class="btn-3d btn-3d-emerald" data-action="jump-game-cheats" data-game="' + esc(g.id) + '">' + ico("shield") + '<span>' + (isRu() ? "Читы игры" : "Game Cheats") + '</span></button>' +
        '<button type="button" class="btn-3d btn-3d-secondary" data-action="open-core-options" data-core="' + esc(g.coreId) + '">' + ico("sliders") + '<span>' + (isRu() ? "Разрешение / ЦП" : "Core Opts") + '</span></button>' +
      '</div>' +

      '<div class="glass-card inspector-meta-card">' +
        '<div class="inspector-field">' +
          '<div class="toggle-title" style="margin-bottom:5px;">' + (isRu() ? "Статус прохождения" : "Completion Status") + '</div>' +
          '<select id="inspectorStatusSelect" class="select-3d" data-game="' + esc(g.id) + '">' +
            COMPLETION_STATUSES.map(function (st) {
              return '<option value="' + esc(st.id) + '"' + (curStatus === st.id ? " selected" : "") + '>' + esc(statusLabel(st)) + '</option>';
            }).join("") +
          '</select>' +
        '</div>' +

        '<div class="inspector-field">' +
          '<div class="inspector-label-row">' +
            '<div class="toggle-title">' + (isRu() ? "Оценка игры" : "Star Rating") + '</div>' +
            '<span class="badge-pill tabular" id="inspectorRatingBadge">' + (curRating > 0 ? curRating + " / 5" : (isRu() ? "Без оценки" : "Unrated")) + '</span>' +
          '</div>' +
          '<div class="star-rating-row" id="inspectorStarRow">' +
            [1, 2, 3, 4, 5].map(function (n) {
              return '<button type="button" class="star-btn' + (n <= curRating ? " filled" : "") + '" data-action="set-game-rating" data-game="' + esc(g.id) + '" data-rating="' + n + '" aria-label="Rating ' + n + '">' + ico("star") + '</button>';
            }).join("") +
          '</div>' +
        '</div>' +

        '<div class="inspector-field">' +
          '<div class="toggle-title" style="margin-bottom:5px;">' + (isRu() ? "Коллекция / тег" : "Collection Tag") + '</div>' +
          '<input type="text" id="inspectorCollectionInput" class="input-well-3d" data-game="' + esc(g.id) + '" value="' + esc(g.customCollection || "") + '" placeholder="' + (isRu() ? "Например: Классика JRPG, Пройти в дороге..." : "e.g. Top JRPGs, Travel Backlog...") + '">' +
        '</div>' +

        '<div class="inspector-field">' +
          '<div class="toggle-title" style="margin-bottom:5px;">' + (isRu() ? "Личные заметки к игре" : "Personal Notes") + '</div>' +
          '<textarea id="inspectorNotesArea" class="textarea-well-3d notes-well-3d" rows="3" data-game="' + esc(g.id) + '" placeholder="' + (isRu() ? "Пароли, советы по боссам, порядок прохождения..." : "Boss notes, passwords, walkthrough hints...") + '">' + esc(g.userNotes || "") + '</textarea>' +
          '<div class="inspector-save-row">' +
            '<button type="button" class="btn-3d btn-3d-primary inspector-save-btn" data-action="save-game-notes" data-game="' + esc(g.id) + '">' + ico("save") + '<span>' + (isRu() ? "Сохранить заметку и статус" : "Save Notes & Status") + '</span></button>' +
          '</div>' +
        '</div>' +
      '</div>' +

      '<div style="margin-bottom:12px;">' +
        '<div class="toggle-title" style="margin-bottom:5px;">' + (isRu() ? "Предпочитаемое ядро для этой игры (" + g.platform + ")" : "Preferred Core for Game") + '</div>' +
        '<select id="inspectorCoreSelect" class="select-3d" data-game="' + esc(g.id) + '">' +
          validCores.map(function (c) {
            return '<option value="' + esc(c.id) + '"' + (c.id === g.coreId ? " selected" : "") + '>' + esc(c.displayName) + (c.bundled ? " [APK]" : "") + '</option>';
          }).join("") +
        '</select>' +
      '</div>' +

      '<div class="toggle-title" style="margin-bottom:7px;">' + (isRu() ? "Быстрые слоты сохранений (0 - 9)" : "Quick Save State Slots (0 - 9)") + '</div>' +
      '<div class="slots-grid-10">' +
        [0, 1, 2, 3, 4, 5, 6, 7, 8, 9].map(function (s) {
          return (
            '<button type="button" class="slot-tile-3d" data-action="launch-game" data-game="' + esc(g.id) + '" data-slot="' + s + '">' +
              '<span class="badge-pill badge-accent tabular">Слот #' + s + '</span>' +
              '<span style="font-size:0.72rem;color:var(--text-secondary);">' + (s <= 1 ? (isRu() ? "Готов к старту" : "Saved State") : (isRu() ? "Запустить слот" : "Boot Slot")) + '</span>' +
            '</button>'
          );
        }).join("") +
      '</div>';

    modal.hidden = false;
  }

  function openRandomRouletteModal() {
    const pool = state.games && state.games.length ? state.games : [];
    if (!pool.length) {
      showToast(isRu() ? "Добавьте игры в библиотеку для рулетки" : "Add games to your library first");
      return;
    }
    if (state.rouletteSpinTimer) {
      clearTimeout(state.rouletteSpinTimer);
      state.rouletteSpinTimer = null;
    }

    const modal = document.getElementById("randomRouletteModal");
    const box = document.getElementById("randomRouletteContent");
    if (!modal || !box) return;

    const chosen = pool[Math.floor(Math.random() * pool.length)];
    state.rouletteGameId = chosen.id;

    const firstSample = pool[Math.floor(Math.random() * pool.length)];
    box.innerHTML =
      '<div class="section-head">' +
        '<div class="section-title-cluster">' +
          '<div class="section-icon-badge">' + ico("shuffle") + '</div>' +
          '<div>' +
            '<h3 class="section-title" id="rouletteModalTitle">' + (isRu() ? "Рулетка: Случайная игра" : "Random Game Roulette") + '</h3>' +
            '<p class="section-subtitle">' + (isRu() ? "Подбор случайного релиза из коллекции (" + pool.length + ")" : "Picking a surprise title from " + pool.length + " games") + '</p>' +
          '</div>' +
        '</div>' +
        '<button type="button" class="icon-btn-3d" data-action="close-modals" aria-label="Close">' + ico("close") + '</button>' +
      '</div>' +
      '<div class="roulette-stage">' +
        '<div class="roulette-reel-card spinning" id="rouletteReelCard">' +
          '<div class="roulette-cover-box"><img id="rouletteCoverImg" src="' + esc(firstSample.cover) + '" alt=""></div>' +
          '<div style="min-width:0;flex:1;">' +
            '<div class="badge-pill badge-accent" id="roulettePlatPill">' + esc(firstSample.platform) + '</div>' +
            '<div class="poster-title" id="rouletteGameTitle" style="margin-top:6px;font-size:1.02rem;">' + esc(firstSample.title) + '</div>' +
            '<div class="poster-sub" id="rouletteGameSub">' + esc(firstSample.region) + ' | ' + esc(firstSample.coreId) + '</div>' +
          '</div>' +
        '</div>' +
        '<div class="action-trio-grid" style="margin-bottom:0;">' +
          '<button type="button" class="btn-3d btn-3d-emerald btn-trio-hero" id="roulettePlayBtn" data-action="launch-game" data-game="' + esc(chosen.id) + '" data-slot="-1">' + ico("play") + '<span>' + (isRu() ? "Играть сейчас" : "Play Now") + '</span></button>' +
          '<button type="button" class="btn-3d btn-3d-secondary" data-action="inspect-game" data-game="' + esc(chosen.id) + '" id="rouletteInspectBtn">' + ico("sliders") + '<span>' + (isRu() ? "Карточка" : "Inspect") + '</span></button>' +
          '<button type="button" class="btn-3d btn-3d-primary" data-action="reroll-roulette">' + ico("shuffle") + '<span>' + (isRu() ? "Другая игра" : "Reroll") + '</span></button>' +
        '</div>' +
      '</div>';

    modal.hidden = false;

    const delays = [55, 65, 80, 100, 130, 170, 210];
    let step = 0;

    function tickSpin() {
      const card = document.getElementById("rouletteReelCard");
      if (!card || modal.hidden) return;
      if (step < delays.length) {
        const sample = pool.length > 1
          ? pool[Math.floor(Math.random() * pool.length)]
          : chosen;
        const img = document.getElementById("rouletteCoverImg");
        const ttl = document.getElementById("rouletteGameTitle");
        const sub = document.getElementById("rouletteGameSub");
        const pill = document.getElementById("roulettePlatPill");
        if (img) img.src = sample.cover;
        if (ttl) ttl.textContent = sample.title;
        if (sub) sub.textContent = sample.region + " | " + sample.coreId;
        if (pill) pill.textContent = sample.platform;
        const wait = delays[step++];
        state.rouletteSpinTimer = setTimeout(tickSpin, wait);
      } else {
        const img = document.getElementById("rouletteCoverImg");
        const ttl = document.getElementById("rouletteGameTitle");
        const sub = document.getElementById("rouletteGameSub");
        const pill = document.getElementById("roulettePlatPill");
        const playBtn = document.getElementById("roulettePlayBtn");
        const inspBtn = document.getElementById("rouletteInspectBtn");
        if (img) img.src = chosen.cover;
        if (ttl) ttl.textContent = chosen.title;
        if (sub) sub.textContent = chosen.region + " | " + chosen.coreId;
        if (pill) pill.textContent = chosen.platform;
        if (playBtn) playBtn.setAttribute("data-game", chosen.id);
        if (inspBtn) inspBtn.setAttribute("data-game", chosen.id);
        card.classList.remove("spinning");
        card.classList.add("locked");
        haptic("success");
        state.rouletteSpinTimer = null;
      }
    }

    state.rouletteSpinTimer = setTimeout(tickSpin, 50);
  }

  function fuzzyScore(query, targetText) {
    if (!query) return 1;
    const q = query.toLowerCase().trim();
    const t = String(targetText || "").toLowerCase();
    if (!q) return 1;
    const exactIdx = t.indexOf(q);
    if (exactIdx === 0) return 1000 - t.length;
    if (exactIdx > 0) return 600 - exactIdx;
    let qi = 0;
    let score = 0;
    for (let ti = 0; ti < t.length && qi < q.length; ti++) {
      if (t[ti] === q[qi]) {
        score += 12;
        qi++;
      }
    }
    return qi === q.length ? score : 0;
  }

  function buildOmniboxMatches(query) {
    const results = [];
    const ru = isRu();

    const actions = [
      { kind: "action", icon: "shuffle", title: ru ? "Случайная игра (Рулетка)" : "Random Game Roulette", meta: ru ? "Быстрый запуск случайной игры" : "Pick & boot a random game", execType: "roulette" },
      { kind: "action", icon: "folder", title: ru ? "Сканировать папку с ROM" : "Scan ROM Folder", meta: state.storagePath, execType: "action", action: "quick-scan-storage" },
      { kind: "action", icon: "cpu", title: ru ? "Автосканер BIOS (MD5)" : "Auto-Scan BIOS (MD5)", meta: ru ? "Проверка системных прошивок" : "Verify system firmware hashes", execType: "action", action: "scan-bios-md5" },
      { kind: "action", icon: "archive", title: ru ? "Экспорт сохранений в ZIP" : "Export Saves Backup ZIP", meta: ru ? "Портативный перенос на ПК" : "Portable PC save transfer", execType: "action", action: "export-saves-zip" },
      { kind: "action", icon: "refresh", title: ru ? "Проверить обновления (2.6.1)" : "Check Updates (2.6.1)", meta: "STORM RETROARCH", execType: "action", action: "check-updates" },
      { kind: "action", icon: "grid", title: ru ? "Все 12 модулей Студии" : "All 12 Studio Modules", meta: ru ? "Быстрый переход по разделам" : "Open 12-module launcher", execType: "hub" }
    ];

    actions.forEach(function (a) {
      const sc = fuzzyScore(query, a.title + " " + a.meta);
      if (sc > 0) results.push(Object.assign({ score: sc + 20 }, a));
    });

    state.games.forEach(function (g) {
      const sc = fuzzyScore(query, g.title + " " + g.platform + " " + g.region + " " + (g.customCollection || ""));
      if (sc > 0) {
        results.push({
          kind: "game",
          icon: "gamepad",
          title: g.title,
          meta: g.platform + " | " + g.region + " | " + g.coreId,
          execType: "game",
          gameId: g.id,
          score: sc + 40
        });
      }
    });

    state.cores.forEach(function (c) {
      const sc = fuzzyScore(query, c.displayName + " " + c.id + " " + c.platform);
      if (sc > 0) {
        results.push({
          kind: "core",
          icon: "cpu",
          title: c.displayName,
          meta: c.platform + (c.installed || c.bundled ? (ru ? " | Готово" : " | Ready") : (ru ? " | Можно скачать" : " | Downloadable")),
          execType: "core",
          coreId: c.id,
          score: sc
        });
      }
    });

    STUDIO_MODULES.forEach(function (m) {
      const lbl = tr(m.key);
      const sc = fuzzyScore(query, lbl + " " + m.id + " " + (ru ? m.descRu : m.descEn));
      if (sc > 0) {
        results.push({
          kind: "module",
          icon: m.icon,
          title: lbl,
          meta: ru ? m.descRu : m.descEn,
          execType: "nav",
          tab: m.id,
          score: sc + 10
        });
      }
    });

    THEMES.forEach(function (th) {
      const sc = fuzzyScore(query, (ru ? "Тема " : "Theme ") + th.label + " " + th.id);
      if (sc > 0) {
        results.push({
          kind: "theme",
          icon: "palette",
          title: (ru ? "Тема: " : "Theme: ") + th.label,
          meta: th.id,
          execType: "theme",
          themeId: th.id,
          score: sc - 5
        });
      }
    });

    results.sort(function (a, b) { return b.score - a.score; });
    return results.slice(0, 28);
  }

  function renderOmniboxResults() {
    const mount = document.getElementById("omniboxResults");
    if (!mount) return;
    const matches = buildOmniboxMatches(state.omniboxQuery);
    state.omniboxResults = matches;
    if (state.omniboxSelectedIdx >= matches.length) state.omniboxSelectedIdx = Math.max(0, matches.length - 1);

    if (!matches.length) {
      mount.innerHTML = '<div class="section-subtitle" style="padding:12px 4px;">' + (isRu() ? "Ничего не найдено по запросу." : "No matching games, cores, or actions.") + '</div>';
      return;
    }

    const kindNames = isRu()
      ? { game: "Игры", core: "Ядра", action: "Действия Студии", module: "Разделы", theme: "Темы" }
      : { game: "Games", core: "Cores", action: "Studio Actions", module: "Modules", theme: "Themes" };

    mount.innerHTML = matches.map(function (item, idx) {
      const sel = idx === state.omniboxSelectedIdx ? " selected" : "";
      return (
        '<button type="button" class="omnibox-item' + sel + '" data-action="omnibox-exec" data-idx="' + idx + '">' +
          '<div class="omnibox-item-left">' +
            ico(item.icon) +
            '<div style="min-width:0;">' +
              '<div class="omnibox-item-title">' + esc(item.title) + '</div>' +
              '<div class="omnibox-item-meta">' + esc(item.meta) + '</div>' +
            '</div>' +
          '</div>' +
          '<span class="badge-pill badge-accent">' + esc(kindNames[item.kind] || item.kind) + '</span>' +
        '</button>'
      );
    }).join("");
  }

  function executeOmniboxItem(idx) {
    const item = state.omniboxResults[idx];
    if (!item) return;
    closeAllModals();
    if (item.execType === "roulette") {
      openRandomRouletteModal();
    } else if (item.execType === "hub") {
      openStudioHubModal();
    } else if (item.execType === "game") {
      openGameInspector(item.gameId);
    } else if (item.execType === "core") {
      navigateToTab("cores");
      state.coresQuery = item.coreId;
      renderShellAndView();
    } else if (item.execType === "nav") {
      navigateToTab(item.tab);
      renderShellAndView();
    } else if (item.execType === "theme") {
      saveSettingsPatch({ theme: item.themeId });
      renderShellAndView();
      showToast((isRu() ? "Применена тема: " : "Theme applied: ") + item.themeId);
    } else if (item.execType === "action") {
      const fake = document.querySelector('[data-action="' + item.action + '"]');
      if (fake) fake.click();
      else if (item.action === "quick-scan-storage" && hasBridge && window.StormBridge.scanPath) {
        window.StormBridge.scanPath(state.storagePath);
      } else if (item.action === "scan-bios-md5" && hasBridge && window.StormBridge.scanBiosFolder) {
        window.StormBridge.scanBiosFolder();
      } else if (item.action === "export-saves-zip" && hasBridge && window.StormBridge.exportSavesBackupZip) {
        window.StormBridge.exportSavesBackupZip();
      } else if (item.action === "check-updates") {
        if (hasBridge && window.StormBridge.checkForUpdates) window.StormBridge.checkForUpdates();
        else showToast(isRu() ? "У вас установлена актуальная версия (2.6.1)" : "STORM RETROARCH 2.6.1 is up to date");
      }
    }
  }

  function openCommandOmniboxModal() {
    const modal = document.getElementById("commandOmniboxModal");
    const box = document.getElementById("commandOmniboxContent");
    if (!modal || !box) return;
    state.omniboxQuery = "";
    state.omniboxSelectedIdx = 0;

    box.innerHTML =
      '<div class="section-head" style="margin-bottom:10px;">' +
        '<div class="section-title-cluster">' +
          '<div class="section-icon-badge">' + ico("command") + '</div>' +
          '<div>' +
            '<h3 class="section-title" id="omniboxModalTitle">' + (isRu() ? "Быстрый поиск и команды" : "Global Command Omnibox") + '</h3>' +
            '<p class="section-subtitle">' + (isRu() ? "Игры, 252 ядра, действия, темы и разделы" : "Instant fuzzy search across games, 252 cores, actions & themes") + '</p>' +
          '</div>' +
        '</div>' +
        '<button type="button" class="icon-btn-3d" data-action="close-modals" aria-label="Close">' + ico("close") + '</button>' +
      '</div>' +
      '<div class="input-well-wrap">' +
        '<span class="input-lead-ico">' + ico("search") + '</span>' +
        '<input type="search" id="omniboxInput" class="input-well-3d" value="" placeholder="' + (isRu() ? "Введите название игры, ядра, темы или команды..." : "Search games, cores, themes or commands...") + '">' +
      '</div>' +
      '<div id="omniboxResults" class="omnibox-results"></div>';

    modal.hidden = false;
    renderOmniboxResults();
    const inp = document.getElementById("omniboxInput");
    if (inp) setTimeout(function () { inp.focus(); }, 20);
  }

  function navigateToTab(nextTab) {
    if (!nextTab) return;
    const prevIdx = NAV_ORDER.indexOf(state.activeTab);
    const nextIdx = NAV_ORDER.indexOf(nextTab);
    if (prevIdx >= 0 && nextIdx >= 0 && prevIdx !== nextIdx) {
      state.pendingViewDirection = nextIdx > prevIdx ? "up" : "down";
    } else {
      state.pendingViewDirection = null;
    }
    state.prevTab = state.activeTab;
    state.activeTab = nextTab;
  }

  function openCoreOptionsModal(coreId) {
    state.coreModalId = coreId || "swanstation";
    if (hasBridge && typeof window.StormBridge.getCoreOptionsJson === "function") {
      try {
        const loaded = safeJsonParse(window.StormBridge.getCoreOptionsJson(state.coreModalId, state.inspectorGameId || ""), null);
        if (loaded && typeof loaded === "object") Object.assign(state.coreOptionsDraft, loaded);
      } catch (_) {}
    }
    const modal = document.getElementById("coreOptionsModal");
    const box = document.getElementById("coreOptionsContent");
    const d = state.coreOptionsDraft;

    box.innerHTML =
      '<div class="section-head">' +
        '<div><h3 class="section-title" id="coreOptionsTitle">' + (isRu() ? "Параметры ядра: " : "Core Options: ") + esc(state.coreModalId) + '</h3><p class="section-subtitle">' + (isRu() ? "Точная настройка графики, ЦП и CD-привода" : "Per-core hardware & enhancement tuning") + '</p></div>' +
        '<button type="button" class="icon-btn-3d" data-action="close-modals" aria-label="Close">' + ico("close") + '</button>' +
      '</div>' +
      '<div style="display:flex;flex-direction:column;gap:9px;">' +
        '<div><div class="toggle-title" style="margin-bottom:4px;">' + (isRu() ? "Внутреннее разрешение (1x - 8x)" : "Internal Resolution") + '</div><select id="coRes" class="select-3d">' + ["1x Native", "2x (720p HD)", "3x (1080p FHD)", "4x (1440p)", "8x (4K)"].map(function (x) { return '<option' + (d.resolution === x ? " selected" : "") + '>' + x + '</option>'; }).join("") + '</select></div>' +
        '<div><div class="toggle-title" style="margin-bottom:4px;">' + (isRu() ? "Разгон ЦП эмулятора (100% - 200%)" : "CPU Overclock") + '</div><select id="coCpu" class="select-3d">' + ["100% (Штатная)", "125%", "150%", "200% (Максимум FPS)"].map(function (x) { return '<option' + (d.cpuOverclock === x ? " selected" : "") + '>' + x + '</option>'; }).join("") + '</select></div>' +
        '<div><div class="toggle-title" style="margin-bottom:4px;">' + (isRu() ? "Пропуск кадров (Frame Skip)" : "Frame Skip") + '</div><select id="coSkip" class="select-3d">' + ["0 (60 FPS)", "1 кадр", "2 кадра", "Авто"].map(function (x) { return '<option' + (d.frameSkip === x ? " selected" : "") + '>' + x + '</option>'; }).join("") + '</select></div>' +
        '<div><div class="toggle-title" style="margin-bottom:4px;">' + (isRu() ? "Режим BIOS" : "BIOS Mode") + '</div><select id="coBios" class="select-3d">' + ["Оригинальный BIOS / Авто", "HLE Эмуляция (без файла)"].map(function (x) { return '<option' + (d.biosMode === x ? " selected" : "") + '>' + x + '</option>'; }).join("") + '</select></div>' +
        '<div class="toggle-row-3d"><div class="toggle-title">PGXP коррекция 3D геометрии</div><button type="button" role="switch" aria-checked="' + Boolean(d.pgxp) + '" class="switch-3d" data-co-bool="pgxp"><span class="switch-track"><span class="switch-thumb"></span></span></button></div>' +
        '<div class="toggle-row-3d"><div class="toggle-title">' + (isRu() ? "Широкоэкранный хак 16:9" : "Widescreen 16:9 Hack") + '</div><button type="button" role="switch" aria-checked="' + Boolean(d.widescreenHack) + '" class="switch-3d" data-co-bool="widescreenHack"><span class="switch-track"><span class="switch-thumb"></span></span></button></div>' +
        '<div class="toggle-row-3d"><div class="toggle-title">' + (isRu() ? "Быстрая загрузка CD (Fast CD-ROM)" : "Fast CD-ROM Speed") + '</div><button type="button" role="switch" aria-checked="' + Boolean(d.fastCdLoad) + '" class="switch-3d" data-co-bool="fastCdLoad"><span class="switch-track"><span class="switch-thumb"></span></span></button></div>' +
        '<button type="button" class="btn-3d btn-3d-primary btn-block" data-action="save-core-options">' + ico("save") + '<span>' + (isRu() ? "Сохранить опции ядра" : "Save Core Options") + '</span></button>' +
      '</div>';
    modal.hidden = false;
  }

  function closeAllModals() {
    if (state.rouletteSpinTimer) {
      clearTimeout(state.rouletteSpinTimer);
      state.rouletteSpinTimer = null;
    }
    ["studioHubModal", "gameInspectorModal", "coreOptionsModal", "randomRouletteModal", "commandOmniboxModal"].forEach(function (id) {
      const m = document.getElementById(id);
      if (m) m.hidden = true;
    });
  }

  window.handleAndroidBack = function () {
    const ids = ["commandOmniboxModal", "randomRouletteModal", "coreOptionsModal", "gameInspectorModal", "studioHubModal"];
    for (let i = 0; i < ids.length; i++) {
      const m = document.getElementById(ids[i]);
      if (m && !m.hidden) {
        closeAllModals();
        return true;
      }
    }
    return false;
  };

  /* ------------------------------------------------------------------------
     12. UNIFIED EVENT DELEGATION & BRIDGE ACTIONS
     ------------------------------------------------------------------------ */
  function runCheatSearch() {
    const q = state.cheatsQuery.trim();
    if (hasBridge && typeof window.StormBridge.searchCheatsCatalogJson === "function") {
      try {
        const res = safeJsonParse(window.StormBridge.searchCheatsCatalogJson(q, state.cheatsSystem === "all" ? "" : state.cheatsSystem, state.cheatsLibraryOnly, 40), []);
        if (Array.isArray(res) && res.length > 0) {
          state.cheatResults = res.map(function (r, idx) {
            return { id: r.id || r.chtFilePath || ("cht_" + idx), title: r.cleanTitle || r.gameName || r.title, system: r.platformFolder || r.system || "ROM", inLibrary: Boolean(r.isLibraryMatch), count: r.count || 6 };
          });
          return;
        }
      } catch (_) {}
    }
    state.cheatResults = PREVIEW_CHEAT_GAMES.filter(function (g) {
      if (state.cheatsLibraryOnly && !g.inLibrary) return false;
      if (state.cheatsSystem !== "all" && g.system !== state.cheatsSystem) return false;
      if (!q) return true;
      return g.title.toLowerCase().indexOf(q.toLowerCase()) >= 0;
    });
  }

  document.addEventListener("click", function (e) {
    const backdrop = e.target.classList && e.target.classList.contains("modal-backdrop") ? e.target : null;
    if (backdrop) {
      haptic("tap");
      closeAllModals();
      return;
    }

    const navBtn = e.target.closest("[data-nav]");
    if (navBtn) {
      haptic("tap");
      navigateToTab(navBtn.getAttribute("data-nav"));
      closeAllModals();
      renderShellAndView();
      return;
    }

    const libPlat = e.target.closest("[data-lib-plat]");
    if (libPlat) {
      haptic("tap");
      state.libPlatform = libPlat.getAttribute("data-lib-plat");
      state.libCoreFilter = "all";
      Array.prototype.forEach.call(document.querySelectorAll("[data-lib-plat]"), function (el) {
        el.classList.toggle("active", el.getAttribute("data-lib-plat") === state.libPlatform);
      });
      const coreSel = document.getElementById("libCoreSelect");
      if (coreSel) coreSel.value = "all";
      updateLibraryResults();
      return;
    }

    const libStatus = e.target.closest("[data-lib-status]");
    if (libStatus) {
      haptic("tap");
      state.libStatusFilter = libStatus.getAttribute("data-lib-status");
      Array.prototype.forEach.call(document.querySelectorAll("[data-lib-status]"), function (el) {
        el.classList.toggle("active", el.getAttribute("data-lib-status") === state.libStatusFilter);
      });
      updateLibraryResults();
      return;
    }

    const libAlpha = e.target.closest("[data-lib-alpha]");
    if (libAlpha) {
      haptic("tap");
      state.libAlphaFilter = libAlpha.getAttribute("data-lib-alpha");
      Array.prototype.forEach.call(document.querySelectorAll("[data-lib-alpha]"), function (el) {
        el.classList.toggle("active", el.getAttribute("data-lib-alpha") === state.libAlphaFilter);
      });
      updateLibraryResults();
      return;
    }

    const libView = e.target.closest("[data-lib-view]");
    if (libView) {
      haptic("tap");
      state.libViewMode = libView.getAttribute("data-lib-view");
      Array.prototype.forEach.call(document.querySelectorAll("[data-lib-view]"), function (el) {
        el.classList.toggle("active", el.getAttribute("data-lib-view") === state.libViewMode);
      });
      updateLibraryResults();
      return;
    }

    const coreFilter = e.target.closest("[data-core-filter]");
    if (coreFilter) {
      haptic("tap");
      state.coresFilter = coreFilter.getAttribute("data-core-filter");
      Array.prototype.forEach.call(document.querySelectorAll("[data-core-filter]"), function (el) {
        el.classList.toggle("active", el.getAttribute("data-core-filter") === state.coresFilter);
      });
      updateCoresResults();
      return;
    }

    const cheatScope = e.target.closest("[data-cheat-scope]");
    if (cheatScope) {
      haptic("tap");
      state.cheatsLibraryOnly = cheatScope.getAttribute("data-cheat-scope") === "lib";
      Array.prototype.forEach.call(document.querySelectorAll("[data-cheat-scope]"), function (el) {
        el.classList.toggle("active", (el.getAttribute("data-cheat-scope") === "lib") === state.cheatsLibraryOnly);
      });
      runCheatSearch();
      updateCheatsResults();
      return;
    }

    const ramBits = e.target.closest("[data-ram-bits]");
    if (ramBits) {
      haptic("tap");
      state.ramBits = ramBits.getAttribute("data-ram-bits");
      renderViewport();
      return;
    }

    const shaderBtn = e.target.closest("[data-shader]");
    if (shaderBtn) {
      haptic("tap");
      saveSettingsPatch({ shaderPreset: shaderBtn.getAttribute("data-shader") });
      showToast((isRu() ? "Активирован шейдер: " : "Shader active: ") + shaderBtn.getAttribute("data-shader"));
      renderViewport();
      return;
    }

    const runAheadBtn = e.target.closest("[data-runahead]");
    if (runAheadBtn) {
      haptic("tap");
      saveSettingsPatch({ runAheadFrames: runAheadBtn.getAttribute("data-runahead") });
      renderViewport();
      return;
    }

    const dspBtn = e.target.closest("[data-dsp-preset]");
    if (dspBtn) {
      haptic("tap");
      const bands = dspBtn.getAttribute("data-bands").split(",").map(Number);
      saveSettingsPatch({ dspPreset: dspBtn.getAttribute("data-dsp-preset"), eqBands: bands });
      renderViewport();
      return;
    }

    const audioMs = e.target.closest("[data-audio-ms]");
    if (audioMs) {
      haptic("tap");
      saveSettingsPatch({ audioLatencyMs: audioMs.getAttribute("data-audio-ms") });
      renderViewport();
      return;
    }

    const biosPlat = e.target.closest("[data-bios-plat]");
    if (biosPlat) {
      haptic("tap");
      state.biosPlatform = biosPlat.getAttribute("data-bios-plat");
      renderViewport();
      return;
    }

    const guidePlat = e.target.closest("[data-guide-plat]");
    if (guidePlat) {
      haptic("tap");
      state.guidePlatform = guidePlat.getAttribute("data-guide-plat");
      renderViewport();
      return;
    }

    const netMode = e.target.closest("[data-netplay-mode]");
    if (netMode) {
      haptic("tap");
      saveSettingsPatch({ netplayMode: netMode.getAttribute("data-netplay-mode") });
      renderViewport();
      return;
    }

    const themeBtn = e.target.closest("[data-set-theme]");
    if (themeBtn) {
      haptic("tap");
      saveSettingsPatch({ theme: themeBtn.getAttribute("data-set-theme") });
      renderShellAndView();
      return;
    }

    const accentBtn = e.target.closest("[data-set-accent]");
    if (accentBtn) {
      haptic("tap");
      saveSettingsPatch({ accent: accentBtn.getAttribute("data-set-accent") });
      renderShellAndView();
      return;
    }

    const langBtn = e.target.closest("[data-set-lang]");
    if (langBtn) {
      haptic("tap");
      saveSettingsPatch({ language: langBtn.getAttribute("data-set-lang") });
      renderShellAndView();
      return;
    }

    const toggleSetting = e.target.closest("[data-toggle-setting]");
    if (toggleSetting) {
      haptic("tap");
      const k = toggleSetting.getAttribute("data-toggle-setting");
      const patch = {};
      patch[k] = !state.settings[k];
      saveSettingsPatch(patch);
      renderViewport();
      return;
    }

    const coBool = e.target.closest("[data-co-bool]");
    if (coBool) {
      haptic("tap");
      const bk = coBool.getAttribute("data-co-bool");
      state.coreOptionsDraft[bk] = !state.coreOptionsDraft[bk];
      openCoreOptionsModal(state.coreModalId);
      return;
    }

    const actEl = e.target.closest("[data-action]");
    if (!actEl) return;
    haptic("tap");
    const act = actEl.getAttribute("data-action");

    if (act === "open-studio-hub") openStudioHubModal();
    else if (act === "close-modals") closeAllModals();
    else if (act === "quick-search") {
      openCommandOmniboxModal();
    } else if (act === "open-roulette" || act === "reroll-roulette") {
      openRandomRouletteModal();
    } else if (act === "omnibox-exec") {
      executeOmniboxItem(Number(actEl.getAttribute("data-idx") || 0));
    } else if (act === "set-game-rating") {
      const gid = actEl.getAttribute("data-game");
      const val = Number(actEl.getAttribute("data-rating") || 0);
      const gm = state.games.find(function (x) { return x.id === gid; });
      const nextVal = (gm && gm.starRating === val) ? 0 : val;
      persistGameMetadata(gid, { starRating: nextVal });
      Array.prototype.forEach.call(document.querySelectorAll("#inspectorStarRow .star-btn"), function (btn) {
        const r = Number(btn.getAttribute("data-rating") || 0);
        btn.classList.toggle("filled", r <= nextVal);
      });
      const rBadge = document.getElementById("inspectorRatingBadge");
      if (rBadge) rBadge.textContent = nextVal > 0 ? (nextVal + " / 5") : (isRu() ? "Без оценки" : "Unrated");
      if (state.activeTab === "library") updateLibraryResults();
      else if (state.activeTab === "dashboard") renderViewport();
      showToast((isRu() ? "Оценка игры: " : "Game rating: ") + nextVal + "/5");
    } else if (act === "save-game-notes") {
      const gid = actEl.getAttribute("data-game");
      const notesEl = document.getElementById("inspectorNotesArea");
      const colEl = document.getElementById("inspectorCollectionInput");
      const stEl = document.getElementById("inspectorStatusSelect");
      const patch = {
        userNotes: notesEl ? notesEl.value : "",
        customCollection: colEl ? colEl.value.trim() : "",
        completionStatus: stEl ? stEl.value : "Не начато"
      };
      persistGameMetadata(gid, patch);
      if (state.activeTab === "library") updateLibraryResults();
      else if (state.activeTab === "dashboard") renderViewport();
      showToast(isRu() ? "Заметки и статус игры сохранены" : "Game notes & status saved");
    } else if (act === "pick-rom-folder") {
      if (hasBridge && window.StormBridge.pickRomFolder) window.StormBridge.pickRomFolder();
      else showToast(isRu() ? "Открыт системный выбор папки с ROM" : "Opening Android ROM folder picker");
    } else if (act === "pick-single-rom") {
      if (hasBridge && window.StormBridge.pickSingleRom) window.StormBridge.pickSingleRom();
      else showToast(isRu() ? "Выбор одиночного образа ROM (.zip/.sfc/.chd/.iso)" : "Opening single ROM picker");
    } else if (act === "quick-scan-storage") {
      if (hasBridge && window.StormBridge.scanPath) window.StormBridge.scanPath(state.storagePath);
      else showToast(isRu() ? "Быстрое сканирование /storage/emulated/0/STORM RETROARCH..." : "Scanning storage...");
    } else if (act === "refresh-covers") {
      if (hasBridge && window.StormBridge.refreshCovers) window.StormBridge.refreshCovers();
      else showToast(isRu() ? "Обновление 3D обложек библиотеки..." : "Refreshing boxart covers...");
    } else if (act === "toggle-fav") {
      const gid = actEl.getAttribute("data-game");
      const gm = state.games.find(function (x) { return x.id === gid; });
      if (gm) gm.favorite = !gm.favorite;
      if (hasBridge && window.StormBridge.toggleFavorite) window.StormBridge.toggleFavorite(gid);
      if (state.activeTab === "library") updateLibraryResults();
      else renderViewport();
    } else if (act === "inspect-game") {
      openGameInspector(actEl.getAttribute("data-game"));
    } else if (act === "launch-game") {
      const gid = actEl.getAttribute("data-game");
      const slot = Number(actEl.getAttribute("data-slot") || -1);
      const gm = state.games.find(function (x) { return x.id === gid; }) || state.games[0];
      closeAllModals();
      if (hasBridge && window.StormBridge.launchGame && gm) {
        window.StormBridge.launchGame(gm.id, gm.coreId, slot);
      } else if (gm) {
        showToast((isRu() ? "Запуск " : "Launching ") + gm.title + " [" + gm.coreId + "]" + (slot >= 0 ? " (Слот #" + slot + ")" : ""));
      }
    } else if (act === "jump-game-cheats") {
      const gid = actEl.getAttribute("data-game");
      closeAllModals();
      navigateToTab("cheats");
      state.selectedCheatGameId = gid;
      if (PREVIEW_CHEAT_CODES[gid]) {
        state.activeCheatEntries = PREVIEW_CHEAT_CODES[gid].map(function (c) { return Object.assign({}, c); });
      }
      renderShellAndView();
    } else if (act === "download-core") {
      const cid = actEl.getAttribute("data-core");
      if (hasBridge && window.StormBridge.downloadCore) {
        window.StormBridge.downloadCore(cid);
      } else {
        const cObj = state.cores.find(function (x) { return x.id === cid; });
        if (cObj) {
          cObj.progress = 55;
          if (state.activeTab === "cores") updateCoresResults();
          else renderViewport();
          setTimeout(function () {
            cObj.progress = 0;
            cObj.installed = true;
            showToast((isRu() ? "Ядро установлено с зеркала STORM CDN: " : "Installed core: ") + cObj.displayName);
            renderShellAndView();
          }, 550);
        }
      }
    } else if (act === "download-core-pack") {
      const pid = actEl.getAttribute("data-pack");
      if (hasBridge && window.StormBridge.downloadCorePack) window.StormBridge.downloadCorePack(pid);
      else {
        state.cores.slice(20, 28).forEach(function (c) { c.installed = true; });
        showToast((isRu() ? "Пакет ядер установлен: " : "Core pack ready: ") + pid);
        renderShellAndView();
      }
    } else if (act === "import-core-file") {
      if (hasBridge && window.StormBridge.importCoreFile) window.StormBridge.importCoreFile();
      else showToast(isRu() ? "Выберите файл ядра .so или .zip" : "Select .so or .zip core file");
    } else if (act === "delete-core") {
      const cid = actEl.getAttribute("data-core");
      if (hasBridge && window.StormBridge.deleteCore) window.StormBridge.deleteCore(cid);
      const cObj = state.cores.find(function (x) { return x.id === cid; });
      if (cObj && !cObj.bundled) cObj.installed = false;
      renderShellAndView();
    } else if (act === "show-all-cores") {
      state.coresShowAll = true;
      updateCoresResults();
    } else if (act === "open-core-options") {
      openCoreOptionsModal(actEl.getAttribute("data-core"));
    } else if (act === "save-core-options") {
      const d = state.coreOptionsDraft;
      const rEl = document.getElementById("coRes");
      const cEl = document.getElementById("coCpu");
      const sEl = document.getElementById("coSkip");
      const bEl = document.getElementById("coBios");
      if (rEl) d.resolution = rEl.value;
      if (cEl) d.cpuOverclock = cEl.value;
      if (sEl) d.frameSkip = sEl.value;
      if (bEl) d.biosMode = bEl.value;
      if (hasBridge && window.StormBridge.saveCoreOptionsJson) {
        window.StormBridge.saveCoreOptionsJson(state.coreModalId, state.inspectorGameId || "", JSON.stringify(d));
      }
      closeAllModals();
      showToast(isRu() ? "Опции ядра сохранены" : "Core options saved");
    } else if (act === "select-cheat-game") {
      const cid = actEl.getAttribute("data-id");
      const sys = actEl.getAttribute("data-sys");
      const ttl = actEl.getAttribute("data-title");
      state.selectedCheatGameId = cid;
      if (hasBridge && window.StormBridge.getGameCheatsJson) {
        const list = safeJsonParse(window.StormBridge.getGameCheatsJson(sys, ttl, cid), null);
        if (Array.isArray(list)) {
          state.activeCheatEntries = list.map(function (c, idx) {
            return { index: idx, description: c.description, code: c.code, enabled: Boolean(c.enabled || c.isEnabled) };
          });
        }
      } else {
        state.activeCheatEntries = (PREVIEW_CHEAT_CODES[cid] || PREVIEW_CHEAT_CODES.chrono_trigger_ru).map(function (c) { return Object.assign({}, c); });
      }
      updateCheatsResults();
    } else if (act === "toggle-cheat-entry") {
      const idx = Number(actEl.getAttribute("data-idx"));
      if (state.activeCheatEntries[idx]) {
        state.activeCheatEntries[idx].enabled = !state.activeCheatEntries[idx].enabled;
        updateCheatsResults();
      }
    } else if (act === "cheats-enable-all" || act === "cheats-disable-all") {
      const flag = act === "cheats-enable-all";
      state.activeCheatEntries.forEach(function (c) { c.enabled = flag; });
      updateCheatsResults();
    } else if (act === "toggle-custom-cheat") {
      state.showCustomCheatForm = !state.showCustomCheatForm;
      updateCheatsResults();
    } else if (act === "append-custom-cheat") {
      const descEl = document.getElementById("newCheatDesc");
      const codeEl = document.getElementById("newCheatCode");
      const dVal = descEl ? descEl.value.trim() : "";
      const cVal = codeEl ? codeEl.value.trim() : "";
      if (dVal && cVal) {
        state.activeCheatEntries.unshift({ index: state.activeCheatEntries.length, description: dVal, code: cVal, enabled: true });
        state.showCustomCheatForm = false;
        showToast(isRu() ? "Собственный чит-код добавлен" : "Custom cheat added");
        updateCheatsResults();
      }
    } else if (act === "save-game-cheats") {
      const sel = state.cheatResults.find(function (x) { return x.id === state.selectedCheatGameId; }) || { id: "game", title: "Game", system: "SNES" };
      if (hasBridge && window.StormBridge.saveGameCheatsJson) {
        window.StormBridge.saveGameCheatsJson(sel.system, sel.title, sel.id, JSON.stringify(state.activeCheatEntries));
      }
      showToast(isRu() ? "Профиль читов сохранён и готов к игре" : "Game cheats saved");
    } else if (act === "download-library-cheats") {
      if (hasBridge && window.StormBridge.downloadLibraryCheatsBatch) window.StormBridge.downloadLibraryCheatsBatch();
      else showToast(isRu() ? "Читы синхронизированы для всех игр библиотеки" : "Library cheats synchronized");
    } else if (act === "download-bezel") {
      const pid = actEl.getAttribute("data-pack");
      if (hasBridge && window.StormBridge.downloadBezelPack) window.StormBridge.downloadBezelPack(pid);
      else {
        const bp = state.bezelPacks.find(function (x) { return x.id === pid; });
        if (bp) bp.installed = true;
        showToast((isRu() ? "Пакет рамок установлен: " : "Bezel pack installed: ") + pid);
        renderViewport();
      }
    } else if (act === "refresh-controllers") {
      if (hasBridge && window.StormBridge.refreshControllersJson) {
        const pads = safeJsonParse(window.StormBridge.refreshControllersJson(), null);
        if (Array.isArray(pads)) state.controllers = pads;
      }
      showToast(isRu() ? "Список Bluetooth / USB контроллеров обновлён" : "Controllers refreshed");
      renderViewport();
    } else if (act === "remap-btn") {
      state.listeningBindKey = actEl.getAttribute("data-key");
      renderViewport();
    } else if (act === "pick-bios-folder") {
      if (hasBridge && window.StormBridge.pickBiosFolder) window.StormBridge.pickBiosFolder();
      else showToast(isRu() ? "Выберите папку с файлами системных BIOS" : "Select BIOS directory");
    } else if (act === "import-bios-file") {
      if (hasBridge && window.StormBridge.importBiosFile) window.StormBridge.importBiosFile();
      else showToast(isRu() ? "Импорт файла прошивки BIOS в system/" : "Importing BIOS firmware");
    } else if (act === "scan-bios-md5") {
      if (hasBridge && window.StormBridge.scanBiosFolder) window.StormBridge.scanBiosFolder();
      else showToast(isRu() ? "Проверка MD5 контрольных сумм BIOS завершена" : "BIOS MD5 check complete");
    } else if (act === "export-saves-zip") {
      if (hasBridge && window.StormBridge.exportSavesBackupZip) window.StormBridge.exportSavesBackupZip();
      else showToast(isRu() ? "Архив STORM_Saves_Backup.zip создан для ПК" : "Exported STORM_Saves_Backup.zip");
    } else if (act === "import-saves-zip") {
      if (hasBridge && window.StormBridge.importSavesBackupZip) window.StormBridge.importSavesBackupZip();
      else showToast(isRu() ? "Выберите .ZIP архив сохранений с ПК" : "Pick PC .ZIP backup");
    } else if (act === "delete-save-slot") {
      const idx = Number(actEl.getAttribute("data-idx"));
      const gid = actEl.getAttribute("data-game");
      const sl = Number(actEl.getAttribute("data-slot"));
      if (hasBridge && window.StormBridge.deleteSaveSlot) window.StormBridge.deleteSaveSlot(gid, sl);
      state.saves.splice(idx, 1);
      renderViewport();
    } else if (act === "apply-rom-patch") {
      const sel = document.getElementById("patchGameSelect");
      const gid = sel ? sel.value : state.romPatchGameId;
      if (hasBridge && window.StormBridge.applyRomPatchPicker) window.StormBridge.applyRomPatchPicker(gid);
      else showToast(isRu() ? "Выберите файл патча .IPS / .BPS / .UPS" : "Select IPS/BPS/UPS patch");
    } else if (act === "create-m3u") {
      const tEl = document.getElementById("m3uTitleInput");
      const aEl = document.getElementById("m3uPathsArea");
      const ttl = tEl ? tEl.value.trim() : state.m3uTitle;
      const lines = aEl ? aEl.value.split("\n").map(function (s) { return s.trim(); }).filter(Boolean) : state.m3uDiscs;
      if (hasBridge && window.StormBridge.createM3uPlaylist) window.StormBridge.createM3uPlaylist(ttl, JSON.stringify(lines));
      showToast((isRu() ? "Создан многодисковый плейлист: " : "Created playlist: ") + ttl + ".m3u");
    } else if (act === "compute-rom-hash") {
      const sel = document.getElementById("hashGameSelect");
      const gid = sel ? sel.value : state.romHashGameId;
      if (hasBridge && window.StormBridge.computeRomHashJson) {
        const h = safeJsonParse(window.StormBridge.computeRomHashJson(gid), null);
        if (h) state.romHashResult = h;
      }
      showToast(isRu() ? "Хеши CRC32 / MD5 / SHA-1 вычислены" : "ROM hashes computed");
      renderViewport();
    } else if (act === "save-ra-login") {
      const u = document.getElementById("raUserInput");
      if (u) saveSettingsPatch({ raUsername: u.value.trim() });
      showToast(isRu() ? "Профиль RetroAchievements подключён" : "RetroAchievements connected");
    } else if (act === "start-netplay") {
      showToast(isRu() ? "Локальный Wi-Fi Netplay активен (Порт 55435)" : "Local Wi-Fi Netplay active on 55435");
    } else if (act === "request-storage") {
      if (hasBridge && window.StormBridge.requestStorageAccess) window.StormBridge.requestStorageAccess();
      else showToast(isRu() ? "Полный доступ к /storage/emulated/0/STORM RETROARCH активен" : "Storage access active");
    } else if (act === "check-updates") {
      if (hasBridge && window.StormBridge.checkForUpdates) window.StormBridge.checkForUpdates();
      else showToast(isRu() ? "У вас установлена актуальная версия (2.6.1)" : "STORM RETROARCH 2.6.1 is up to date");
    } else if (act === "install-update") {
      if (hasBridge && window.StormBridge.installUpdateApk) window.StormBridge.installUpdateApk();
    }
  });

  document.addEventListener("input", function (e) {
    const t = e.target;
    if (!t) return;
    if (t.id === "libSearchInput") {
      state.libQuery = t.value;
      scheduleDebounced("libSearch", updateLibraryResults, 25);
    } else if (t.id === "libZoomSlider") {
      state.libZoom = Number(t.value);
      applyVisualTokens();
      const lbl = document.getElementById("libZoomLabel");
      if (lbl) lbl.textContent = state.libZoom + "%";
    } else if (t.id === "coresSearchInput") {
      state.coresQuery = t.value;
      scheduleDebounced("coresSearch", updateCoresResults, 25);
    } else if (t.id === "cheatsSearchInput") {
      state.cheatsQuery = t.value;
      scheduleDebounced("cheatsSearch", function () {
        runCheatSearch();
        updateCheatsResults();
      }, 25);
    } else if (t.id === "omniboxInput") {
      state.omniboxQuery = t.value;
      state.omniboxSelectedIdx = 0;
      renderOmniboxResults();
    } else if (t.hasAttribute("data-eq-band")) {
      const bi = Number(t.getAttribute("data-eq-band"));
      state.settings.eqBands[bi] = Number(t.value);
      saveSettingsPatch({ eqBands: state.settings.eqBands });
      renderViewport();
    } else if (t.id === "audVolSlider") {
      saveSettingsPatch({ masterVolume: Number(t.value) });
      renderViewport();
    } else if (t.id === "audGainSlider") {
      saveSettingsPatch({ preAmpDb: Number(t.value) });
      renderViewport();
    } else if (t.id === "ctrlOpacitySlider") {
      saveSettingsPatch({ touchOpacity: Number(t.value) });
      renderViewport();
    } else if (t.id === "ctrlScaleSlider") {
      saveSettingsPatch({ touchScale: Number(t.value) });
      renderViewport();
    } else if (t.id === "uiScaleSlider") {
      saveSettingsPatch({ uiScale: Number(t.value) });
      renderViewport();
    }
  });

  document.addEventListener("change", function (e) {
    const t = e.target;
    if (!t) return;
    if (t.id === "libCoreSelect") { state.libCoreFilter = t.value; updateLibraryResults(); }
    else if (t.id === "libSortSelect") { state.libSort = t.value; updateLibraryResults(); }
    else if (t.id === "coresPlatSelect") { state.coresPlatform = t.value; updateCoresResults(); }
    else if (t.id === "cheatsSysSelect") { state.cheatsSystem = t.value; runCheatSearch(); updateCheatsResults(); }
    else if (t.id === "ramCompareSelect") { state.ramCompare = t.value; }
    else if (t.id === "gfxBackendSelect") { saveSettingsPatch({ videoBackend: t.value }); renderViewport(); }
    else if (t.id === "gfxScaleSelect") { saveSettingsPatch({ internalScale: t.value }); }
    else if (t.id === "gfxAspectSelect") { saveSettingsPatch({ aspectRatio: t.value }); }
    else if (t.id === "ctrlLayoutSelect") { saveSettingsPatch({ touchLayout: t.value }); renderViewport(); }
    else if (t.id === "ctrlThemeSelect") { saveSettingsPatch({ touchColorTheme: t.value }); renderViewport(); }
    else if (t.id === "ctrlHapticSelect") { saveSettingsPatch({ hapticStrength: t.value }); }
    else if (t.id === "ctrlTurboSelect") { saveSettingsPatch({ turboHz: t.value }); renderViewport(); }
    else if (t.id === "fontFamilySelect") { saveSettingsPatch({ font: t.value }); renderShellAndView(); }
    else if (t.id === "inspectorStatusSelect") {
      const gid = t.getAttribute("data-game");
      persistGameMetadata(gid, { completionStatus: t.value });
      if (state.activeTab === "library") updateLibraryResults();
      else if (state.activeTab === "dashboard") renderViewport();
      showToast((isRu() ? "Статус: " : "Status: ") + t.value);
    } else if (t.id === "inspectorNotesArea") {
      const gid = t.getAttribute("data-game");
      persistGameMetadata(gid, { userNotes: t.value });
    } else if (t.id === "inspectorCollectionInput") {
      const gid = t.getAttribute("data-game");
      persistGameMetadata(gid, { customCollection: t.value.trim() });
    } else if (t.id === "inspectorCoreSelect") {
      const gid = t.getAttribute("data-game");
      const gm = state.games.find(function (x) { return x.id === gid; });
      if (gm) gm.coreId = t.value;
      if (hasBridge && window.StormBridge.setPreferredCore) window.StormBridge.setPreferredCore(gid, t.value);
      showToast((isRu() ? "Назначено ядро: " : "Assigned core: ") + t.value);
      if (state.activeTab === "library") updateLibraryResults();
      else renderViewport();
    }
  });

  /* ------------------------------------------------------------------------
     13. PHYSICAL GAMEPAD & KEYBOARD NAVIGATION (ESC MODAL LAW + L1/R1/DPAD)
     ------------------------------------------------------------------------ */
  function cycleModule(delta) {
    const idx = STUDIO_MODULES.findIndex(function (m) { return m.id === state.activeTab; });
    const next = (idx + delta + STUDIO_MODULES.length) % STUDIO_MODULES.length;
    navigateToTab(STUDIO_MODULES[next].id);
    closeAllModals();
    haptic("tap");
    renderShellAndView();
  }

  function moveSpatialFocus(dir) {
    const focusables = Array.prototype.slice.call(
      document.querySelectorAll("button:not([disabled]), select:not([disabled]), input:not([disabled]), [tabindex='0']")
    ).filter(function (el) {
      return el.offsetParent !== null;
    });
    if (!focusables.length) return;
    const cur = document.activeElement;
    const idx = focusables.indexOf(cur);
    const step = (dir === "down" || dir === "right") ? 1 : -1;
    const next = focusables[(idx + step + focusables.length) % focusables.length];
    if (next) next.focus();
  }

  document.addEventListener("keydown", function (e) {
    if (state.listeningBindKey) {
      e.preventDefault();
      const k = state.listeningBindKey;
      state.controllerMap[k] = e.code || e.key;
      state.listeningBindKey = null;
      if (hasBridge && window.StormBridge.saveControllerMappingJson && state.controllers[0]) {
        window.StormBridge.saveControllerMappingJson(state.controllers[0].descriptor, JSON.stringify(state.controllerMap));
      }
      renderViewport();
      return;
    }
    if (e.key === "Escape") {
      closeAllModals();
      return;
    }
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k") {
      e.preventDefault();
      openCommandOmniboxModal();
      return;
    }
    const omniModal = document.getElementById("commandOmniboxModal");
    if (omniModal && !omniModal.hidden && state.omniboxResults.length > 0) {
      if (e.key === "ArrowDown") {
        e.preventDefault();
        state.omniboxSelectedIdx = (state.omniboxSelectedIdx + 1) % state.omniboxResults.length;
        renderOmniboxResults();
        return;
      } else if (e.key === "ArrowUp") {
        e.preventDefault();
        state.omniboxSelectedIdx = (state.omniboxSelectedIdx - 1 + state.omniboxResults.length) % state.omniboxResults.length;
        renderOmniboxResults();
        return;
      } else if (e.key === "Enter") {
        e.preventDefault();
        executeOmniboxItem(state.omniboxSelectedIdx);
        return;
      }
    }
    const tag = (e.target && e.target.tagName) || "";
    if (tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT") return;
    if (e.key === "PageDown") { e.preventDefault(); cycleModule(1); }
    else if (e.key === "PageUp") { e.preventDefault(); cycleModule(-1); }
    else if (e.key === "ArrowDown") { e.preventDefault(); moveSpatialFocus("down"); }
    else if (e.key === "ArrowUp") { e.preventDefault(); moveSpatialFocus("up"); }
    else if (e.key === "ArrowRight") { e.preventDefault(); moveSpatialFocus("right"); }
    else if (e.key === "ArrowLeft") { e.preventDefault(); moveSpatialFocus("left"); }
  });

  // Zero-idle-CPU Gamepad Poller (runs RAF only while a physical controller is connected)
  let gamepadLoopActive = false;
  let lastPadActionAt = 0;

  function pollGamepadLoop(ts) {
    const pads = navigator.getGamepads ? navigator.getGamepads() : [];
    let anyConnected = false;
    for (let i = 0; i < pads.length; i++) {
      const gp = pads[i];
      if (!gp || !gp.connected) continue;
      anyConnected = true;
      if (ts - lastPadActionAt > 190) {
        if (gp.buttons[4] && gp.buttons[4].pressed) { cycleModule(-1); lastPadActionAt = ts; }
        else if (gp.buttons[5] && gp.buttons[5].pressed) { cycleModule(1); lastPadActionAt = ts; }
        else if (gp.buttons[1] && gp.buttons[1].pressed) { closeAllModals(); lastPadActionAt = ts; }
        else if (gp.buttons[0] && gp.buttons[0].pressed && document.activeElement) { document.activeElement.click(); lastPadActionAt = ts; }
        else if ((gp.buttons[12] && gp.buttons[12].pressed) || gp.axes[1] < -0.55) { moveSpatialFocus("up"); lastPadActionAt = ts; }
        else if ((gp.buttons[13] && gp.buttons[13].pressed) || gp.axes[1] > 0.55) { moveSpatialFocus("down"); lastPadActionAt = ts; }
        else if ((gp.buttons[14] && gp.buttons[14].pressed) || gp.axes[0] < -0.55) { moveSpatialFocus("left"); lastPadActionAt = ts; }
        else if ((gp.buttons[15] && gp.buttons[15].pressed) || gp.axes[0] > 0.55) { moveSpatialFocus("right"); lastPadActionAt = ts; }
      }
    }
    if (anyConnected) requestAnimationFrame(pollGamepadLoop);
    else gamepadLoopActive = false;
  }

  window.addEventListener("gamepadconnected", function () {
    if (!gamepadLoopActive) {
      gamepadLoopActive = true;
      requestAnimationFrame(pollGamepadLoop);
    }
  });

  /* ------------------------------------------------------------------------
     14. INITIALIZATION
     ------------------------------------------------------------------------ */
  function initStudio() {
    // Detect OS language default (ru preferred if Russian locale or preview default)
    const navLang = ((navigator.language || "ru").slice(0, 2)).toLowerCase();
    if (I18N[navLang] && navLang === "ru") {
      state.settings.language = "ru";
    }

    if (hasBridge && typeof window.StormBridge.getBootstrapJson === "function") {
      try {
        const boot = safeJsonParse(window.StormBridge.getBootstrapJson(), null);
        if (boot) ingestBootstrap(boot);
      } catch (_) {}
    }

    renderShellAndView();
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initStudio);
  } else {
    initStudio();
  }
})();
