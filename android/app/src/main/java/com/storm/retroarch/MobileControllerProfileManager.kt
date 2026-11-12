package com.storm.retroarch

import android.content.Context
import android.hardware.input.InputManager
import android.os.Handler
import android.os.Looper
import android.view.InputDevice
import android.view.KeyEvent
import android.view.MotionEvent
import org.json.JSONObject
import java.io.File
import java.nio.charset.StandardCharsets
import java.util.Locale
import java.util.concurrent.ConcurrentHashMap

data class MobileControllerDevice(
    val deviceId: Int,
    val descriptor: String,
    val name: String,
    val vendorId: Int,
    val productId: Int,
    val controllerType: String,
    val playerPort: Int
) {
    val vendorProductHex: String
        get() = String.format(Locale.US, "%04X:%04X", vendorId and 0xFFFF, productId and 0xFFFF)

    val profileKey: String
        get() {
            val safeName = name.trim().replace(Regex("\\s+"), "_")
            return "${vendorProductHex}_$safeName"
        }
}

data class MobileControllerProfile(
    val profileKey: String,
    val deviceName: String,
    val vendorId: Int,
    val productId: Int,
    val controllerType: String,
    val buttonBindings: MutableMap<String, Int>,
    val hotkeyBindings: MutableMap<String, Int>,
    var analogDeadzone: Float = 0.18f,
    var requireSelectForHotkeys: Boolean = false
)

object MobileControllerProfileManager {

    const val TYPE_XBOX = "Xbox"
    const val TYPE_PLAYSTATION = "DualSense/DualShock"
    const val TYPE_SWITCH_PRO = "Switch Pro"
    const val TYPE_8BITDO = "8BitDo"
    const val TYPE_ANDROID_HANDHELD = "Android Handheld"
    const val TYPE_ARCADE_STICK = "Arcade Stick"
    const val TYPE_GENERIC = "Generic Gamepad"

    // Standard Libretro RETRO_DEVICE_ID_JOYPAD_* indices (0..15)
    const val RETRO_ID_B = 0
    const val RETRO_ID_Y = 1
    const val RETRO_ID_SELECT = 2
    const val RETRO_ID_START = 3
    const val RETRO_ID_UP = 4
    const val RETRO_ID_DOWN = 5
    const val RETRO_ID_LEFT = 6
    const val RETRO_ID_RIGHT = 7
    const val RETRO_ID_A = 8
    const val RETRO_ID_X = 9
    const val RETRO_ID_L1 = 10
    const val RETRO_ID_R1 = 11
    const val RETRO_ID_L2 = 12
    const val RETRO_ID_R2 = 13
    const val RETRO_ID_L3 = 14
    const val RETRO_ID_R3 = 15

    val RETROPAD_BUTTON_ORDER: List<Pair<String, Int>> = listOf(
        "B" to RETRO_ID_B,
        "Y" to RETRO_ID_Y,
        "SELECT" to RETRO_ID_SELECT,
        "START" to RETRO_ID_START,
        "UP" to RETRO_ID_UP,
        "DOWN" to RETRO_ID_DOWN,
        "LEFT" to RETRO_ID_LEFT,
        "RIGHT" to RETRO_ID_RIGHT,
        "A" to RETRO_ID_A,
        "X" to RETRO_ID_X,
        "L1" to RETRO_ID_L1,
        "R1" to RETRO_ID_R1,
        "L2" to RETRO_ID_L2,
        "R2" to RETRO_ID_R2,
        "L3" to RETRO_ID_L3,
        "R3" to RETRO_ID_R3
    )

    // Exact 6 hotkey action identifiers
    const val HOTKEY_QUICK_SAVE = "Quick Save"
    const val HOTKEY_QUICK_LOAD = "Quick Load"
    const val HOTKEY_REWIND = "Rewind"
    const val HOTKEY_FAST_FORWARD = "Fast-Forward"
    const val HOTKEY_QUICK_MENU = "Quick Menu Drawer"
    const val HOTKEY_SCREENSHOT = "Screenshot"

    val HOTKEY_ACTIONS: List<String> = listOf(
        HOTKEY_QUICK_SAVE,
        HOTKEY_QUICK_LOAD,
        HOTKEY_REWIND,
        HOTKEY_FAST_FORWARD,
        HOTKEY_QUICK_MENU,
        HOTKEY_SCREENSHOT
    )

    private val loadedProfiles = ConcurrentHashMap<String, MobileControllerProfile>()
    @Volatile
    private var initializedFromDisk = false

    fun getPublicProfilesFile(): File {
        val dir = File(MobileCoreRegistry.STORM_PUBLIC_ROOT, "autoconfig")
        try {
            if (!dir.exists()) dir.mkdirs()
        } catch (_: Throwable) {
        }
        return File(dir, "mobile_profiles.json")
    }

    private fun getFallbackInternalProfilesFile(context: Context?): File? {
        if (context == null) return null
        val dir = File(context.applicationContext.filesDir, "autoconfig")
        if (!dir.exists()) dir.mkdirs()
        return File(dir, "mobile_profiles.json")
    }

    /**
     * Enumerates all connected physical gamepads, Bluetooth/USB controllers, handheld controls, and arcade sticks.
     */
    fun enumerateConnectedControllers(context: Context? = null): List<MobileControllerDevice> {
        ensureProfilesLoaded(context)
        val result = ArrayList<MobileControllerDevice>()
        val ids = InputDevice.getDeviceIds()
        var port = 0

        for (id in ids) {
            val dev = InputDevice.getDevice(id) ?: continue
            if (dev.isVirtual) continue
            val sources = dev.sources
            val isGamepad = (sources and InputDevice.SOURCE_GAMEPAD) == InputDevice.SOURCE_GAMEPAD
            val isJoystick = (sources and InputDevice.SOURCE_JOYSTICK) == InputDevice.SOURCE_JOYSTICK
            if (!isGamepad && !isJoystick) continue

            // Filter out built-in volume/power GPIO buttons masquerading as input devices
            val lowerName = dev.name.lowercase(Locale.ROOT)
            if (lowerName.contains("gpio-keys") || lowerName.contains("qpnp_pon") || lowerName.contains("headset")) {
                continue
            }

            val type = classifyControllerType(dev.vendorId, dev.productId, dev.name)
            val info = MobileControllerDevice(
                deviceId = dev.id,
                descriptor = dev.descriptor ?: "dev_${dev.id}",
                name = dev.name ?: "Controller #${port + 1}",
                vendorId = dev.vendorId,
                productId = dev.productId,
                controllerType = type,
                playerPort = port
            )
            result.add(info)
            // Ensure profile exists in memory and persisted on disk
            getOrCreateProfile(info, context)
            port++
        }

        return result
    }

    fun classifyControllerType(vendorId: Int, productId: Int, rawName: String?): String {
        val name = (rawName ?: "").lowercase(Locale.ROOT)
        return when {
            name.contains("arcade") || name.contains("fightstick") || name.contains("qanba") ||
                name.contains("hitbox") || name.contains("hori rap") || name.contains("real arcade") ||
                name.contains("mad catz") || name.contains("mayflash f") ->
                TYPE_ARCADE_STICK

            vendorId == 0x2DC8 || name.contains("8bitdo") ->
                TYPE_8BITDO

            vendorId == 0x054C || name.contains("dualsense") || name.contains("dualshock") ||
                name.contains("playstation") || (name.contains("wireless controller") && vendorId == 0x054C) ->
                TYPE_PLAYSTATION

            vendorId == 0x057E || name.contains("switch pro") || name.contains("pro controller") ||
                name.contains("joy-con") || name.contains("nintendo") ->
                TYPE_SWITCH_PRO

            name.contains("odin") || name.contains("retroid") || name.contains("anbernic") ||
                name.contains("ayaneo") || name.contains("razer kishi") || name.contains("gamesir") ||
                name.contains("backbone") || name.contains("g cloud") || name.contains("gpd") ||
                name.contains("xbox cloud") && name.contains("handheld") ->
                TYPE_ANDROID_HANDHELD

            vendorId == 0x045E || name.contains("xbox") || name.contains("x-box") ->
                TYPE_XBOX

            else -> TYPE_GENERIC
        }
    }

    fun createDefaultProfile(
        profileKey: String,
        deviceName: String,
        vendorId: Int,
        productId: Int,
        controllerType: String
    ): MobileControllerProfile {
        // Nintendo/8BitDo layouts match SNES physical A/B & X/Y labels directly;
        // Xbox/PlayStation map physical South (BUTTON_A) -> RetroPad B and East (BUTTON_B) -> RetroPad A
        // or match physical legends cleanly so confirm/jump feel natural out-of-the-box.
        val isNintendoLegend = controllerType == TYPE_SWITCH_PRO || controllerType == TYPE_8BITDO
        val buttons = LinkedHashMap<String, Int>().apply {
            put("B", if (isNintendoLegend) KeyEvent.KEYCODE_BUTTON_B else KeyEvent.KEYCODE_BUTTON_A)
            put("A", if (isNintendoLegend) KeyEvent.KEYCODE_BUTTON_A else KeyEvent.KEYCODE_BUTTON_B)
            put("Y", if (isNintendoLegend) KeyEvent.KEYCODE_BUTTON_Y else KeyEvent.KEYCODE_BUTTON_X)
            put("X", if (isNintendoLegend) KeyEvent.KEYCODE_BUTTON_X else KeyEvent.KEYCODE_BUTTON_Y)
            put("SELECT", KeyEvent.KEYCODE_BUTTON_SELECT)
            put("START", KeyEvent.KEYCODE_BUTTON_START)
            put("UP", KeyEvent.KEYCODE_DPAD_UP)
            put("DOWN", KeyEvent.KEYCODE_DPAD_DOWN)
            put("LEFT", KeyEvent.KEYCODE_DPAD_LEFT)
            put("RIGHT", KeyEvent.KEYCODE_DPAD_RIGHT)
            put("L1", KeyEvent.KEYCODE_BUTTON_L1)
            put("R1", KeyEvent.KEYCODE_BUTTON_R1)
            put("L2", KeyEvent.KEYCODE_BUTTON_L2)
            put("R2", KeyEvent.KEYCODE_BUTTON_R2)
            put("L3", KeyEvent.KEYCODE_BUTTON_THUMBL)
            put("R3", KeyEvent.KEYCODE_BUTTON_THUMBR)
        }

        val hotkeys = LinkedHashMap<String, Int>().apply {
            put(HOTKEY_QUICK_MENU, KeyEvent.KEYCODE_BUTTON_MODE)
            put(HOTKEY_QUICK_SAVE, KeyEvent.KEYCODE_F2)
            put(HOTKEY_QUICK_LOAD, KeyEvent.KEYCODE_F4)
            put(HOTKEY_REWIND, KeyEvent.KEYCODE_MEDIA_REWIND)
            put(HOTKEY_FAST_FORWARD, KeyEvent.KEYCODE_MEDIA_FAST_FORWARD)
            put(HOTKEY_SCREENSHOT, KeyEvent.KEYCODE_SYSRQ)
        }

        return MobileControllerProfile(
            profileKey = profileKey,
            deviceName = deviceName,
            vendorId = vendorId,
            productId = productId,
            controllerType = controllerType,
            buttonBindings = buttons,
            hotkeyBindings = hotkeys,
            analogDeadzone = if (controllerType == TYPE_ARCADE_STICK) 0.10f else 0.18f,
            requireSelectForHotkeys = false
        )
    }

    fun ensureProfilesLoaded(context: Context? = null) {
        if (!initializedFromDisk) {
            loadAllProfiles(context)
        }
    }

    fun getOrCreateProfile(device: InputDevice, context: Context? = null): MobileControllerProfile {
        val type = classifyControllerType(device.vendorId, device.productId, device.name)
        val info = MobileControllerDevice(
            deviceId = device.id,
            descriptor = device.descriptor ?: "dev_${device.id}",
            name = device.name ?: "Controller",
            vendorId = device.vendorId,
            productId = device.productId,
            controllerType = type,
            playerPort = 0
        )
        return getOrCreateProfile(info, context)
    }

    fun getOrCreateProfile(deviceInfo: MobileControllerDevice, context: Context? = null): MobileControllerProfile {
        ensureProfilesLoaded(context)
        loadedProfiles[deviceInfo.profileKey]?.let { return it }

        val created = createDefaultProfile(
            profileKey = deviceInfo.profileKey,
            deviceName = deviceInfo.name,
            vendorId = deviceInfo.vendorId,
            productId = deviceInfo.productId,
            controllerType = deviceInfo.controllerType
        )
        loadedProfiles[created.profileKey] = created
        saveAllProfiles(context)
        return created
    }

    fun saveProfile(profile: MobileControllerProfile, context: Context? = null): Boolean {
        ensureProfilesLoaded(context)
        loadedProfiles[profile.profileKey] = profile
        return saveAllProfiles(context)
    }

    fun resetProfileToDefaults(deviceInfo: MobileControllerDevice, context: Context? = null): MobileControllerProfile {
        val fresh = createDefaultProfile(
            profileKey = deviceInfo.profileKey,
            deviceName = deviceInfo.name,
            vendorId = deviceInfo.vendorId,
            productId = deviceInfo.productId,
            controllerType = deviceInfo.controllerType
        )
        loadedProfiles[fresh.profileKey] = fresh
        saveAllProfiles(context)
        return fresh
    }

    fun loadAllProfiles(context: Context? = null): Map<String, MobileControllerProfile> {
        initializedFromDisk = true
        val pubFile = getPublicProfilesFile()
        val sourceFile = when {
            pubFile.exists() && pubFile.canRead() && pubFile.length() > 2L -> pubFile
            else -> getFallbackInternalProfilesFile(context)?.takeIf { it.exists() && it.length() > 2L }
        } ?: return loadedProfiles

        try {
            val jsonStr = sourceFile.readText(StandardCharsets.UTF_8)
            val root = JSONObject(jsonStr)
            val profilesObj = root.optJSONObject("profiles") ?: return loadedProfiles
            val keys = profilesObj.keys()
            while (keys.hasNext()) {
                val pKey = keys.next()
                val obj = profilesObj.optJSONObject(pKey) ?: continue
                val devName = obj.optString("deviceName", pKey)
                val vid = obj.optInt("vendorId", 0)
                val pid = obj.optInt("productId", 0)
                val cType = obj.optString("controllerType", classifyControllerType(vid, pid, devName))
                val deadzone = obj.optDouble("analogDeadzone", 0.18).toFloat().coerceIn(0.05f, 0.50f)
                val reqSelect = obj.optBoolean("requireSelectForHotkeys", false)

                val baseDefault = createDefaultProfile(pKey, devName, vid, pid, cType)
                obj.optJSONObject("buttons")?.let { bJson ->
                    val bKeys = bJson.keys()
                    while (bKeys.hasNext()) {
                        val bName = bKeys.next()
                        baseDefault.buttonBindings[bName] = bJson.optInt(bName, 0)
                    }
                }
                obj.optJSONObject("hotkeys")?.let { hJson ->
                    val hKeys = hJson.keys()
                    while (hKeys.hasNext()) {
                        val hName = hKeys.next()
                        baseDefault.hotkeyBindings[hName] = hJson.optInt(hName, 0)
                    }
                }
                baseDefault.analogDeadzone = deadzone
                baseDefault.requireSelectForHotkeys = reqSelect
                loadedProfiles[pKey] = baseDefault
            }
        } catch (_: Throwable) {
        }
        return loadedProfiles
    }

    fun saveAllProfiles(context: Context? = null): Boolean {
        val root = JSONObject()
        root.put("version", "2.4.0")
        val profilesJson = JSONObject()

        for ((key, prof) in loadedProfiles) {
            val pObj = JSONObject()
            pObj.put("deviceName", prof.deviceName)
            pObj.put("vendorId", prof.vendorId)
            pObj.put("productId", prof.productId)
            pObj.put("controllerType", prof.controllerType)
            pObj.put("analogDeadzone", prof.analogDeadzone.toDouble())
            pObj.put("requireSelectForHotkeys", prof.requireSelectForHotkeys)

            val bObj = JSONObject()
            for ((bName, code) in prof.buttonBindings) {
                bObj.put(bName, code)
            }
            pObj.put("buttons", bObj)

            val hObj = JSONObject()
            for ((hName, code) in prof.hotkeyBindings) {
                hObj.put(hName, code)
            }
            pObj.put("hotkeys", hObj)

            profilesJson.put(key, pObj)
        }
        root.put("profiles", profilesJson)

        val serialized = root.toString(2)
        var savedPrimary = false
        try {
            val pubFile = getPublicProfilesFile()
            pubFile.parentFile?.mkdirs()
            pubFile.writeText(serialized, StandardCharsets.UTF_8)
            savedPrimary = true
        } catch (_: Throwable) {
        }

        try {
            getFallbackInternalProfilesFile(context)?.let { fallback ->
                fallback.parentFile?.mkdirs()
                fallback.writeText(serialized, StandardCharsets.UTF_8)
                if (!savedPrimary) savedPrimary = true
            }
        } catch (_: Throwable) {
        }
        return savedPrimary
    }

    /**
     * Resolves an Android `KeyEvent` keyCode into a Libretro `RETRO_DEVICE_ID_JOYPAD_*` index (`0..15`),
     * using the active controller's saved profile.
     */
    fun mapKeyEventToRetroPadId(device: InputDevice?, keyCode: Int, context: Context? = null): Int? {
        val profile = if (device != null) {
            getOrCreateProfile(device, context)
        } else {
            createDefaultProfile("default", "Generic", 0, 0, TYPE_GENERIC)
        }

        for ((retroName, retroId) in RETROPAD_BUTTON_ORDER) {
            if (profile.buttonBindings[retroName] == keyCode) {
                return retroId
            }
        }
        return null
    }

    /**
     * Resolves an Android `KeyEvent` keyCode into a configured hotkey action (`Quick Save`, `Quick Load`,
     * `Rewind`, `Fast-Forward`, `Quick Menu Drawer`, `Screenshot`), if bound.
     */
    fun mapKeyEventToHotkey(
        device: InputDevice?,
        keyCode: Int,
        isSelectHeld: Boolean = false,
        context: Context? = null
    ): String? {
        val profile = if (device != null) {
            getOrCreateProfile(device, context)
        } else {
            return null
        }

        if (profile.requireSelectForHotkeys && !isSelectHeld && keyCode != KeyEvent.KEYCODE_BUTTON_MODE) {
            return null
        }

        for ((action, boundCode) in profile.hotkeyBindings) {
            if (boundCode != 0 && boundCode == keyCode) {
                return action
            }
        }
        return null
    }

    /**
     * Converts D-Pad HAT axes, Left Stick axes, and L2/R2 analog triggers from a `MotionEvent`
     * into a 16-bit Libretro RetroPad bitmask (`1 shl RETRO_ID_*`).
     */
    fun extractMotionRetroPadBitmask(event: MotionEvent, context: Context? = null): Int {
        val deadzone = event.device?.let { getOrCreateProfile(it, context).analogDeadzone } ?: 0.18f
        var mask = 0

        val hatX = event.getAxisValue(MotionEvent.AXIS_HAT_X)
        val hatY = event.getAxisValue(MotionEvent.AXIS_HAT_Y)
        val stickX = event.getAxisValue(MotionEvent.AXIS_X)
        val stickY = event.getAxisValue(MotionEvent.AXIS_Y)

        if (hatX < -0.5f || stickX < -deadzone) mask = mask or (1 shl RETRO_ID_LEFT)
        if (hatX > 0.5f || stickX > deadzone) mask = mask or (1 shl RETRO_ID_RIGHT)
        if (hatY < -0.5f || stickY < -deadzone) mask = mask or (1 shl RETRO_ID_UP)
        if (hatY > 0.5f || stickY > deadzone) mask = mask or (1 shl RETRO_ID_DOWN)

        val lTrigger = maxOf(
            event.getAxisValue(MotionEvent.AXIS_LTRIGGER),
            event.getAxisValue(MotionEvent.AXIS_BRAKE)
        )
        val rTrigger = maxOf(
            event.getAxisValue(MotionEvent.AXIS_RTRIGGER),
            event.getAxisValue(MotionEvent.AXIS_GAS)
        )
        if (lTrigger > 0.35f) mask = mask or (1 shl RETRO_ID_L2)
        if (rTrigger > 0.35f) mask = mask or (1 shl RETRO_ID_R2)

        return mask
    }

    fun registerControllerListener(
        context: Context,
        onControllersChanged: (List<MobileControllerDevice>) -> Unit
    ): InputManager.InputDeviceListener {
        val im = context.getSystemService(Context.INPUT_SERVICE) as InputManager
        val listener = object : InputManager.InputDeviceListener {
            override fun onInputDeviceAdded(deviceId: Int) {
                onControllersChanged(enumerateConnectedControllers(context))
            }

            override fun onInputDeviceRemoved(deviceId: Int) {
                onControllersChanged(enumerateConnectedControllers(context))
            }

            override fun onInputDeviceChanged(deviceId: Int) {
                onControllersChanged(enumerateConnectedControllers(context))
            }
        }
        im.registerInputDeviceListener(listener, Handler(Looper.getMainLooper()))
        return listener
    }

    fun unregisterControllerListener(context: Context, listener: InputManager.InputDeviceListener) {
        val im = context.getSystemService(Context.INPUT_SERVICE) as InputManager
        im.unregisterInputDeviceListener(listener)
    }

    fun keyCodeToDisplayName(keyCode: Int): String {
        if (keyCode == 0) return "Unbound"
        return when (keyCode) {
            KeyEvent.KEYCODE_BUTTON_A -> "BTN A (South)"
            KeyEvent.KEYCODE_BUTTON_B -> "BTN B (East)"
            KeyEvent.KEYCODE_BUTTON_X -> "BTN X (West)"
            KeyEvent.KEYCODE_BUTTON_Y -> "BTN Y (North)"
            KeyEvent.KEYCODE_BUTTON_L1 -> "L1 / LB"
            KeyEvent.KEYCODE_BUTTON_R1 -> "R1 / RB"
            KeyEvent.KEYCODE_BUTTON_L2 -> "L2 / LT"
            KeyEvent.KEYCODE_BUTTON_R2 -> "R2 / RT"
            KeyEvent.KEYCODE_BUTTON_THUMBL -> "L3 (Stick)"
            KeyEvent.KEYCODE_BUTTON_THUMBR -> "R3 (Stick)"
            KeyEvent.KEYCODE_BUTTON_SELECT -> "SELECT / BACK"
            KeyEvent.KEYCODE_BUTTON_START -> "START / OPTIONS"
            KeyEvent.KEYCODE_BUTTON_MODE -> "GUIDE / HOME"
            KeyEvent.KEYCODE_DPAD_UP -> "D-PAD UP"
            KeyEvent.KEYCODE_DPAD_DOWN -> "D-PAD DOWN"
            KeyEvent.KEYCODE_DPAD_LEFT -> "D-PAD LEFT"
            KeyEvent.KEYCODE_DPAD_RIGHT -> "D-PAD RIGHT"
            else -> KeyEvent.keyCodeToString(keyCode).removePrefix("KEYCODE_")
        }
    }
}
