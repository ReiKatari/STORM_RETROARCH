package com.storm.retroarch

import android.content.Context
import android.hardware.input.InputManager
import android.os.Handler
import android.os.Looper
import android.view.InputDevice
import android.view.KeyEvent
import android.view.MotionEvent
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.nio.charset.StandardCharsets
import java.util.Locale
import java.util.concurrent.ConcurrentHashMap
import kotlin.math.abs
import kotlin.math.roundToInt

data class MobileControllerDevice(
    val deviceId: Int,
    val descriptor: String,
    val name: String,
    val vendorId: Int,
    val productId: Int,
    val controllerType: String,
    val playerPort: Int,
    val rawName: String = name,
    val supportedAxes: List<Int> = emptyList()
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
    var requireSelectForHotkeys: Boolean = false,
    val axisBindings: MutableMap<String, Int> = LinkedHashMap(),
    var steeringDeadzone: Float = 0.04f,
    var triggerThreshold: Float = 0.25f,
    var chordModifier: String = MobileControllerProfileManager.CHORD_NONE
)

data class MobileAxisState(
    val lx: Int,
    val ly: Int,
    val rx: Int,
    val ry: Int,
    val l2: Int,
    val r2: Int
)

private data class KnownHardwareSpec(
    val displayName: String,
    val controllerType: String
)

object MobileControllerProfileManager {

    const val MAX_PORTS = 8

    const val TYPE_FLYDIGI = "Flydigi"
    const val TYPE_XBOX = "Xbox"
    const val TYPE_PLAYSTATION = "DualSense/DualShock"
    const val TYPE_SWITCH_PRO = "Switch Pro"
    const val TYPE_8BITDO = "8BitDo"
    const val TYPE_WHEEL = "Racing Wheel"
    const val TYPE_FLIGHT_STICK = "Flight Stick / HOTAS"
    const val TYPE_ARCADE_STICK = "Arcade Stick"
    const val TYPE_LEVERLESS = "Leverless / HitBox"
    const val TYPE_ANDROID_HANDHELD = "Android Handheld"
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

    // Logical analog axis keys persisted in axisBindings
    const val AXIS_KEY_LX = "LS_X"
    const val AXIS_KEY_LY = "LS_Y"
    const val AXIS_KEY_RX = "RS_X"
    const val AXIS_KEY_RY = "RS_Y"
    const val AXIS_KEY_L2 = "L2_AXIS"
    const val AXIS_KEY_R2 = "R2_AXIS"

    // Hotkey chord modifier modes
    const val CHORD_NONE = "NONE"
    const val CHORD_SELECT = "SELECT"
    const val CHORD_MODE = "MODE"
    const val CHORD_L3 = "L3"

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

    val RETROPAD_AXIS_ORDER: List<Pair<String, String>> = listOf(
        AXIS_KEY_LX to "Left Stick / Wheel X",
        AXIS_KEY_LY to "Left Stick / Pitch Y",
        AXIS_KEY_RX to "Right Stick / Look X",
        AXIS_KEY_RY to "Right Stick / Throttle Y",
        AXIS_KEY_L2 to "L2 Analog / Brake Pedal",
        AXIS_KEY_R2 to "R2 Analog / Gas Pedal"
    )

    // Hotkey action identifiers (6 core + 6 extended Desktop parity)
    const val HOTKEY_QUICK_SAVE = "Quick Save"
    const val HOTKEY_QUICK_LOAD = "Quick Load"
    const val HOTKEY_REWIND = "Rewind"
    const val HOTKEY_FAST_FORWARD = "Fast-Forward"
    const val HOTKEY_QUICK_MENU = "Quick Menu Drawer"
    const val HOTKEY_SCREENSHOT = "Screenshot"
    const val HOTKEY_PAUSE = "Pause / Resume"
    const val HOTKEY_RESET = "Reset Core"
    const val HOTKEY_SLOT_NEXT = "Next Save Slot"
    const val HOTKEY_SLOT_PREV = "Prev Save Slot"
    const val HOTKEY_MUTE = "Audio Mute"
    const val HOTKEY_SHADER_NEXT = "Next Shader"

    val HOTKEY_ACTIONS: List<String> = listOf(
        HOTKEY_QUICK_SAVE,
        HOTKEY_QUICK_LOAD,
        HOTKEY_REWIND,
        HOTKEY_FAST_FORWARD,
        HOTKEY_QUICK_MENU,
        HOTKEY_SCREENSHOT,
        HOTKEY_PAUSE,
        HOTKEY_RESET,
        HOTKEY_SLOT_NEXT,
        HOTKEY_SLOT_PREV,
        HOTKEY_MUTE,
        HOTKEY_SHADER_NEXT
    )

    // Known VID:PID signatures mirroring Desktop ControllerHardwareDatabase
    private val KNOWN_VID_PID: Map<Int, KnownHardwareSpec> = mapOf(
        // Flydigi (APEX 5 EVA, APEX 4, Vader 4/3, Direwolf 3)
        vidPidKey(0x37D7, 0x2501) to KnownHardwareSpec("Flydigi APEX 5 / APEX 5 EVA Wireless", TYPE_FLYDIGI),
        vidPidKey(0x37D7, 0x2502) to KnownHardwareSpec("Flydigi APEX 5 EVA Wired", TYPE_FLYDIGI),
        vidPidKey(0x37D7, 0x2401) to KnownHardwareSpec("Flydigi APEX 4 / APEX 4 BMW", TYPE_FLYDIGI),
        vidPidKey(0x37D7, 0x2301) to KnownHardwareSpec("Flydigi Vader 4 / Vader 4 Pro", TYPE_FLYDIGI),
        vidPidKey(0x37D7, 0x2201) to KnownHardwareSpec("Flydigi Direwolf 3 / Vader 3", TYPE_FLYDIGI),

        // Logitech Racing Wheels
        vidPidKey(0x046D, 0xC294) to KnownHardwareSpec("Logitech Driving Force", TYPE_WHEEL),
        vidPidKey(0x046D, 0xC299) to KnownHardwareSpec("Logitech G25 Racing Wheel", TYPE_WHEEL),
        vidPidKey(0x046D, 0xC29A) to KnownHardwareSpec("Logitech Driving Force GT", TYPE_WHEEL),
        vidPidKey(0x046D, 0xC29B) to KnownHardwareSpec("Logitech G27 Racing Wheel", TYPE_WHEEL),
        vidPidKey(0x046D, 0xC24F) to KnownHardwareSpec("Logitech G29 Driving Force (PS)", TYPE_WHEEL),
        vidPidKey(0x046D, 0xC260) to KnownHardwareSpec("Logitech G29 Driving Force", TYPE_WHEEL),
        vidPidKey(0x046D, 0xC262) to KnownHardwareSpec("Logitech G920 Driving Force (Xbox)", TYPE_WHEEL),
        vidPidKey(0x046D, 0xC266) to KnownHardwareSpec("Logitech G923 TRUEFORCE (PS)", TYPE_WHEEL),
        vidPidKey(0x046D, 0xC26E) to KnownHardwareSpec("Logitech G923 TRUEFORCE (Xbox)", TYPE_WHEEL),
        vidPidKey(0x046D, 0xC268) to KnownHardwareSpec("Logitech G PRO Racing Wheel", TYPE_WHEEL),

        // Thrustmaster Racing Wheels & Flight Sticks
        vidPidKey(0x044F, 0xB65E) to KnownHardwareSpec("Thrustmaster T500RS", TYPE_WHEEL),
        vidPidKey(0x044F, 0xB66E) to KnownHardwareSpec("Thrustmaster T300RS", TYPE_WHEEL),
        vidPidKey(0x044F, 0xB66F) to KnownHardwareSpec("Thrustmaster TX Racing Wheel", TYPE_WHEEL),
        vidPidKey(0x044F, 0xB677) to KnownHardwareSpec("Thrustmaster T150 Force Feedback", TYPE_WHEEL),
        vidPidKey(0x044F, 0xB67F) to KnownHardwareSpec("Thrustmaster TMX Force Feedback", TYPE_WHEEL),
        vidPidKey(0x044F, 0xB696) to KnownHardwareSpec("Thrustmaster T248 Racing Wheel", TYPE_WHEEL),
        vidPidKey(0x044F, 0xB668) to KnownHardwareSpec("Thrustmaster T80 Racing Wheel", TYPE_WHEEL),
        vidPidKey(0x044F, 0xB10A) to KnownHardwareSpec("Thrustmaster T.16000M", TYPE_FLIGHT_STICK),
        vidPidKey(0x044F, 0x0402) to KnownHardwareSpec("Thrustmaster HOTAS Warthog Stick", TYPE_FLIGHT_STICK),
        vidPidKey(0x044F, 0x0404) to KnownHardwareSpec("Thrustmaster HOTAS Warthog Throttle", TYPE_FLIGHT_STICK),
        vidPidKey(0x044F, 0xB67B) to KnownHardwareSpec("Thrustmaster TWCS Throttle", TYPE_FLIGHT_STICK),
        vidPidKey(0x044F, 0xB108) to KnownHardwareSpec("Thrustmaster T.Flight Hotas X/4/One", TYPE_FLIGHT_STICK),

        // Fanatec & MOZA Racing Wheels
        vidPidKey(0x0EB7, 0x0001) to KnownHardwareSpec("Fanatec ClubSport Wheel Base", TYPE_WHEEL),
        vidPidKey(0x0EB7, 0x0005) to KnownHardwareSpec("Fanatec CSL Elite Wheel Base", TYPE_WHEEL),
        vidPidKey(0x0EB7, 0x0020) to KnownHardwareSpec("Fanatec CSL DD / GT DD Pro", TYPE_WHEEL),
        vidPidKey(0x346E, 0x0002) to KnownHardwareSpec("MOZA R9 Direct Drive Wheel", TYPE_WHEEL),
        vidPidKey(0x346E, 0x0004) to KnownHardwareSpec("MOZA R5 Direct Drive Wheel", TYPE_WHEEL),
        vidPidKey(0x346E, 0x0005) to KnownHardwareSpec("MOZA R16 / R21 Wheel Base", TYPE_WHEEL),
        vidPidKey(0x346E, 0x0006) to KnownHardwareSpec("MOZA R12 Direct Drive Wheel", TYPE_WHEEL),
        vidPidKey(0x346E, 0x0007) to KnownHardwareSpec("MOZA R3 Racing Wheel", TYPE_WHEEL),

        // Flight Sticks (VKB, VIRPIL, Saitek/Logitech, WinWing)
        vidPidKey(0x231D, 0x0200) to KnownHardwareSpec("VKB Gladiator NXT EVO", TYPE_FLIGHT_STICK),
        vidPidKey(0x231D, 0x011E) to KnownHardwareSpec("VKB Gunfighter III", TYPE_FLIGHT_STICK),
        vidPidKey(0x06A3, 0x0762) to KnownHardwareSpec("Logitech / Saitek X52 HOTAS", TYPE_FLIGHT_STICK),
        vidPidKey(0x06A3, 0x0763) to KnownHardwareSpec("Logitech / Saitek X52 Pro HOTAS", TYPE_FLIGHT_STICK),
        vidPidKey(0x0738, 0x2221) to KnownHardwareSpec("Logitech / Saitek X56 Rhino HOTAS", TYPE_FLIGHT_STICK),
        vidPidKey(0x06A3, 0x0BAC) to KnownHardwareSpec("Logitech Flight Yoke System", TYPE_FLIGHT_STICK),

        // Arcade Sticks & Leverless / HitBox
        vidPidKey(0x2E8A, 0x1002) to KnownHardwareSpec("GP2040-CE Leverless Controller", TYPE_LEVERLESS),
        vidPidKey(0x0F0D, 0x0184) to KnownHardwareSpec("HORI Fighting Stick Alpha", TYPE_ARCADE_STICK),
        vidPidKey(0x0F0D, 0x00AA) to KnownHardwareSpec("HORI Real Arcade Pro", TYPE_ARCADE_STICK),
        vidPidKey(0x1532, 0x0A00) to KnownHardwareSpec("Razer Atrox Arcade Stick", TYPE_ARCADE_STICK),
        vidPidKey(0x1532, 0x0401) to KnownHardwareSpec("Razer Panthera Arcade Stick", TYPE_ARCADE_STICK),
        vidPidKey(0x1532, 0x0A39) to KnownHardwareSpec("Razer Kitsune Leverless", TYPE_LEVERLESS),
        vidPidKey(0x2C22, 0x2300) to KnownHardwareSpec("Qanba Obsidian / Drone Arcade Stick", TYPE_ARCADE_STICK),
        vidPidKey(0x0079, 0x181B) to KnownHardwareSpec("Mayflash F300 / F500 Arcade Stick", TYPE_ARCADE_STICK),

        // Sony / Microsoft / Nintendo
        vidPidKey(0x054C, 0x0CE6) to KnownHardwareSpec("Sony DualSense Wireless Controller", TYPE_PLAYSTATION),
        vidPidKey(0x054C, 0x0DF2) to KnownHardwareSpec("Sony DualSense Edge Controller", TYPE_PLAYSTATION),
        vidPidKey(0x054C, 0x09CC) to KnownHardwareSpec("Sony DualShock 4 (CUH-ZCT2)", TYPE_PLAYSTATION),
        vidPidKey(0x054C, 0x05C4) to KnownHardwareSpec("Sony DualShock 4 (CUH-ZCT1)", TYPE_PLAYSTATION),
        vidPidKey(0x045E, 0x028E) to KnownHardwareSpec("Xbox 360 Controller", TYPE_XBOX),
        vidPidKey(0x045E, 0x02EA) to KnownHardwareSpec("Xbox One Wireless Controller", TYPE_XBOX),
        vidPidKey(0x045E, 0x02FD) to KnownHardwareSpec("Xbox One S Bluetooth Controller", TYPE_XBOX),
        vidPidKey(0x045E, 0x0B12) to KnownHardwareSpec("Xbox Series X/S Controller", TYPE_XBOX),
        vidPidKey(0x045E, 0x0B13) to KnownHardwareSpec("Xbox Series X/S Bluetooth", TYPE_XBOX),
        vidPidKey(0x057E, 0x2009) to KnownHardwareSpec("Nintendo Switch Pro Controller", TYPE_SWITCH_PRO)
    )

    private fun vidPidKey(vid: Int, pid: Int): Int = ((vid and 0xFFFF) shl 16) or (pid and 0xFFFF)

    private val loadedProfiles = ConcurrentHashMap<String, MobileControllerProfile>()
    private val portAssignments = ConcurrentHashMap<Int, String>()

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

    private fun getPortAssignmentsFile(context: Context?): File {
        val pubDir = File(MobileCoreRegistry.STORM_PUBLIC_ROOT, "autoconfig")
        try {
            if (!pubDir.exists()) pubDir.mkdirs()
            if (pubDir.canWrite()) {
                return File(pubDir, "port_assignments.json")
            }
        } catch (_: Throwable) {
        }
        if (context != null) {
            val intDir = File(context.applicationContext.filesDir, "autoconfig")
            if (!intDir.exists()) intDir.mkdirs()
            return File(intDir, "port_assignments.json")
        }
        return File(pubDir, "port_assignments.json")
    }

    fun isIgnoredSystemInputDevice(dev: InputDevice): Boolean {
        if (dev.isVirtual) return true
        val lowerName = (dev.name ?: "").lowercase(Locale.ROOT)
        if (lowerName.contains("gpio-keys") ||
            lowerName.contains("qpnp_pon") ||
            lowerName.contains("headset") ||
            lowerName.contains("uinput-fpc") ||
            lowerName.contains("fingerprint") ||
            lowerName.contains("goodix") ||
            lowerName.contains("hall_sensor") ||
            lowerName.contains("sec_touchscreen") ||
            lowerName.contains("accelerometer")
        ) {
            return true
        }
        val sources = dev.sources
        val isGamepad = (sources and InputDevice.SOURCE_GAMEPAD) == InputDevice.SOURCE_GAMEPAD
        val isJoystick = (sources and InputDevice.SOURCE_JOYSTICK) == InputDevice.SOURCE_JOYSTICK
        return !isGamepad && !isJoystick
    }

    /**
     * Enumerates connected physical controllers (gamepads, Flydigi, wheels, HOTAS, arcade/leverless sticks)
     * and computes each device's assigned RetroPad port (0..7, or -1 if detached from active ports).
     */
    fun enumerateConnectedControllers(context: Context? = null): List<MobileControllerDevice> {
        ensureProfilesLoaded(context)
        val rawDevices = ArrayList<MobileControllerDevice>()
        val ids = InputDevice.getDeviceIds()
        val seenDescriptors = HashSet<String>()

        for (id in ids) {
            val dev = InputDevice.getDevice(id) ?: continue
            if (isIgnoredSystemInputDevice(dev)) continue

            val descriptor = dev.descriptor ?: "dev_${dev.id}"
            if (!seenDescriptors.add(descriptor)) continue

            val spec = resolveHardwareSpec(dev.vendorId, dev.productId, dev.name)
            val axes = dev.motionRanges
                .filter { range ->
                    (range.source and InputDevice.SOURCE_JOYSTICK) == InputDevice.SOURCE_JOYSTICK ||
                        (range.source and InputDevice.SOURCE_GAMEPAD) == InputDevice.SOURCE_GAMEPAD
                }
                .map { it.axis }
                .distinct()

            val placeholder = MobileControllerDevice(
                deviceId = dev.id,
                descriptor = descriptor,
                name = spec.displayName,
                vendorId = dev.vendorId,
                productId = dev.productId,
                controllerType = spec.controllerType,
                playerPort = -1,
                rawName = dev.name ?: spec.displayName,
                supportedAxes = axes
            )
            getOrCreateProfile(placeholder, context)
            rawDevices.add(placeholder)
        }

        // Resolve deterministic port routing (manual assignments override auto slot ordering)
        val assignedDeviceDescriptors = HashSet<String>()
        val portOwners = arrayOfNulls<MobileControllerDevice>(MAX_PORTS)
        for (port in 0 until MAX_PORTS) {
            val rule = portAssignments[port] ?: "auto"
            if (rule == "none" || rule == "auto") continue
            val matched = rawDevices.firstOrNull { d ->
                !assignedDeviceDescriptors.contains(d.descriptor) &&
                    (d.descriptor == rule || d.profileKey == rule || d.name.equals(rule, ignoreCase = true))
            }
            if (matched != null) {
                portOwners[port] = matched
                assignedDeviceDescriptors.add(matched.descriptor)
            }
        }

        var nextFreeIdx = 0
        for (port in 0 until MAX_PORTS) {
            val rule = portAssignments[port] ?: "auto"
            if (portOwners[port] != null || rule == "none") continue
            while (nextFreeIdx < rawDevices.size && assignedDeviceDescriptors.contains(rawDevices[nextFreeIdx].descriptor)) {
                nextFreeIdx++
            }
            if (nextFreeIdx < rawDevices.size) {
                val chosen = rawDevices[nextFreeIdx]
                portOwners[port] = chosen
                assignedDeviceDescriptors.add(chosen.descriptor)
                nextFreeIdx++
            }
        }

        val portByDescriptor = HashMap<String, Int>()
        for (port in 0 until MAX_PORTS) {
            portOwners[port]?.let { portByDescriptor[it.descriptor] = port }
        }

        return rawDevices.map { d ->
            val p = portByDescriptor[d.descriptor] ?: -1
            d.copy(playerPort = p)
        }
    }

    private fun resolveHardwareSpec(vendorId: Int, productId: Int, rawName: String?): KnownHardwareSpec {
        KNOWN_VID_PID[vidPidKey(vendorId, productId)]?.let { return it }
        val cType = classifyControllerType(vendorId, productId, rawName)
        val fallbackName = (rawName ?: "").trim().ifBlank { cType }
        return KnownHardwareSpec(fallbackName, cType)
    }

    fun classifyControllerType(vendorId: Int, productId: Int, rawName: String?): String {
        KNOWN_VID_PID[vidPidKey(vendorId, productId)]?.let { return it.controllerType }
        val name = (rawName ?: "").lowercase(Locale.ROOT)
        return when {
            vendorId == 0x37D7 || name.contains("flydigi") || name.contains("apex 5") ||
                name.contains("apex 4") || name.contains("vader") || name.contains("direwolf") ->
                TYPE_FLYDIGI

            vendorId == 0x346E || vendorId == 0x0EB7 ||
                name.contains("wheel") || name.contains("driving force") ||
                name.contains("g29") || name.contains("g920") || name.contains("g923") ||
                name.contains("t300") || name.contains("t150") || name.contains("t248") ||
                name.contains("fanatec") || name.contains("moza") || name.contains("simagic") ->
                TYPE_WHEEL

            vendorId == 0x231D || vendorId == 0x3344 || vendorId == 0x4098 || vendorId == 0x06A3 ||
                name.contains("hotas") || name.contains("flight") || name.contains("warthog") ||
                name.contains("gladiator") || name.contains("vkb") || name.contains("virpil") ||
                name.contains("winwing") || name.contains("t.16000") || name.contains("throttle") ||
                name.contains("rudder") || name.contains("yoke") ->
                TYPE_FLIGHT_STICK

            vendorId == 0x2E8A || name.contains("hitbox") || name.contains("leverless") ||
                name.contains("gp2040") || name.contains("kitsune") || name.contains("haute") ||
                name.contains("snackbox") ->
                TYPE_LEVERLESS

            name.contains("arcade") || name.contains("fightstick") || name.contains("qanba") ||
                name.contains("hori rap") || name.contains("real arcade") || name.contains("fighting stick") ||
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

            vendorId == 0x3537 || vendorId == 0x3525 ||
                name.contains("odin") || name.contains("retroid") || name.contains("anbernic") ||
                name.contains("ayaneo") || name.contains("razer kishi") || name.contains("gamesir") ||
                name.contains("backbone") || name.contains("g cloud") || name.contains("gpd") ||
                name.contains("gulikit") || (name.contains("xbox cloud") && name.contains("handheld")) ->
                TYPE_ANDROID_HANDHELD

            vendorId == 0x045E || name.contains("xbox") || name.contains("x-box") ->
                TYPE_XBOX

            else -> TYPE_GENERIC
        }
    }

    fun getPortAssignmentsMap(context: Context? = null): Map<Int, String> {
        ensureProfilesLoaded(context)
        val out = LinkedHashMap<Int, String>()
        for (port in 0 until MAX_PORTS) {
            out[port] = portAssignments[port] ?: "auto"
        }
        return out
    }

    fun setPortAssignment(port: Int, target: String, context: Context? = null): Boolean {
        if (port !in 0 until MAX_PORTS) return false
        ensureProfilesLoaded(context)
        val normalized = target.trim().ifBlank { "auto" }
        portAssignments[port] = normalized
        return savePortAssignments(context)
    }

    fun resolvePortForDevice(device: InputDevice?, context: Context? = null): Int {
        if (device == null) return 0
        val desc = device.descriptor ?: "dev_${device.id}"
        val connected = enumerateConnectedControllers(context)
        connected.firstOrNull { it.descriptor == desc || it.deviceId == device.id }?.let { match ->
            return match.playerPort
        }
        return 0
    }

    fun getProfileForDevice(device: InputDevice, context: Context? = null): MobileControllerProfile {
        return getOrCreateProfile(device, context)
    }

    fun getProfileByKey(profileKey: String, context: Context? = null): MobileControllerProfile? {
        ensureProfilesLoaded(context)
        return loadedProfiles[profileKey]
    }

    fun getAllLoadedProfiles(context: Context? = null): Map<String, MobileControllerProfile> {
        ensureProfilesLoaded(context)
        return loadedProfiles
    }

    fun createDefaultProfile(
        profileKey: String,
        deviceName: String,
        vendorId: Int,
        productId: Int,
        controllerType: String,
        supportedAxes: List<Int> = emptyList()
    ): MobileControllerProfile {
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

        val axes = LinkedHashMap<String, Int>().apply {
            val hasWheelAxis = supportedAxes.contains(MotionEvent.AXIS_WHEEL)
            val hasGasBrake = supportedAxes.contains(MotionEvent.AXIS_GAS) || supportedAxes.contains(MotionEvent.AXIS_BRAKE)
            val hasThrottle = supportedAxes.contains(MotionEvent.AXIS_THROTTLE)

            put(AXIS_KEY_LX, if (controllerType == TYPE_WHEEL && hasWheelAxis) MotionEvent.AXIS_WHEEL else MotionEvent.AXIS_X)
            put(AXIS_KEY_LY, MotionEvent.AXIS_Y)
            put(
                AXIS_KEY_RX,
                if (controllerType == TYPE_FLIGHT_STICK && supportedAxes.contains(MotionEvent.AXIS_RUDDER))
                    MotionEvent.AXIS_RUDDER else MotionEvent.AXIS_Z
            )
            put(
                AXIS_KEY_RY,
                if (controllerType == TYPE_FLIGHT_STICK && hasThrottle)
                    MotionEvent.AXIS_THROTTLE else MotionEvent.AXIS_RZ
            )
            put(
                AXIS_KEY_L2,
                if (controllerType == TYPE_WHEEL && hasGasBrake)
                    MotionEvent.AXIS_BRAKE else MotionEvent.AXIS_LTRIGGER
            )
            put(
                AXIS_KEY_R2,
                if (controllerType == TYPE_WHEEL && hasGasBrake)
                    MotionEvent.AXIS_GAS else MotionEvent.AXIS_RTRIGGER
            )
        }

        val hotkeys = LinkedHashMap<String, Int>().apply {
            put(HOTKEY_QUICK_MENU, KeyEvent.KEYCODE_BUTTON_MODE)
            put(HOTKEY_QUICK_SAVE, KeyEvent.KEYCODE_F2)
            put(HOTKEY_QUICK_LOAD, KeyEvent.KEYCODE_F4)
            put(HOTKEY_REWIND, KeyEvent.KEYCODE_MEDIA_REWIND)
            put(HOTKEY_FAST_FORWARD, KeyEvent.KEYCODE_MEDIA_FAST_FORWARD)
            put(HOTKEY_SCREENSHOT, KeyEvent.KEYCODE_SYSRQ)
            put(HOTKEY_PAUSE, KeyEvent.KEYCODE_P)
            put(HOTKEY_RESET, 0)
            put(HOTKEY_SLOT_NEXT, KeyEvent.KEYCODE_F7)
            put(HOTKEY_SLOT_PREV, KeyEvent.KEYCODE_F6)
            put(HOTKEY_MUTE, KeyEvent.KEYCODE_F9)
            put(HOTKEY_SHADER_NEXT, KeyEvent.KEYCODE_M)
        }

        val defaultAnalogDz = when (controllerType) {
            TYPE_WHEEL, TYPE_FLIGHT_STICK -> 0.05f
            TYPE_ARCADE_STICK, TYPE_LEVERLESS -> 0.10f
            TYPE_FLYDIGI -> 0.12f
            else -> 0.18f
        }

        return MobileControllerProfile(
            profileKey = profileKey,
            deviceName = deviceName,
            vendorId = vendorId,
            productId = productId,
            controllerType = controllerType,
            buttonBindings = buttons,
            hotkeyBindings = hotkeys,
            analogDeadzone = defaultAnalogDz,
            requireSelectForHotkeys = false,
            axisBindings = axes,
            steeringDeadzone = if (controllerType == TYPE_WHEEL) 0.03f else 0.06f,
            triggerThreshold = 0.25f,
            chordModifier = CHORD_NONE
        )
    }

    fun ensureProfilesLoaded(context: Context? = null) {
        if (!initializedFromDisk) {
            loadAllProfiles(context)
            loadPortAssignments(context)
        }
    }

    fun getOrCreateProfile(device: InputDevice, context: Context? = null): MobileControllerProfile {
        val spec = resolveHardwareSpec(device.vendorId, device.productId, device.name)
        val axes = device.motionRanges
            .filter { r ->
                (r.source and InputDevice.SOURCE_JOYSTICK) == InputDevice.SOURCE_JOYSTICK ||
                    (r.source and InputDevice.SOURCE_GAMEPAD) == InputDevice.SOURCE_GAMEPAD
            }
            .map { it.axis }
            .distinct()

        val info = MobileControllerDevice(
            deviceId = device.id,
            descriptor = device.descriptor ?: "dev_${device.id}",
            name = spec.displayName,
            vendorId = device.vendorId,
            productId = device.productId,
            controllerType = spec.controllerType,
            playerPort = 0,
            rawName = device.name ?: spec.displayName,
            supportedAxes = axes
        )
        return getOrCreateProfile(info, context)
    }

    fun getOrCreateProfile(deviceInfo: MobileControllerDevice, context: Context? = null): MobileControllerProfile {
        ensureProfilesLoaded(context)
        loadedProfiles[deviceInfo.profileKey]?.let { existing ->
            if (existing.axisBindings.isEmpty()) {
                val defaults = createDefaultProfile(
                    deviceInfo.profileKey,
                    deviceInfo.name,
                    deviceInfo.vendorId,
                    deviceInfo.productId,
                    deviceInfo.controllerType,
                    deviceInfo.supportedAxes
                )
                existing.axisBindings.putAll(defaults.axisBindings)
            }
            return existing
        }

        val created = createDefaultProfile(
            profileKey = deviceInfo.profileKey,
            deviceName = deviceInfo.name,
            vendorId = deviceInfo.vendorId,
            productId = deviceInfo.productId,
            controllerType = deviceInfo.controllerType,
            supportedAxes = deviceInfo.supportedAxes
        )
        loadedProfiles[created.profileKey] = created
        saveAllProfiles(context)
        return created
    }

    fun saveProfile(profile: MobileControllerProfile, context: Context? = null): Boolean {
        ensureProfilesLoaded(context)
        profile.requireSelectForHotkeys = profile.chordModifier != CHORD_NONE
        loadedProfiles[profile.profileKey] = profile
        return saveAllProfiles(context)
    }

    fun resetProfileToDefaults(deviceInfo: MobileControllerDevice, context: Context? = null): MobileControllerProfile {
        val fresh = createDefaultProfile(
            profileKey = deviceInfo.profileKey,
            deviceName = deviceInfo.name,
            vendorId = deviceInfo.vendorId,
            productId = deviceInfo.productId,
            controllerType = deviceInfo.controllerType,
            supportedAxes = deviceInfo.supportedAxes
        )
        loadedProfiles[fresh.profileKey] = fresh
        saveAllProfiles(context)
        return fresh
    }

    fun resetProfileByKey(profileKey: String, context: Context? = null): MobileControllerProfile? {
        ensureProfilesLoaded(context)
        val existing = loadedProfiles[profileKey] ?: return null
        val fresh = createDefaultProfile(
            profileKey = existing.profileKey,
            deviceName = existing.deviceName,
            vendorId = existing.vendorId,
            productId = existing.productId,
            controllerType = existing.controllerType
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
                val spec = resolveHardwareSpec(vid, pid, devName)
                val cType = obj.optString("controllerType", spec.controllerType)
                val deadzone = obj.optDouble("analogDeadzone", 0.18).toFloat().coerceIn(0.02f, 0.50f)
                val steeringDz = obj.optDouble("steeringDeadzone", 0.04).toFloat().coerceIn(0.0f, 0.30f)
                val trigThr = obj.optDouble("triggerThreshold", 0.25).toFloat().coerceIn(0.05f, 0.90f)
                val reqSelect = obj.optBoolean("requireSelectForHotkeys", false)
                val chordMod = obj.optString("chordModifier", if (reqSelect) CHORD_SELECT else CHORD_NONE)

                val baseDefault = createDefaultProfile(pKey, devName, vid, pid, cType)
                obj.optJSONObject("buttons")?.let { bJson ->
                    val bKeys = bJson.keys()
                    while (bKeys.hasNext()) {
                        val bName = bKeys.next()
                        baseDefault.buttonBindings[bName] = bJson.optInt(bName, 0)
                    }
                }
                obj.optJSONObject("axes")?.let { aJson ->
                    val aKeys = aJson.keys()
                    while (aKeys.hasNext()) {
                        val aName = aKeys.next()
                        baseDefault.axisBindings[aName] = aJson.optInt(aName, 0)
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
                baseDefault.steeringDeadzone = steeringDz
                baseDefault.triggerThreshold = trigThr
                baseDefault.chordModifier = chordMod
                baseDefault.requireSelectForHotkeys = reqSelect || chordMod != CHORD_NONE
                loadedProfiles[pKey] = baseDefault
            }
        } catch (_: Throwable) {
        }
        return loadedProfiles
    }

    fun saveAllProfiles(context: Context? = null): Boolean {
        val root = JSONObject()
        root.put("version", "2.7.5")
        val profilesJson = JSONObject()

        for ((key, prof) in loadedProfiles) {
            profilesJson.put(key, profileToJsonObject(prof))
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

    private fun loadPortAssignments(context: Context? = null) {
        portAssignments.clear()
        val f = getPortAssignmentsFile(context)
        if (!f.exists() || f.length() < 2L) return
        try {
            val root = JSONObject(f.readText(StandardCharsets.UTF_8))
            val obj = root.optJSONObject("ports") ?: return
            for (port in 0 until MAX_PORTS) {
                val v = obj.optString(port.toString(), "auto").ifBlank { "auto" }
                portAssignments[port] = v
            }
        } catch (_: Throwable) {
        }
    }

    private fun savePortAssignments(context: Context? = null): Boolean {
        return try {
            val root = JSONObject()
            val obj = JSONObject()
            for (port in 0 until MAX_PORTS) {
                obj.put(port.toString(), portAssignments[port] ?: "auto")
            }
            root.put("version", "2.7.5")
            root.put("ports", obj)
            val f = getPortAssignmentsFile(context)
            f.parentFile?.mkdirs()
            f.writeText(root.toString(2), StandardCharsets.UTF_8)
            true
        } catch (_: Throwable) {
            false
        }
    }

    fun profileToJsonObject(prof: MobileControllerProfile): JSONObject {
        val pObj = JSONObject()
        pObj.put("profileKey", prof.profileKey)
        pObj.put("deviceName", prof.deviceName)
        pObj.put("vendorId", prof.vendorId)
        pObj.put("productId", prof.productId)
        pObj.put("controllerType", prof.controllerType)
        pObj.put("analogDeadzone", prof.analogDeadzone.toDouble())
        pObj.put("steeringDeadzone", prof.steeringDeadzone.toDouble())
        pObj.put("triggerThreshold", prof.triggerThreshold.toDouble())
        pObj.put("requireSelectForHotkeys", prof.requireSelectForHotkeys)
        pObj.put("chordModifier", prof.chordModifier)

        val bObj = JSONObject()
        for ((bName, code) in prof.buttonBindings) {
            bObj.put(bName, code)
        }
        pObj.put("buttons", bObj)

        val bDisplayObj = JSONObject()
        for ((bName, code) in prof.buttonBindings) {
            bDisplayObj.put(bName, keyCodeToDisplayName(code))
        }
        pObj.put("buttonNames", bDisplayObj)

        val aObj = JSONObject()
        val aDisplayObj = JSONObject()
        for ((aName, axisCode) in prof.axisBindings) {
            aObj.put(aName, axisCode)
            aDisplayObj.put(aName, axisCodeToDisplayName(axisCode))
        }
        pObj.put("axes", aObj)
        pObj.put("axisNames", aDisplayObj)

        val hObj = JSONObject()
        val hDisplayObj = JSONObject()
        for ((hName, code) in prof.hotkeyBindings) {
            hObj.put(hName, code)
            hDisplayObj.put(hName, keyCodeToDisplayName(code))
        }
        pObj.put("hotkeys", hObj)
        pObj.put("hotkeyNames", hDisplayObj)
        return pObj
    }

    fun isChordModifierActive(
        profile: MobileControllerProfile,
        heldKeyCodes: Set<Int>,
        isSelectFallback: Boolean = false
    ): Boolean {
        return when (profile.chordModifier) {
            CHORD_SELECT -> heldKeyCodes.contains(KeyEvent.KEYCODE_BUTTON_SELECT) || isSelectFallback
            CHORD_MODE -> heldKeyCodes.contains(KeyEvent.KEYCODE_BUTTON_MODE)
            CHORD_L3 -> heldKeyCodes.contains(KeyEvent.KEYCODE_BUTTON_THUMBL)
            CHORD_NONE -> !profile.requireSelectForHotkeys || isSelectFallback || heldKeyCodes.contains(KeyEvent.KEYCODE_BUTTON_SELECT)
            else -> true
        }
    }

    /**
     * Resolves an Android KeyEvent keyCode into a Libretro RETRO_DEVICE_ID_JOYPAD_* index (0..15).
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
     * Resolves an Android KeyEvent keyCode into a configured hotkey action.
     */
    fun mapKeyEventToHotkey(
        device: InputDevice?,
        keyCode: Int,
        isSelectHeld: Boolean = false,
        context: Context? = null,
        heldKeyCodes: Set<Int> = emptySet()
    ): String? {
        val profile = if (device != null) {
            getOrCreateProfile(device, context)
        } else {
            return null
        }

        val chordHeld = isChordModifierActive(profile, heldKeyCodes, isSelectHeld)
        val allowGuideDirect = keyCode == KeyEvent.KEYCODE_BUTTON_MODE && profile.chordModifier != CHORD_MODE
        if (!chordHeld && !allowGuideDirect) {
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
     * Reads an analog axis with graceful fallback between standard and racing/flight axes.
     */
    private fun readAxisWithFallback(event: MotionEvent, primaryAxis: Int, fallbackAxis: Int = -1): Float {
        val v = if (primaryAxis >= 0) event.getAxisValue(primaryAxis) else 0f
        if (abs(v) > 0.002f || fallbackAxis < 0) return v
        return event.getAxisValue(fallbackAxis)
    }

    /**
     * Converts D-Pad HAT axes, steering/stick axes, and L2/R2 analog triggers/pedals
     * into a 16-bit Libretro RetroPad bitmask (1 shl RETRO_ID_*).
     */
    fun extractMotionRetroPadBitmask(event: MotionEvent, context: Context? = null): Int {
        val profile = event.device?.let { getOrCreateProfile(it, context) }
        val stickDz = profile?.analogDeadzone ?: 0.18f
        val effectiveDz = if (profile?.controllerType == TYPE_WHEEL) maxOf(profile.steeringDeadzone, 0.08f) else stickDz
        val trigThr = profile?.triggerThreshold ?: 0.25f

        val axisX = profile?.axisBindings?.get(AXIS_KEY_LX) ?: MotionEvent.AXIS_X
        val axisY = profile?.axisBindings?.get(AXIS_KEY_LY) ?: MotionEvent.AXIS_Y
        val axisL2 = profile?.axisBindings?.get(AXIS_KEY_L2) ?: MotionEvent.AXIS_LTRIGGER
        val axisR2 = profile?.axisBindings?.get(AXIS_KEY_R2) ?: MotionEvent.AXIS_RTRIGGER

        var mask = 0
        val hatX = event.getAxisValue(MotionEvent.AXIS_HAT_X)
        val hatY = event.getAxisValue(MotionEvent.AXIS_HAT_Y)
        val stickX = readAxisWithFallback(event, axisX, MotionEvent.AXIS_WHEEL)
        val stickY = readAxisWithFallback(event, axisY, MotionEvent.AXIS_Y)

        if (hatX < -0.5f || stickX < -effectiveDz) mask = mask or (1 shl RETRO_ID_LEFT)
        if (hatX > 0.5f || stickX > effectiveDz) mask = mask or (1 shl RETRO_ID_RIGHT)
        if (hatY < -0.5f || stickY < -stickDz) mask = mask or (1 shl RETRO_ID_UP)
        if (hatY > 0.5f || stickY > stickDz) mask = mask or (1 shl RETRO_ID_DOWN)

        val lTrigger = maxOf(
            event.getAxisValue(axisL2),
            event.getAxisValue(MotionEvent.AXIS_BRAKE)
        )
        val rTrigger = maxOf(
            event.getAxisValue(axisR2),
            event.getAxisValue(MotionEvent.AXIS_GAS)
        )
        if (lTrigger > trigThr) mask = mask or (1 shl RETRO_ID_L2)
        if (rTrigger > trigThr) mask = mask or (1 shl RETRO_ID_R2)

        return mask
    }

    /**
     * Extracts proportional -32767..32767 analog sticks/wheel/flight axes and 0..32767 L2/R2 pedals.
     */
    fun extractAnalogAxesState(event: MotionEvent, context: Context? = null): MobileAxisState {
        val profile = event.device?.let { getOrCreateProfile(it, context) }
        val stickDz = profile?.analogDeadzone ?: 0.18f
        val xDz = if (profile?.controllerType == TYPE_WHEEL) profile.steeringDeadzone else stickDz

        val axisLx = profile?.axisBindings?.get(AXIS_KEY_LX) ?: MotionEvent.AXIS_X
        val axisLy = profile?.axisBindings?.get(AXIS_KEY_LY) ?: MotionEvent.AXIS_Y
        val axisRx = profile?.axisBindings?.get(AXIS_KEY_RX) ?: MotionEvent.AXIS_Z
        val axisRy = profile?.axisBindings?.get(AXIS_KEY_RY) ?: MotionEvent.AXIS_RZ
        val axisL2 = profile?.axisBindings?.get(AXIS_KEY_L2) ?: MotionEvent.AXIS_LTRIGGER
        val axisR2 = profile?.axisBindings?.get(AXIS_KEY_R2) ?: MotionEvent.AXIS_RTRIGGER

        val rawLx = readAxisWithFallback(event, axisLx, MotionEvent.AXIS_WHEEL)
        val rawLy = readAxisWithFallback(event, axisLy, MotionEvent.AXIS_Y)
        val rawRx = readAxisWithFallback(event, axisRx, MotionEvent.AXIS_RX)
        val rawRy = readAxisWithFallback(event, axisRy, MotionEvent.AXIS_RY)
        val rawL2 = maxOf(event.getAxisValue(axisL2), event.getAxisValue(MotionEvent.AXIS_BRAKE)).coerceIn(0f, 1f)
        val rawR2 = maxOf(event.getAxisValue(axisR2), event.getAxisValue(MotionEvent.AXIS_GAS)).coerceIn(0f, 1f)

        fun scaleSigned(v: Float, dz: Float): Int {
            if (abs(v) <= dz) return 0
            val sign = if (v < 0f) -1f else 1f
            val norm = ((abs(v) - dz) / (1f - dz).coerceAtLeast(0.05f)).coerceIn(0f, 1f)
            return (sign * norm * 32767f).roundToInt()
        }

        fun scaleTrigger(v: Float): Int {
            if (v <= 0.03f) return 0
            return (v.coerceIn(0f, 1f) * 32767f).roundToInt()
        }

        return MobileAxisState(
            lx = scaleSigned(rawLx, xDz),
            ly = scaleSigned(rawLy, stickDz),
            rx = scaleSigned(rawRx, stickDz),
            ry = scaleSigned(rawRy, stickDz),
            l2 = scaleTrigger(rawL2),
            r2 = scaleTrigger(rawR2)
        )
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
            KeyEvent.KEYCODE_BUTTON_C -> "BTN C"
            KeyEvent.KEYCODE_BUTTON_Z -> "BTN Z"
            KeyEvent.KEYCODE_BUTTON_1 -> "BTN 1"
            KeyEvent.KEYCODE_BUTTON_2 -> "BTN 2"
            KeyEvent.KEYCODE_BUTTON_3 -> "BTN 3"
            KeyEvent.KEYCODE_BUTTON_4 -> "BTN 4"
            KeyEvent.KEYCODE_BUTTON_5 -> "BTN 5"
            KeyEvent.KEYCODE_BUTTON_6 -> "BTN 6"
            KeyEvent.KEYCODE_BUTTON_7 -> "BTN 7"
            KeyEvent.KEYCODE_BUTTON_8 -> "BTN 8"
            KeyEvent.KEYCODE_BUTTON_9 -> "BTN 9"
            KeyEvent.KEYCODE_BUTTON_10 -> "BTN 10"
            KeyEvent.KEYCODE_BUTTON_11 -> "BTN 11"
            KeyEvent.KEYCODE_BUTTON_12 -> "BTN 12"
            else -> KeyEvent.keyCodeToString(keyCode).removePrefix("KEYCODE_")
        }
    }

    fun axisCodeToDisplayName(axisCode: Int): String {
        if (axisCode < 0) return "Unbound"
        return when (axisCode) {
            MotionEvent.AXIS_X -> "Left Stick X"
            MotionEvent.AXIS_Y -> "Left Stick Y"
            MotionEvent.AXIS_Z -> "Right Stick X (Z)"
            MotionEvent.AXIS_RZ -> "Right Stick Y (RZ)"
            MotionEvent.AXIS_RX -> "Axis RX"
            MotionEvent.AXIS_RY -> "Axis RY"
            MotionEvent.AXIS_HAT_X -> "D-Pad Hat X"
            MotionEvent.AXIS_HAT_Y -> "D-Pad Hat Y"
            MotionEvent.AXIS_LTRIGGER -> "L2 Trigger"
            MotionEvent.AXIS_RTRIGGER -> "R2 Trigger"
            MotionEvent.AXIS_GAS -> "Gas Pedal"
            MotionEvent.AXIS_BRAKE -> "Brake Pedal"
            MotionEvent.AXIS_WHEEL -> "Steering Wheel"
            MotionEvent.AXIS_THROTTLE -> "Flight Throttle"
            MotionEvent.AXIS_RUDDER -> "Rudder Yaw"
            else -> MotionEvent.axisToString(axisCode).removePrefix("AXIS_")
        }
    }

    fun buildControllersPayloadJson(context: Context): JSONObject {
        val devs = enumerateConnectedControllers(context)
        val controllersArr = JSONArray()
        for (c in devs) {
            val prof = getOrCreateProfile(c, context)
            val axesNamesArr = JSONArray()
            for (ax in c.supportedAxes) {
                axesNamesArr.put(axisCodeToDisplayName(ax))
            }
            val obj = JSONObject()
            obj.put("deviceId", c.deviceId)
            obj.put("descriptor", c.descriptor)
            obj.put("profileKey", c.profileKey)
            obj.put("name", c.name)
            obj.put("rawName", c.rawName)
            obj.put("vendorId", c.vendorId)
            obj.put("productId", c.productId)
            obj.put("vidPid", c.vendorProductHex)
            obj.put("port", c.playerPort)
            obj.put("type", c.controllerType)
            obj.put("supportedAxes", axesNamesArr)
            obj.put("profile", profileToJsonObject(prof))
            controllersArr.put(obj)
        }

        val portsObj = JSONObject()
        for ((p, rule) in getPortAssignmentsMap(context)) {
            portsObj.put(p.toString(), rule)
        }

        val result = JSONObject()
        result.put("controllers", controllersArr)
        result.put("portAssignments", portsObj)
        return result
    }
}
