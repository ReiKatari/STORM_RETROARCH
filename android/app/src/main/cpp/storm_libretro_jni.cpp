#include "libretro.h"

#include <jni.h>
#include <android/log.h>
#include <android/native_window.h>
#include <android/native_window_jni.h>
#include <EGL/egl.h>
#include <EGL/eglext.h>
#include <GLES3/gl3.h>
#include <dlfcn.h>
#include <sys/stat.h>
#include <time.h>

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <deque>
#include <fstream>
#include <mutex>
#include <string>
#include <unordered_map>
#include <vector>

#define STORM_TAG "StormLibretroJNI"
#define LOGI(...) __android_log_print(ANDROID_LOG_INFO, STORM_TAG, __VA_ARGS__)
#define LOGW(...) __android_log_print(ANDROID_LOG_WARN, STORM_TAG, __VA_ARGS__)
#define LOGE(...) __android_log_print(ANDROID_LOG_ERROR, STORM_TAG, __VA_ARGS__)

namespace {

constexpr int MAX_PORTS = 8;
constexpr size_t AUDIO_RING_CAPACITY = 65536; // Stereo int16 samples (~680ms at 48kHz)
constexpr size_t MAX_REWIND_SNAPSHOTS = 120;
constexpr size_t MAX_REWIND_TOTAL_BYTES = 128ULL * 1024ULL * 1024ULL; // 128 MB cap
constexpr size_t MAX_ROM_IN_MEMORY_BYTES = 128ULL * 1024ULL * 1024ULL;

// Precomputed 16-bit -> RGBA_8888 LUTs (Android little-endian: [R, G, B, A])
uint32_t g_rgb1555ToRgba[65536];
uint32_t g_rgb565ToRgba[65536];
std::once_flag g_lutInitFlag;

void initPixelLutsOnce() {
    std::call_once(g_lutInitFlag, []() {
        for (uint32_t v = 0; v < 65536; ++v) {
            uint32_t r5_1555 = (v >> 10) & 0x1Fu;
            uint32_t g5_1555 = (v >> 5) & 0x1Fu;
            uint32_t b5_1555 = v & 0x1Fu;
            uint32_t r1 = (r5_1555 * 255u + 15u) / 31u;
            uint32_t g1 = (g5_1555 * 255u + 15u) / 31u;
            uint32_t b1 = (b5_1555 * 255u + 15u) / 31u;
            g_rgb1555ToRgba[v] = 0xFF000000u | (b1 << 16) | (g1 << 8) | r1;

            uint32_t r5_565 = (v >> 11) & 0x1Fu;
            uint32_t g6_565 = (v >> 5) & 0x3Fu;
            uint32_t b5_565 = v & 0x1Fu;
            uint32_t r2 = (r5_565 * 255u + 15u) / 31u;
            uint32_t g2 = (g6_565 * 255u + 31u) / 63u;
            uint32_t b2 = (b5_565 * 255u + 15u) / 31u;
            g_rgb565ToRgba[v] = 0xFF000000u | (b2 << 16) | (g2 << 8) | r2;
        }
    });
}

void ensureDirRecursive(const std::string& path) {
    if (path.empty()) return;
    std::string cur;
    cur.reserve(path.size() + 1);
    for (size_t i = 0; i < path.size(); ++i) {
        char c = path[i];
        if (c == '\\') c = '/';
        cur.push_back(c);
        if (c == '/' && cur.size() > 1) {
            mkdir(cur.c_str(), 0775);
        }
    }
    mkdir(cur.c_str(), 0775);
}

void ensureParentDir(const std::string& filePath) {
    size_t pos = filePath.find_last_of("/\\");
    if (pos != std::string::npos && pos > 0) {
        ensureDirRecursive(filePath.substr(0, pos));
    }
}

std::string jstringToStd(JNIEnv* env, jstring jstr) {
    if (!jstr) return std::string();
    const char* chars = env->GetStringUTFChars(jstr, nullptr);
    if (!chars) return std::string();
    std::string out(chars);
    env->ReleaseStringUTFChars(jstr, chars);
    return out;
}

// ==================== LOCKS & CORE STATE ====================

std::mutex g_coreMutex;
std::mutex g_surfaceMutex;
std::mutex g_audioMutex;
std::mutex g_ramFreezeMutex;

void* g_coreHandle = nullptr;
bool g_gameLoaded = false;

retro_init_fn g_retroInit = nullptr;
retro_deinit_fn g_retroDeinit = nullptr;
retro_api_version_fn g_retroApiVersion = nullptr;
retro_get_system_info_fn g_retroGetSystemInfo = nullptr;
retro_get_system_av_info_fn g_retroGetSystemAvInfo = nullptr;
retro_set_environment_fn g_retroSetEnvironment = nullptr;
retro_set_video_refresh_fn g_retroSetVideoRefresh = nullptr;
retro_set_audio_sample_fn g_retroSetAudioSample = nullptr;
retro_set_audio_sample_batch_fn g_retroSetAudioSampleBatch = nullptr;
retro_set_input_poll_fn g_retroSetInputPoll = nullptr;
retro_set_input_state_fn g_retroSetInputState = nullptr;
retro_set_controller_port_device_fn g_retroSetControllerPortDevice = nullptr;
retro_reset_fn g_retroReset = nullptr;
retro_run_fn g_retroRun = nullptr;
retro_serialize_size_fn g_retroSerializeSize = nullptr;
retro_serialize_fn g_retroSerialize = nullptr;
retro_unserialize_fn g_retroUnserialize = nullptr;
retro_cheat_reset_fn g_retroCheatReset = nullptr;
retro_cheat_set_fn g_retroCheatSet = nullptr;
retro_load_game_fn g_retroLoadGame = nullptr;
retro_unload_game_fn g_retroUnloadGame = nullptr;
retro_get_memory_data_fn g_retroGetMemoryData = nullptr;
retro_get_memory_size_fn g_retroGetMemorySize = nullptr;

std::string g_systemDir;
std::string g_saveDir;
std::string g_coreAssetsDir;
std::string g_libretroPath;
std::string g_coreLoadedInfo;
std::vector<uint8_t> g_romBuffer;

enum retro_pixel_format g_pixelFormat = RETRO_PIXEL_FORMAT_0RGB1555;
bool g_supportNoGame = false;
unsigned g_rotation = 0;
unsigned g_perfLevel = 4;

unsigned g_baseWidth = 256;
unsigned g_baseHeight = 224;
unsigned g_maxWidth = 512;
unsigned g_maxHeight = 480;
float g_aspectRatio = 4.0f / 3.0f;
std::atomic<double> g_coreTargetFps{60.0};
std::atomic<double> g_measuredFps{60.0};
std::atomic<double> g_sampleRate{44100.0};
std::chrono::steady_clock::time_point g_lastFrameTimestamp{};
bool g_hasLastFrameTimestamp = false;

std::unordered_map<std::string, std::string> g_variables;
std::atomic<bool> g_variablesDirty{false};
retro_frame_time_callback g_frameTimeCb{nullptr, 0};

struct CapturedMemRange {
    uint8_t* ptr = nullptr;
    size_t len = 0;
    uint64_t flags = 0;
};
std::vector<CapturedMemRange> g_memoryDescriptors;

// ==================== EGL OFFSCREEN HW RENDER ====================

bool g_hwRenderRequested = false;
bool g_hwContextInitialized = false;
retro_hw_render_callback g_hwCallback{};
EGLDisplay g_eglDisplay = EGL_NO_DISPLAY;
EGLContext g_eglContext = EGL_NO_CONTEXT;
EGLSurface g_eglSurface = EGL_NO_SURFACE;
GLuint g_hwFbo = 0;
GLuint g_hwColorTex = 0;
GLuint g_hwDepthRbo = 0;
unsigned g_hwFboWidth = 0;
unsigned g_hwFboHeight = 0;
std::vector<uint8_t> g_hwReadbackBuf;

uintptr_t RETRO_CALLCONV hwGetCurrentFramebuffer() {
    return static_cast<uintptr_t>(g_hwFbo);
}

retro_proc_address_t RETRO_CALLCONV hwGetProcAddress(const char* sym) {
    if (!sym) return nullptr;
    return reinterpret_cast<retro_proc_address_t>(eglGetProcAddress(sym));
}

bool ensureHwContextOnCurrentThread() {
    if (!g_hwRenderRequested) return false;
    if (g_eglDisplay != EGL_NO_DISPLAY && g_eglContext != EGL_NO_CONTEXT) {
        if (eglGetCurrentContext() != g_eglContext) {
            eglMakeCurrent(g_eglDisplay, g_eglSurface, g_eglSurface, g_eglContext);
        }
        if (!g_hwContextInitialized && g_hwCallback.context_reset) {
            g_hwContextInitialized = true;
            g_hwCallback.context_reset();
        }
        return true;
    }

    g_eglDisplay = eglGetDisplay(EGL_DEFAULT_DISPLAY);
    if (g_eglDisplay == EGL_NO_DISPLAY) return false;
    if (!eglInitialize(g_eglDisplay, nullptr, nullptr)) {
        g_eglDisplay = EGL_NO_DISPLAY;
        return false;
    }

    const bool wantEs3 = (g_hwCallback.context_type == RETRO_HW_CONTEXT_OPENGLES3) ||
                         (g_hwCallback.context_type == RETRO_HW_CONTEXT_OPENGLES_VERSION && g_hwCallback.version_major >= 3);
    const EGLint renderableBit = wantEs3 ? EGL_OPENGL_ES3_BIT_KHR : EGL_OPENGL_ES2_BIT;
    const EGLint depthBits = g_hwCallback.depth ? 24 : 0;
    const EGLint stencilBits = g_hwCallback.stencil ? 8 : 0;

    const EGLint cfgAttribs[] = {
        EGL_SURFACE_TYPE, EGL_PBUFFER_BIT,
        EGL_RENDERABLE_TYPE, renderableBit,
        EGL_RED_SIZE, 8,
        EGL_GREEN_SIZE, 8,
        EGL_BLUE_SIZE, 8,
        EGL_ALPHA_SIZE, 8,
        EGL_DEPTH_SIZE, depthBits,
        EGL_STENCIL_SIZE, stencilBits,
        EGL_NONE
    };
    EGLConfig config = nullptr;
    EGLint numConfigs = 0;
    if (!eglChooseConfig(g_eglDisplay, cfgAttribs, &config, 1, &numConfigs) || numConfigs < 1) {
        const EGLint fallbackAttribs[] = {
            EGL_SURFACE_TYPE, EGL_PBUFFER_BIT,
            EGL_RENDERABLE_TYPE, EGL_OPENGL_ES2_BIT,
            EGL_RED_SIZE, 8,
            EGL_GREEN_SIZE, 8,
            EGL_BLUE_SIZE, 8,
            EGL_ALPHA_SIZE, 8,
            EGL_DEPTH_SIZE, 16,
            EGL_NONE
        };
        if (!eglChooseConfig(g_eglDisplay, fallbackAttribs, &config, 1, &numConfigs) || numConfigs < 1) {
            return false;
        }
    }

    const EGLint pbufAttribs[] = { EGL_WIDTH, 16, EGL_HEIGHT, 16, EGL_NONE };
    g_eglSurface = eglCreatePbufferSurface(g_eglDisplay, config, pbufAttribs);
    if (g_eglSurface == EGL_NO_SURFACE) return false;

    const EGLint ctxEs3[] = { EGL_CONTEXT_CLIENT_VERSION, 3, EGL_NONE };
    const EGLint ctxEs2[] = { EGL_CONTEXT_CLIENT_VERSION, 2, EGL_NONE };
    if (wantEs3) {
        g_eglContext = eglCreateContext(g_eglDisplay, config, EGL_NO_CONTEXT, ctxEs3);
    }
    if (g_eglContext == EGL_NO_CONTEXT) {
        g_eglContext = eglCreateContext(g_eglDisplay, config, EGL_NO_CONTEXT, ctxEs2);
    }
    if (g_eglContext == EGL_NO_CONTEXT) {
        eglDestroySurface(g_eglDisplay, g_eglSurface);
        g_eglSurface = EGL_NO_SURFACE;
        return false;
    }

    if (!eglMakeCurrent(g_eglDisplay, g_eglSurface, g_eglSurface, g_eglContext)) {
        eglDestroyContext(g_eglDisplay, g_eglContext);
        eglDestroySurface(g_eglDisplay, g_eglSurface);
        g_eglContext = EGL_NO_CONTEXT;
        g_eglSurface = EGL_NO_SURFACE;
        return false;
    }

    g_hwFboWidth = std::max(64u, std::min(2048u, g_maxWidth > 0 ? g_maxWidth : 640u));
    g_hwFboHeight = std::max(64u, std::min(2048u, g_maxHeight > 0 ? g_maxHeight : 480u));

    glGenFramebuffers(1, &g_hwFbo);
    glBindFramebuffer(GL_FRAMEBUFFER, g_hwFbo);

    glGenTextures(1, &g_hwColorTex);
    glBindTexture(GL_TEXTURE_2D, g_hwColorTex);
    glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA, static_cast<GLsizei>(g_hwFboWidth),
                 static_cast<GLsizei>(g_hwFboHeight), 0, GL_RGBA, GL_UNSIGNED_BYTE, nullptr);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
    glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, g_hwColorTex, 0);

    if (g_hwCallback.depth || g_hwCallback.stencil) {
        glGenRenderbuffers(1, &g_hwDepthRbo);
        glBindRenderbuffer(GL_RENDERBUFFER, g_hwDepthRbo);
        GLenum internalFmt = g_hwCallback.stencil ? GL_DEPTH24_STENCIL8 : GL_DEPTH_COMPONENT16;
        glRenderbufferStorage(GL_RENDERBUFFER, internalFmt,
                              static_cast<GLsizei>(g_hwFboWidth), static_cast<GLsizei>(g_hwFboHeight));
        glFramebufferRenderbuffer(GL_FRAMEBUFFER, GL_DEPTH_ATTACHMENT, GL_RENDERBUFFER, g_hwDepthRbo);
        if (g_hwCallback.stencil) {
            glFramebufferRenderbuffer(GL_FRAMEBUFFER, GL_STENCIL_ATTACHMENT, GL_RENDERBUFFER, g_hwDepthRbo);
        }
    }
    glBindFramebuffer(GL_FRAMEBUFFER, 0);

    if (g_hwCallback.context_reset) {
        g_hwContextInitialized = true;
        g_hwCallback.context_reset();
    }
    return true;
}

void destroyHwContext() {
    if (g_eglDisplay != EGL_NO_DISPLAY && g_eglContext != EGL_NO_CONTEXT) {
        eglMakeCurrent(g_eglDisplay, g_eglSurface, g_eglSurface, g_eglContext);
        if (g_hwContextInitialized && g_hwCallback.context_destroy) {
            g_hwCallback.context_destroy();
        }
        if (g_hwDepthRbo) {
            glDeleteRenderbuffers(1, &g_hwDepthRbo);
            g_hwDepthRbo = 0;
        }
        if (g_hwColorTex) {
            glDeleteTextures(1, &g_hwColorTex);
            g_hwColorTex = 0;
        }
        if (g_hwFbo) {
            glDeleteFramebuffers(1, &g_hwFbo);
            g_hwFbo = 0;
        }
        eglMakeCurrent(g_eglDisplay, EGL_NO_SURFACE, EGL_NO_SURFACE, EGL_NO_CONTEXT);
        eglDestroyContext(g_eglDisplay, g_eglContext);
        if (g_eglSurface != EGL_NO_SURFACE) {
            eglDestroySurface(g_eglDisplay, g_eglSurface);
        }
        eglTerminate(g_eglDisplay);
    }
    g_eglDisplay = EGL_NO_DISPLAY;
    g_eglContext = EGL_NO_CONTEXT;
    g_eglSurface = EGL_NO_SURFACE;
    g_hwRenderRequested = false;
    g_hwContextInitialized = false;
    std::memset(&g_hwCallback, 0, sizeof(g_hwCallback));
    g_hwReadbackBuf.clear();
}

// ==================== SURFACE & VIDEO RENDERING ====================

ANativeWindow* g_nativeWindow = nullptr;
int g_surfacePhysW = 0;
int g_surfacePhysH = 0;
int g_configuredBufW = 0;
int g_configuredBufH = 0;

std::vector<uint32_t> g_frameRgba;
std::atomic<int> g_frameWidth{0};
std::atomic<int> g_frameHeight{0};
std::atomic<int> g_shaderMode{0}; // 0=Pixel-Perfect, 1=Bilinear Smooth, 2=CRT Scanlines, 3=LCD Grid
std::atomic<int> g_aspectMode{0}; // 0=Core Aspect Fit, 1=Stretch, 2=4:3, 3=16:9, 4=Integer Scale

inline uint32_t darkenRgba75(uint32_t px) {
    uint32_t rgb = ((px & 0x00FEFEFEu) >> 1) + ((px & 0x00FCFCFCu) >> 2);
    return 0xFF000000u | rgb;
}

inline uint32_t darkenRgba68(uint32_t px) {
    uint32_t half = (px & 0x00FEFEFEu) >> 1;
    uint32_t eighth = (px & 0x00F8F8F8u) >> 3;
    uint32_t sixteenth = (px & 0x00F0F0F0u) >> 4;
    return 0xFF000000u | (half + eighth + sixteenth);
}

inline uint32_t bilinearSampleRgba(uint32_t c00, uint32_t c10, uint32_t c01, uint32_t c11, uint32_t fx, uint32_t fy) {
    // fx, fy in [0..256]
    uint32_t ifx = 256u - fx;
    uint32_t ify = 256u - fy;
    uint32_t w00 = (ifx * ify) >> 8;
    uint32_t w10 = (fx * ify) >> 8;
    uint32_t w01 = (ifx * fy) >> 8;
    uint32_t w11 = (fx * fy) >> 8;

    uint32_t rb = (((c00 & 0x00FF00FFu) * w00) +
                   ((c10 & 0x00FF00FFu) * w10) +
                   ((c01 & 0x00FF00FFu) * w01) +
                   ((c11 & 0x00FF00FFu) * w11)) >> 8;
    uint32_t g = (((c00 & 0x0000FF00u) * w00) +
                  ((c10 & 0x0000FF00u) * w10) +
                  ((c01 & 0x0000FF00u) * w01) +
                  ((c11 & 0x0000FF00u) * w11)) >> 8;
    return 0xFF000000u | (rb & 0x00FF00FFu) | (g & 0x0000FF00u);
}

void presentFrameLocked() {
    if (!g_nativeWindow) return;
    const int srcW = g_frameWidth.load(std::memory_order_relaxed);
    const int srcH = g_frameHeight.load(std::memory_order_relaxed);
    if (srcW <= 0 || srcH <= 0 || g_frameRgba.size() < static_cast<size_t>(srcW * srcH)) return;

    const int shader = g_shaderMode.load(std::memory_order_relaxed);
    const int aspectMode = g_aspectMode.load(std::memory_order_relaxed);

    // Choose crisp output buffer size so scanlines / LCD grid / pixel edges render cleanly
    int scaleMult = (srcH <= 256) ? 3 : 2;
    int targetH = std::clamp(srcH * scaleMult, 480, 960);
    float surfAspect = (g_surfacePhysW > 0 && g_surfacePhysH > 0)
                           ? (static_cast<float>(g_surfacePhysW) / static_cast<float>(g_surfacePhysH))
                           : ((g_aspectRatio > 0.1f) ? g_aspectRatio : (static_cast<float>(srcW) / static_cast<float>(srcH)));
    int targetW = std::clamp(static_cast<int>(std::lround(targetH * surfAspect)), 320, 1920);

    if (targetW != g_configuredBufW || targetH != g_configuredBufH) {
        if (ANativeWindow_setBuffersGeometry(g_nativeWindow, targetW, targetH, AHARDWAREBUFFER_FORMAT_R8G8B8A8_UNORM) == 0) {
            g_configuredBufW = targetW;
            g_configuredBufH = targetH;
        }
    }

    ANativeWindow_Buffer winBuf{};
    if (ANativeWindow_lock(g_nativeWindow, &winBuf, nullptr) != 0 || !winBuf.bits) {
        return;
    }

    const int dstW = winBuf.width;
    const int dstH = winBuf.height;
    const int dstStride = winBuf.stride;
    uint32_t* dstBase = static_cast<uint32_t*>(winBuf.bits);

    if (dstW > 0 && dstH > 0 && dstStride >= dstW) {
        int vpX = 0, vpY = 0, vpW = dstW, vpH = dstH;
        if (aspectMode == 4) {
            int intScale = std::max(1, std::min(dstW / srcW, dstH / srcH));
            vpW = std::min(dstW, srcW * intScale);
            vpH = std::min(dstH, srcH * intScale);
            vpX = (dstW - vpW) / 2;
            vpY = (dstH - vpH) / 2;
        } else if (aspectMode != 1) {
            float gameAspect = (g_aspectRatio > 0.1f) ? g_aspectRatio : (static_cast<float>(srcW) / static_cast<float>(srcH));
            if (aspectMode == 2) gameAspect = 4.0f / 3.0f;
            else if (aspectMode == 3) gameAspect = 16.0f / 9.0f;

            float dstAspect = static_cast<float>(dstW) / static_cast<float>(dstH);
            if (dstAspect > gameAspect) {
                vpH = dstH;
                vpW = std::clamp(static_cast<int>(std::lround(dstH * gameAspect)), 1, dstW);
            } else {
                vpW = dstW;
                vpH = std::clamp(static_cast<int>(std::lround(dstW / gameAspect)), 1, dstH);
            }
            vpX = (dstW - vpW) / 2;
            vpY = (dstH - vpH) / 2;
        }

        // Clear top/bottom letterbox bars
        for (int y = 0; y < vpY; ++y) {
            std::fill_n(dstBase + y * dstStride, dstW, 0xFF000000u);
        }
        for (int y = vpY + vpH; y < dstH; ++y) {
            std::fill_n(dstBase + y * dstStride, dstW, 0xFF000000u);
        }

        std::vector<int> xMap(static_cast<size_t>(vpW));
        for (int vx = 0; vx < vpW; ++vx) {
            xMap[static_cast<size_t>(vx)] = (vx * srcW) / vpW;
        }

        const uint32_t* srcPixels = g_frameRgba.data();

        if (shader == 1) {
            // 1 = Bilinear Smooth (16.16 / 8-bit fractional fixed-point)
            std::vector<int> sx0Tab(static_cast<size_t>(vpW));
            std::vector<int> sx1Tab(static_cast<size_t>(vpW));
            std::vector<uint32_t> fxTab(static_cast<size_t>(vpW));
            for (int vx = 0; vx < vpW; ++vx) {
                int64_t pos256 = (static_cast<int64_t>(vx * 2 + 1) * srcW * 128) / vpW - 128;
                if (pos256 < 0) pos256 = 0;
                int s0 = static_cast<int>(pos256 >> 8);
                uint32_t frac = static_cast<uint32_t>(pos256 & 0xFF);
                if (s0 >= srcW - 1) {
                    s0 = srcW - 1;
                    frac = 0;
                }
                sx0Tab[static_cast<size_t>(vx)] = s0;
                sx1Tab[static_cast<size_t>(vx)] = std::min(srcW - 1, s0 + 1);
                fxTab[static_cast<size_t>(vx)] = frac;
            }

            for (int vy = 0; vy < vpH; ++vy) {
                uint32_t* dstRow = dstBase + (vpY + vy) * dstStride;
                if (vpX > 0) std::fill_n(dstRow, vpX, 0xFF000000u);
                if (vpX + vpW < dstW) std::fill_n(dstRow + vpX + vpW, dstW - (vpX + vpW), 0xFF000000u);

                int64_t posY256 = (static_cast<int64_t>(vy * 2 + 1) * srcH * 128) / vpH - 128;
                if (posY256 < 0) posY256 = 0;
                int sy0 = static_cast<int>(posY256 >> 8);
                uint32_t fy = static_cast<uint32_t>(posY256 & 0xFF);
                if (sy0 >= srcH - 1) {
                    sy0 = srcH - 1;
                    fy = 0;
                }
                int sy1 = std::min(srcH - 1, sy0 + 1);
                const uint32_t* row0 = srcPixels + sy0 * srcW;
                const uint32_t* row1 = srcPixels + sy1 * srcW;

                uint32_t* vpOut = dstRow + vpX;
                for (int vx = 0; vx < vpW; ++vx) {
                    int x0 = sx0Tab[static_cast<size_t>(vx)];
                    int x1 = sx1Tab[static_cast<size_t>(vx)];
                    vpOut[vx] = bilinearSampleRgba(row0[x0], row0[x1], row1[x0], row1[x1],
                                                   fxTab[static_cast<size_t>(vx)], fy);
                }
            }
        } else if (shader == 2) {
            // 2 = CRT Scanlines
            for (int vy = 0; vy < vpH; ++vy) {
                uint32_t* dstRow = dstBase + (vpY + vy) * dstStride;
                if (vpX > 0) std::fill_n(dstRow, vpX, 0xFF000000u);
                if (vpX + vpW < dstW) std::fill_n(dstRow + vpX + vpW, dstW - (vpX + vpW), 0xFF000000u);

                int sy = (vy * srcH) / vpH;
                int nextSy = ((vy + 1) * srcH) / vpH;
                bool isScanlineGap = (vpH >= srcH * 2) ? (nextSy != sy) : ((vy & 1) == 1);
                const uint32_t* srcRow = srcPixels + sy * srcW;
                uint32_t* vpOut = dstRow + vpX;

                if (isScanlineGap) {
                    for (int vx = 0; vx < vpW; ++vx) {
                        vpOut[vx] = darkenRgba68(srcRow[xMap[static_cast<size_t>(vx)]]);
                    }
                } else {
                    for (int vx = 0; vx < vpW; ++vx) {
                        vpOut[vx] = srcRow[xMap[static_cast<size_t>(vx)]];
                    }
                }
            }
        } else if (shader == 3) {
            // 3 = LCD Grid (Handheld subpixel cell matrix)
            std::vector<uint8_t> xEdge(static_cast<size_t>(vpW), 0);
            if (vpW >= srcW * 2) {
                for (int vx = 0; vx < vpW - 1; ++vx) {
                    if (xMap[static_cast<size_t>(vx + 1)] != xMap[static_cast<size_t>(vx)]) {
                        xEdge[static_cast<size_t>(vx)] = 1;
                    }
                }
            } else {
                for (int vx = 0; vx < vpW; ++vx) {
                    xEdge[static_cast<size_t>(vx)] = static_cast<uint8_t>(vx & 1);
                }
            }

            for (int vy = 0; vy < vpH; ++vy) {
                uint32_t* dstRow = dstBase + (vpY + vy) * dstStride;
                if (vpX > 0) std::fill_n(dstRow, vpX, 0xFF000000u);
                if (vpX + vpW < dstW) std::fill_n(dstRow + vpX + vpW, dstW - (vpX + vpW), 0xFF000000u);

                int sy = (vy * srcH) / vpH;
                int nextSy = ((vy + 1) * srcH) / vpH;
                bool yEdge = (vpH >= srcH * 2) ? (nextSy != sy) : ((vy & 1) == 1);
                const uint32_t* srcRow = srcPixels + sy * srcW;
                uint32_t* vpOut = dstRow + vpX;

                if (yEdge) {
                    for (int vx = 0; vx < vpW; ++vx) {
                        vpOut[vx] = darkenRgba75(srcRow[xMap[static_cast<size_t>(vx)]]);
                    }
                } else {
                    for (int vx = 0; vx < vpW; ++vx) {
                        uint32_t px = srcRow[xMap[static_cast<size_t>(vx)]];
                        vpOut[vx] = xEdge[static_cast<size_t>(vx)] ? darkenRgba75(px) : px;
                    }
                }
            }
        } else {
            // 0 = Pixel-Perfect Nearest-Neighbor
            int prevSy = -1;
            for (int vy = 0; vy < vpH; ++vy) {
                uint32_t* dstRow = dstBase + (vpY + vy) * dstStride;
                if (vpX > 0) std::fill_n(dstRow, vpX, 0xFF000000u);
                if (vpX + vpW < dstW) std::fill_n(dstRow + vpX + vpW, dstW - (vpX + vpW), 0xFF000000u);

                int sy = (vy * srcH) / vpH;
                uint32_t* vpOut = dstRow + vpX;
                if (sy == prevSy && vy > 0) {
                    std::memcpy(vpOut, dstBase + (vpY + vy - 1) * dstStride + vpX,
                                static_cast<size_t>(vpW) * sizeof(uint32_t));
                } else {
                    const uint32_t* srcRow = srcPixels + sy * srcW;
                    for (int vx = 0; vx < vpW; ++vx) {
                        vpOut[vx] = srcRow[xMap[static_cast<size_t>(vx)]];
                    }
                    prevSy = sy;
                }
            }
        }
    }

    ANativeWindow_unlockAndPost(g_nativeWindow);
}

// ==================== STEREO PCM16 AUDIO RING BUFFER ====================

int16_t g_audioRing[AUDIO_RING_CAPACITY];
size_t g_audioReadIdx = 0;
size_t g_audioWriteIdx = 0;
size_t g_audioCount = 0;

void pushAudioStereoSamples(const int16_t* samples, size_t stereoFrames) {
    if (!samples || stereoFrames == 0) return;
    size_t sampleCount = stereoFrames * 2;
    std::lock_guard<std::mutex> lock(g_audioMutex);

    if (sampleCount >= AUDIO_RING_CAPACITY) {
        samples += (sampleCount - AUDIO_RING_CAPACITY);
        sampleCount = AUDIO_RING_CAPACITY;
        g_audioReadIdx = 0;
        g_audioWriteIdx = 0;
        g_audioCount = 0;
    }

    size_t freeSpace = AUDIO_RING_CAPACITY - g_audioCount;
    if (sampleCount > freeSpace) {
        size_t drop = (sampleCount - freeSpace + 1u) & ~size_t(1u);
        if (drop > g_audioCount) drop = g_audioCount;
        g_audioReadIdx = (g_audioReadIdx + drop) % AUDIO_RING_CAPACITY;
        g_audioCount -= drop;
    }

    size_t firstChunk = std::min(sampleCount, AUDIO_RING_CAPACITY - g_audioWriteIdx);
    std::memcpy(g_audioRing + g_audioWriteIdx, samples, firstChunk * sizeof(int16_t));
    size_t secondChunk = sampleCount - firstChunk;
    if (secondChunk > 0) {
        std::memcpy(g_audioRing, samples + firstChunk, secondChunk * sizeof(int16_t));
    }
    g_audioWriteIdx = (g_audioWriteIdx + sampleCount) % AUDIO_RING_CAPACITY;
    g_audioCount += sampleCount;
}

// ==================== INPUT & RUMBLE STATE ====================

std::atomic<uint32_t> g_joypadButtons[MAX_PORTS]{};
std::atomic<int16_t> g_analogAxes[MAX_PORTS][4][2]{};
std::atomic<int16_t> g_pointerX{0};
std::atomic<int16_t> g_pointerY{0};
std::atomic<bool> g_pointerPressed{false};
std::atomic<uint16_t> g_rumbleStrong[MAX_PORTS]{};
std::atomic<uint16_t> g_rumbleWeak[MAX_PORTS]{};

// ==================== REWIND & RAM SCANNER / FREEZE ====================

std::deque<std::vector<uint8_t>> g_rewindRing;
size_t g_rewindTotalBytes = 0;

struct RamCandidate {
    uint32_t address;
    uint32_t lastValue;
};

struct FrozenRamEntry {
    uint32_t address;
    int widthBytes;
    uint32_t value;
};

int g_ramScanWidthBytes = 1;
std::vector<RamCandidate> g_ramCandidates;
std::vector<FrozenRamEntry> g_frozenRamEntries;

bool getSystemRamRegionLocked(uint8_t** outPtr, size_t* outSize) {
    *outPtr = nullptr;
    *outSize = 0;
    if (!g_gameLoaded) return false;

    if (g_retroGetMemoryData && g_retroGetMemorySize) {
        void* p = g_retroGetMemoryData(RETRO_MEMORY_SYSTEM_RAM);
        size_t sz = g_retroGetMemorySize(RETRO_MEMORY_SYSTEM_RAM);
        if (p && sz > 0) {
            *outPtr = static_cast<uint8_t*>(p);
            *outSize = sz;
            return true;
        }
    }

    // Fallback to largest descriptor reported via RETRO_ENVIRONMENT_SET_MEMORY_MAPS
    size_t bestLen = 0;
    uint8_t* bestPtr = nullptr;
    for (const auto& d : g_memoryDescriptors) {
        if (d.ptr && d.len > bestLen) {
            bestLen = d.len;
            bestPtr = d.ptr;
        }
    }
    if (bestPtr && bestLen > 0) {
        *outPtr = bestPtr;
        *outSize = bestLen;
        return true;
    }

    if (g_retroGetMemoryData && g_retroGetMemorySize) {
        void* p = g_retroGetMemoryData(RETRO_MEMORY_SAVE_RAM);
        size_t sz = g_retroGetMemorySize(RETRO_MEMORY_SAVE_RAM);
        if (p && sz > 0) {
            *outPtr = static_cast<uint8_t*>(p);
            *outSize = sz;
            return true;
        }
    }
    return false;
}

inline uint32_t widthMask(int widthBytes) {
    if (widthBytes == 1) return 0xFFu;
    if (widthBytes == 2) return 0xFFFFu;
    return 0xFFFFFFFFu;
}

inline uint32_t readRamLittleEndian(const uint8_t* ram, uint32_t addr, int widthBytes) {
    if (widthBytes == 1) {
        return static_cast<uint32_t>(ram[addr]);
    }
    if (widthBytes == 2) {
        return static_cast<uint32_t>(ram[addr]) |
               (static_cast<uint32_t>(ram[addr + 1]) << 8);
    }
    return static_cast<uint32_t>(ram[addr]) |
           (static_cast<uint32_t>(ram[addr + 1]) << 8) |
           (static_cast<uint32_t>(ram[addr + 2]) << 16) |
           (static_cast<uint32_t>(ram[addr + 3]) << 24);
}

inline void writeRamLittleEndian(uint8_t* ram, uint32_t addr, int widthBytes, uint32_t val) {
    ram[addr] = static_cast<uint8_t>(val & 0xFFu);
    if (widthBytes >= 2) {
        ram[addr + 1] = static_cast<uint8_t>((val >> 8) & 0xFFu);
    }
    if (widthBytes == 4) {
        ram[addr + 2] = static_cast<uint8_t>((val >> 16) & 0xFFu);
        ram[addr + 3] = static_cast<uint8_t>((val >> 24) & 0xFFu);
    }
}

void applyFrozenRamLocked() {
    std::lock_guard<std::mutex> freezeLock(g_ramFreezeMutex);
    if (g_frozenRamEntries.empty()) return;

    uint8_t* ram = nullptr;
    size_t ramSize = 0;
    if (!getSystemRamRegionLocked(&ram, &ramSize) || !ram || ramSize == 0) return;

    for (const auto& entry : g_frozenRamEntries) {
        if (entry.widthBytes != 1 && entry.widthBytes != 2 && entry.widthBytes != 4) continue;
        if (static_cast<size_t>(entry.address) + static_cast<size_t>(entry.widthBytes) <= ramSize) {
            writeRamLittleEndian(ram, entry.address, entry.widthBytes, entry.value);
        }
    }
}

// ==================== LIBRETRO CALLBACKS ====================

void RETRO_CALLCONV coreLogCallback(enum retro_log_level level, const char* fmt, ...) {
    if (!fmt) return;
    int prio = ANDROID_LOG_INFO;
    switch (level) {
        case RETRO_LOG_DEBUG: prio = ANDROID_LOG_DEBUG; break;
        case RETRO_LOG_INFO:  prio = ANDROID_LOG_INFO; break;
        case RETRO_LOG_WARN:  prio = ANDROID_LOG_WARN; break;
        case RETRO_LOG_ERROR: prio = ANDROID_LOG_ERROR; break;
        default: break;
    }
    va_list args;
    va_start(args, fmt);
    __android_log_vprint(prio, "StormCore", fmt, args);
    va_end(args);
}

bool RETRO_CALLCONV rumbleCallback(unsigned port, enum retro_rumble_effect effect, uint16_t strength) {
    if (port >= MAX_PORTS) return false;
    if (effect == RETRO_RUMBLE_STRONG) {
        g_rumbleStrong[port].store(strength, std::memory_order_relaxed);
    } else if (effect == RETRO_RUMBLE_WEAK) {
        g_rumbleWeak[port].store(strength, std::memory_order_relaxed);
    }
    return true;
}

retro_time_t RETRO_CALLCONV perfGetTimeUsec() {
    struct timespec ts{};
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return static_cast<retro_time_t>(ts.tv_sec) * 1000000LL + static_cast<retro_time_t>(ts.tv_nsec / 1000LL);
}

retro_perf_tick_t RETRO_CALLCONV perfGetCounter() {
    struct timespec ts{};
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return static_cast<retro_perf_tick_t>(ts.tv_sec) * 1000000000ULL + static_cast<retro_perf_tick_t>(ts.tv_nsec);
}

uint64_t RETRO_CALLCONV perfGetCpuFeatures() {
    return 0;
}

void RETRO_CALLCONV perfRegister(struct retro_perf_counter* counter) {
    if (counter) counter->registered = true;
}

void RETRO_CALLCONV perfStart(struct retro_perf_counter* counter) {
    if (counter) {
        counter->call_cnt++;
        counter->start = perfGetCounter();
    }
}

void RETRO_CALLCONV perfStop(struct retro_perf_counter* counter) {
    if (counter) {
        counter->total += (perfGetCounter() - counter->start);
    }
}

void RETRO_CALLCONV perfLog() {}

void parseVariablesOptionString(const char* key, const char* descAndVals) {
    if (!key || !descAndVals) return;
    std::string k(key);
    if (k.empty() || g_variables.find(k) != g_variables.end()) return;

    const char* semi = std::strchr(descAndVals, ';');
    if (!semi) return;
    const char* p = semi + 1;
    while (*p == ' ' || *p == '\t') ++p;
    const char* pipe = std::strchr(p, '|');
    std::string defVal = pipe ? std::string(p, static_cast<size_t>(pipe - p)) : std::string(p);
    if (!defVal.empty()) {
        g_variables[k] = defVal;
    }
}

void parseCoreOptionDefsV1(const struct retro_core_option_definition* defs) {
    if (!defs) return;
    for (size_t i = 0; defs[i].key != nullptr; ++i) {
        std::string k(defs[i].key);
        if (g_variables.find(k) != g_variables.end()) continue;
        if (defs[i].default_value && defs[i].default_value[0] != '\0') {
            g_variables[k] = defs[i].default_value;
        } else if (defs[i].values[0].value) {
            g_variables[k] = defs[i].values[0].value;
        }
    }
}

void parseCoreOptionDefsV2(const struct retro_core_option_v2_definition* defs) {
    if (!defs) return;
    for (size_t i = 0; defs[i].key != nullptr; ++i) {
        std::string k(defs[i].key);
        if (g_variables.find(k) != g_variables.end()) continue;
        if (defs[i].default_value && defs[i].default_value[0] != '\0') {
            g_variables[k] = defs[i].default_value;
        } else if (defs[i].values[0].value) {
            g_variables[k] = defs[i].values[0].value;
        }
    }
}

bool RETRO_CALLCONV environmentCallback(unsigned cmd, void* data) {
    switch (cmd) {
        case RETRO_ENVIRONMENT_GET_SYSTEM_DIRECTORY: {
            if (!data) return false;
            *static_cast<const char**>(data) = g_systemDir.c_str();
            return true;
        }
        case RETRO_ENVIRONMENT_GET_SAVE_DIRECTORY: {
            if (!data) return false;
            *static_cast<const char**>(data) = g_saveDir.c_str();
            return true;
        }
        case RETRO_ENVIRONMENT_GET_CORE_ASSETS_DIRECTORY: {
            if (!data) return false;
            *static_cast<const char**>(data) = g_coreAssetsDir.c_str();
            return true;
        }
        case RETRO_ENVIRONMENT_GET_LIBRETRO_PATH: {
            if (!data) return false;
            *static_cast<const char**>(data) = g_libretroPath.c_str();
            return true;
        }
        case RETRO_ENVIRONMENT_SET_PIXEL_FORMAT: {
            if (!data) return false;
            const auto fmt = *static_cast<const enum retro_pixel_format*>(data);
            if (fmt == RETRO_PIXEL_FORMAT_0RGB1555 ||
                fmt == RETRO_PIXEL_FORMAT_XRGB8888 ||
                fmt == RETRO_PIXEL_FORMAT_RGB565) {
                g_pixelFormat = fmt;
                return true;
            }
            return false;
        }
        case RETRO_ENVIRONMENT_GET_CAN_DUPE: {
            if (!data) return false;
            *static_cast<bool*>(data) = true;
            return true;
        }
        case RETRO_ENVIRONMENT_GET_OVERSCAN: {
            if (!data) return false;
            *static_cast<bool*>(data) = false;
            return true;
        }
        case RETRO_ENVIRONMENT_SET_ROTATION: {
            if (!data) return false;
            g_rotation = (*static_cast<const unsigned*>(data)) & 3u;
            return true;
        }
        case RETRO_ENVIRONMENT_SET_PERFORMANCE_LEVEL: {
            if (data) g_perfLevel = *static_cast<const unsigned*>(data);
            return true;
        }
        case RETRO_ENVIRONMENT_SET_INPUT_DESCRIPTORS: {
            return true;
        }
        case RETRO_ENVIRONMENT_SET_SUPPORT_NO_GAME: {
            if (data) g_supportNoGame = *static_cast<const bool*>(data);
            return true;
        }
        case RETRO_ENVIRONMENT_SET_VARIABLES: {
            if (!data) return false;
            const auto* vars = static_cast<const struct retro_variable*>(data);
            for (size_t i = 0; vars[i].key != nullptr; ++i) {
                parseVariablesOptionString(vars[i].key, vars[i].value);
            }
            return true;
        }
        case RETRO_ENVIRONMENT_GET_CORE_OPTIONS_VERSION: {
            if (!data) return false;
            *static_cast<unsigned*>(data) = 0;
            return true;
        }
        case RETRO_ENVIRONMENT_SET_CORE_OPTIONS: {
            if (!data) return false;
            parseCoreOptionDefsV1(static_cast<const struct retro_core_option_definition*>(data));
            return true;
        }
        case RETRO_ENVIRONMENT_SET_CORE_OPTIONS_INTL: {
            if (!data) return false;
            const auto* intl = static_cast<const struct retro_core_options_intl*>(data);
            parseCoreOptionDefsV1(intl->us);
            return true;
        }
        case RETRO_ENVIRONMENT_SET_CORE_OPTIONS_V2: {
            if (!data) return false;
            const auto* v2 = static_cast<const struct retro_core_options_v2*>(data);
            parseCoreOptionDefsV2(v2->definitions);
            return true;
        }
        case RETRO_ENVIRONMENT_SET_CORE_OPTIONS_V2_INTL: {
            if (!data) return false;
            const auto* intl = static_cast<const struct retro_core_options_v2_intl*>(data);
            if (intl->us) parseCoreOptionDefsV2(intl->us->definitions);
            return true;
        }
        case RETRO_ENVIRONMENT_SET_CORE_OPTIONS_DISPLAY:
        case RETRO_ENVIRONMENT_SET_CORE_OPTIONS_UPDATE_DISPLAY_CALLBACK: {
            return true;
        }
        case RETRO_ENVIRONMENT_GET_VARIABLE: {
            if (!data) return false;
            auto* req = static_cast<struct retro_variable*>(data);
            if (!req->key) {
                req->value = nullptr;
                return false;
            }
            auto it = g_variables.find(req->key);
            if (it != g_variables.end()) {
                req->value = it->second.c_str();
                return true;
            }
            req->value = nullptr;
            return false;
        }
        case RETRO_ENVIRONMENT_SET_VARIABLE: {
            if (!data) return true;
            const auto* kv = static_cast<const struct retro_variable*>(data);
            if (kv->key && kv->value) {
                g_variables[kv->key] = kv->value;
                g_variablesDirty.store(true, std::memory_order_relaxed);
            }
            return true;
        }
        case RETRO_ENVIRONMENT_GET_VARIABLE_UPDATE: {
            if (!data) return false;
            *static_cast<bool*>(data) = g_variablesDirty.exchange(false, std::memory_order_relaxed);
            return true;
        }
        case RETRO_ENVIRONMENT_GET_RUMBLE_INTERFACE: {
            if (!data) return false;
            auto* rumble = static_cast<struct retro_rumble_interface*>(data);
            rumble->set_rumble_state = rumbleCallback;
            return true;
        }
        case RETRO_ENVIRONMENT_GET_LOG_INTERFACE: {
            if (!data) return false;
            auto* logCb = static_cast<struct retro_log_callback*>(data);
            logCb->log = coreLogCallback;
            return true;
        }
        case RETRO_ENVIRONMENT_GET_PERF_INTERFACE: {
            if (!data) return false;
            auto* perf = static_cast<struct retro_perf_callback*>(data);
            perf->get_time_usec = perfGetTimeUsec;
            perf->get_cpu_features = perfGetCpuFeatures;
            perf->get_perf_counter = perfGetCounter;
            perf->perf_register = perfRegister;
            perf->perf_start = perfStart;
            perf->perf_stop = perfStop;
            perf->perf_log = perfLog;
            return true;
        }
        case RETRO_ENVIRONMENT_SET_GEOMETRY: {
            if (!data) return false;
            const auto* geom = static_cast<const struct retro_game_geometry*>(data);
            if (geom->base_width > 0) g_baseWidth = geom->base_width;
            if (geom->base_height > 0) g_baseHeight = geom->base_height;
            if (geom->aspect_ratio > 0.05f) {
                g_aspectRatio = geom->aspect_ratio;
            } else if (g_baseWidth > 0 && g_baseHeight > 0) {
                g_aspectRatio = static_cast<float>(g_baseWidth) / static_cast<float>(g_baseHeight);
            }
            return true;
        }
        case RETRO_ENVIRONMENT_SET_SYSTEM_AV_INFO: {
            if (!data) return false;
            const auto* av = static_cast<const struct retro_system_av_info*>(data);
            if (av->geometry.base_width > 0) g_baseWidth = av->geometry.base_width;
            if (av->geometry.base_height > 0) g_baseHeight = av->geometry.base_height;
            if (av->geometry.max_width > 0) g_maxWidth = av->geometry.max_width;
            if (av->geometry.max_height > 0) g_maxHeight = av->geometry.max_height;
            g_aspectRatio = (av->geometry.aspect_ratio > 0.05f)
                                ? av->geometry.aspect_ratio
                                : (static_cast<float>(g_baseWidth) / static_cast<float>(std::max(1u, g_baseHeight)));
            if (av->timing.fps > 1.0) g_coreTargetFps.store(av->timing.fps, std::memory_order_relaxed);
            if (av->timing.sample_rate > 1000.0) g_sampleRate.store(av->timing.sample_rate, std::memory_order_relaxed);
            return true;
        }
        case RETRO_ENVIRONMENT_GET_INPUT_DEVICE_CAPABILITIES: {
            if (!data) return false;
            *static_cast<uint64_t*>(data) =
                (1ULL << RETRO_DEVICE_JOYPAD) |
                (1ULL << RETRO_DEVICE_ANALOG) |
                (1ULL << RETRO_DEVICE_POINTER) |
                (1ULL << RETRO_DEVICE_LIGHTGUN);
            return true;
        }
        case RETRO_ENVIRONMENT_GET_INPUT_BITMASKS: {
            if (data) *static_cast<bool*>(data) = true;
            return true;
        }
        case RETRO_ENVIRONMENT_GET_INPUT_MAX_USERS: {
            if (!data) return false;
            *static_cast<unsigned*>(data) = MAX_PORTS;
            return true;
        }
        case RETRO_ENVIRONMENT_GET_LANGUAGE: {
            if (!data) return false;
            *static_cast<unsigned*>(data) = RETRO_LANGUAGE_ENGLISH;
            return true;
        }
        case RETRO_ENVIRONMENT_GET_USERNAME: {
            if (!data) return false;
            *static_cast<const char**>(data) = "STORM";
            return true;
        }
        case RETRO_ENVIRONMENT_GET_AUDIO_VIDEO_ENABLE: {
            if (!data) return false;
            *static_cast<int*>(data) = 3;
            return true;
        }
        case RETRO_ENVIRONMENT_GET_FASTFORWARDING: {
            if (!data) return false;
            *static_cast<bool*>(data) = false;
            return true;
        }
        case RETRO_ENVIRONMENT_GET_TARGET_REFRESH_RATE: {
            if (!data) return false;
            *static_cast<float*>(data) = 60.0f;
            return true;
        }
        case RETRO_ENVIRONMENT_SET_MEMORY_MAPS: {
            if (!data) return false;
            const auto* mmap = static_cast<const struct retro_memory_map*>(data);
            g_memoryDescriptors.clear();
            if (mmap->descriptors && mmap->num_descriptors > 0) {
                for (unsigned i = 0; i < mmap->num_descriptors; ++i) {
                    const auto& d = mmap->descriptors[i];
                    if (d.ptr && d.len > 0) {
                        g_memoryDescriptors.push_back(CapturedMemRange{
                            static_cast<uint8_t*>(d.ptr), d.len, d.flags
                        });
                    }
                }
            }
            return true;
        }
        case RETRO_ENVIRONMENT_GET_PREFERRED_HW_RENDER: {
            if (!data) return false;
            *static_cast<unsigned*>(data) = RETRO_HW_CONTEXT_OPENGLES3;
            return true;
        }
        case RETRO_ENVIRONMENT_SET_HW_RENDER: {
            if (!data) return false;
            auto* hw = static_cast<struct retro_hw_render_callback*>(data);
            if (hw->context_type == RETRO_HW_CONTEXT_OPENGLES2 ||
                hw->context_type == RETRO_HW_CONTEXT_OPENGLES3 ||
                hw->context_type == RETRO_HW_CONTEXT_OPENGLES_VERSION) {
                hw->get_current_framebuffer = hwGetCurrentFramebuffer;
                hw->get_proc_address = hwGetProcAddress;
                g_hwCallback = *hw;
                g_hwRenderRequested = true;
                g_hwContextInitialized = false;
                return true;
            }
            return false;
        }
        case RETRO_ENVIRONMENT_SET_FRAME_TIME_CALLBACK: {
            if (!data) return false;
            g_frameTimeCb = *static_cast<const struct retro_frame_time_callback*>(data);
            return true;
        }
        case RETRO_ENVIRONMENT_GET_MESSAGE_INTERFACE_VERSION: {
            if (!data) return false;
            *static_cast<unsigned*>(data) = 0;
            return true;
        }
        case RETRO_ENVIRONMENT_SET_MESSAGE:
        case RETRO_ENVIRONMENT_SET_MESSAGE_EXT:
        case RETRO_ENVIRONMENT_SET_CONTROLLER_INFO:
        case RETRO_ENVIRONMENT_SET_SUBSYSTEM_INFO:
        case RETRO_ENVIRONMENT_SET_SERIALIZATION_QUIRKS:
        case RETRO_ENVIRONMENT_SET_SUPPORT_ACHIEVEMENTS:
        case RETRO_ENVIRONMENT_SET_MINIMUM_AUDIO_LATENCY:
        case RETRO_ENVIRONMENT_SET_KEYBOARD_CALLBACK: {
            return true;
        }
        default:
            return false;
    }
}

void RETRO_CALLCONV videoRefreshCallback(const void* data, unsigned width, unsigned height, size_t pitch) {
    if (width == 0 || height == 0 || width > 4096 || height > 4096) return;

    std::lock_guard<std::mutex> surfLock(g_surfaceMutex);

    if (data == nullptr) {
        // Duped frame: reuse previous g_frameRgba
        presentFrameLocked();
        return;
    }

    const size_t totalPixels = static_cast<size_t>(width) * static_cast<size_t>(height);
    if (g_frameRgba.size() != totalPixels) {
        g_frameRgba.resize(totalPixels, 0xFF000000u);
    }
    g_frameWidth.store(static_cast<int>(width), std::memory_order_relaxed);
    g_frameHeight.store(static_cast<int>(height), std::memory_order_relaxed);

    if (data == RETRO_HW_FRAME_BUFFER_VALID && g_hwRenderRequested && g_hwFbo != 0) {
        const size_t byteCount = totalPixels * 4u;
        if (g_hwReadbackBuf.size() < byteCount) {
            g_hwReadbackBuf.resize(byteCount);
        }
        glBindFramebuffer(GL_FRAMEBUFFER, g_hwFbo);
        glPixelStorei(GL_PACK_ALIGNMENT, 4);
        glReadPixels(0, 0, static_cast<GLsizei>(width), static_cast<GLsizei>(height),
                     GL_RGBA, GL_UNSIGNED_BYTE, g_hwReadbackBuf.data());
        glBindFramebuffer(GL_FRAMEBUFFER, 0);

        const auto* srcRows = reinterpret_cast<const uint32_t*>(g_hwReadbackBuf.data());
        if (g_hwCallback.bottom_left_origin) {
            for (unsigned y = 0; y < height; ++y) {
                const uint32_t* sRow = srcRows + static_cast<size_t>(height - 1u - y) * width;
                uint32_t* dRow = g_frameRgba.data() + static_cast<size_t>(y) * width;
                for (unsigned x = 0; x < width; ++x) {
                    dRow[x] = sRow[x] | 0xFF000000u;
                }
            }
        } else {
            for (size_t i = 0; i < totalPixels; ++i) {
                g_frameRgba[i] = srcRows[i] | 0xFF000000u;
            }
        }
    } else if (g_pixelFormat == RETRO_PIXEL_FORMAT_XRGB8888) {
        const auto* srcBytes = static_cast<const uint8_t*>(data);
        for (unsigned y = 0; y < height; ++y) {
            const auto* srcRow = reinterpret_cast<const uint32_t*>(srcBytes + static_cast<size_t>(y) * pitch);
            uint32_t* dstRow = g_frameRgba.data() + static_cast<size_t>(y) * width;
            for (unsigned x = 0; x < width; ++x) {
                uint32_t xrgb = srcRow[x];
                uint32_t r = (xrgb >> 16) & 0xFFu;
                uint32_t g = (xrgb >> 8) & 0xFFu;
                uint32_t b = xrgb & 0xFFu;
                dstRow[x] = 0xFF000000u | (b << 16) | (g << 8) | r;
            }
        }
    } else if (g_pixelFormat == RETRO_PIXEL_FORMAT_RGB565) {
        const auto* srcBytes = static_cast<const uint8_t*>(data);
        for (unsigned y = 0; y < height; ++y) {
            const auto* srcRow = reinterpret_cast<const uint16_t*>(srcBytes + static_cast<size_t>(y) * pitch);
            uint32_t* dstRow = g_frameRgba.data() + static_cast<size_t>(y) * width;
            for (unsigned x = 0; x < width; ++x) {
                dstRow[x] = g_rgb565ToRgba[srcRow[x]];
            }
        }
    } else {
        // Default RETRO_PIXEL_FORMAT_0RGB1555
        const auto* srcBytes = static_cast<const uint8_t*>(data);
        for (unsigned y = 0; y < height; ++y) {
            const auto* srcRow = reinterpret_cast<const uint16_t*>(srcBytes + static_cast<size_t>(y) * pitch);
            uint32_t* dstRow = g_frameRgba.data() + static_cast<size_t>(y) * width;
            for (unsigned x = 0; x < width; ++x) {
                dstRow[x] = g_rgb1555ToRgba[srcRow[x]];
            }
        }
    }

    presentFrameLocked();
}

void RETRO_CALLCONV audioSampleCallback(int16_t left, int16_t right) {
    int16_t pair[2] = { left, right };
    pushAudioStereoSamples(pair, 1);
}

size_t RETRO_CALLCONV audioSampleBatchCallback(const int16_t* data, size_t frames) {
    pushAudioStereoSamples(data, frames);
    return frames;
}

void RETRO_CALLCONV inputPollCallback() {}

int16_t RETRO_CALLCONV inputStateCallback(unsigned port, unsigned device, unsigned index, unsigned id) {
    if (port >= MAX_PORTS) return 0;
    const unsigned baseDevice = device & RETRO_DEVICE_MASK;

    if (baseDevice == RETRO_DEVICE_JOYPAD) {
        uint32_t mask = g_joypadButtons[port].load(std::memory_order_relaxed);
        if (id == RETRO_DEVICE_ID_JOYPAD_MASK) {
            return static_cast<int16_t>(mask & 0xFFFFu);
        }
        if (id < 32u) {
            return ((mask >> id) & 1u) ? 1 : 0;
        }
        return 0;
    }

    if (baseDevice == RETRO_DEVICE_ANALOG) {
        if (index < 4u && id < 2u) {
            return g_analogAxes[port][index][id].load(std::memory_order_relaxed);
        }
        return 0;
    }

    if (baseDevice == RETRO_DEVICE_POINTER) {
        if (index > 0) return 0;
        switch (id) {
            case RETRO_DEVICE_ID_POINTER_X:
                return g_pointerX.load(std::memory_order_relaxed);
            case RETRO_DEVICE_ID_POINTER_Y:
                return g_pointerY.load(std::memory_order_relaxed);
            case RETRO_DEVICE_ID_POINTER_PRESSED:
                return g_pointerPressed.load(std::memory_order_relaxed) ? 1 : 0;
            case RETRO_DEVICE_ID_POINTER_COUNT:
                return g_pointerPressed.load(std::memory_order_relaxed) ? 1 : 0;
            default:
                return 0;
        }
    }

    if (baseDevice == RETRO_DEVICE_LIGHTGUN) {
        bool pressed = g_pointerPressed.load(std::memory_order_relaxed);
        switch (id) {
            case RETRO_DEVICE_ID_LIGHTGUN_SCREEN_X:
                return g_pointerX.load(std::memory_order_relaxed);
            case RETRO_DEVICE_ID_LIGHTGUN_SCREEN_Y:
                return g_pointerY.load(std::memory_order_relaxed);
            case RETRO_DEVICE_ID_LIGHTGUN_TRIGGER:
                return pressed ? 1 : 0;
            case RETRO_DEVICE_ID_LIGHTGUN_IS_OFFSCREEN:
                return 0;
            default:
                return 0;
        }
    }

    return 0;
}

// ==================== INTERNAL UNLOAD ====================

void unloadCoreInternalLocked() {
    if (g_gameLoaded && g_retroUnloadGame) {
        g_retroUnloadGame();
    }
    g_gameLoaded = false;

    if (g_hwRenderRequested) {
        destroyHwContext();
    }

    if (g_retroDeinit && g_coreHandle) {
        g_retroDeinit();
    }

    if (g_coreHandle) {
        dlclose(g_coreHandle);
        g_coreHandle = nullptr;
    }

    g_retroInit = nullptr;
    g_retroDeinit = nullptr;
    g_retroApiVersion = nullptr;
    g_retroGetSystemInfo = nullptr;
    g_retroGetSystemAvInfo = nullptr;
    g_retroSetEnvironment = nullptr;
    g_retroSetVideoRefresh = nullptr;
    g_retroSetAudioSample = nullptr;
    g_retroSetAudioSampleBatch = nullptr;
    g_retroSetInputPoll = nullptr;
    g_retroSetInputState = nullptr;
    g_retroSetControllerPortDevice = nullptr;
    g_retroReset = nullptr;
    g_retroRun = nullptr;
    g_retroSerializeSize = nullptr;
    g_retroSerialize = nullptr;
    g_retroUnserialize = nullptr;
    g_retroCheatReset = nullptr;
    g_retroCheatSet = nullptr;
    g_retroLoadGame = nullptr;
    g_retroUnloadGame = nullptr;
    g_retroGetMemoryData = nullptr;
    g_retroGetMemorySize = nullptr;

    g_romBuffer.clear();
    g_romBuffer.shrink_to_fit();
    g_rewindRing.clear();
    g_rewindTotalBytes = 0;
    g_ramCandidates.clear();
    g_ramCandidates.shrink_to_fit();
    {
        std::lock_guard<std::mutex> freezeLock(g_ramFreezeMutex);
        g_frozenRamEntries.clear();
    }
    g_memoryDescriptors.clear();
    g_variables.clear();
    g_variablesDirty.store(false, std::memory_order_relaxed);
    g_frameTimeCb = { nullptr, 0 };
    g_coreLoadedInfo.clear();
    g_hasLastFrameTimestamp = false;

    {
        std::lock_guard<std::mutex> audioLock(g_audioMutex);
        g_audioReadIdx = 0;
        g_audioWriteIdx = 0;
        g_audioCount = 0;
    }
    for (int i = 0; i < MAX_PORTS; ++i) {
        g_rumbleStrong[i].store(0, std::memory_order_relaxed);
        g_rumbleWeak[i].store(0, std::memory_order_relaxed);
    }
}

template <typename Fn>
bool resolveSym(void* handle, const char* name, Fn& outFn, bool required = true) {
    void* sym = dlsym(handle, name);
    if (!sym) {
        if (required) {
            LOGE("Missing required Libretro symbol: %s", name);
        }
        outFn = nullptr;
        return !required;
    }
    outFn = reinterpret_cast<Fn>(sym);
    return true;
}

bool writeBinaryFileAtomic(const std::string& path, const void* data, size_t size) {
    if (path.empty() || !data || size == 0) return false;
    ensureParentDir(path);
    std::string tmpPath = path + ".tmp";
    {
        std::ofstream ofs(tmpPath, std::ios::binary | std::ios::trunc);
        if (!ofs.is_open()) {
            std::ofstream directOfs(path, std::ios::binary | std::ios::trunc);
            if (!directOfs.is_open()) return false;
            directOfs.write( static_cast<const char*>(data), static_cast<std::streamsize>(size));
            return directOfs.good();
        }
        ofs.write(static_cast<const char*>(data), static_cast<std::streamsize>(size));
        if (!ofs.good()) {
            ofs.close();
            std::remove(tmpPath.c_str());
            return false;
        }
    }
    if (std::rename(tmpPath.c_str(), path.c_str()) != 0) {
        std::remove(path.c_str());
        if (std::rename(tmpPath.c_str(), path.c_str()) != 0) {
            std::remove(tmpPath.c_str());
            return false;
        }
    }
    return true;
}

} // namespace

// ==================== JNI EXPORTS ====================

extern "C" {

JNIEXPORT void JNICALL
Java_com_storm_retroarch_NativeLibretroBridge_nativeSetSurface(JNIEnv* env, jclass /*clazz*/, jobject surface) {
    initPixelLutsOnce();
    std::lock_guard<std::mutex> surfLock(g_surfaceMutex);
    if (g_nativeWindow) {
        ANativeWindow_release(g_nativeWindow);
        g_nativeWindow = nullptr;
    }
    g_configuredBufW = 0;
    g_configuredBufH = 0;
    g_surfacePhysW = 0;
    g_surfacePhysH = 0;

    if (surface != nullptr) {
        g_nativeWindow = ANativeWindow_fromSurface(env, surface);
        if (g_nativeWindow) {
            g_surfacePhysW = ANativeWindow_getWidth(g_nativeWindow);
            g_surfacePhysH = ANativeWindow_getHeight(g_nativeWindow);
            if (!g_frameRgba.empty()) {
                presentFrameLocked();
            }
        }
    }
}

JNIEXPORT void JNICALL
Java_com_storm_retroarch_NativeLibretroBridge_nativeOnSurfaceChanged(JNIEnv* /*env*/, jclass /*clazz*/, jint width, jint height) {
    std::lock_guard<std::mutex> surfLock(g_surfaceMutex);
    if (width > 0 && height > 0) {
        g_surfacePhysW = width;
        g_surfacePhysH = height;
        g_configuredBufW = 0;
        g_configuredBufH = 0;
    }
}

JNIEXPORT void JNICALL
Java_com_storm_retroarch_NativeLibretroBridge_nativeSetShaderMode(JNIEnv* /*env*/, jclass /*clazz*/, jint mode) {
    g_shaderMode.store(std::clamp(static_cast<int>(mode), 0, 3), std::memory_order_relaxed);
}

JNIEXPORT jint JNICALL
Java_com_storm_retroarch_NativeLibretroBridge_nativeGetShaderMode(JNIEnv* /*env*/, jclass /*clazz*/) {
    return static_cast<jint>(g_shaderMode.load(std::memory_order_relaxed));
}

JNIEXPORT void JNICALL
Java_com_storm_retroarch_NativeLibretroBridge_nativeSetAspectMode(JNIEnv* /*env*/, jclass /*clazz*/, jint mode) {
    g_aspectMode.store(std::clamp(static_cast<int>(mode), 0, 4), std::memory_order_relaxed);
}

JNIEXPORT jint JNICALL
Java_com_storm_retroarch_NativeLibretroBridge_nativeGetAspectMode(JNIEnv* /*env*/, jclass /*clazz*/) {
    return static_cast<jint>(g_aspectMode.load(std::memory_order_relaxed));
}

JNIEXPORT jbyteArray JNICALL
Java_com_storm_retroarch_NativeLibretroBridge_nativeCaptureFrameRgba(JNIEnv* env, jclass /*clazz*/) {
    std::lock_guard<std::mutex> surfLock(g_surfaceMutex);
    const int w = g_frameWidth.load(std::memory_order_relaxed);
    const int h = g_frameHeight.load(std::memory_order_relaxed);
    const size_t expectedPixels = static_cast<size_t>(std::max(0, w)) * static_cast<size_t>(std::max(0, h));
    if (w <= 0 || h <= 0 || g_frameRgba.size() < expectedPixels) {
        return nullptr;
    }
    const jsize byteLen = static_cast<jsize>(expectedPixels * 4u);
    jbyteArray arr = env->NewByteArray(byteLen);
    if (!arr) return nullptr;
    env->SetByteArrayRegion(arr, 0, byteLen, reinterpret_cast<const jbyte*>(g_frameRgba.data()));
    return arr;
}

JNIEXPORT jint JNICALL
Java_com_storm_retroarch_NativeLibretroBridge_nativeGetFrameWidth(JNIEnv* /*env*/, jclass /*clazz*/) {
    return static_cast<jint>(g_frameWidth.load(std::memory_order_relaxed));
}

JNIEXPORT jint JNICALL
Java_com_storm_retroarch_NativeLibretroBridge_nativeGetFrameHeight(JNIEnv* /*env*/, jclass /*clazz*/) {
    return static_cast<jint>(g_frameHeight.load(std::memory_order_relaxed));
}

JNIEXPORT jfloat JNICALL
Java_com_storm_retroarch_NativeLibretroBridge_nativeGetAspectRatio(JNIEnv* /*env*/, jclass /*clazz*/) {
    return static_cast<jfloat>(g_aspectRatio);
}

JNIEXPORT jint JNICALL
Java_com_storm_retroarch_NativeLibretroBridge_nativePullAudioSamples(
    JNIEnv* env, jclass /*clazz*/, jshortArray outShortArray, jint maxSamples) {
    if (!outShortArray || maxSamples <= 0) return 0;
    const jsize arrayLen = env->GetArrayLength(outShortArray);
    size_t req = static_cast<size_t>(std::min<jint>(maxSamples, arrayLen)) & ~size_t(1u);
    if (req == 0) return 0;

    std::lock_guard<std::mutex> lock(g_audioMutex);
    size_t toRead = std::min(req, g_audioCount) & ~size_t(1u);
    if (toRead == 0) return 0;

    size_t firstChunk = std::min(toRead, AUDIO_RING_CAPACITY - g_audioReadIdx);
    env->SetShortArrayRegion(outShortArray, 0, static_cast<jsize>(firstChunk),
                             reinterpret_cast<const jshort*>(g_audioRing + g_audioReadIdx));
    size_t secondChunk = toRead - firstChunk;
    if (secondChunk > 0) {
        env->SetShortArrayRegion(outShortArray, static_cast<jsize>(firstChunk),
                                 static_cast<jsize>(secondChunk),
                                 reinterpret_cast<const jshort*>(g_audioRing));
    }
    g_audioReadIdx = (g_audioReadIdx + toRead) % AUDIO_RING_CAPACITY;
    g_audioCount -= toRead;
    return static_cast<jint>(toRead);
}

JNIEXPORT jint JNICALL
Java_com_storm_retroarch_NativeLibretroBridge_nativeGetSampleRate(JNIEnv* /*env*/, jclass /*clazz*/) {
    double sr = g_sampleRate.load(std::memory_order_relaxed);
    if (sr < 8000.0 || sr > 192000.0) sr = 44100.0;
    return static_cast<jint>(std::lround(sr));
}

JNIEXPORT void JNICALL
Java_com_storm_retroarch_NativeLibretroBridge_nativeSetButton(
    JNIEnv* /*env*/, jclass /*clazz*/, jint port, jint id, jboolean pressed) {
    if (port < 0 || port >= MAX_PORTS || id < 0 || id >= 32) return;
    uint32_t bit = (1u << static_cast<uint32_t>(id));
    if (pressed) {
        g_joypadButtons[port].fetch_or(bit, std::memory_order_relaxed);
    } else {
        g_joypadButtons[port].fetch_and(~bit, std::memory_order_relaxed);
    }
}

JNIEXPORT void JNICALL
Java_com_storm_retroarch_NativeLibretroBridge_nativeSetAnalog(
    JNIEnv* /*env*/, jclass /*clazz*/, jint port, jint index, jint id, jint value) {
    if (port < 0 || port >= MAX_PORTS || index < 0 || index >= 4 || id < 0 || id >= 2) return;
    int clamped = std::clamp(static_cast<int>(value), -32767, 32767);
    g_analogAxes[port][index][id].store(static_cast<int16_t>(clamped), std::memory_order_relaxed);
}

JNIEXPORT void JNICALL
Java_com_storm_retroarch_NativeLibretroBridge_nativeSetPointer(
    JNIEnv* /*env*/, jclass /*clazz*/, jint x, jint y, jboolean pressed) {
    int cx = std::clamp(static_cast<int>(x), -32767, 32767);
    int cy = std::clamp(static_cast<int>(y), -32767, 32767);
    g_pointerX.store(static_cast<int16_t>(cx), std::memory_order_relaxed);
    g_pointerY.store(static_cast<int16_t>(cy), std::memory_order_relaxed);
    g_pointerPressed.store(pressed != JNI_FALSE, std::memory_order_relaxed);
}

JNIEXPORT jboolean JNICALL
Java_com_storm_retroarch_NativeLibretroBridge_nativeLoadCoreAndGame(
    JNIEnv* env, jclass /*clazz*/, jstring coreSoPath, jstring romPath, jstring systemDir, jstring saveDir) {
    initPixelLutsOnce();
    const std::string corePath = jstringToStd(env, coreSoPath);
    const std::string gamePath = jstringToStd(env, romPath);
    const std::string sysPath = jstringToStd(env, systemDir);
    const std::string savPath = jstringToStd(env, saveDir);

    if (corePath.empty()) {
        LOGE("nativeLoadCoreAndGame: coreSoPath is empty");
        return JNI_FALSE;
    }

    std::lock_guard<std::mutex> lock(g_coreMutex);
    unloadCoreInternalLocked();

    ensureDirRecursive(sysPath);
    ensureDirRecursive(savPath);
    g_systemDir = sysPath;
    g_saveDir = savPath;
    g_coreAssetsDir = sysPath;
    g_libretroPath = corePath;
    g_pixelFormat = RETRO_PIXEL_FORMAT_0RGB1555;
    g_supportNoGame = false;
    g_rotation = 0;

    dlerror();
    g_coreHandle = dlopen(corePath.c_str(), RTLD_NOW | RTLD_LOCAL);
    if (!g_coreHandle) {
        g_coreHandle = dlopen(corePath.c_str(), RTLD_LAZY | RTLD_LOCAL);
    }
    if (!g_coreHandle) {
        const char* err = dlerror();
        LOGE("dlopen failed for %s: %s", corePath.c_str(), err ? err : "unknown error");
        return JNI_FALSE;
    }

    bool ok = true;
    ok &= resolveSym(g_coreHandle, "retro_init", g_retroInit);
    ok &= resolveSym(g_coreHandle, "retro_deinit", g_retroDeinit);
    ok &= resolveSym(g_coreHandle, "retro_api_version", g_retroApiVersion);
    ok &= resolveSym(g_coreHandle, "retro_get_system_info", g_retroGetSystemInfo);
    ok &= resolveSym(g_coreHandle, "retro_get_system_av_info", g_retroGetSystemAvInfo);
    ok &= resolveSym(g_coreHandle, "retro_set_environment", g_retroSetEnvironment);
    ok &= resolveSym(g_coreHandle, "retro_set_video_refresh", g_retroSetVideoRefresh);
    ok &= resolveSym(g_coreHandle, "retro_set_audio_sample", g_retroSetAudioSample);
    ok &= resolveSym(g_coreHandle, "retro_set_audio_sample_batch", g_retroSetAudioSampleBatch);
    ok &= resolveSym(g_coreHandle, "retro_set_input_poll", g_retroSetInputPoll);
    ok &= resolveSym(g_coreHandle, "retro_set_input_state", g_retroSetInputState);
    resolveSym(g_coreHandle, "retro_set_controller_port_device", g_retroSetControllerPortDevice, false);
    ok &= resolveSym(g_coreHandle, "retro_reset", g_retroReset);
    ok &= resolveSym(g_coreHandle, "retro_run", g_retroRun);
    resolveSym(g_coreHandle, "retro_serialize_size", g_retroSerializeSize, false);
    resolveSym(g_coreHandle, "retro_serialize", g_retroSerialize, false);
    resolveSym(g_coreHandle, "retro_unserialize", g_retroUnserialize, false);
    resolveSym(g_coreHandle, "retro_cheat_reset", g_retroCheatReset, false);
    resolveSym(g_coreHandle, "retro_cheat_set", g_retroCheatSet, false);
    ok &= resolveSym(g_coreHandle, "retro_load_game", g_retroLoadGame);
    ok &= resolveSym(g_coreHandle, "retro_unload_game", g_retroUnloadGame);
    resolveSym(g_coreHandle, "retro_get_memory_data", g_retroGetMemoryData, false);
    resolveSym(g_coreHandle, "retro_get_memory_size", g_retroGetMemorySize, false);

    if (!ok) {
        unloadCoreInternalLocked();
        return JNI_FALSE;
    }

    // 1. Set environment BEFORE retro_init per Libretro spec
    g_retroSetEnvironment(environmentCallback);

    struct retro_system_info sysInfo{};
    g_retroGetSystemInfo(&sysInfo);

    // 2. Initialize core & attach A/V/Input callbacks
    g_retroInit();
    g_retroSetVideoRefresh(videoRefreshCallback);
    g_retroSetAudioSample(audioSampleCallback);
    g_retroSetAudioSampleBatch(audioSampleBatchCallback);
    g_retroSetInputPoll(inputPollCallback);
    g_retroSetInputState(inputStateCallback);

    // 3. Prepare game payload
    struct retro_game_info gameInfo{};
    bool passGameInfo = true;
    if (gamePath.empty()) {
        if (!g_supportNoGame) {
            LOGE("ROM path empty and core does not support contentless boot");
            unloadCoreInternalLocked();
            return JNI_FALSE;
        }
        passGameInfo = false;
    } else {
        gameInfo.path = gamePath.c_str();
        gameInfo.meta = nullptr;
        gameInfo.data = nullptr;
        gameInfo.size = 0;

        if (!sysInfo.need_fullpath) {
            std::ifstream romFile(gamePath, std::ios::binary | std::ios::ate);
            if (!romFile.is_open()) {
                LOGE("Failed to open ROM file: %s", gamePath.c_str());
                unloadCoreInternalLocked();
                return JNI_FALSE;
            }
            const auto fileLen = romFile.tellg();
            if (fileLen > 0 && static_cast<uint64_t>(fileLen) <= MAX_ROM_IN_MEMORY_BYTES) {
                g_romBuffer.resize(static_cast<size_t>(fileLen));
                romFile.seekg(0, std::ios::beg);
                romFile.read(reinterpret_cast<char*>(g_romBuffer.data()), fileLen);
                gameInfo.data = g_romBuffer.data();
                gameInfo.size = g_romBuffer.size();
            }
        }
    }

    if (!g_retroLoadGame(passGameInfo ? &gameInfo : nullptr)) {
        LOGE("retro_load_game returned false for %s", gamePath.c_str());
        unloadCoreInternalLocked();
        return JNI_FALSE;
    }

    struct retro_system_av_info avInfo{};
    g_retroGetSystemAvInfo(&avInfo);
    g_baseWidth = avInfo.geometry.base_width > 0 ? avInfo.geometry.base_width : 256u;
    g_baseHeight = avInfo.geometry.base_height > 0 ? avInfo.geometry.base_height : 224u;
    g_maxWidth = avInfo.geometry.max_width > 0 ? avInfo.geometry.max_width : g_baseWidth;
    g_maxHeight = avInfo.geometry.max_height > 0 ? avInfo.geometry.max_height : g_baseHeight;
    g_aspectRatio = (avInfo.geometry.aspect_ratio > 0.05f)
                        ? avInfo.geometry.aspect_ratio
                        : (static_cast<float>(g_baseWidth) / static_cast<float>(g_baseHeight));

    double targetFps = (avInfo.timing.fps > 1.0 && avInfo.timing.fps < 240.0) ? avInfo.timing.fps : 60.0;
    double targetSr = (avInfo.timing.sample_rate >= 8000.0 && avInfo.timing.sample_rate <= 192000.0)
                          ? avInfo.timing.sample_rate
                          : 44100.0;
    g_coreTargetFps.store(targetFps, std::memory_order_relaxed);
    g_measuredFps.store(targetFps, std::memory_order_relaxed);
    g_sampleRate.store(targetSr, std::memory_order_relaxed);

    if (g_retroSetControllerPortDevice) {
        g_retroSetControllerPortDevice(0, RETRO_DEVICE_JOYPAD);
        g_retroSetControllerPortDevice(1, RETRO_DEVICE_JOYPAD);
    }

    // Release any EGL context from the loader thread so the emulation thread can claim it cleanly
    if (g_eglDisplay != EGL_NO_DISPLAY && eglGetCurrentContext() != EGL_NO_CONTEXT) {
        eglMakeCurrent(g_eglDisplay, EGL_NO_SURFACE, EGL_NO_SURFACE, EGL_NO_CONTEXT);
    }

    const char* libName = sysInfo.library_name ? sysInfo.library_name : "Libretro Core";
    const char* libVer = sysInfo.library_version ? sysInfo.library_version : "";
    char infoBuf[256];
    std::snprintf(infoBuf, sizeof(infoBuf), "%s %s | %ux%u @ %.2f FPS | %d Hz",
                  libName, libVer, g_baseWidth, g_baseHeight, targetFps, static_cast<int>(std::lround(targetSr)));
    g_coreLoadedInfo = infoBuf;
    g_gameLoaded = true;

    LOGI("Loaded core: %s", g_coreLoadedInfo.c_str());
    return JNI_TRUE;
}

JNIEXPORT void JNICALL
Java_com_storm_retroarch_NativeLibretroBridge_nativeRunFrame(JNIEnv* /*env*/, jclass /*clazz*/) {
    std::lock_guard<std::mutex> lock(g_coreMutex);
    if (!g_gameLoaded || !g_retroRun) return;

    if (g_hwRenderRequested) {
        ensureHwContextOnCurrentThread();
    }

    if (g_frameTimeCb.callback) {
        double fps = g_coreTargetFps.load(std::memory_order_relaxed);
        retro_usec_t dt = (fps > 1.0) ? static_cast<retro_usec_t>(1000000.0 / fps) : 16667;
        g_frameTimeCb.callback(dt);
    }

    applyFrozenRamLocked();
    g_retroRun();
    applyFrozenRamLocked();

    auto now = std::chrono::steady_clock::now();
    if (g_hasLastFrameTimestamp) {
        double dtSec = std::chrono::duration<double>(now - g_lastFrameTimestamp).count();
        if (dtSec > 0.0005 && dtSec < 1.0) {
            double instFps = 1.0 / dtSec;
            double prev = g_measuredFps.load(std::memory_order_relaxed);
            g_measuredFps.store(prev * 0.90 + instFps * 0.10, std::memory_order_relaxed);
        }
    }
    g_lastFrameTimestamp = now;
    g_hasLastFrameTimestamp = true;
}

JNIEXPORT void JNICALL
Java_com_storm_retroarch_NativeLibretroBridge_nativeReset(JNIEnv* /*env*/, jclass /*clazz*/) {
    std::lock_guard<std::mutex> lock(g_coreMutex);
    if (g_gameLoaded && g_retroReset) {
        g_retroReset();
    }
}

JNIEXPORT void JNICALL
Java_com_storm_retroarch_NativeLibretroBridge_nativeUnload(JNIEnv* /*env*/, jclass /*clazz*/) {
    std::lock_guard<std::mutex> lock(g_coreMutex);
    unloadCoreInternalLocked();
}

JNIEXPORT jdouble JNICALL
Java_com_storm_retroarch_NativeLibretroBridge_nativeGetFps(JNIEnv* /*env*/, jclass /*clazz*/) {
    return static_cast<jdouble>(g_coreTargetFps.load(std::memory_order_relaxed));
}

JNIEXPORT jdouble JNICALL
Java_com_storm_retroarch_NativeLibretroBridge_nativeGetMeasuredFps(JNIEnv* /*env*/, jclass /*clazz*/) {
    return static_cast<jdouble>(g_measuredFps.load(std::memory_order_relaxed));
}

JNIEXPORT jstring JNICALL
Java_com_storm_retroarch_NativeLibretroBridge_nativeGetCoreLoadedInfo(JNIEnv* env, jclass /*clazz*/) {
    std::lock_guard<std::mutex> lock(g_coreMutex);
    return env->NewStringUTF(g_coreLoadedInfo.c_str());
}

JNIEXPORT void JNICALL
Java_com_storm_retroarch_NativeLibretroBridge_nativeSetCoreVariable(
    JNIEnv* env, jclass /*clazz*/, jstring key, jstring value) {
    std::string k = jstringToStd(env, key);
    std::string v = jstringToStd(env, value);
    if (k.empty()) return;
    std::lock_guard<std::mutex> lock(g_coreMutex);
    g_variables[k] = v;
    g_variablesDirty.store(true, std::memory_order_relaxed);
}

JNIEXPORT jint JNICALL
Java_com_storm_retroarch_NativeLibretroBridge_nativeGetRumbleStrength(
    JNIEnv* /*env*/, jclass /*clazz*/, jint port) {
    if (port < 0 || port >= MAX_PORTS) return 0;
    uint32_t s = g_rumbleStrong[port].load(std::memory_order_relaxed);
    uint32_t w = g_rumbleWeak[port].load(std::memory_order_relaxed);
    return static_cast<jint>(std::max(s, w));
}

// ==================== SRAM & SAVE STATES ====================

JNIEXPORT jboolean JNICALL
Java_com_storm_retroarch_NativeLibretroBridge_nativeLoadSram(JNIEnv* env, jclass /*clazz*/, jstring srmPath) {
    std::string path = jstringToStd(env, srmPath);
    if (path.empty()) return JNI_FALSE;

    std::lock_guard<std::mutex> lock(g_coreMutex);
    if (!g_gameLoaded || !g_retroGetMemoryData || !g_retroGetMemorySize) return JNI_FALSE;
    void* ptr = g_retroGetMemoryData(RETRO_MEMORY_SAVE_RAM);
    size_t sz = g_retroGetMemorySize(RETRO_MEMORY_SAVE_RAM);
    if (!ptr || sz == 0) return JNI_FALSE;

    std::ifstream ifs(path, std::ios::binary | std::ios::ate);
    if (!ifs.is_open()) return JNI_FALSE;
    const auto fileSize = ifs.tellg();
    if (fileSize <= 0) return JNI_FALSE;
    size_t toRead = std::min(sz, static_cast<size_t>(fileSize));
    ifs.seekg(0, std::ios::beg);
    ifs.read(static_cast<char*>(ptr), static_cast<std::streamsize>(toRead));
    return ifs.good() || ifs.eof() ? JNI_TRUE : JNI_FALSE;
}

JNIEXPORT jboolean JNICALL
Java_com_storm_retroarch_NativeLibretroBridge_nativeSaveSram(JNIEnv* env, jclass /*clazz*/, jstring srmPath) {
    std::string path = jstringToStd(env, srmPath);
    if (path.empty()) return JNI_FALSE;

    std::lock_guard<std::mutex> lock(g_coreMutex);
    if (!g_gameLoaded || !g_retroGetMemoryData || !g_retroGetMemorySize) return JNI_FALSE;
    const void* ptr = g_retroGetMemoryData(RETRO_MEMORY_SAVE_RAM);
    size_t sz = g_retroGetMemorySize(RETRO_MEMORY_SAVE_RAM);
    if (!ptr || sz == 0) return JNI_FALSE;

    return writeBinaryFileAtomic(path, ptr, sz) ? JNI_TRUE : JNI_FALSE;
}

JNIEXPORT jboolean JNICALL
Java_com_storm_retroarch_NativeLibretroBridge_nativeSaveState(JNIEnv* env, jclass /*clazz*/, jstring statePath) {
    std::string path = jstringToStd(env, statePath);
    if (path.empty()) return JNI_FALSE;

    std::lock_guard<std::mutex> lock(g_coreMutex);
    if (!g_gameLoaded || !g_retroSerializeSize || !g_retroSerialize) return JNI_FALSE;
    size_t sz = g_retroSerializeSize();
    if (sz == 0) return JNI_FALSE;

    std::vector<uint8_t> buf(sz);
    if (!g_retroSerialize(buf.data(), buf.size())) return JNI_FALSE;
    return writeBinaryFileAtomic(path, buf.data(), buf.size()) ? JNI_TRUE : JNI_FALSE;
}

JNIEXPORT jboolean JNICALL
Java_com_storm_retroarch_NativeLibretroBridge_nativeLoadState(JNIEnv* env, jclass /*clazz*/, jstring statePath) {
    std::string path = jstringToStd(env, statePath);
    if (path.empty()) return JNI_FALSE;

    std::lock_guard<std::mutex> lock(g_coreMutex);
    if (!g_gameLoaded || !g_retroUnserialize) return JNI_FALSE;

    std::ifstream ifs(path, std::ios::binary | std::ios::ate);
    if (!ifs.is_open()) return JNI_FALSE;
    const auto fileSize = ifs.tellg();
    if (fileSize <= 0) return JNI_FALSE;

    std::vector<uint8_t> buf(static_cast<size_t>(fileSize));
    ifs.seekg(0, std::ios::beg);
    ifs.read(reinterpret_cast<char*>(buf.data()), fileSize);
    if (!ifs && !ifs.eof()) return JNI_FALSE;

    return g_retroUnserialize(buf.data(), buf.size()) ? JNI_TRUE : JNI_FALSE;
}

// ==================== REWIND RING BUFFER ====================

JNIEXPORT void JNICALL
Java_com_storm_retroarch_NativeLibretroBridge_nativePushRewindSnapshot(JNIEnv* /*env*/, jclass /*clazz*/) {
    std::lock_guard<std::mutex> lock(g_coreMutex);
    if (!g_gameLoaded || !g_retroSerializeSize || !g_retroSerialize) return;
    size_t sz = g_retroSerializeSize();
    if (sz == 0 || sz > 32ULL * 1024ULL * 1024ULL) return;

    std::vector<uint8_t> snap;
    while (!g_rewindRing.empty() &&
           (g_rewindRing.size() >= MAX_REWIND_SNAPSHOTS || g_rewindTotalBytes + sz > MAX_REWIND_TOTAL_BYTES)) {
        g_rewindTotalBytes -= g_rewindRing.front().size();
        if (snap.empty() && g_rewindRing.front().capacity() >= sz) {
            snap = std::move(g_rewindRing.front());
        }
        g_rewindRing.pop_front();
    }

    snap.resize(sz);
    if (g_retroSerialize(snap.data(), snap.size())) {
        g_rewindTotalBytes += snap.size();
        g_rewindRing.push_back(std::move(snap));
    }
}

JNIEXPORT jboolean JNICALL
Java_com_storm_retroarch_NativeLibretroBridge_nativePopRewindSnapshot(JNIEnv* /*env*/, jclass /*clazz*/) {
    std::lock_guard<std::mutex> lock(g_coreMutex);
    if (!g_gameLoaded || !g_retroUnserialize || g_rewindRing.empty()) return JNI_FALSE;

    std::vector<uint8_t> snap = std::move(g_rewindRing.back());
    g_rewindRing.pop_back();
    g_rewindTotalBytes = (g_rewindTotalBytes >= snap.size()) ? (g_rewindTotalBytes - snap.size()) : 0;

    return g_retroUnserialize(snap.data(), snap.size()) ? JNI_TRUE : JNI_FALSE;
}

JNIEXPORT jint JNICALL
Java_com_storm_retroarch_NativeLibretroBridge_nativeGetRewindCount(JNIEnv* /*env*/, jclass /*clazz*/) {
    std::lock_guard<std::mutex> lock(g_coreMutex);
    return static_cast<jint>(g_rewindRing.size());
}

JNIEXPORT void JNICALL
Java_com_storm_retroarch_NativeLibretroBridge_nativeClearRewind(JNIEnv* /*env*/, jclass /*clazz*/) {
    std::lock_guard<std::mutex> lock(g_coreMutex);
    g_rewindRing.clear();
    g_rewindTotalBytes = 0;
}

// ==================== CHEATS & LIVE NATIVE RAM SCANNER ====================

JNIEXPORT void JNICALL
Java_com_storm_retroarch_NativeLibretroBridge_nativeResetCheats(JNIEnv* /*env*/, jclass /*clazz*/) {
    std::lock_guard<std::mutex> lock(g_coreMutex);
    if (g_gameLoaded && g_retroCheatReset) {
        g_retroCheatReset();
    }
}

JNIEXPORT void JNICALL
Java_com_storm_retroarch_NativeLibretroBridge_nativeApplyCheat(
    JNIEnv* env, jclass /*clazz*/, jint index, jboolean enabled, jstring code) {
    if (index < 0 || !code) return;
    std::string cheatCode = jstringToStd(env, code);
    std::lock_guard<std::mutex> lock(g_coreMutex);
    if (g_gameLoaded && g_retroCheatSet) {
        g_retroCheatSet(static_cast<unsigned>(index), enabled != JNI_FALSE, cheatCode.c_str());
    }
}

JNIEXPORT jint JNICALL
Java_com_storm_retroarch_NativeLibretroBridge_nativeGetSystemRamSize(JNIEnv* /*env*/, jclass /*clazz*/) {
    std::lock_guard<std::mutex> lock(g_coreMutex);
    uint8_t* ram = nullptr;
    size_t sz = 0;
    if (!getSystemRamRegionLocked(&ram, &sz)) return 0;
    return static_cast<jint>(std::min<size_t>(sz, static_cast<size_t>(INT32_MAX)));
}

JNIEXPORT jint JNICALL
Java_com_storm_retroarch_NativeLibretroBridge_nativeRamScanFirst(
    JNIEnv* /*env*/, jclass /*clazz*/, jint widthBytes, jlong compareValue) {
    if (widthBytes != 1 && widthBytes != 2 && widthBytes != 4) return 0;

    std::lock_guard<std::mutex> lock(g_coreMutex);
    g_ramCandidates.clear();
    g_ramScanWidthBytes = static_cast<int>(widthBytes);

    uint8_t* ram = nullptr;
    size_t ramSize = 0;
    if (!getSystemRamRegionLocked(&ram, &ramSize) || !ram || ramSize < static_cast<size_t>(widthBytes)) {
        return 0;
    }

    const size_t limit = ramSize - static_cast<size_t>(widthBytes) + 1u;
    const bool matchAny = (compareValue == INT64_MIN);
    const uint32_t target = static_cast<uint32_t>(compareValue) & widthMask(widthBytes);

    g_ramCandidates.reserve(std::min<size_t>(limit, 65536u));
    for (size_t addr = 0; addr < limit; ++addr) {
        uint32_t cur = readRamLittleEndian(ram, static_cast<uint32_t>(addr), widthBytes);
        if (matchAny || cur == target) {
            g_ramCandidates.push_back(RamCandidate{ static_cast<uint32_t>(addr), cur });
        }
    }
    return static_cast<jint>(g_ramCandidates.size());
}

JNIEXPORT jint JNICALL
Java_com_storm_retroarch_NativeLibretroBridge_nativeRamScanNext(
    JNIEnv* /*env*/, jclass /*clazz*/, jint mode, jlong compareValue) {
    std::lock_guard<std::mutex> lock(g_coreMutex);
    if (g_ramCandidates.empty()) return 0;

    uint8_t* ram = nullptr;
    size_t ramSize = 0;
    const int wb = g_ramScanWidthBytes;
    if (!getSystemRamRegionLocked(&ram, &ramSize) || !ram || ramSize < static_cast<size_t>(wb)) {
        g_ramCandidates.clear();
        return 0;
    }

    const uint32_t target = static_cast<uint32_t>(compareValue) & widthMask(wb);
    size_t writeIdx = 0;
    for (size_t i = 0; i < g_ramCandidates.size(); ++i) {
        const RamCandidate cand = g_ramCandidates[i];
        if (static_cast<size_t>(cand.address) + static_cast<size_t>(wb) > ramSize) continue;

        const uint32_t cur = readRamLittleEndian(ram, cand.address, wb);
        bool keep = false;
        switch (mode) {
            case 0: keep = (cur == target); break;          // Exact
            case 1: keep = (cur > cand.lastValue); break;   // Increased
            case 2: keep = (cur < cand.lastValue); break;   // Decreased
            case 3: keep = (cur != cand.lastValue); break;  // Changed
            case 4: keep = (cur == cand.lastValue); break;  // Unchanged
            default: keep = (cur == target); break;
        }
        if (keep) {
            g_ramCandidates[writeIdx++] = RamCandidate{ cand.address, cur };
        }
    }
    g_ramCandidates.resize(writeIdx);
    return static_cast<jint>(g_ramCandidates.size());
}

JNIEXPORT jlongArray JNICALL
Java_com_storm_retroarch_NativeLibretroBridge_nativeRamGetCandidates(
    JNIEnv* env, jclass /*clazz*/, jint maxCount) {
    if (maxCount <= 0) return env->NewLongArray(0);

    std::lock_guard<std::mutex> lock(g_coreMutex);
    uint8_t* ram = nullptr;
    size_t ramSize = 0;
    const bool hasRam = getSystemRamRegionLocked(&ram, &ramSize) && ram != nullptr;
    const int wb = g_ramScanWidthBytes;

    const size_t count = std::min<size_t>(g_ramCandidates.size(), static_cast<size_t>(maxCount));
    jlongArray out = env->NewLongArray(static_cast<jsize>(count));
    if (!out || count == 0) return out;

    std::vector<jlong> packed(count);
    for (size_t i = 0; i < count; ++i) {
        const uint32_t addr = g_ramCandidates[i].address;
        uint32_t curVal = g_ramCandidates[i].lastValue;
        if (hasRam && static_cast<size_t>(addr) + static_cast<size_t>(wb) <= ramSize) {
            curVal = readRamLittleEndian(ram, addr, wb);
            g_ramCandidates[i].lastValue = curVal;
        }
        uint64_t entry = (static_cast<uint64_t>(addr) << 32) | (static_cast<uint64_t>(curVal) & 0xFFFFFFFFULL);
        packed[i] = static_cast<jlong>(entry);
    }
    env->SetLongArrayRegion(out, 0, static_cast<jsize>(count), packed.data());
    return out;
}

JNIEXPORT jboolean JNICALL
Java_com_storm_retroarch_NativeLibretroBridge_nativeRamWriteValue(
    JNIEnv* /*env*/, jclass /*clazz*/, jint address, jint widthBytes, jlong value) {
    if (address < 0 || (widthBytes != 1 && widthBytes != 2 && widthBytes != 4)) return JNI_FALSE;

    std::lock_guard<std::mutex> lock(g_coreMutex);
    uint8_t* ram = nullptr;
    size_t ramSize = 0;
    if (!getSystemRamRegionLocked(&ram, &ramSize) || !ram) return JNI_FALSE;
    if (static_cast<size_t>(address) + static_cast<size_t>(widthBytes) > ramSize) return JNI_FALSE;

    writeRamLittleEndian(ram, static_cast<uint32_t>(address), static_cast<int>(widthBytes),
                         static_cast<uint32_t>(value) & widthMask(static_cast<int>(widthBytes)));
    return JNI_TRUE;
}

JNIEXPORT void JNICALL
Java_com_storm_retroarch_NativeLibretroBridge_nativeRamSetFrozen(
    JNIEnv* /*env*/, jclass /*clazz*/, jint address, jint widthBytes, jlong value, jboolean frozen) {
    if (address < 0 || (widthBytes != 1 && widthBytes != 2 && widthBytes != 4)) return;
    const uint32_t addr = static_cast<uint32_t>(address);
    const uint32_t maskedVal = static_cast<uint32_t>(value) & widthMask(static_cast<int>(widthBytes));

    {
        std::lock_guard<std::mutex> freezeLock(g_ramFreezeMutex);
        auto it = std::find_if(g_frozenRamEntries.begin(), g_frozenRamEntries.end(),
                               [addr](const FrozenRamEntry& e) { return e.address == addr; });
        if (frozen != JNI_FALSE) {
            if (it != g_frozenRamEntries.end()) {
                it->widthBytes = static_cast<int>(widthBytes);
                it->value = maskedVal;
            } else {
                g_frozenRamEntries.push_back(FrozenRamEntry{ addr, static_cast<int>(widthBytes), maskedVal });
            }
        } else {
            if (it != g_frozenRamEntries.end()) {
                g_frozenRamEntries.erase(it);
            }
        }
    }

    if (frozen != JNI_FALSE) {
        std::lock_guard<std::mutex> lock(g_coreMutex);
        uint8_t* ram = nullptr;
        size_t ramSize = 0;
        if (getSystemRamRegionLocked(&ram, &ramSize) && ram &&
            static_cast<size_t>(addr) + static_cast<size_t>(widthBytes) <= ramSize) {
            writeRamLittleEndian(ram, addr, static_cast<int>(widthBytes), maskedVal);
        }
    }
}

JNIEXPORT void JNICALL
Java_com_storm_retroarch_NativeLibretroBridge_nativeRamClearFrozen(JNIEnv* /*env*/, jclass /*clazz*/) {
    std::lock_guard<std::mutex> freezeLock(g_ramFreezeMutex);
    g_frozenRamEntries.clear();
}

JNIEXPORT jint JNICALL JNI_OnLoad(JavaVM* vm, void* /*reserved*/) {
    initPixelLutsOnce();
    JNIEnv* env = nullptr;
    if (vm->GetEnv(reinterpret_cast<void**>(&env), JNI_VERSION_1_6) != JNI_OK || !env) {
        return JNI_ERR;
    }
    LOGI("STORM RETROARCH Native Libretro JNI Bridge 2.4.0 initialized");
    return JNI_VERSION_1_6;
}

} // extern "C"
