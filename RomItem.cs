using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StormRetroArch;

public class RomItem : INotifyPropertyChanged
{
    private string _name = "";
    private string _filePath = "";
    private string _extension = "";
    private string _platform = "";
    private string _coreDll = "";
    private string _coreDisplayName = "";
    private long _fileSizeBytes;
    private string _formattedSize = "";
    private bool _isCoreInstalled;
    private string _statusText = "";
    private double _progressValue;
    private bool _isDownloading;
    private ImageSource? _icon;
    private bool _isFavorite;
    private DateTime? _lastPlayed;
    private string _lastPlayedFormatted = "-";

    public string Title => Name;
    public string Crc32 { get; set; } = "";

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public string FilePath
    {
        get => _filePath;
        set => SetField(ref _filePath, value);
    }

    public string Extension
    {
        get => _extension;
        set => SetField(ref _extension, value);
    }

    public string Platform
    {
        get => _platform;
        set => SetField(ref _platform, value);
    }

    public string CoreDll
    {
        get => _coreDll;
        set => SetField(ref _coreDll, value);
    }

    public string CoreDisplayName
    {
        get => _coreDisplayName;
        set => SetField(ref _coreDisplayName, value);
    }

    public long FileSizeBytes
    {
        get => _fileSizeBytes;
        set
        {
            if (SetField(ref _fileSizeBytes, value))
            {
                FormattedSize = FormatFileSize(value);
            }
        }
    }

    public string FormattedSize
    {
        get => _formattedSize;
        set => SetField(ref _formattedSize, value);
    }

    public bool IsCoreInstalled
    {
        get => _isCoreInstalled;
        set => SetField(ref _isCoreInstalled, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    public double ProgressValue
    {
        get => _progressValue;
        set => SetField(ref _progressValue, value);
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        set => SetField(ref _isDownloading, value);
    }

    public ImageSource? Icon
    {
        get => _icon;
        set => SetField(ref _icon, value);
    }

    public bool IsFavorite
    {
        get => _isFavorite;
        set => SetField(ref _isFavorite, value);
    }

    public DateTime? LastPlayed
    {
        get => _lastPlayed;
        set
        {
            if (SetField(ref _lastPlayed, value))
            {
                LastPlayedFormatted = value.HasValue ? value.Value.ToString("yyyy-MM-dd HH:mm") : "-";
            }
        }
    }

    public string LastPlayedFormatted
    {
        get => _lastPlayedFormatted;
        set => SetField(ref _lastPlayedFormatted, value);
    }

    public static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{(bytes / 1024.0):F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{(bytes / (1024.0 * 1024.0)):F1} MB";
        return $"{(bytes / (1024.0 * 1024.0 * 1024.0)):F2} GB";
    }

    public void GeneratePixelIcon()
    {
        try
        {
            int w = 32, h = 32;
            var wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
            byte[] pixels = new byte[w * h * 4];

            // Deterministic color palette based on platform name
            uint hash = (uint)Math.Abs(Platform.GetHashCode());
            byte r = (byte)(60 + (hash & 0x7F));
            byte g = (byte)(60 + ((hash >> 7) & 0x7F));
            byte b = (byte)(80 + ((hash >> 14) & 0x7F));

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = (y * w + x) * 4;
                    bool isBorder = x == 0 || x == w - 1 || y == 0 || y == h - 1;
                    bool isCartridgeNotch = (y < 4 && (x < 4 || x > w - 5));
                    bool isLabelArea = (y >= 8 && y <= 22 && x >= 5 && x <= w - 6);

                    if (isCartridgeNotch)
                    {
                        pixels[idx] = 0;     // B
                        pixels[idx + 1] = 0; // G
                        pixels[idx + 2] = 0; // R
                        pixels[idx + 3] = 0; // Alpha
                    }
                    else if (isBorder)
                    {
                        pixels[idx] = 20;
                        pixels[idx + 1] = 16;
                        pixels[idx + 2] = 30;
                        pixels[idx + 3] = 255;
                    }
                    else if (isLabelArea)
                    {
                        pixels[idx] = (byte)Math.Min(255, b + 40);
                        pixels[idx + 1] = (byte)Math.Min(255, g + 20);
                        pixels[idx + 2] = (byte)Math.Min(255, r + 40);
                        pixels[idx + 3] = 255;
                    }
                    else
                    {
                        pixels[idx] = b;
                        pixels[idx + 1] = g;
                        pixels[idx + 2] = r;
                        pixels[idx + 3] = 255;
                    }
                }
            }

            wb.WritePixels(new System.Windows.Int32Rect(0, 0, w, h), pixels, w * 4, 0);
            wb.Freeze();
            Icon = wb;
        }
        catch { }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
