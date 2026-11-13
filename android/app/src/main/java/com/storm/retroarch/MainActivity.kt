package com.storm.retroarch

import android.Manifest
import android.annotation.SuppressLint
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.Color
import android.hardware.input.InputManager
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.Environment
import android.provider.Settings
import android.view.View
import android.view.ViewGroup
import android.view.Window
import android.view.WindowManager
import android.webkit.WebChromeClient
import android.webkit.WebResourceRequest
import android.webkit.WebResourceResponse
import android.webkit.WebSettings
import android.webkit.WebView
import android.webkit.WebViewClient
import android.widget.FrameLayout
import androidx.activity.ComponentActivity
import androidx.activity.OnBackPressedCallback
import androidx.activity.result.contract.ActivityResultContracts
import org.json.JSONObject
import java.io.BufferedInputStream
import java.io.File
import java.io.FileInputStream
import java.net.URLDecoder
import java.util.Locale

class MainActivity : ComponentActivity() {

    companion object {
        private const val BG_STUDIO_HEX = 0xFF0D0B18.toInt()
        private const val LOCAL_ASSET_HOST = "stormlocal.app"
        private const val LOCAL_SCHEME = "stormfile"
        private const val ENTRY_ASSET_URL = "file:///android_asset/ui/index.html"
    }

    private lateinit var rootContainer: FrameLayout
    private lateinit var webView: WebView
    private lateinit var stormBridge: StormNativeBridge
    private var controllerDeviceListener: InputManager.InputDeviceListener? = null
    private var hasCompletedFirstResume = false

    // === ActivityResultContracts Wired into StormNativeBridge ===

    private val romFolderPickerLauncher = registerForActivityResult(
        ActivityResultContracts.OpenDocumentTree()
    ) { uri: Uri? ->
        if (uri != null) {
            takePersistableTreePermissionSafe(uri)
            stormBridge.onRomFolderPicked(uri)
        }
    }

    private val singleRomPickerLauncher = registerForActivityResult(
        ActivityResultContracts.OpenDocument()
    ) { uri: Uri? ->
        if (uri != null) {
            takePersistableDocPermissionSafe(uri)
            stormBridge.onSingleRomPicked(uri)
        }
    }

    private val coreFilePickerLauncher = registerForActivityResult(
        ActivityResultContracts.OpenDocument()
    ) { uri: Uri? ->
        if (uri != null) {
            stormBridge.onCoreFilePicked(uri)
        }
    }

    private val biosFolderPickerLauncher = registerForActivityResult(
        ActivityResultContracts.OpenDocumentTree()
    ) { uri: Uri? ->
        if (uri != null) {
            takePersistableTreePermissionSafe(uri)
            stormBridge.onBiosFolderPicked(uri)
        }
    }

    private val biosFilePickerLauncher = registerForActivityResult(
        ActivityResultContracts.OpenDocument()
    ) { uri: Uri? ->
        if (uri != null) {
            stormBridge.onBiosFilePicked(uri)
        }
    }

    private val savesZipPickerLauncher = registerForActivityResult(
        ActivityResultContracts.OpenDocument()
    ) { uri: Uri? ->
        if (uri != null) {
            stormBridge.onSavesZipPicked(uri)
        }
    }

    private val patchFilePickerLauncher = registerForActivityResult(
        ActivityResultContracts.OpenDocument()
    ) { uri: Uri? ->
        if (uri != null) {
            stormBridge.onPatchFilePicked(uri)
        }
    }

    private val manageAllFilesLauncher = registerForActivityResult(
        ActivityResultContracts.StartActivityForResult()
    ) {
        stormBridge.onStoragePermissionReturned()
    }

    private val legacyStoragePermsLauncher = registerForActivityResult(
        ActivityResultContracts.RequestMultiplePermissions()
    ) {
        stormBridge.onStoragePermissionReturned()
    }

    @SuppressLint("SetJavaScriptEnabled")
    override fun onCreate(savedInstanceState: Bundle?) {
        requestWindowFeature(Window.FEATURE_NO_TITLE)
        super.onCreate(savedInstanceState)

        configureWindowBarsAndCutout()
        applyPreferredRefreshRate(enableHighRefresh = true)

        StormThemeEngine.loadFromPrefs(this)
        StormLocalization.loadFromPrefs(this)

        stormBridge = StormNativeBridge(this)

        rootContainer = FrameLayout(this).apply {
            setBackgroundColor(BG_STUDIO_HEX)
        }

        webView = WebView(this).apply {
            setBackgroundColor(BG_STUDIO_HEX)
            setLayerType(View.LAYER_TYPE_HARDWARE, null)
            overScrollMode = View.OVER_SCROLL_NEVER
            isVerticalScrollBarEnabled = false
            isHorizontalScrollBarEnabled = false

            settings.apply {
                javaScriptEnabled = true
                domStorageEnabled = true
                databaseEnabled = true
                allowFileAccess = true
                allowContentAccess = true
                @Suppress("DEPRECATION")
                allowFileAccessFromFileURLs = true
                @Suppress("DEPRECATION")
                allowUniversalAccessFromFileURLs = true
                mediaPlaybackRequiresUserGesture = false
                mixedContentMode = WebSettings.MIXED_CONTENT_ALWAYS_ALLOW
                cacheMode = WebSettings.LOAD_DEFAULT
                textZoom = 100
                setSupportZoom(false)
                builtInZoomControls = false
                displayZoomControls = false
            }

            webChromeClient = WebChromeClient()
            webViewClient = object : WebViewClient() {
                override fun shouldInterceptRequest(
                    view: WebView?,
                    request: WebResourceRequest?
                ): WebResourceResponse? {
                    val reqUri = request?.url ?: return null
                    interceptLocalStudioAsset(reqUri)?.let { return it }
                    return super.shouldInterceptRequest(view, request)
                }
            }

            addJavascriptInterface(stormBridge, "StormBridge")
        }

        rootContainer.addView(
            webView,
            FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.MATCH_PARENT
            )
        )
        setContentView(rootContainer)

        registerBackHandler()
        registerLiveControllerListener()

        webView.loadUrl(ENTRY_ASSET_URL)
    }

    private fun configureWindowBarsAndCutout() {
        window.statusBarColor = BG_STUDIO_HEX
        window.navigationBarColor = BG_STUDIO_HEX
        window.setBackgroundDrawableResource(android.R.color.black)

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
            window.attributes = window.attributes.apply {
                layoutInDisplayCutoutMode =
                    WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES
            }
        }
    }

    fun applyPreferredRefreshRate(enableHighRefresh: Boolean) {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.M) return
        try {
            @Suppress("DEPRECATION")
            val display = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                this.display
            } else {
                windowManager.defaultDisplay
            } ?: return

            val modes = display.supportedModes
            if (modes.isNullOrEmpty()) return

            val chosen = if (enableHighRefresh) {
                modes.maxWithOrNull(
                    compareBy<android.view.Display.Mode> { it.refreshRate }
                        .thenBy { it.physicalWidth * it.physicalHeight }
                )
            } else {
                modes.minByOrNull { kotlin.math.abs(it.refreshRate - 60f) }
            } ?: return

            window.attributes = window.attributes.apply {
                preferredDisplayModeId = chosen.modeId
            }
        } catch (_: Throwable) {
        }
    }

    /**
     * Zero-latency local file responder for cover PNGs (/storage/emulated/0/STORM RETROARCH/thumbnails/...)
     * and save-state / gallery screenshots via https://stormlocal.app/file?path=... and stormfile://...
     */
    private fun interceptLocalStudioAsset(uri: Uri): WebResourceResponse? {
        val scheme = uri.scheme?.lowercase(Locale.ROOT) ?: return null
        val host = uri.host?.lowercase(Locale.ROOT) ?: ""

        val rawPath: String? = when {
            scheme == "https" && host == LOCAL_ASSET_HOST -> {
                uri.getQueryParameter("path") ?: uri.path
            }
            scheme == LOCAL_SCHEME -> {
                val combined = buildString {
                    if (!uri.host.isNullOrBlank() && uri.host != "localhost") {
                        append("/").append(uri.host)
                    }
                    if (!uri.path.isNullOrBlank()) {
                        append(uri.path)
                    }
                }
                uri.getQueryParameter("path") ?: combined
            }
            else -> null
        }

        if (rawPath.isNullOrBlank()) return null

        val decodedPath = try {
            URLDecoder.decode(rawPath, "UTF-8")
        } catch (_: Throwable) {
            rawPath
        }

        val targetFile = File(decodedPath)
        val headers = mapOf(
            "Access-Control-Allow-Origin" to "*",
            "Cache-Control" to "public, max-age=3600"
        )

        if (!targetFile.exists() || !targetFile.isFile || !targetFile.canRead()) {
            return WebResourceResponse("text/plain", "UTF-8", 404, "Not Found", headers, null)
        }

        val mime = when (targetFile.extension.lowercase(Locale.ROOT)) {
            "png" -> "image/png"
            "jpg", "jpeg" -> "image/jpeg"
            "webp" -> "image/webp"
            "svg" -> "image/svg+xml"
            "json" -> "application/json"
            else -> "application/octet-stream"
        }

        return try {
            val stream = BufferedInputStream(FileInputStream(targetFile), 16 * 1024)
            WebResourceResponse(mime, null, 200, "OK", headers, stream)
        } catch (_: Throwable) {
            WebResourceResponse("text/plain", "UTF-8", 500, "Error", headers, null)
        }
    }

    private fun registerBackHandler() {
        onBackPressedDispatcher.addCallback(
            this,
            object : OnBackPressedCallback(true) {
                override fun handleOnBackPressed() {
                    if (!::webView.isInitialized) {
                        moveTaskToBack(true)
                        return
                    }
                    val jsCheck = "(function(){" +
                        "if (window.handleAndroidBack) return Boolean(window.handleAndroidBack());" +
                        "var openModal = document.querySelector('.modal-backdrop:not([hidden])');" +
                        "if (openModal) { openModal.hidden = true; return true; }" +
                        "return false;" +
                        "})()"
                    webView.evaluateJavascript(jsCheck) { result ->
                        val handled = result?.trim()?.equals("true", ignoreCase = true) == true
                        if (!handled) {
                            if (webView.canGoBack()) {
                                webView.goBack()
                            } else {
                                moveTaskToBack(true)
                            }
                        }
                    }
                }
            }
        )
    }

    private fun registerLiveControllerListener() {
        try {
            controllerDeviceListener = MobileControllerProfileManager.registerControllerListener(this) {
                if (::stormBridge.isInitialized) {
                    stormBridge.emitStormEvent(
                        "bootstrapUpdated",
                        JSONObject().put("controllers", stormBridge.serializeControllersArray())
                    )
                }
            }
        } catch (_: Throwable) {
        }
    }

    fun evaluateJsOnMain(script: String) {
        if (isFinishing || isDestroyed || !::webView.isInitialized) return
        webView.evaluateJavascript(script, null)
    }

    // === Picker & Permission Launchers Called by StormNativeBridge ===

    fun openRomFolderPicker() {
        try {
            romFolderPickerLauncher.launch(null)
        } catch (_: Throwable) {
        }
    }

    fun openSingleRomPicker() {
        try {
            singleRomPickerLauncher.launch(arrayOf("*/*"))
        } catch (_: Throwable) {
        }
    }

    fun openCoreFilePicker() {
        try {
            coreFilePickerLauncher.launch(arrayOf("*/*"))
        } catch (_: Throwable) {
        }
    }

    fun openBiosFolderPicker() {
        try {
            biosFolderPickerLauncher.launch(null)
        } catch (_: Throwable) {
        }
    }

    fun openBiosFilePicker() {
        try {
            biosFilePickerLauncher.launch(arrayOf("*/*"))
        } catch (_: Throwable) {
        }
    }

    fun openSavesZipPicker() {
        try {
            savesZipPickerLauncher.launch(arrayOf("application/zip", "application/octet-stream", "*/*"))
        } catch (_: Throwable) {
        }
    }

    fun openPatchFilePicker() {
        try {
            patchFilePickerLauncher.launch(arrayOf("*/*"))
        } catch (_: Throwable) {
        }
    }

    fun requestAllFilesStoragePermission() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            if (Environment.isExternalStorageManager()) {
                stormBridge.onStoragePermissionReturned()
                return
            }
            try {
                val intent = Intent(
                    Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION,
                    Uri.parse("package:$packageName")
                )
                manageAllFilesLauncher.launch(intent)
            } catch (_: Throwable) {
                try {
                    manageAllFilesLauncher.launch(Intent(Settings.ACTION_MANAGE_ALL_FILES_ACCESS_PERMISSION))
                } catch (_: Throwable) {
                }
            }
        } else {
            val needed = arrayOf(
                Manifest.permission.READ_EXTERNAL_STORAGE,
                Manifest.permission.WRITE_EXTERNAL_STORAGE
            ).filter { checkSelfPermission(it) != PackageManager.PERMISSION_GRANTED }
            if (needed.isEmpty()) {
                stormBridge.onStoragePermissionReturned()
            } else {
                legacyStoragePermsLauncher.launch(needed.toTypedArray())
            }
        }
    }

    fun launchApkPackageInstaller(apkFile: File) {
        try {
            val contentUri = StormApkFileProvider.getUriForFile(this, apkFile)
            val installIntent = Intent(Intent.ACTION_VIEW).apply {
                setDataAndType(contentUri, "application/vnd.android.package-archive")
                addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            }
            startActivity(installIntent)
        } catch (_: Throwable) {
        }
    }

    private fun takePersistableTreePermissionSafe(uri: Uri) {
        try {
            val flags = Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_GRANT_WRITE_URI_PERMISSION
            contentResolver.takePersistableUriPermission(uri, flags)
        } catch (_: Throwable) {
            try {
                contentResolver.takePersistableUriPermission(uri, Intent.FLAG_GRANT_READ_URI_PERMISSION)
            } catch (_: Throwable) {
            }
        }
    }

    private fun takePersistableDocPermissionSafe(uri: Uri) {
        try {
            contentResolver.takePersistableUriPermission(uri, Intent.FLAG_GRANT_READ_URI_PERMISSION)
        } catch (_: Throwable) {
        }
    }

    override fun onResume() {
        super.onResume()
        if (::webView.isInitialized) {
            webView.resumeTimers()
            webView.onResume()
            evaluateJsOnMain("window.__stormSetPaused?.(false);")
        }
        if (hasCompletedFirstResume && ::stormBridge.isInitialized) {
            // Sync save states & storage status after returning from EmulationActivity or system Settings
            val games = MobileLibraryAndCoverEngine.scanLibraryDirectories(applicationContext)
            val updatePayload = JSONObject()
                .put("storageGranted", stormBridge.hasAllFilesStorageAccess())
                .put("saves", stormBridge.serializeSaveSlotsOverview(games))
            stormBridge.emitStormEvent("bootstrapUpdated", updatePayload)
        }
        hasCompletedFirstResume = true
    }

    override fun onPause() {
        if (::webView.isInitialized) {
            evaluateJsOnMain("window.__stormSetPaused?.(true);")
            webView.onPause()
            webView.pauseTimers()
        }
        super.onPause()
    }

    override fun onStop() {
        if (::webView.isInitialized) {
            evaluateJsOnMain("window.__stormSetPaused?.(true);")
            webView.onPause()
            webView.pauseTimers()
        }
        super.onStop()
    }

    override fun onDestroy() {
        controllerDeviceListener?.let {
            MobileControllerProfileManager.unregisterControllerListener(this, it)
        }
        if (::webView.isInitialized) {
            webView.removeJavascriptInterface("StormBridge")
            rootContainer.removeAllViews()
            webView.destroy()
        }
        super.onDestroy()
    }
}
