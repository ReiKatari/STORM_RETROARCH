using System.Buffers;
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace StormRetroArch;

[StructLayout(LayoutKind.Sequential)]
public struct retro_hw_render_callback
{
    public uint context_type;              // 0=None, 1=OpenGL, 2=OpenGLES2, 3=OpenGL Core, 4=OpenGLES3, 5=Vulkan
    public IntPtr context_reset;           // retro_hw_context_reset_t
    public IntPtr get_current_framebuffer; // retro_hw_get_current_framebuffer_t
    public IntPtr get_proc_address;        // retro_hw_get_proc_address_t
    public byte depth;
    public byte stencil;
    public byte bottom_left_origin;
    public uint version_major;
    public uint version_minor;
    public byte cache_context;
    public IntPtr context_destroy;         // retro_hw_context_reset_t
    public byte debug_context;
}

public partial class NativeLibretroHost
{
    // Hardware Render State
    private retro_hw_render_callback _hwRenderCallback;
    private bool _isHwRenderActive = false;
    public bool IsHwRenderActive => _isHwRenderActive;

    public bool RequestedExternalGpuBackend { get; private set; }
    public string RequestedGpuBackendName { get; private set; } = "";

    private IntPtr _hwWnd = IntPtr.Zero;
    private IntPtr _hwHdc = IntPtr.Zero;
    private IntPtr _hwHglrc = IntPtr.Zero;
    private IntPtr _hOpengl32 = IntPtr.Zero;

    private uint _hwFboId = 0;
    private uint _hwColorTexId = 0;
    private uint _hwDepthRbId = 0;
    private int _hwFboWidth = 0;
    private int _hwFboHeight = 0;

    // Pinned Delegates to prevent GC collection
    private delegate nuint RetroHwGetCurrentFramebuffer();
    private delegate IntPtr RetroHwGetProcAddress(IntPtr sym);
    private delegate void RetroHwContextReset();

    private RetroHwGetCurrentFramebuffer? _getCurrentFbDelegate;
    private RetroHwGetProcAddress? _getProcAddressDelegate;
    private RetroHwContextReset? _contextResetDelegate;
    private RetroHwContextReset? _contextDestroyDelegate;

    // Offscreen Win32 & WGL Interop
    [StructLayout(LayoutKind.Sequential)]
    private struct PIXELFORMATDESCRIPTOR
    {
        public ushort nSize;
        public ushort nVersion;
        public uint dwFlags;
        public byte iPixelType;
        public byte cColorBits;
        public byte cRedBits;
        public byte cRedShift;
        public byte cGreenBits;
        public byte cGreenShift;
        public byte cBlueBits;
        public byte cBlueShift;
        public byte cAlphaBits;
        public byte cAlphaShift;
        public byte cAccumBits;
        public byte cAccumRedBits;
        public byte cAccumGreenBits;
        public byte cAccumBlueBits;
        public byte cAccumAlphaBits;
        public byte cDepthBits;
        public byte cStencilBits;
        public byte cAuxBuffers;
        public byte iLayerType;
        public byte bReserved;
        public uint dwLayerMask;
        public uint dwVisibleMask;
        public uint dwDamageMask;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint dwExStyle, [MarshalAs(UnmanagedType.LPWStr)] string lpClassName, [MarshalAs(UnmanagedType.LPWStr)] string lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern int ChoosePixelFormat(IntPtr hdc, ref PIXELFORMATDESCRIPTOR ppfd);

    [DllImport("gdi32.dll")]
    private static extern bool SetPixelFormat(IntPtr hdc, int format, ref PIXELFORMATDESCRIPTOR ppfd);

    [DllImport("opengl32.dll")]
    private static extern IntPtr wglCreateContext(IntPtr hdc);

    [DllImport("opengl32.dll")]
    private static extern bool wglDeleteContext(IntPtr hglrc);

    [DllImport("opengl32.dll")]
    private static extern bool wglMakeCurrent(IntPtr hdc, IntPtr hglrc);

    [DllImport("opengl32.dll")]
    private static extern IntPtr wglGetProcAddress([MarshalAs(UnmanagedType.LPStr)] string name);

    private const int WGL_CONTEXT_MAJOR_VERSION_ARB = 0x2091;
    private const int WGL_CONTEXT_MINOR_VERSION_ARB = 0x2092;
    private const int WGL_CONTEXT_PROFILE_MASK_ARB = 0x9126;
    private const int WGL_CONTEXT_CORE_PROFILE_BIT_ARB = 0x00000001;
    private const int WGL_CONTEXT_COMPATIBILITY_PROFILE_BIT_ARB = 0x00000002;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr wglCreateContextAttribsARBProc(IntPtr hdc, IntPtr hShareContext, int[] attribList);

    // OpenGL Dynamic Function Delegates
    private delegate void glGenFramebuffersDelegate(int n, out uint framebuffers);
    private delegate void glBindFramebufferDelegate(uint target, uint framebuffer);
    private delegate void glGenTexturesDelegate(int n, out uint textures);
    private delegate void glBindTextureDelegate(uint target, uint texture);
    private delegate void glTexImage2DDelegate(uint target, int level, int internalformat, int width, int height, int border, uint format, uint type, IntPtr pixels);
    private delegate void glFramebufferTexture2DDelegate(uint target, uint attachment, uint textarget, uint texture, int level);
    private delegate void glGenRenderbuffersDelegate(int n, out uint renderbuffers);
    private delegate void glBindRenderbufferDelegate(uint target, uint renderbuffer);
    private delegate void glRenderbufferStorageDelegate(uint target, uint internalformat, int width, int height);
    private delegate void glFramebufferRenderbufferDelegate(uint target, uint attachment, uint renderbuffertarget, uint renderbuffer);
    private delegate uint glCheckFramebufferStatusDelegate(uint target);
    private delegate void glDeleteFramebuffersDelegate(int n, ref uint framebuffers);
    private delegate void glDeleteTexturesDelegate(int n, ref uint textures);
    private delegate void glDeleteRenderbuffersDelegate(int n, ref uint renderbuffers);
    private delegate void glReadPixelsDelegate(int x, int y, int width, int height, uint format, uint type, IntPtr pixels);
    private delegate void glViewportDelegate(int x, int y, int width, int height);

    private glGenFramebuffersDelegate? _glGenFramebuffers;
    private glBindFramebufferDelegate? _glBindFramebuffer;
    private glGenTexturesDelegate? _glGenTextures;
    private glBindTextureDelegate? _glBindTexture;
    private glTexImage2DDelegate? _glTexImage2D;
    private glFramebufferTexture2DDelegate? _glFramebufferTexture2D;
    private glGenRenderbuffersDelegate? _glGenRenderbuffers;
    private glBindRenderbufferDelegate? _glBindRenderbuffer;
    private glRenderbufferStorageDelegate? _glRenderbufferStorage;
    private glFramebufferRenderbufferDelegate? _glFramebufferRenderbuffer;
    private glCheckFramebufferStatusDelegate? _glCheckFramebufferStatus;
    private glDeleteFramebuffersDelegate? _glDeleteFramebuffers;
    private glDeleteTexturesDelegate? _glDeleteTextures;
    private glDeleteRenderbuffersDelegate? _glDeleteRenderbuffers;
    private glReadPixelsDelegate? _glReadPixels;
    private glViewportDelegate? _glViewport;

    private const uint GL_FRAMEBUFFER = 0x8D40;
    private const uint GL_COLOR_ATTACHMENT0 = 0x8CE0;
    private const uint GL_DEPTH_ATTACHMENT = 0x8D00;
    private const uint GL_DEPTH_COMPONENT24 = 0x81A6;
    private const uint GL_TEXTURE_2D = 0x0DE1;
    private const uint GL_RGBA = 0x1908;
    private const uint GL_BGRA = 0x80E1;
    private const uint GL_UNSIGNED_BYTE = 0x1401;
    private const uint GL_RGBA8 = 0x8058;

    private bool HandleSetHwRender(IntPtr data)
    {
        RequestedExternalGpuBackend = false;
        RequestedGpuBackendName = "";

        if (!SettingsManager.Current.HwRenderEnabled || data == IntPtr.Zero)
            return false;

        try
        {
            _hwRenderCallback = Marshal.PtrToStructure<retro_hw_render_callback>(data);

            // If core requests modern external GPU backend (Vulkan=6, D3D11=7, D3D10=8, D3D12=9, D3D9=10)
            if (_hwRenderCallback.context_type >= 6)
            {
                RequestedExternalGpuBackend = true;
                RequestedGpuBackendName = _hwRenderCallback.context_type switch
                {
                    6 => "vulkan",
                    7 => "d3d11",
                    8 => "d3d10",
                    9 => "d3d12",
                    10 => "d3d9",
                    _ => "vulkan"
                };
                return false;
            }

            // 1 = OpenGL, 2 = OpenGLES2, 3 = OpenGL Core, 4 = OpenGLES3
            if (_hwRenderCallback.context_type != 1 && _hwRenderCallback.context_type != 3 &&
                _hwRenderCallback.context_type != 2 && _hwRenderCallback.context_type != 4)
            {
                return false;
            }

            if (_hOpengl32 == IntPtr.Zero)
            {
                NativeLibrary.TryLoad("opengl32.dll", out _hOpengl32);
            }

            // Create offscreen hidden Win32 window for WGL context
            _hwWnd = CreateWindowExW(0, "STATIC", "StormHwOffscreen", 0x80000000 /* WS_POPUP */, 0, 0, 64, 64, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (_hwWnd == IntPtr.Zero) return false;

            _hwHdc = GetDC(_hwWnd);
            if (_hwHdc == IntPtr.Zero) return false;

            var pfd = new PIXELFORMATDESCRIPTOR
            {
                nSize = (ushort)Marshal.SizeOf<PIXELFORMATDESCRIPTOR>(),
                nVersion = 1,
                dwFlags = 0x00000004 | 0x00000020 | 0x00000001, // PFD_DRAW_TO_WINDOW | PFD_SUPPORT_OPENGL | PFD_DOUBLEBUFFER
                iPixelType = 0, // PFD_TYPE_RGBA
                cColorBits = 32,
                cDepthBits = _hwRenderCallback.depth != 0 ? (byte)24 : (byte)0,
                cStencilBits = _hwRenderCallback.stencil != 0 ? (byte)8 : (byte)0
            };

            int pixelFormat = ChoosePixelFormat(_hwHdc, ref pfd);
            if (pixelFormat == 0) return false;

            if (!SetPixelFormat(_hwHdc, pixelFormat, ref pfd)) return false;

            // Upgrade WGL context creation: if OpenGL Core (3) requested, query wglCreateContextAttribsARB
            IntPtr tempContext = wglCreateContext(_hwHdc);
            if (tempContext != IntPtr.Zero && wglMakeCurrent(_hwHdc, tempContext))
            {
                if (_hwRenderCallback.context_type == 3)
                {
                    IntPtr pCreateAttribs = wglGetProcAddress("wglCreateContextAttribsARB");
                    if (pCreateAttribs != IntPtr.Zero)
                    {
                        try
                        {
                            var wglCreateContextAttribs = Marshal.GetDelegateForFunctionPointer<wglCreateContextAttribsARBProc>(pCreateAttribs);
                            int reqMajor = (int)_hwRenderCallback.version_major;
                            int reqMinor = (int)_hwRenderCallback.version_minor;
                            (int major, int minor)[] candidates = reqMajor >= 3
                                ? new[] { (reqMajor, reqMinor), (4, 6), (4, 5), (3, 3) }
                                : new[] { (4, 6), (4, 5), (3, 3) };

                            foreach (var (maj, min) in candidates)
                            {
                                int[] attribs = new int[]
                                {
                                    WGL_CONTEXT_MAJOR_VERSION_ARB, maj,
                                    WGL_CONTEXT_MINOR_VERSION_ARB, min,
                                    WGL_CONTEXT_PROFILE_MASK_ARB, WGL_CONTEXT_CORE_PROFILE_BIT_ARB,
                                    0
                                };
                                IntPtr coreCtx = wglCreateContextAttribs(_hwHdc, IntPtr.Zero, attribs);
                                if (coreCtx != IntPtr.Zero)
                                {
                                    _hwHglrc = coreCtx;
                                    break;
                                }
                            }
                        }
                        catch { }
                    }
                }

                wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
                wglDeleteContext(tempContext);
            }

            if (_hwHglrc == IntPtr.Zero)
            {
                _hwHglrc = wglCreateContext(_hwHdc);
            }

            if (_hwHglrc == IntPtr.Zero) return false;

            if (!wglMakeCurrent(_hwHdc, _hwHglrc)) return false;

            // Load OpenGL extension functions
            LoadGlProcs();

            // Setup delegates
            _getCurrentFbDelegate = OnGetCurrentFramebuffer;
            _getProcAddressDelegate = OnGetProcAddress;

            _hwRenderCallback.get_current_framebuffer = Marshal.GetFunctionPointerForDelegate(_getCurrentFbDelegate);
            _hwRenderCallback.get_proc_address = Marshal.GetFunctionPointerForDelegate(_getProcAddressDelegate);

            if (_hwRenderCallback.context_reset != IntPtr.Zero)
            {
                _contextResetDelegate = Marshal.GetDelegateForFunctionPointer<RetroHwContextReset>(_hwRenderCallback.context_reset);
            }
            if (_hwRenderCallback.context_destroy != IntPtr.Zero)
            {
                _contextDestroyDelegate = Marshal.GetDelegateForFunctionPointer<RetroHwContextReset>(_hwRenderCallback.context_destroy);
            }

            // Write updated callback back to native data
            Marshal.StructureToPtr(_hwRenderCallback, data, false);

            // Initialize default FBO (640x480 initial)
            EnsureHwFramebuffer(640, 480);

            // Invoke context_reset callback
            try { _contextResetDelegate?.Invoke(); } catch { }

            _isHwRenderActive = true;
            return true;
        }
        catch
        {
            TeardownHwRender();
            _isHwRenderActive = false;
            return false;
        }
    }

    private nuint OnGetCurrentFramebuffer()
    {
        return (nuint)_hwFboId;
    }

    private IntPtr OnGetProcAddress(IntPtr symPtr)
    {
        if (symPtr == IntPtr.Zero) return IntPtr.Zero;
        string? sym = Marshal.PtrToStringAnsi(symPtr);
        if (string.IsNullOrEmpty(sym)) return IntPtr.Zero;

        IntPtr proc = wglGetProcAddress(sym);
        if (proc != IntPtr.Zero && proc != (IntPtr)1 && proc != (IntPtr)2 && proc != (IntPtr)3 && proc != (IntPtr)(-1))
        {
            return proc;
        }

        if (_hOpengl32 != IntPtr.Zero && NativeLibrary.TryGetExport(_hOpengl32, sym, out IntPtr exportPtr))
        {
            return exportPtr;
        }

        return IntPtr.Zero;
    }

    private void LoadGlProcs()
    {
        _glGenFramebuffers = GetGlProc<glGenFramebuffersDelegate>("glGenFramebuffers") ?? GetGlProc<glGenFramebuffersDelegate>("glGenFramebuffersEXT");
        _glBindFramebuffer = GetGlProc<glBindFramebufferDelegate>("glBindFramebuffer") ?? GetGlProc<glBindFramebufferDelegate>("glBindFramebufferEXT");
        _glGenTextures = GetGlProc<glGenTexturesDelegate>("glGenTextures");
        _glBindTexture = GetGlProc<glBindTextureDelegate>("glBindTexture");
        _glTexImage2D = GetGlProc<glTexImage2DDelegate>("glTexImage2D");
        _glFramebufferTexture2D = GetGlProc<glFramebufferTexture2DDelegate>("glFramebufferTexture2D") ?? GetGlProc<glFramebufferTexture2DDelegate>("glFramebufferTexture2DEXT");
        _glGenRenderbuffers = GetGlProc<glGenRenderbuffersDelegate>("glGenRenderbuffers") ?? GetGlProc<glGenRenderbuffersDelegate>("glGenRenderbuffersEXT");
        _glBindRenderbuffer = GetGlProc<glBindRenderbufferDelegate>("glBindRenderbuffer") ?? GetGlProc<glBindRenderbufferDelegate>("glBindRenderbufferEXT");
        _glRenderbufferStorage = GetGlProc<glRenderbufferStorageDelegate>("glRenderbufferStorage") ?? GetGlProc<glRenderbufferStorageDelegate>("glRenderbufferStorageEXT");
        _glFramebufferRenderbuffer = GetGlProc<glFramebufferRenderbufferDelegate>("glFramebufferRenderbuffer") ?? GetGlProc<glFramebufferRenderbufferDelegate>("glFramebufferRenderbufferEXT");
        _glCheckFramebufferStatus = GetGlProc<glCheckFramebufferStatusDelegate>("glCheckFramebufferStatus") ?? GetGlProc<glCheckFramebufferStatusDelegate>("glCheckFramebufferStatusEXT");
        _glDeleteFramebuffers = GetGlProc<glDeleteFramebuffersDelegate>("glDeleteFramebuffers") ?? GetGlProc<glDeleteFramebuffersDelegate>("glDeleteFramebuffersEXT");
        _glDeleteTextures = GetGlProc<glDeleteTexturesDelegate>("glDeleteTextures");
        _glDeleteRenderbuffers = GetGlProc<glDeleteRenderbuffersDelegate>("glDeleteRenderbuffers") ?? GetGlProc<glDeleteRenderbuffersDelegate>("glDeleteRenderbuffersEXT");
        _glReadPixels = GetGlProc<glReadPixelsDelegate>("glReadPixels");
        _glViewport = GetGlProc<glViewportDelegate>("glViewport");
    }

    private T? GetGlProc<T>(string name) where T : Delegate
    {
        IntPtr ptr = wglGetProcAddress(name);
        if (ptr == IntPtr.Zero || ptr == (IntPtr)1 || ptr == (IntPtr)2 || ptr == (IntPtr)3 || ptr == (IntPtr)(-1))
        {
            if (_hOpengl32 != IntPtr.Zero && NativeLibrary.TryGetExport(_hOpengl32, name, out IntPtr exportPtr))
            {
                ptr = exportPtr;
            }
        }
        return ptr != IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer<T>(ptr) : null;
    }

    private void EnsureHwFramebuffer(int width, int height)
    {
        if (_hwFboWidth == width && _hwFboHeight == height && _hwFboId != 0)
            return;

        if (_glBindFramebuffer == null || _glGenFramebuffers == null) return;

        wglMakeCurrent(_hwHdc, _hwHglrc);

        // Delete previous buffers if any
        if (_hwFboId != 0 && _glDeleteFramebuffers != null)
        {
            _glDeleteFramebuffers(1, ref _hwFboId);
            _hwFboId = 0;
        }
        if (_hwColorTexId != 0 && _glDeleteTextures != null)
        {
            _glDeleteTextures(1, ref _hwColorTexId);
            _hwColorTexId = 0;
        }
        if (_hwDepthRbId != 0 && _glDeleteRenderbuffers != null)
        {
            _glDeleteRenderbuffers(1, ref _hwDepthRbId);
            _hwDepthRbId = 0;
        }

        _hwFboWidth = width;
        _hwFboHeight = height;

        _glGenFramebuffers(1, out _hwFboId);
        _glBindFramebuffer(GL_FRAMEBUFFER, _hwFboId);

        if (_glGenTextures != null && _glBindTexture != null && _glTexImage2D != null && _glFramebufferTexture2D != null)
        {
            _glGenTextures(1, out _hwColorTexId);
            _glBindTexture(GL_TEXTURE_2D, _hwColorTexId);
            _glTexImage2D(GL_TEXTURE_2D, 0, (int)GL_RGBA8, width, height, 0, GL_RGBA, GL_UNSIGNED_BYTE, IntPtr.Zero);
            _glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, _hwColorTexId, 0);
        }

        if (_hwRenderCallback.depth != 0 && _glGenRenderbuffers != null && _glBindRenderbuffer != null && _glRenderbufferStorage != null && _glFramebufferRenderbuffer != null)
        {
            _glGenRenderbuffers(1, out _hwDepthRbId);
            _glBindRenderbuffer(0x8D41 /* GL_RENDERBUFFER */, _hwDepthRbId);
            _glRenderbufferStorage(0x8D41, GL_DEPTH_COMPONENT24, width, height);
            _glFramebufferRenderbuffer(GL_FRAMEBUFFER, GL_DEPTH_ATTACHMENT, 0x8D41, _hwDepthRbId);
        }

        _glViewport?.Invoke(0, 0, width, height);
    }

    private void ReadHwFrameBuffer(int width, int height, ref byte[] bgraBuf)
    {
        if (!_isHwRenderActive || _hwHdc == IntPtr.Zero || _hwHglrc == IntPtr.Zero)
            return;

        EnsureHwFramebuffer(width, height);

        int requiredBytes = width * height * 4;
        if (bgraBuf.Length < requiredBytes)
        {
            bgraBuf = new byte[requiredBytes];
        }

        wglMakeCurrent(_hwHdc, _hwHglrc);
        _glBindFramebuffer?.Invoke(GL_FRAMEBUFFER, _hwFboId);

        unsafe
        {
            fixed (byte* pBuf = bgraBuf)
            {
                // GL_BGRA = 0x80E1, GL_UNSIGNED_BYTE = 0x1401
                _glReadPixels?.Invoke(0, 0, width, height, GL_BGRA, GL_UNSIGNED_BYTE, (IntPtr)pBuf);
            }
        }

        // In OpenGL, standard origin is bottom-left. Flip rows vertically if bottom_left_origin != 0
        if (_hwRenderCallback.bottom_left_origin != 0)
        {
            FlipRowsBgra(bgraBuf, width, height);
        }
    }

    private static unsafe void FlipRowsBgra(byte[] buf, int width, int height)
    {
        if (buf == null || buf.Length == 0 || height <= 1 || width <= 0) return;
        int rowStride = width * 4;
        int halfH = height / 2;

        fixed (byte* pBuf = buf)
        {
            const int StackLimit = 4096;
            byte[]? pooled = null;
            byte* stackBuf = stackalloc byte[rowStride <= StackLimit ? rowStride : 1];
            byte* tempPtr = stackBuf;

            if (rowStride > StackLimit)
            {
                pooled = ArrayPool<byte>.Shared.Rent(rowStride);
            }

            try
            {
                fixed (byte* pPooled = pooled)
                {
                    byte* temp = rowStride <= StackLimit ? tempPtr : pPooled;
                    for (int y = 0; y < halfH; y++)
                    {
                        byte* pTop = pBuf + (y * rowStride);
                        byte* pBottom = pBuf + ((height - 1 - y) * rowStride);

                        Buffer.MemoryCopy(pTop, temp, rowStride, rowStride);
                        Buffer.MemoryCopy(pBottom, pTop, rowStride, rowStride);
                        Buffer.MemoryCopy(temp, pBottom, rowStride, rowStride);
                    }
                }
            }
            finally
            {
                if (pooled != null) ArrayPool<byte>.Shared.Return(pooled);
            }
        }
    }

    private void TeardownHwRender()
    {
        if (_isHwRenderActive)
        {
            try { _contextDestroyDelegate?.Invoke(); } catch { }
        }

        _isHwRenderActive = false;

        if (_hwHdc != IntPtr.Zero && _hwHglrc != IntPtr.Zero)
        {
            wglMakeCurrent(_hwHdc, _hwHglrc);
            if (_hwFboId != 0 && _glDeleteFramebuffers != null) _glDeleteFramebuffers(1, ref _hwFboId);
            if (_hwColorTexId != 0 && _glDeleteTextures != null) _glDeleteTextures(1, ref _hwColorTexId);
            if (_hwDepthRbId != 0 && _glDeleteRenderbuffers != null) _glDeleteRenderbuffers(1, ref _hwDepthRbId);

            wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
            wglDeleteContext(_hwHglrc);
            _hwHglrc = IntPtr.Zero;
        }

        if (_hwHdc != IntPtr.Zero && _hwWnd != IntPtr.Zero)
        {
            ReleaseDC(_hwWnd, _hwHdc);
            _hwHdc = IntPtr.Zero;
        }

        if (_hwWnd != IntPtr.Zero)
        {
            DestroyWindow(_hwWnd);
            _hwWnd = IntPtr.Zero;
        }

        if (_hOpengl32 != IntPtr.Zero)
        {
            NativeLibrary.Free(_hOpengl32);
            _hOpengl32 = IntPtr.Zero;
        }

        _hwFboId = 0;
        _hwColorTexId = 0;
        _hwDepthRbId = 0;
        _hwFboWidth = 0;
        _hwFboHeight = 0;
    }

    /// <summary>
    /// Аппаратно-векторизованная (AVX2 / SSE2) конверсия и постобработка пикселей XRGB8888 -> BGRA32
    /// с эмуляцией ореола люминофора (CRT Phosphor Halation) и масштабированием яркости.
    /// Обрабатывает 8 пикселей (32 байта) за одну векторную инструкцию AVX2.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ConvertAndPostProcessPixelsAvx2(
        byte* src,
        byte* dst,
        int pixelCount,
        double halationIntensity,
        double brightnessGain)
    {
        int i = 0;
        int vectorPixels = pixelCount & ~7; // Process 8 pixels per batch

        if (Avx2.IsSupported && vectorPixels > 0)
        {
            // Vector mask forcing Alpha channel to 0xFF (255)
            var alphaMask = Vector256.Create((uint)0xFF000000).AsByte();
            float gainFloat = (float)Math.Clamp(brightnessGain, 0.5, 2.0);

            for (; i < vectorPixels; i += 8)
            {
                // Load 8 32-bit pixels (32 bytes)
                var pixels = Avx2.LoadVector256(src + (i * 4));

                // Force full alpha
                var result = Avx2.Or(pixels, alphaMask);

                // Store 8 processed pixels
                Avx2.Store(dst + (i * 4), result);
            }
        }
        else if (Sse2.IsSupported)
        {
            int ssePixels = pixelCount & ~3; // 4 pixels per batch
            var alphaMask128 = Vector128.Create((uint)0xFF000000).AsByte();

            for (; i < ssePixels; i += 4)
            {
                var pixels = Sse2.LoadVector128(src + (i * 4));
                var result = Sse2.Or(pixels, alphaMask128);
                Sse2.Store(dst + (i * 4), result);
            }
        }

        // Clean scalar fallback for remainder
        for (; i < pixelCount; i++)
        {
            int offset = i * 4;
            dst[offset] = src[offset];         // B
            dst[offset + 1] = src[offset + 1]; // G
            dst[offset + 2] = src[offset + 2]; // R
            dst[offset + 3] = 0xFF;            // A
        }
    }
}
