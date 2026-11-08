using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;

namespace StormRetroArch;

public enum RamValueWidth
{
    Byte8 = 1,
    UInt16 = 2,
    UInt32 = 4
}

public enum RamCompareMode
{
    ExactValue,
    NotEqual,
    Increased,
    Decreased,
    Changed,
    Unchanged
}

public class RamMatchEntry : INotifyPropertyChanged
{
    private uint _address;
    private uint _currentValue;
    private uint _previousValue;
    private RamValueWidth _width = RamValueWidth.Byte8;
    private bool _isFrozen;
    private string _descriptionRu = string.Empty;

    public uint Address
    {
        get => _address;
        set
        {
            if (_address != value)
            {
                _address = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HexAddress));
            }
        }
    }

    public string HexAddress => $"0x{Address:X6}";

    public uint CurrentValue
    {
        get => _currentValue;
        set
        {
            if (_currentValue != value)
            {
                _currentValue = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FormattedValues));
            }
        }
    }

    public uint PreviousValue
    {
        get => _previousValue;
        set
        {
            if (_previousValue != value)
            {
                _previousValue = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FormattedValues));
            }
        }
    }

    public RamValueWidth Width
    {
        get => _width;
        set
        {
            if (_width != value)
            {
                _width = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsFrozen
    {
        get => _isFrozen;
        set
        {
            if (_isFrozen != value)
            {
                _isFrozen = value;
                OnPropertyChanged();
            }
        }
    }

    public string DescriptionRu
    {
        get => _descriptionRu;
        set
        {
            if (_descriptionRu != value)
            {
                _descriptionRu = value;
                OnPropertyChanged();
            }
        }
    }

    public string FormattedValues => $"{CurrentValue} (было: {PreviousValue})";

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// High-speed Live RAM Cheat Scanner & Memory Trainer Engine for STORM RETROARCH.
/// Operates directly on NativeLibretroHost System RAM (RETRO_MEMORY_SYSTEM_RAM = 2)
/// and provides a realistic 64 KB simulated retro RAM buffer when no core is running.
/// </summary>
public sealed class RamCheatScannerEngine
{
    public static RamCheatScannerEngine Shared { get; } = new();

    private readonly object _scanLock = new();
    private readonly ConcurrentDictionary<uint, (RamValueWidth Width, uint Value)> _frozenValues = new();
    private readonly byte[] _simulatedRam = new byte[65536];
    private readonly Random _simRnd = new(1337);

    private byte[]? _previousRamSnapshot;
    private readonly List<uint> _candidateAddresses = new();
    private readonly Dictionary<uint, uint> _candidatePreviousValues = new();

    private List<RamMatchEntry> _matches = new();
    private int _totalCandidateCount;
    private double _scanElapsedMs;
    private int _scanIteration;
    private RamValueWidth _activeWidth = RamValueWidth.Byte8;

    public IReadOnlyList<RamMatchEntry> Matches
    {
        get
        {
            lock (_scanLock)
            {
                return _matches;
            }
        }
    }

    public int TotalCandidateCount
    {
        get
        {
            lock (_scanLock)
            {
                return _totalCandidateCount;
            }
        }
    }

    public double ScanElapsedMs
    {
        get
        {
            lock (_scanLock)
            {
                return _scanElapsedMs;
            }
        }
    }

    public int ScanIteration
    {
        get
        {
            lock (_scanLock)
            {
                return _scanIteration;
            }
        }
    }

    public bool HasActiveScan => _scanIteration > 0;
    public int FrozenCount => _frozenValues.Count;

    public RamCheatScannerEngine()
    {
        InitSimulatedRam();
    }

    private void InitSimulatedRam()
    {
        Array.Clear(_simulatedRam, 0, _simulatedRam.Length);

        // Simulated Retro Game RAM template:
        // 0x00A0: HP (ushort = 100)
        _simulatedRam[0x00A0] = 100;
        _simulatedRam[0x00A1] = 0;

        // 0x00A2: Lives (byte = 3)
        _simulatedRam[0x00A2] = 3;

        // 0x00A4: Score (uint = 12500)
        BitConverter.GetBytes((uint)12500).CopyTo(_simulatedRam, 0x00A4);

        // 0x00A8: Gold / Coins (ushort = 50)
        BitConverter.GetBytes((ushort)50).CopyTo(_simulatedRam, 0x00A8);

        // Pre-fill some typical sprite/game state registers
        for (int i = 0x0100; i < 0x0400; i++)
        {
            _simulatedRam[i] = (byte)(i * 3 + 17);
        }
    }

    private byte[] GetCurrentRamSnapshot()
    {
        byte[]? hostRam = null;
        try
        {
            hostRam = NativeLibretroHost.Current.ReadSystemRamSnapshot();
        }
        catch { }

        if (hostRam != null && hostRam.Length > 0)
        {
            return hostRam;
        }

        // Realistic Simulated Retro RAM
        lock (_simulatedRam)
        {
            // Gently advance simulation if not frozen
            if (!_frozenValues.ContainsKey(0x00A4))
            {
                uint curScore = BitConverter.ToUInt32(_simulatedRam, 0x00A4);
                curScore += 10;
                BitConverter.GetBytes(curScore).CopyTo(_simulatedRam, 0x00A4);
            }

            byte[] copy = new byte[_simulatedRam.Length];
            Buffer.BlockCopy(_simulatedRam, 0, copy, 0, _simulatedRam.Length);
            return copy;
        }
    }

    public void StartFirstScan(RamValueWidth width, RamCompareMode mode, uint targetValue)
    {
        lock (_scanLock)
        {
            var sw = Stopwatch.StartNew();
            _activeWidth = width;
            _scanIteration = 1;
            _candidateAddresses.Clear();
            _candidatePreviousValues.Clear();

            byte[] currentRam = GetCurrentRamSnapshot();
            _previousRamSnapshot = (byte[])currentRam.Clone();

            int stride = (int)width;
            int maxOffset = currentRam.Length - stride;

            for (uint addr = 0; addr <= maxOffset; addr += 1)
            {
                uint val = ReadRamValue(currentRam, addr, width);
                bool match = mode switch
                {
                    RamCompareMode.ExactValue => val == targetValue,
                    RamCompareMode.NotEqual => val != targetValue,
                    RamCompareMode.Increased => val > targetValue,
                    RamCompareMode.Decreased => val < targetValue,
                    RamCompareMode.Changed => true,
                    RamCompareMode.Unchanged => true,
                    _ => false
                };

                if (match)
                {
                    _candidateAddresses.Add(addr);
                    _candidatePreviousValues[addr] = val;
                }
            }

            sw.Stop();
            _scanElapsedMs = sw.Elapsed.TotalMilliseconds;
            _totalCandidateCount = _candidateAddresses.Count;
            BuildMatchesFromCandidates(currentRam);
        }
    }

    public void NextScan(RamCompareMode mode, uint targetValue)
    {
        lock (_scanLock)
        {
            if (_candidateAddresses.Count == 0 || _previousRamSnapshot == null)
            {
                StartFirstScan(_activeWidth, mode, targetValue);
                return;
            }

            var sw = Stopwatch.StartNew();
            _scanIteration++;

            byte[] currentRam = GetCurrentRamSnapshot();
            var nextCandidates = new List<uint>(_candidateAddresses.Count);

            foreach (uint addr in _candidateAddresses)
            {
                if (addr + (uint)_activeWidth > currentRam.Length) continue;

                uint currentVal = ReadRamValue(currentRam, addr, _activeWidth);
                _candidatePreviousValues.TryGetValue(addr, out uint prevVal);

                bool match = mode switch
                {
                    RamCompareMode.ExactValue => currentVal == targetValue,
                    RamCompareMode.NotEqual => currentVal != targetValue,
                    RamCompareMode.Increased => targetValue > 0 ? (currentVal == prevVal + targetValue) : (currentVal > prevVal),
                    RamCompareMode.Decreased => targetValue > 0 ? (prevVal >= targetValue && currentVal == prevVal - targetValue) : (currentVal < prevVal),
                    RamCompareMode.Changed => currentVal != prevVal,
                    RamCompareMode.Unchanged => currentVal == prevVal,
                    _ => false
                };

                if (match)
                {
                    nextCandidates.Add(addr);
                    _candidatePreviousValues[addr] = currentVal;
                }
            }

            _candidateAddresses.Clear();
            _candidateAddresses.AddRange(nextCandidates);
            _previousRamSnapshot = (byte[])currentRam.Clone();

            sw.Stop();
            _scanElapsedMs = sw.Elapsed.TotalMilliseconds;
            _totalCandidateCount = _candidateAddresses.Count;
            BuildMatchesFromCandidates(currentRam);
        }
    }

    public void ResetScan()
    {
        lock (_scanLock)
        {
            _candidateAddresses.Clear();
            _candidatePreviousValues.Clear();
            _matches.Clear();
            _previousRamSnapshot = null;
            _totalCandidateCount = 0;
            _scanIteration = 0;
            _scanElapsedMs = 0;
        }
    }

    private void BuildMatchesFromCandidates(byte[] currentRam)
    {
        var topList = new List<RamMatchEntry>(Math.Min(_candidateAddresses.Count, 250));
        int limit = Math.Min(_candidateAddresses.Count, 250);

        for (int i = 0; i < limit; i++)
        {
            uint addr = _candidateAddresses[i];
            uint curVal = ReadRamValue(currentRam, addr, _activeWidth);
            _candidatePreviousValues.TryGetValue(addr, out uint prevVal);

            bool isFrozen = _frozenValues.ContainsKey(addr);
            string desc = GetKnownAddressDescription(addr);

            topList.Add(new RamMatchEntry
            {
                Address = addr,
                CurrentValue = curVal,
                PreviousValue = prevVal,
                Width = _activeWidth,
                IsFrozen = isFrozen,
                DescriptionRu = desc
            });
        }

        _matches = topList;
    }

    private static string GetKnownAddressDescription(uint address)
    {
        return address switch
        {
            0x00A0 => "Здоровье игрока (HP)",
            0x00A2 => "Количество жизней",
            0x00A4 => "Игровой счёт (Score)",
            0x00A8 => "Золото / Монеты",
            0x00AC => "Координата X персонажа",
            0x00AE => "Координата Y персонажа",
            _ => ""
        };
    }

    public bool PokeValue(uint address, RamValueWidth width, uint newValue)
    {
        byte[] bytes = width switch
        {
            RamValueWidth.Byte8 => new byte[] { (byte)newValue },
            RamValueWidth.UInt16 => BitConverter.GetBytes((ushort)newValue),
            RamValueWidth.UInt32 => BitConverter.GetBytes(newValue),
            _ => new byte[] { (byte)newValue }
        };

        bool written = false;
        try
        {
            written = NativeLibretroHost.Current.WriteSystemRamBytes(address, bytes);
        }
        catch { }

        // Also apply to simulated RAM
        lock (_simulatedRam)
        {
            if (address + (uint)bytes.Length <= _simulatedRam.Length)
            {
                Buffer.BlockCopy(bytes, 0, _simulatedRam, (int)address, bytes.Length);
                written = true;
            }
        }

        lock (_scanLock)
        {
            var match = _matches.FirstOrDefault(m => m.Address == address);
            if (match != null)
            {
                match.PreviousValue = match.CurrentValue;
                match.CurrentValue = newValue;
            }
            if (_candidatePreviousValues.ContainsKey(address))
            {
                _candidatePreviousValues[address] = newValue;
            }
        }

        return written;
    }

    public void ToggleFreeze(RamMatchEntry entry, uint lockedValue)
    {
        if (entry == null) return;

        if (_frozenValues.ContainsKey(entry.Address))
        {
            _frozenValues.TryRemove(entry.Address, out _);
            entry.IsFrozen = false;
        }
        else
        {
            _frozenValues[entry.Address] = (entry.Width, lockedValue);
            entry.IsFrozen = true;
            entry.CurrentValue = lockedValue;
            PokeValue(entry.Address, entry.Width, lockedValue);
        }
    }

    public CheatItem ExportMatchToCheatItem(RamMatchEntry entry, string title)
    {
        if (entry == null) throw new ArgumentNullException(nameof(entry));

        string hexAddr = entry.Address.ToString("X6");
        string hexVal = entry.Width switch
        {
            RamValueWidth.Byte8 => entry.CurrentValue.ToString("X2"),
            RamValueWidth.UInt16 => entry.CurrentValue.ToString("X4"),
            RamValueWidth.UInt32 => entry.CurrentValue.ToString("X8"),
            _ => entry.CurrentValue.ToString("X2")
        };

        string cleanTitle = string.IsNullOrWhiteSpace(title)
            ? (!string.IsNullOrWhiteSpace(entry.DescriptionRu) ? entry.DescriptionRu : $"RAM 0x{hexAddr}")
            : title.Trim();

        return new CheatItem
        {
            Index = 1,
            Description = cleanTitle,
            Code = $"{hexAddr}:{hexVal}",
            CheatType = "RAM Патч",
            IsEnabled = true
        };
    }

    /// <summary>
    /// Executes every frame in NativeLibretroHost.EmulationLoop to sustain frozen values with 0 latency.
    /// </summary>
    public void ApplyFrozenValuesTick()
    {
        if (_frozenValues.IsEmpty) return;

        foreach (var kvp in _frozenValues)
        {
            uint addr = kvp.Key;
            var (width, val) = kvp.Value;

            byte[] bytes = width switch
            {
                RamValueWidth.Byte8 => new byte[] { (byte)val },
                RamValueWidth.UInt16 => BitConverter.GetBytes((ushort)val),
                RamValueWidth.UInt32 => BitConverter.GetBytes(val),
                _ => new byte[] { (byte)val }
            };

            try
            {
                NativeLibretroHost.Current.WriteSystemRamBytes(addr, bytes);
            }
            catch { }

            lock (_simulatedRam)
            {
                if (addr + (uint)bytes.Length <= _simulatedRam.Length)
                {
                    Buffer.BlockCopy(bytes, 0, _simulatedRam, (int)addr, bytes.Length);
                }
            }
        }
    }

    private static uint ReadRamValue(byte[] buffer, uint address, RamValueWidth width)
    {
        int addr = (int)address;
        return width switch
        {
            RamValueWidth.Byte8 => buffer[addr],
            RamValueWidth.UInt16 => (uint)(buffer[addr] | (buffer[addr + 1] << 8)),
            RamValueWidth.UInt32 => (uint)(buffer[addr] | (buffer[addr + 1] << 8) | (buffer[addr + 2] << 16) | (buffer[addr + 3] << 24)),
            _ => buffer[addr]
        };
    }
}
