using System;
using System.Net.Sockets;
using System.Text;

namespace StormRetroArch;

/// <summary>
/// Libretro UDP Network Command Bridge.
/// Communicates in real-time with the embedded RetroArch engine on 127.0.0.1:55355.
/// </summary>
public static class UdpCommandBridge
{
    public const int DefaultPort = 55355;

    public static void Send(string command)
    {
        try
        {
            using var client = new UdpClient();
            byte[] bytes = Encoding.UTF8.GetBytes(command.Trim() + "\n");
            client.Send(bytes, bytes.Length, "127.0.0.1", DefaultPort);
        }
        catch { }
    }

    public static void PauseToggle() => Send("PAUSE_TOGGLE");
    public static void SaveState() => Send("SAVE_STATE");
    public static void LoadState() => Send("LOAD_STATE");
    public static void Rewind() => Send("REWIND");
    public static void FastForward() => Send("FAST_FORWARD");
    public static void ShaderNext() => Send("SHADER_NEXT");
    public static void Screenshot() => Send("SCREENSHOT");
    public static void Reset() => Send("RESET");
    public static void VolumeUp() => Send("VOLUME_UP");
    public static void VolumeDown() => Send("VOLUME_DOWN");
    public static void Mute() => Send("MUTE");
    public static void FullscreenToggle() => Send("FULLSCREEN_TOGGLE");
    public static void Quit() => Send("QUIT");

    // Disc Eject / Swap commands
    public static void DiskEjectToggle() => Send("DISK_EJECT_TOGGLE");
    public static void DiskNext() => Send("DISK_NEXT");
    public static void DiskPrev() => Send("DISK_PREV");

    public static void EnsureNetworkCommandConfig(string retroArchDir)
    {
        try
        {
            RetroArchConfigManager.SetBool("network_cmd_enable", true);
            RetroArchConfigManager.SetInt("network_cmd_port", DefaultPort);
            RetroArchConfigManager.Save();
        }
        catch { }
    }
}
