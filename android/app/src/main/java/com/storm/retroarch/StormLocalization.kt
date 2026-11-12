package com.storm.retroarch

import android.content.Context
import java.util.Locale
import java.util.concurrent.CopyOnWriteArrayList

data class StormLanguageOption(
    val code: String,
    val nativeName: String,
    val englishName: String
)

object StormLocalization {

    private const val PREFS_NAME = "storm_retroarch_locale_prefs"
    private const val KEY_LANG = "selected_lang"

    val LANGUAGES: List<StormLanguageOption> = listOf(
        StormLanguageOption("ru", "Русский", "Russian"),
        StormLanguageOption("en", "English", "English"),
        StormLanguageOption("de", "Deutsch", "German"),
        StormLanguageOption("fr", "Français", "French"),
        StormLanguageOption("zh", "中文", "Chinese"),
        StormLanguageOption("ko", "한국어", "Korean"),
        StormLanguageOption("ja", "日本語", "Japanese"),
        StormLanguageOption("es", "Español", "Spanish"),
        StormLanguageOption("it", "Italiano", "Italian"),
        StormLanguageOption("pt", "Português", "Portuguese"),
        StormLanguageOption("hi", "हिन्दी", "Hindi")
    )

    private val SUPPORTED_CODES: Set<String> = LANGUAGES.map { it.code }.toSet()

    @Volatile
    var currentLanguage: String = detectDefaultLanguage()
        private set

    private val listeners = CopyOnWriteArrayList<(String) -> Unit>()

    fun detectDefaultLanguage(): String {
        val raw = try {
            Locale.getDefault().language.lowercase(Locale.ROOT).trim()
        } catch (_: Throwable) {
            "en"
        }
        val normalized = when {
            raw.startsWith("zh") -> "zh"
            raw.startsWith("pt") -> "pt"
            raw.startsWith("es") -> "es"
            raw.startsWith("de") -> "de"
            raw.startsWith("fr") -> "fr"
            raw.startsWith("ko") -> "ko"
            raw.startsWith("ja") -> "ja"
            raw.startsWith("it") -> "it"
            raw.startsWith("hi") -> "hi"
            raw.startsWith("ru") || raw == "be" || raw == "uk" || raw == "kk" -> "ru"
            else -> raw
        }
        return if (SUPPORTED_CODES.contains(normalized)) normalized else "en"
    }

    fun loadFromPrefs(ctx: Context): String {
        val saved = ctx.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
            .getString(KEY_LANG, null)
        val resolved = if (saved != null && SUPPORTED_CODES.contains(saved)) saved else detectDefaultLanguage()
        currentLanguage = resolved
        return resolved
    }

    fun setLanguage(lang: String, ctx: Context? = null) {
        val clean = lang.lowercase(Locale.ROOT).trim()
        val resolved = if (SUPPORTED_CODES.contains(clean)) clean else detectDefaultLanguage()
        currentLanguage = resolved
        if (ctx != null) {
            ctx.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
                .edit()
                .putString(KEY_LANG, resolved)
                .apply()
        }
        for (l in listeners) l.invoke(resolved)
    }

    fun addLanguageChangeListener(listener: (String) -> Unit) {
        if (!listeners.contains(listener)) listeners.add(listener)
    }

    fun removeLanguageChangeListener(listener: (String) -> Unit) {
        listeners.remove(listener)
    }

    fun tr(key: String): String = tr(currentLanguage, key)

    fun tr(lang: String, key: String): String {
        val code = lang.lowercase(Locale.ROOT).trim()
        val langMap = DICTIONARIES[code]
        if (langMap != null) {
            langMap[key]?.let { return it }
        }
        DICTIONARIES["en"]?.get(key)?.let { return it }
        DICTIONARIES["ru"]?.get(key)?.let { return it }
        return key.replace('_', ' ').replaceFirstChar {
            if (it.isLowerCase()) it.titlecase(Locale.ROOT) else it.toString()
        }
    }

    fun trFormat(lang: String, key: String, vararg args: Any): String {
        val template = tr(lang, key)
        return try {
            String.format(Locale.ROOT, template, *args)
        } catch (_: Throwable) {
            template
        }
    }

    // Compact row builder mapping a key across all 11 languages in exact order:
    // [0] ru, [1] en, [2] de, [3] fr, [4] zh, [5] ko, [6] ja, [7] es, [8] it, [9] pt, [10] hi
    private val DICTIONARIES: Map<String, Map<String, String>> by lazy {
        val langs = listOf("ru", "en", "de", "fr", "zh", "ko", "ja", "es", "it", "pt", "hi")
        val maps = langs.associateWith { LinkedHashMap<String, String>(220) }

        fun row(
            key: String,
            ru: String, en: String, de: String, fr: String, zh: String,
            ko: String, ja: String, es: String, it: String, pt: String, hi: String
        ) {
            val vals = arrayOf(ru, en, de, fr, zh, ko, ja, es, it, pt, hi)
            for (i in langs.indices) {
                maps[langs[i]]!![key] = vals[i]
            }
        }

        // App & Navigation Tabs
        row("app_title", "STORM RETROARCH", "STORM RETROARCH", "STORM RETROARCH", "STORM RETROARCH", "STORM RETROARCH", "STORM RETROARCH", "STORM RETROARCH", "STORM RETROARCH", "STORM RETROARCH", "STORM RETROARCH", "STORM RETROARCH")
        row("app_subtitle", "Мультисистемная студия эмуляции", "Multi-System Emulation Studio", "Multi-System-Emulationsstudio", "Studio d'émulation multi-systèmes", "多平台全能模拟工作室", "멀티 시스템 에뮬레이션 스튜디오", "マルチシステム・エミュレーションスタジオ", "Estudio de emulación multisistema", "Studio di emulazione multi-sistema", "Estúdio de emulação multissistema", "मल्टी-सिस्टम इम्यूलेशन स्टूडियो")
        row("tab_library", "Библиотека", "Library", "Bibliothek", "Bibliothèque", "游戏库", "라이브러리", "ライブラリ", "Biblioteca", "Libreria", "Biblioteca", "लाइब्रेरी")
        row("tab_cores", "Ядра", "Cores", "Kerne", "Cœurs", "核心", "코어", "コア", "Núcleos", "Core", "Núcleos", "कोर")
        row("tab_cheats", "Читы и RAM", "Cheats & RAM", "Cheats & RAM", "Triches & RAM", "金手指与内存", "치트 및 RAM", "チートとRAM", "Trucos y RAM", "Trucchi e RAM", "Cheats e RAM", "चीट्स और RAM")
        row("tab_bios", "BIOS и Рамки", "BIOS & Bezels", "BIOS & Rahmen", "BIOS & Cadres", "BIOS与边框", "BIOS 및 베젤", "BIOSとベゼル", "BIOS y Marcos", "BIOS e Cornici", "BIOS e Molduras", "BIOS और बेज़ल")
        row("tab_bezels", "Рамки и Шейдеры", "Bezels & Shaders", "Rahmen & Shader", "Cadres & Shaders", "边框与着色器", "베젤 및 셰이더", "ベゼルとシェーダー", "Marcos y Shaders", "Cornici e Shader", "Molduras e Shaders", "बेज़ल और शेडर")
        row("tab_controllers", "Геймпады", "Controllers", "Controller", "Manettes", "手柄配置", "컨트롤러", "コントローラー", "Mandos", "Controller", "Controles", "कंट्रोलर")
        row("tab_saves", "Сохранения", "Save States", "Spielstände", "Sauvegardes", "即时存档", "세이브 스테이트", "ステートセーブ", "Partidas", "Salvataggi", "Saves", "सेव स्टेट्स")
        row("tab_settings", "Настройки", "Settings", "Einstellungen", "Paramètres", "设置", "설정", "設定", "Ajustes", "Impostazioni", "Configurações", "सेटिंग्स")

        // Smart Collections & Library Filters
        row("col_all", "Все игры", "All Games", "Alle Spiele", "Tous les jeux", "全部游戏", "모든 게임", "すべてのゲーム", "Todos los juegos", "Tutti i giochi", "Todos os jogos", "सभी गेम्स")
        row("col_favorites", "Избранное", "Favorites", "Favoriten", "Favoris", "收藏夹", "즐겨찾기", "お気に入り", "Favoritos", "Preferiti", "Favoritos", "पसंदीदा")
        row("col_rus_mods", "Русские версии", "Russian Mods", "Russische Mods", "Versions russes", "俄语汉化版", "러시아어 패치", "ロシア語版", "Mods en ruso", "Mod in russo", "Mods em russo", "रूसी संस्करण")
        row("col_classic_8_16", "Классика 8/16 бит", "Classic 8/16-Bit", "Klassik 8/16-Bit", "Classiques 8/16 bits", "经典 8/16 位", "클래식 8/16비트", "クラシック 8/16ビット", "Clásicos 8/16 bits", "Classici 8/16 bit", "Clássicos 8/16 bits", "क्लासिक 8/16-बिट")
        row("col_cd_3d", "CD и 3D консоли", "CD & 3D Consoles", "CD- & 3D-Konsolen", "Consoles CD & 3D", "光盘与3D主机", "CD 및 3D 콘솔", "CD・3Dコンソール", "Consolas CD y 3D", "Console CD e 3D", "Consoles CD e 3D", "CD और 3D कंसोल")
        row("col_dos_pc", "DOS и Компьютеры", "DOS & Computers", "DOS & Computer", "DOS & Ordinateurs", "DOS与经典电脑", "DOS 및 컴퓨터", "DOS・レトロPC", "DOS y Ordenadores", "DOS e Computer", "DOS e Computadores", "DOS और कंप्यूटर")
        row("col_recent", "Недавние", "Recently Played", "Zuletzt gespielt", "Joués récemment", "最近游玩", "최근 플레이", "最近プレイしたゲーム", "Recientes", "Giocati di recente", "Jogados recentemente", "हाल ही में खेले गए")
        row("filter_all_platforms", "Все платформы", "All Platforms", "Alle Plattformen", "Toutes plateformes", "所有平台", "모든 플랫폼", "すべての機種", "Todas las plataformas", "Tutte le piattaforme", "Todas as plataformas", "सभी प्लेटफ़ॉर्म")
        row("filter_all_cores", "Все совместимые ядра", "All Compatible Cores", "Alle kompatiblen Kerne", "Tous les cœurs compatibles", "所有兼容核心", "호환되는 모든 코어", "対応するすべてのコア", "Todos los núcleos compatibles", "Tutti i core compatibili", "Todos os núcleos compatíveis", "सभी संगत कोर")
        row("search_hint", "Поиск игр, платформ или регионов...", "Search games, platforms, or regions...", "Spiele, Plattformen oder Regionen suchen...", "Rechercher jeux, plateformes ou régions...", "搜索游戏、平台或地区...", "게임, 플랫폼 또는 지역 검색...", "ゲーム、機種、リージョンを検索...", "Buscar juegos, plataformas o regiones...", "Cerca giochi, piattaforme o regioni...", "Pesquisar jogos, plataformas ou regiões...", "गेम, प्लेटफ़ॉर्म या क्षेत्र खोजें...")
        row("scan_folders", "Сканировать папки", "Scan Folders", "Ordner scannen", "Analyser dossiers", "扫描游戏目录", "폴더 스캔", "フォルダをスキャン", "Escanear carpetas", "Scansiona cartelle", "Verificar pastas", "फ़ोल्डर स्कैन करें")
        row("add_rom_folder", "Добавить папку ROM", "Add ROM Folder", "ROM-Ordner hinzufügen", "Ajouter dossier ROM", "添加 ROM 文件夹", "ROM 폴더 추가", "ROMフォルダを追加", "Añadir carpeta ROM", "Aggiungi cartella ROM", "Adicionar pasta ROM", "ROM फ़ोल्डर जोड़ें")
        row("refresh_covers", "Обновить обложки", "Refresh Covers", "Cover aktualisieren", "Actualiser jaquettes", "刷新封面", "표지 새로고침", "カバーを更新", "Actualizar portadas", "Aggiorna copertine", "Atualizar capas", "कवर रीफ़्रेश करें")
        row("empty_library", "Игры не найдены. Поместите ROM-файлы в /storage/emulated/0/STORM RETROARCH/roms или добавьте папку.", "No games found. Place ROMs in /storage/emulated/0/STORM RETROARCH/roms or add a folder.", "Keine Spiele gefunden. ROMs in /storage/emulated/0/STORM RETROARCH/roms ablegen oder Ordner wählen.", "Aucun jeu trouvé. Placez vos ROMs dans /storage/emulated/0/STORM RETROARCH/roms ou ajoutez un dossier.", "未找到游戏。请将 ROM 放入 /storage/emulated/0/STORM RETROARCH/roms 或添加文件夹。", "게임을 찾을 수 없습니다. ROM을 /storage/emulated/0/STORM RETROARCH/roms에 넣거나 폴더를 추가하세요.", "ゲームが見つかりません。ROMを /storage/emulated/0/STORM RETROARCH/roms に配置するかフォルダを追加してください。", "No se encontraron juegos. Coloque ROMs en /storage/emulated/0/STORM RETROARCH/roms o añada una carpeta.", "Nessun gioco trovato. Inserisci le ROM in /storage/emulated/0/STORM RETROARCH/roms o aggiungi una cartella.", "Nenhum jogo encontrado. Coloque ROMs em /storage/emulated/0/STORM RETROARCH/roms ou adicione uma pasta.", "कोई गेम नहीं मिला। /storage/emulated/0/STORM RETROARCH/roms में ROM रखें या फ़ोल्डर जोड़ें।")
        row("launch_game", "Запустить", "Play", "Starten", "Jouer", "启动游戏", "실행", "プレイ", "Jugar", "Gioca", "Jogar", "खेलें")
        row("assign_core", "Выбрать ядро", "Select Core", "Kern wählen", "Choisir cœur", "选择核心", "코어 선택", "コアを選択", "Elegir núcleo", "Scegli core", "Escolher núcleo", "कोर चुनें")
        row("create_m3u", "Создать плейлист .m3u", "Create .m3u Playlist", ".m3u-Wiedergabeliste erstellen", "Créer liste .m3u", "生成多碟 .m3u", ".m3u 목록 생성", ".m3uプレイリスト作成", "Crear lista .m3u", "Crea playlist .m3u", "Criar lista .m3u", ".m3u प्लेलिस्ट बनाएं")
        row("soft_patch_ready", "Авто-патч IPS/BPS активен", "IPS/BPS Soft-Patch Ready", "IPS/BPS-Softpatch bereit", "Patch IPS/BPS actif", "IPS/BPS 软补丁已就绪", "IPS/BPS 소프트 패치 활성", "IPS/BPSソフトパッチ有効", "Parche IPS/BPS activo", "Soft-patch IPS/BPS attiva", "Soft-patch IPS/BPS ativo", "IPS/BPS सॉफ्ट-पैच सक्रिय")
        row("favorite_toggle", "В избранное", "Toggle Favorite", "Favorit umschalten", "Basculer favori", "切换收藏", "즐겨찾기 전환", "お気に入り切替", "Alternar favorito", "Preferito", "Alternar favorito", "पसंदीदा बदलें")

        // Core Manager
        row("cores_title", "Менеджер ядер Libretro", "Libretro Core Manager", "Libretro-Kernverwaltung", "Gestionnaire de cœurs Libretro", "Libretro 核心管理器", "Libretro 코어 관리자", "Libretro コアマネージャー", "Gestor de núcleos Libretro", "Gestione Core Libretro", "Gerenciador de Núcleos Libretro", "Libretro कोर प्रबंधक")
        row("cores_installed", "Установленные", "Installed", "Installiert", "Installés", "已安装", "설치됨", "インストール済み", "Instalados", "Installati", "Instalados", "इंस्टॉल किए गए")
        row("cores_available", "Все ядра каталога", "All Catalog Cores", "Alle Katalogkerne", "Tous les cœurs", "全部目录核心", "전체 카탈로그 코어", "すべてのカタログコア", "Todos los núcleos", "Tutti i core", "Todos os núcleos", "सभी कैटलॉग कोर")
        row("cores_experimental", "Экспериментальные", "Experimental", "Experimentell", "Expérimental", "实验性核心", "실험적 코어", "実験的コア", "Experimentales", "Sperimentali", "Experimentais", "प्रयोगात्मक")
        row("core_install", "Скачать", "Download", "Herunterladen", "Télécharger", "下载核心", "다운로드", "ダウンロード", "Descargar", "Scarica", "Baixar", "डाउनलोड करें")
        row("core_update", "Обновить", "Update", "Aktualisieren", "Mettre à jour", "更新核心", "업데이트", "更新", "Actualizar", "Aggiorna", "Atualizar", "अपडेट करें")
        row("core_installed_badge", "Установлено", "Installed", "Installiert", "Installé", "已安装", "설치 완료", "導入済み", "Instalado", "Installato", "Instalado", "इंस्टॉल है")
        row("core_not_installed", "Не установлено", "Not Installed", "Nicht installiert", "Non installé", "未安装", "미설치", "未導入", "No instalado", "Non installato", "Não instalado", "इंस्टॉल नहीं")
        row("core_downloading", "Загрузка ядра...", "Downloading core...", "Kern wird geladen...", "Téléchargement du cœur...", "正在下载核心...", "코어 다운로드 중...", "コアをダウンロード中...", "Descargando núcleo...", "Download core...", "Baixando núcleo...", "कोर डाउनलोड हो रहा है...")
        row("core_extracting", "Распаковка SELinux .so...", "Extracting SELinux .so...", "Entpacke SELinux .so...", "Extraction SELinux .so...", "正在解压核心 .so...", "SELinux .so 압축 해제 중...", "SELinux .so を展開中...", "Extrayendo SELinux .so...", "Estrazione SELinux .so...", "Extraindo SELinux .so...", "SELinux .so निकाला जा रहा है...")
        row("core_ready", "Ядро готово к запуску", "Core ready to launch", "Kern startbereit", "Cœur prêt à lancer", "核心已就绪", "코어 실행 준비 완료", "コアの準備が完了しました", "Núcleo listo", "Core pronto", "Núcleo pronto", "कोर चलाने के लिए तैयार")
        row("core_failed", "Ошибка загрузки ядра", "Core download failed", "Kern-Download fehlgeschlagen", "Échec du téléchargement", "核心下载失败", "코어 다운로드 실패", "コアのダウンロードに失敗", "Error al descargar núcleo", "Download core fallito", "Falha ao baixar núcleo", "कोर डाउनलोड विफल")
        row("core_sync_external", "Импорт .so из папки cores", "Sync .so from cores folder", ".so aus cores-Ordner importieren", "Importer .so du dossier cores", "从 cores 目录同步 .so", "cores 폴더에서 .so 동기화", "coresフォルダから.soを同期", "Sincronizar .so de carpeta cores", "Sincronizza .so dalla cartella cores", "Sincronizar .so da pasta cores", "cores फ़ोल्डर से .so सिंक करें")
        row("core_abi_detected", "Архитектура ЦП", "CPU ABI", "CPU-Architektur", "Architecture CPU", "处理器架构", "CPU 아키텍처", "CPUアーキテクチャ", "Arquitectura CPU", "Architettura CPU", "Arquitetura CPU", "CPU आर्किटेक्चर")

        // Cheats & RAM Scanner
        row("cheats_title", "База читов и сканер памяти RAM", "Cheat Database & RAM Scanner", "Cheat-Datenbank & RAM-Scanner", "Base de triches & Scanner RAM", "金手指库与内存扫描器", "치트 데이터베이스 및 RAM 스캐너", "チートDBとRAMスキャナー", "Base de trucos y Escáner RAM", "Database Trucchi e Scanner RAM", "Banco de Cheats e Scanner RAM", "चीट डेटाबेस और RAM स्कैनर")
        row("cheats_in_library", "В библиотеке", "In Library", "In Bibliothek", "Dans la bibliothèque", "游戏库匹配", "라이브러리 보유", "ライブラリ内", "En biblioteca", "In libreria", "Na biblioteca", "लाइब्रेरी में")
        row("cheats_all_db", "Вся база .cht", "All .cht Files", "Alle .cht-Dateien", "Tous les fichiers .cht", "全部 .cht 文件", "전체 .cht 파일", "すべての.cht", "Todos los .cht", "Tutti i file .cht", "Todos os .cht", "सभी .cht फ़ाइलें")
        row("cheats_download_db", "Скачать базу читов Libretro", "Download Libretro Cheats", "Libretro-Cheats laden", "Télécharger triches Libretro", "下载官方金手指库", "Libretro 치트 DB 다운로드", "公式チートDBを取得", "Descargar trucos Libretro", "Scarica trucchi Libretro", "Baixar cheats Libretro", "Libretro चीट्स डाउनलोड करें")
        row("cheats_search_hint", "Поиск игры или описания чита...", "Search game or cheat description...", "Spiel oder Cheat suchen...", "Chercher jeu ou code...", "搜索游戏或金手指说明...", "게임 또는 치트 설명 검색...", "ゲーム名やチート内容を検索...", "Buscar juego o truco...", "Cerca gioco o trucco...", "Pesquisar jogo ou cheat...", "गेम या चीट खोजें...")
        row("cheats_save", "Сохранить .cht", "Save .cht", ".cht speichern", "Enregistrer .cht", "保存 .cht", ".cht 저장", ".chtを保存", "Guardar .cht", "Salva .cht", "Salvar .cht", ".cht सहेजें")
        row("cheats_add_code", "Добавить код", "Add Cheat Code", "Cheat-Code hinzufügen", "Ajouter un code", "添加金手指代码", "치트 코드 추가", "コードを追加", "Añadir código", "Aggiungi codice", "Adicionar código", "चीट कोड जोड़ें")
        row("cheats_enabled", "Включено", "Enabled", "Aktiviert", "Activé", "已启用", "활성화됨", "有効", "Activado", "Attivo", "Ativado", "सक्रिय")
        row("cheats_disabled", "Выключено", "Disabled", "Deaktiviert", "Désactivé", "已禁用", "비활성화됨", "無効", "Desactivado", "Disattivato", "Desativado", "निष्क्रिय")
        row("ram_scanner_title", "Живой сканер памяти RAM", "Live RAM Cheat Scanner", "Live-RAM-Scanner", "Scanner mémoire RAM", "实时内存搜索器", "실시간 RAM 스캐너", "リアルタイムRAMスキャナー", "Escáner de memoria RAM", "Scanner memoria RAM", "Scanner de memória RAM", "लाइव RAM स्कैनर")
        row("ram_scan_new", "Новый поиск", "New Scan", "Neue Suche", "Nouvelle recherche", "首次扫描", "새 스캔", "新規スキャン", "Nuevo escaneo", "Nuova scansione", "Nova busca", "नया स्कैन")
        row("ram_scan_next", "Отсеять", "Next Scan", "Weiter filtern", "Filtrer", "再次扫描", "다음 스캔", "絞り込み", "Siguiente escaneo", "Filtra risultati", "Filtrar busca", "अगला स्कैन")
        row("ram_scan_exact", "Точное значение", "Exact Value", "Exakter Wert", "Valeur exacte", "精确数值", "정확한 값", "完全一致", "Valor exacto", "Valore esatto", "Valor exato", "सटीक मान")
        row("ram_scan_increased", "Увеличилось", "Increased", "Gestiegen", "Augmenté", "数值增加", "증가함", "増加した値", "Aumentó", "Aumentato", "Aumentou", "बढ़ा हुआ")
        row("ram_scan_decreased", "Уменьшилось", "Decreased", "Gesunken", "Diminué", "数值减少", "감소함", "減少した値", "Disminuyó", "Diminuito", "Diminuiu", "घटा हुआ")
        row("ram_freeze_value", "Заморозить адрес", "Freeze Address", "Adresse einfrieren", "Figer l'adresse", "锁定内存地址", "주소 고정", "アドレスを固定", "Congelar dirección", "Blocca indirizzo", "Congelar endereço", "पता फ़्रीज़ करें")
        row("ram_matches_found", "Найдено адресов", "Matches Found", "Treffer gefunden", "Adresses trouvées", "匹配地址数", "검색된 주소", "一致アドレス数", "Direcciones halladas", "Indirizzi trovati", "Endereços encontrados", "मिले पते")

        // BIOS & Mega Bezel Manager
        row("bios_title", "Проверка системных BIOS (system/)", "System BIOS Verifier (system/)", "System-BIOS-Prüfung (system/)", "Vérificateur BIOS (system/)", "系统 BIOS 校验器 (system/)", "시스템 BIOS 검증기 (system/)", "システムBIOS検証 (system/)", "Verificador de BIOS (system/)", "Verifica BIOS (system/)", "Verificador de BIOS (system/)", "सिस्टम BIOS सत्यापन (system/)")
        row("bios_verified", "MD5 подтверждён", "MD5 Verified", "MD5 bestätigt", "MD5 vérifié", "MD5 校验通过", "MD5 검증 완료", "MD5一致", "MD5 verificado", "MD5 verificato", "MD5 verificado", "MD5 सत्यापित")
        row("bios_present", "Файл найден", "Present", "Vorhanden", "Présent", "文件已存在", "파일 있음", "検出済み", "Presente", "Presente", "Presente", "मौजूद है")
        row("bios_missing", "Отсутствует", "Missing", "Fehlt", "Manquant", "缺失", "누락됨", "未検出", "Falta", "Mancante", "Ausente", "अनुपस्थित")
        row("bios_required", "Обязательный", "Required", "Erforderlich", "Requis", "必需", "필수", "必須", "Obligatorio", "Richiesto", "Obrigatório", "आवश्यक")
        row("bios_optional", "Опциональный", "Optional", "Optional", "Optionnel", "可选", "선택", "任意", "Opcional", "Opzionale", "Opcional", "वैकल्पिक")
        row("bios_import_file", "Импорт файла BIOS", "Import BIOS File", "BIOS-Datei importieren", "Importer fichier BIOS", "导入 BIOS 文件", "BIOS 파일 가져오기", "BIOSファイルを導入", "Importar archivo BIOS", "Importa file BIOS", "Importar arquivo BIOS", "BIOS फ़ाइल आयात करें")
        row("bios_rescan", "Перепроверить MD5", "Rescan MD5", "MD5 neu prüfen", "Revérifier MD5", "重新校验 MD5", "MD5 다시 검사", "MD5を再スキャン", "Reescanear MD5", "Riscansiona MD5", "Verificar MD5 novamente", "MD5 पुनः जांचें")
        row("bezel_title", "Пакеты рамок Mega Bezel и Шейдеры", "Mega Bezel & Shader Packs", "Mega-Bezel- & Shader-Pakete", "Packs Mega Bezel & Shaders", "Mega Bezel 边框与着色器包", "Mega Bezel 및 셰이더 팩", "Mega Bezel・シェーダーパック", "Paquetes Mega Bezel y Shaders", "Pacchetti Mega Bezel e Shader", "Pacotes Mega Bezel e Shaders", "Mega Bezel और शेडर पैक")
        row("bezel_install", "Установить пакет", "Install Pack", "Paket installieren", "Installer le pack", "安装资源包", "팩 설치", "パックを導入", "Instalar paquete", "Installa pacchetto", "Instalar pacote", "पैक इंस्टॉल करें")
        row("bezel_installed", "Пакет установлен", "Pack Installed", "Paket installiert", "Pack installé", "已安装资源包", "설치 완료", "導入済み", "Paquete instalado", "Pacchetto installato", "Pacote instalado", "पैक इंस्टॉल है")
        row("bezel_presets_count", "Пресеты", "Presets", "Presets", "Préréglages", "预设数量", "프리셋", "プリセット数", "Preajustes", "Preset", "Predefinições", "प्रीसेट")

        // Touch Overlay Editor & Quick Menu
        row("touch_overlay_title", "Редактор сенсорного геймпада", "Touch Gamepad Editor", "Touch-Gamepad-Editor", "Éditeur de manette tactile", "触屏虚拟手柄编辑器", "터치 게임패드 편집기", "タッチゲームパッド編集", "Editor de mando táctil", "Editor gamepad touch", "Editor de controle de toque", "टच गेमपैड संपादक")
        row("touch_edit_layout", "Изменить раскладку", "Edit Layout", "Layout anpassen", "Modifier disposition", "调整按键布局", "레이아웃 편집", "配置を編集", "Editar disposición", "Modifica layout", "Editar layout", "लेआउट संपादित करें")
        row("touch_reset_layout", "Сбросить позиции", "Reset Positions", "Positionen zurücksetzen", "Réinitialiser positions", "重置默认位置", "위치 초기화", "配置をリセット", "Restablecer posiciones", "Ripristina posizioni", "Redefinir posições", "स्थिति रीसेट करें")
        row("touch_save_layout", "Сохранить раскладку", "Save Layout", "Layout speichern", "Enregistrer disposition", "保存布局", "레이아웃 저장", "配置を保存", "Guardar disposición", "Salva layout", "Salvar layout", "लेआउट सहेजें")
        row("touch_opacity", "Прозрачность кнопок", "Button Opacity", "Tasten-Deckkraft", "Opacité des boutons", "按键透明度", "버튼 투명도", "ボタンの不透明度", "Opacidad de botones", "Opacità pulsanti", "Opacidade dos botões", "बटन पारदर्शिता")
        row("touch_scale", "Масштаб элементов", "Control Scale", "Steuerungsgröße", "Échelle des contrôles", "控件缩放", "컨트롤 크기", "ボタンサイズ", "Escala de controles", "Scala controlli", "Escala dos controles", "नियंत्रण आकार")
        row("touch_haptics", "Тактильная отдача", "Haptic Vibration", "Haptisches Feedback", "Retour haptique", "触觉震动反馈", "햅틱 진동", "触覚フィードバック", "Vibración háptica", "Vibrazione aptica", "Vibração tátil", "हैप्टिक कंपन")
        row("quick_menu", "Быстрое меню", "Quick Menu", "Schnellmenü", "Menu rapide", "快捷菜单", "퀵 메뉴", "クイックメニュー", "Menú rápido", "Menu rapido", "Menu rápido", "क्विक मेनू")
        row("quick_resume", "Продолжить игру", "Resume Game", "Weiterspielen", "Reprendre", "继续游戏", "게임 재개", "ゲームに戻る", "Reanudar juego", "Riprendi gioco", "Continuar jogo", "गेम जारी रखें")
        row("quick_restart", "Перезапуск ROM", "Restart ROM", "ROM neu starten", "Redémarrer ROM", "重启游戏", "ROM 재시작", "リセット", "Reiniciar ROM", "Riavvia ROM", "Reiniciar ROM", "ROM रीस्टार्ट करें")
        row("quick_screenshot", "Скриншот экрана", "Take Screenshot", "Screenshot aufnehmen", "Capture d'écran", "保存截图", "스크린샷 촬영", "スクリーンショット", "Captura de pantalla", "Cattura schermata", "Capturar tela", "स्क्रीनशॉट लें")
        row("quick_fast_forward", "Турбо-ускорение", "Fast-Forward", "Schnellvorlauf", "Avance rapide", "快进加速", "배속 플레이", "早送り", "Avance rápido", "Avanzamento rapido", "Avanço rápido", "फ़ास्ट-फ़ॉरवर्ड")
        row("quick_rewind", "Перемотка назад", "Rewind", "Zurückspulen", "Rembobiner", "时光倒流", "되감기", "巻き戻し", "Rebobinar", "Riavvolgi", "Retroceder", "रिवाइंड")
        row("quick_exit", "Выйти в библиотеку", "Exit to Library", "Zur Bibliothek", "Quitter vers bibliothèque", "退出到游戏库", "라이브러리로 나가기", "ライブラリへ戻る", "Salir a biblioteca", "Esci alla libreria", "Sair para biblioteca", "लाइब्रेरी में लौटें")

        // Controller Profiles & Hotkeys
        row("controllers_title", "Профили физических геймпадов", "Hardware Controller Profiles", "Hardware-Controller-Profile", "Profils de manettes physiques", "外接手柄与按键映射", "물리 컨트롤러 프로필", "物理コントローラー設定", "Perfiles de mandos físicos", "Profili controller fisici", "Perfis de controles físicos", "हार्डवेयर कंट्रोलर प्रोफ़ाइल")
        row("controllers_connected", "Подключённые устройства", "Connected Controllers", "Verbundene Controller", "Manettes connectées", "已连接控制器", "연결된 컨트롤러", "接続中のコントローラー", "Mandos conectados", "Controller connessi", "Controles conectados", "कनेक्टेड कंट्रोलर")
        row("controllers_none", "Физические геймпады не обнаружены. Подключите Bluetooth или USB контроллер.", "No hardware gamepads detected. Connect a Bluetooth or USB controller.", "Keine Controller erkannt. Bluetooth- oder USB-Gamepad verbinden.", "Aucune manette détectée. Connectez une manette Bluetooth ou USB.", "未检测到物理手柄，请连接蓝牙或 USB 控制器。", "감지된 게임패드가 없습니다. 블루투스 또는 USB 패드를 연결하세요.", "物理コントローラーが未接続です。BluetoothまたはUSB接続してください。", "No se detectaron mandos. Conecte un mando Bluetooth o USB.", "Nessun controller rilevato. Collega un gamepad Bluetooth o USB.", "Nenhum controle detectado. Conecte um gamepad Bluetooth ou USB.", "कोई गेमपैड नहीं मिला। ब्लूटूथ या USB कंट्रोलर जोड़ें।")
        row("controllers_refresh", "Обновить список", "Refresh Devices", "Geräte aktualisieren", "Actualiser appareils", "刷新设备", "장치 새로고침", "デバイス再検出", "Actualizar dispositivos", "Aggiorna dispositivi", "Atualizar dispositivos", "डिवाइस रीफ़्रेश करें")
        row("controllers_remap", "Назначение кнопок", "Button Mapping", "Tastenbelegung", "Attribution des boutons", "按键映射", "버튼 매핑", "ボタン割り当て", "Mapeo de botones", "Mappatura pulsanti", "Mapeamento de botões", "बटन मैपिंग")
        row("controllers_hotkeys", "Горячие клавиши", "Hotkey Shortcuts", "Tastenkürzel", "Raccourcis", "快捷键绑定", "단축키 설정", "ホットキー", "Atajos rápidos", "Scorciatoie", "Atalhos rápidos", "हॉटकी शॉर्टकट")
        row("hotkey_quick_save", "Быстрое сохранение", "Quick Save", "Schnellspeichern", "Sauvegarde rapide", "快速存档", "빠른 저장", "クイックセーブ", "Guardado rápido", "Salvataggio rapido", "Salvamento rápido", "क्विक सेव")
        row("hotkey_quick_load", "Быстрая загрузка", "Quick Load", "Schnellladen", "Chargement rapide", "快速读档", "빠른 불러오기", "クイックロード", "Carga rápida", "Caricamento rapido", "Carregamento rápido", "क्विक लोड")
        row("hotkey_rewind", "Перемотка времени", "Rewind", "Zurückspulen", "Rembobinage", "倒带回溯", "되감기", "巻き戻し", "Rebobinar", "Riavvolgi", "Retroceder", "रिवाइंड")
        row("hotkey_fast_forward", "Ускорение времени", "Fast-Forward", "Schnellvorlauf", "Avance rapide", "快进切换", "빨리감기", "早送り切替", "Avance rápido", "Avanzamento rapido", "Avanço rápido", "फ़ास्ट-फ़ॉरवर्ड")
        row("hotkey_quick_menu", "Меню эмулятора", "Quick Menu Drawer", "Schnellmenü öffnen", "Ouvrir menu rapide", "呼出快捷菜单", "퀵 메뉴 열기", "クイックメニュー表示", "Abrir menú rápido", "Apri menu rapido", "Abrir menu rápido", "क्विक मेनू खोलें")
        row("hotkey_screenshot", "Снимок экрана", "Screenshot", "Bildschirmfoto", "Capture d'écran", "游戏截图", "스크린샷", "スクリーンショット", "Captura de pantalla", "Schermata", "Captura de tela", "स्क्रीनशॉट")
        row("controllers_save_profile", "Сохранить профиль", "Save Profile", "Profil speichern", "Enregistrer profil", "保存配置", "프로필 저장", "プロファイルを保存", "Guardar perfil", "Salva profilo", "Salvar perfil", "प्रोफ़ाइल सहेजें")
        row("controllers_reset_profile", "По умолчанию", "Reset Defaults", "Standardwerte", "Par défaut", "恢复默认", "기본값 복원", "初期設定に戻す", "Valores predeterminados", "Predefiniti", "Restaurar padrão", "डिफ़ॉल्ट रीसेट")

        // Save States
        row("saves_title", "Слоты быстрых сохранений и SRAM", "Save State Slots & SRAM", "Speicherplätze & SRAM", "Emplacements de sauvegarde & SRAM", "即时存档槽位与电池存档", "세이브 슬롯 및 SRAM", "ステートセーブスロット・SRAM", "Ranuras de guardado y SRAM", "Slot di salvataggio e SRAM", "Slots de Save State e SRAM", "सेव स्टेट स्लॉट और SRAM")
        row("saves_slot", "Слот", "Slot", "Slot", "Emplacement", "槽位", "슬롯", "スロット", "Ranura", "Slot", "Slot", "स्लॉट")
        row("saves_quick_save", "Сохранить состояние", "Save State", "Status speichern", "Sauvegarder l'état", "写入即时存档", "상태 저장", "ステート保存", "Guardar estado", "Salva stato", "Salvar estado", "स्टेट सहेजें")
        row("saves_quick_load", "Загрузить состояние", "Load State", "Status laden", "Charger l'état", "读取即时存档", "상태 불러오기", "ステート読込", "Cargar estado", "Carica stato", "Carregar estado", "स्टेट लोड करें")
        row("saves_empty_slot", "Слот пуст", "Empty Slot", "Leerer Slot", "Emplacement vide", "空槽位", "빈 슬롯", "空きスロット", "Ranura vacía", "Slot vuoto", "Slot vazio", "खाली स्लॉट")
        row("saves_delete", "Удалить слот", "Delete State", "Status löschen", "Supprimer l'état", "删除存档", "세이브 삭제", "セーブ削除", "Eliminar estado", "Elimina stato", "Excluir estado", "सेव हटाएं")

        // Settings & Auto-Update
        row("settings_title", "Параметры студии STORM RETROARCH", "STORM RETROARCH Studio Settings", "STORM RETROARCH Einstellungen", "Paramètres STORM RETROARCH", "STORM RETROARCH 全局设置", "STORM RETROARCH 스튜디오 설정", "STORM RETROARCH スタジオ設定", "Ajustes de STORM RETROARCH", "Impostazioni STORM RETROARCH", "Configurações do STORM RETROARCH", "STORM RETROARCH स्टूडियो सेटिंग्स")
        row("settings_theme", "Визуальная тема (9 тем)", "Visual Theme (9 Themes)", "Design-Thema (9 Themen)", "Thème visuel (9 thèmes)", "视觉主题 (9款主题)", "비주얼 테마 (9종)", "ビジュアルテーマ (全9種)", "Tema visual (9 temas)", "Tema visivo (9 temi)", "Tema visual (9 temas)", "विज़ुअल थीम (9 थीम)")
        row("settings_accent", "Акцентный цвет (13 палитр)", "Accent Color (13 Palettes)", "Akzentfarbe (13 Farben)", "Couleur d'accentuation (13)", "强调色 (13种色彩)", "포인트 색상 (13종)", "アクセントカラー (13色)", "Color de acento (13)", "Colore accento (13)", "Cor de destaque (13)", "एक्सेंट रंग (13 पैलेट)")
        row("settings_font", "Шрифтовой профиль (10 шрифтов)", "Typography Profile (10 Fonts)", "Schriftprofil (10 Schriften)", "Police d'interface (10)", "字体配置 (10款字体)", "글꼴 프로필 (10종)", "フォントプロファイル (10種)", "Tipografía (10 fuentes)", "Profilo carattere (10)", "Perfil de fonte (10)", "फ़ॉन्ट प्रोफ़ाइल (10 फ़ॉन्ट)")
        row("settings_language", "Язык интерфейса (11 языков)", "Interface Language (11 Languages)", "Oberflächensprache (11 Sprachen)", "Langue de l'interface (11)", "界面语言 (11种语言)", "인터페이스 언어 (11개국어)", "表示言語 (11言語)", "Idioma de interfaz (11)", "Lingua interfaccia (11)", "Idioma da interface (11)", "इंटरफ़ेस भाषा (11 भाषाएं)")
        row("settings_vsync", "Вертикальная синхронизация (VSync)", "Vertical Sync (VSync)", "Vertikale Synchronisation", "Synchronisation verticale", "垂直同步 (VSync)", "수직 동기화 (VSync)", "垂直同期 (VSync)", "Sincronización vertical", "Sincronia verticale (VSync)", "Sincronização vertical", "वर्टिकल सिंक (VSync)")
        row("settings_integer_scale", "Целочисленное масштабирование пикселей", "Pixel-Exact Integer Scaling", "Ganzzahlige Pixel-Skalierung", "Mise à l'échelle entière", "整数倍像素缩放", "정수 배율 픽셀 스케일링", "整数ピクセルスケーリング", "Escalado entero de píxeles", "Scala intera pixel", "Escala inteira de pixels", "पिक्सेल पूर्णांक स्केलिंग")
        row("settings_fps_hud", "Счётчик кадров FPS / Время кадра", "FPS & Frame-Time HUD", "FPS- & Frametime-Anzeige", "Compteur FPS & Temps d'image", "显示帧率与帧时间 HUD", "FPS 및 프레임 타임 표시", "FPS・フレームタイム表示", "Contador FPS en pantalla", "Indicatore FPS su schermo", "Contador de FPS na tela", "FPS और फ़्रेम-टाइम HUD")
        row("settings_audio_latency", "Низкая задержка звука", "Low-Latency Audio Engine", "Audio mit niedriger Latenz", "Audio basse latence", "低延迟音频引擎", "저지연 오디오 엔진", "低遅延オーディオエンジン", "Audio de baja latencia", "Audio a bassa latenza", "Áudio de baixa latência", "कम विलंबता ऑडियो")
        row("settings_runahead", "Опережение ввода (Run-Ahead 1 кадр)", "Input Run-Ahead (1 Frame)", "Run-Ahead (1 Frame)", "Run-Ahead (1 image)", "提前运行降低输入延迟 (1帧)", "런어헤드 입력 지연 감소 (1프레임)", "先行入力 Run-Ahead (1フレーム)", "Run-Ahead (1 fotograma)", "Run-Ahead (1 fotogramma)", "Run-Ahead (1 quadro)", "रन-अहेड इनपुट (1 फ़्रेम)")
        row("settings_storage_root", "Корневая папка STORM RETROARCH", "STORM RETROARCH Root Folder", "STORM RETROARCH Stammordner", "Dossier racine STORM RETROARCH", "STORM RETROARCH 根目录", "STORM RETROARCH 루트 폴더", "STORM RETROARCH ルートフォルダ", "Carpeta raíz STORM RETROARCH", "Cartella principale STORM RETROARCH", "Pasta raiz STORM RETROARCH", "STORM RETROARCH रूट फ़ोल्डर")
        row("settings_check_updates", "Проверить обновления APK", "Check for APK Updates", "Nach APK-Updates suchen", "Vérifier mises à jour APK", "检查 APK 版本更新", "APK 업데이트 확인", "APKの更新を確認", "Buscar actualizaciones APK", "Controlla aggiornamenti APK", "Verificar atualizações APK", "APK अपडेट जांचें")
        row("update_checking", "Проверка релизов GitHub...", "Checking GitHub releases...", "Prüfe GitHub-Releases...", "Vérification GitHub...", "正在检查 GitHub 最新版本...", "GitHub 릴리스 확인 중...", "GitHubリリースを確認中...", "Comprobando versiones en GitHub...", "Controllo versioni GitHub...", "Verificando versões no GitHub...", "GitHub रिलीज़ जांची जा रही है...")
        row("update_up_to_date", "Установлена актуальная версия", "You have the latest version", "Aktuellste Version installiert", "Vous avez la dernière version", "当前已是最新版本", "최신 버전이 설치되어 있습니다", "最新バージョンを使用中です", "Ya tienes la última versión", "Hai già l'ultima versione", "Você já tem a versão mais recente", "आपके पास नवीनतम संस्करण है")
        row("update_available", "Доступно обновление", "Update available", "Update verfügbar", "Mise à jour disponible", "发现新版本", "업데이트 가능", "新しい更新があります", "Actualización disponible", "Aggiornamento disponibile", "Atualização disponível", "नया अपडेट उपलब्ध है")
        row("update_download_apk", "Скачать APK обновления", "Download Update APK", "Update-APK herunterladen", "Télécharger l'APK", "下载更新 APK", "업데이트 APK 다운로드", "更新APKをダウンロード", "Descargar APK", "Scarica APK", "Baixar APK", "अपडेट APK डाउनलोड करें")
        row("update_ready_install", "APK сохранён в папке updates", "APK saved to updates folder", "APK im Ordner updates gespeichert", "APK enregistré dans updates", "安装包已保存至 updates 目录", "updates 폴더에 APK 저장 완료", "updatesフォルダにAPKを保存しました", "APK guardado en carpeta updates", "APK salvato nella cartella updates", "APK salvo na pasta updates", "APK updates फ़ोल्डर में सहेजा गया")

        // Common Dialog Buttons
        row("btn_ok", "ОК", "OK", "OK", "OK", "确定", "확인", "OK", "Aceptar", "OK", "OK", "ठीक है")
        row("btn_cancel", "Отмена", "Cancel", "Abbrechen", "Annuler", "取消", "취소", "キャンセル", "Cancelar", "Annulla", "Cancelar", "रद्द करें")
        row("btn_close", "Закрыть", "Close", "Schließen", "Fermer", "关闭", "닫기", "閉じる", "Cerrar", "Chiudi", "Fechar", "बंद करें")
        row("btn_apply", "Применить", "Apply", "Anwenden", "Appliquer", "应用", "적용", "適用", "Aplicar", "Applica", "Aplicar", "लागू करें")
        row("btn_reset", "Сброс", "Reset", "Zurücksetzen", "Réinitialiser", "重置", "초기화", "リセット", "Restablecer", "Ripristina", "Redefinir", "रीसेट")

        maps
    }
}
