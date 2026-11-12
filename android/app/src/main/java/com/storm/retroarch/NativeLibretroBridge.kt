package com.storm.retroarch

import android.util.Log
import android.view.Surface

/**
 * Thread-safe Kotlin bridge to libstorm_retroarch_jni.so (Libretro C API v1 host).
 */
object NativeLibretroBridge {

    private const val TAG = "StormLibretroBridge"

    @Volatile
    var isLibraryLoaded: Boolean = false
        private set

    // Libretro Joypad Button IDs
    const val RETRO_DEVICE_ID_JOYPAD_B = 0
    const val RETRO_DEVICE_ID_JOYPAD_Y = 1
    const val RETRO_DEVICE_ID_JOYPAD_SELECT = 2
    const val RETRO_DEVICE_ID_JOYPAD_START = 3
    const val RETRO_DEVICE_ID_JOYPAD_UP = 4
    const val RETRO_DEVICE_ID_JOYPAD_DOWN = 5
    const val RETRO_DEVICE_ID_JOYPAD_LEFT = 6
    const val RETRO_DEVICE_ID_JOYPAD_RIGHT = 7
    const val RETRO_DEVICE_ID_JOYPAD_A = 8
    const val RETRO_DEVICE_ID_JOYPAD_X = 9
    const val RETRO_DEVICE_ID_JOYPAD_L = 10
    const val RETRO_DEVICE_ID_JOYPAD_R = 11
    const val RETRO_DEVICE_ID_JOYPAD_L2 = 12
    const val RETRO_DEVICE_ID_JOYPAD_R2 = 13
    const val RETRO_DEVICE_ID_JOYPAD_L3 = 14
    const val RETRO_DEVICE_ID_JOYPAD_R3 = 15

    // Libretro Analog Indices & Axis IDs
    const val RETRO_DEVICE_INDEX_ANALOG_LEFT = 0
    const val RETRO_DEVICE_INDEX_ANALOG_RIGHT = 1
    const val RETRO_DEVICE_ID_ANALOG_X = 0
    const val RETRO_DEVICE_ID_ANALOG_Y = 1

    // Shader Modes
    const val SHADER_PIXEL_PERFECT = 0
    const val SHADER_BILINEAR_SMOOTH = 1
    const val SHADER_CRT_SCANLINES = 2
    const val SHADER_LCD_GRID = 3

    // Aspect Ratio Modes
    const val ASPECT_CORE_FIT = 0
    const val ASPECT_STRETCH = 1
    const val ASPECT_4_3 = 2
    const val ASPECT_16_9 = 3
    const val ASPECT_INTEGER = 4

    // RAM Scanner Modes
    const val RAM_SCAN_EXACT = 0
    const val RAM_SCAN_INCREASED = 1
    const val RAM_SCAN_DECREASED = 2
    const val RAM_SCAN_CHANGED = 3
    const val RAM_SCAN_UNCHANGED = 4

    init {
        try {
            System.loadLibrary("storm_retroarch_jni")
            isLibraryLoaded = true
        } catch (t: Throwable) {
            isLibraryLoaded = false
            Log.e(TAG, "Failed to load libstorm_retroarch_jni.so", t)
        }
    }

    // Surface, Shader, Aspect & Frame Capture
    @JvmStatic external fun nativeSetSurface(surface: Surface?)
    @JvmStatic external fun nativeOnSurfaceChanged(width: Int, height: Int)
    @JvmStatic external fun nativeSetShaderMode(mode: Int)
    @JvmStatic external fun nativeGetShaderMode(): Int
    @JvmStatic external fun nativeSetAspectMode(mode: Int)
    @JvmStatic external fun nativeGetAspectMode(): Int
    @JvmStatic external fun nativeCaptureFrameRgba(): ByteArray?
    @JvmStatic external fun nativeGetFrameWidth(): Int
    @JvmStatic external fun nativeGetFrameHeight(): Int
    @JvmStatic external fun nativeGetAspectRatio(): Float

    // Stereo PCM16 Audio Ring Buffer
    @JvmStatic external fun nativePullAudioSamples(outShortArray: ShortArray, maxSamples: Int): Int
    @JvmStatic external fun nativeGetSampleRate(): Int

    // Gamepad, Analog & Pointer Input
    @JvmStatic external fun nativeSetButton(port: Int, id: Int, pressed: Boolean)
    @JvmStatic external fun nativeSetAnalog(port: Int, index: Int, id: Int, value: Int)
    @JvmStatic external fun nativeSetPointer(x: Int, y: Int, pressed: Boolean)

    // Core & Emulation Lifecycle
    @JvmStatic external fun nativeLoadCoreAndGame(
        coreSoPath: String,
        romPath: String,
        systemDir: String,
        saveDir: String
    ): Boolean
    @JvmStatic external fun nativeRunFrame()
    @JvmStatic external fun nativeReset()
    @JvmStatic external fun nativeUnload()
    @JvmStatic external fun nativeGetFps(): Double
    @JvmStatic external fun nativeGetMeasuredFps(): Double
    @JvmStatic external fun nativeGetCoreLoadedInfo(): String
    @JvmStatic external fun nativeSetCoreVariable(key: String, value: String)
    @JvmStatic external fun nativeGetRumbleStrength(port: Int): Int

    // Battery SRAM & Save States
    @JvmStatic external fun nativeLoadSram(srmPath: String): Boolean
    @JvmStatic external fun nativeSaveSram(srmPath: String): Boolean
    @JvmStatic external fun nativeSaveState(statePath: String): Boolean
    @JvmStatic external fun nativeLoadState(statePath: String): Boolean

    // Rewind Ring Buffer (up to 120 snapshots in RAM)
    @JvmStatic external fun nativePushRewindSnapshot()
    @JvmStatic external fun nativePopRewindSnapshot(): Boolean
    @JvmStatic external fun nativeGetRewindCount(): Int
    @JvmStatic external fun nativeClearRewind()

    // Cheats & Live Native RAM Scanner (RETRO_MEMORY_SYSTEM_RAM)
    @JvmStatic external fun nativeResetCheats()
    @JvmStatic external fun nativeApplyCheat(index: Int, enabled: Boolean, code: String)
    @JvmStatic external fun nativeGetSystemRamSize(): Int
    @JvmStatic external fun nativeRamScanFirst(widthBytes: Int, compareValue: Long): Int
    @JvmStatic external fun nativeRamScanNext(mode: Int, compareValue: Long): Int
    @JvmStatic external fun nativeRamGetCandidates(maxCount: Int): LongArray
    @JvmStatic external fun nativeRamWriteValue(address: Int, widthBytes: Int, value: Long): Boolean
    @JvmStatic external fun nativeRamSetFrozen(address: Int, widthBytes: Int, value: Long, frozen: Boolean)
    @JvmStatic external fun nativeRamClearFrozen()

    @JvmStatic
    fun unpackCandidateAddress(packed: Long): Int = (packed ushr 32).toInt()

    @JvmStatic
    fun unpackCandidateValue(packed: Long): Long = packed and 0xFFFFFFFFL
}
