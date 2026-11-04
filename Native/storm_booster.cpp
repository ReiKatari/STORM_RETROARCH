/**
 * STORM RETROARCH 1.0.0 - Native C++20 AVX2 Accelerator & UIPI Hook Module
 * Copyright (C) 2026 STORM TEAM. All Rights Reserved.
 */

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <avrt.h>
#include <mmsystem.h>
#include <shellapi.h>
#include <immintrin.h>
#include <thread>
#include <chrono>
#include <atomic>
#include <string>

#pragma comment(lib, "winmm.lib")
#pragma comment(lib, "avrt.lib")
#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "user32.lib")
#pragma comment(lib, "gdi32.lib")

#define STORM_API extern "C" __declspec(dllexport)

// Forward declarations
static void RunBoosterOptimization();

static std::atomic<bool> g_Running{true};
static std::thread g_WorkerThread;

STORM_API void __cdecl StormBooster_Init(void) {
    // Explicit initialization entry point
}

// AVX2 accelerated memory streaming helper
STORM_API void __cdecl StormBooster_Avx2StreamCopy(void* __restrict dst, const void* __restrict src, size_t bytes) {
    size_t chunks = bytes / 32;
    __m256i* d = reinterpret_cast<__m256i*>(dst);
    const __m256i* s = reinterpret_cast<const __m256i*>(src);
    for (size_t i = 0; i < chunks; ++i) {
        __m256i data = _mm256_loadu_si256(s + i);
        _mm256_stream_si256(d + i, data);
    }
    _mm_sfence();
}

struct WindowSearchContext {
    DWORD targetPid;
    HWND targetHwnd;
};

static BOOL CALLBACK EnumWindowsCallback(HWND hwnd, LPARAM lParam) {
    WindowSearchContext* ctx = reinterpret_cast<WindowSearchContext*>(lParam);
    DWORD pid = 0;
    GetWindowThreadProcessId(hwnd, &pid);
    if (pid == ctx->targetPid) {
        wchar_t className[256] = {0};
        GetClassNameW(hwnd, className, 256);
        wchar_t title[512] = {0};
        GetWindowTextW(hwnd, title, 512);

        // Check if window is visible or top-level retroarch window
        if (IsWindowVisible(hwnd)) {
            ctx->targetHwnd = hwnd;
            return FALSE; // Stop enumeration
        }
    }
    return TRUE;
}

static void RunBoosterOptimization() {
    // 1. Establish 1ms high-precision multimedia timer and Realtime/High priority
    timeBeginPeriod(1);
    SetPriorityClass(GetCurrentProcess(), HIGH_PRIORITY_CLASS);

    // MMCSS Games profile
    DWORD taskIndex = 0;
    HANDLE hMmTask = AvSetMmThreadCharacteristicsW(L"Games", &taskIndex);
    if (hMmTask) {
        AvSetMmThreadPriority(hMmTask, AVRT_PRIORITY_HIGH);
    }

    const DWORD currentPid = GetCurrentProcessId();
    HWND retroarchHwnd = NULL;

    // Search window up to 10 seconds (200 * 50ms)
    for (int i = 0; i < 200 && g_Running.load(); ++i) {
        WindowSearchContext ctx = { currentPid, NULL };
        EnumWindows(EnumWindowsCallback, reinterpret_cast<LPARAM>(&ctx));
        if (ctx.targetHwnd) {
            retroarchHwnd = ctx.targetHwnd;
            break;
        }
        std::this_thread::sleep_for(std::chrono::milliseconds(50));
    }

    if (retroarchHwnd) {
        // Set window title to STORM RETROARCH 1.0.0
        SetWindowTextW(retroarchHwnd, L"STORM RETROARCH 1.0.0");

        // Load AppIcon.ico next to retroarch_engine.exe
        wchar_t exePath[MAX_PATH] = {0};
        GetModuleFileNameW(NULL, exePath, MAX_PATH);
        wchar_t* lastSlash = wcsrchr(exePath, L'\\');
        if (lastSlash) {
            *(lastSlash + 1) = L'\0';
            std::wstring iconPath = std::wstring(exePath) + L"AppIcon.ico";
            HICON hBig = (HICON)LoadImageW(NULL, iconPath.c_str(), IMAGE_ICON, 256, 256, LR_LOADFROMFILE);
            HICON hSmall = (HICON)LoadImageW(NULL, iconPath.c_str(), IMAGE_ICON, 32, 32, LR_LOADFROMFILE);
            if (hBig) {
                SendMessageW(retroarchHwnd, WM_SETICON, ICON_BIG, (LPARAM)hBig);
            }
            if (hSmall) {
                SendMessageW(retroarchHwnd, WM_SETICON, ICON_SMALL, (LPARAM)hSmall);
            }
        }

        // Enable Drag & Drop
        DragAcceptFiles(retroarchHwnd, TRUE);

        // Unlock UAC UIPI restrictions for Drag & Drop
        ChangeWindowMessageFilterEx(retroarchHwnd, WM_DROPFILES, 1 /* MSGFLT_ALLOW */, NULL);
        ChangeWindowMessageFilterEx(retroarchHwnd, WM_COPYDATA, 1 /* MSGFLT_ALLOW */, NULL);
        ChangeWindowMessageFilterEx(retroarchHwnd, 0x0049 /* WM_COPYGLOBALDATA */, 1 /* MSGFLT_ALLOW */, NULL);
    }
}

BOOL WINAPI DllMain(HINSTANCE hinstDLL, DWORD fdwReason, LPVOID lpvReserved) {
    if (fdwReason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(hinstDLL);
        g_WorkerThread = std::thread(RunBoosterOptimization);
        g_WorkerThread.detach();
    } else if (fdwReason == DLL_PROCESS_DETACH) {
        g_Running.store(false);
        timeEndPeriod(1);
    }
    return TRUE;
}
