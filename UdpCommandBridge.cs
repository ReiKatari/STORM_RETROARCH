using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace StormRetroArch;

public static class UdpCommandBridge
{
    public const int DefaultPort = 55355;
    public const string DefaultHost = "127.0.0.1";

    public static void EnsureNetworkCommandConfig()
    {
        try
        {
            RetroArchConfigManager.SetString("network_cmd_enable", "true");
            RetroArchConfigManager.SetString("network_cmd_port", DefaultPort.ToString());
            RetroArchConfigManager.Save();
        }
        catch { }
    }

    public static async Task<bool> SendCommandAsync(string command, int port = DefaultPort, string host = DefaultHost)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;

        try
        {
            using var client = new UdpClient();
            string msg = command.Trim() + "\n";
            byte[] bytes = Encoding.ASCII.GetBytes(msg);
            int sent = await client.SendAsync(bytes, bytes.Length, host, port);
            return sent > 0;
        }
        catch
        {
            return false;
        }
    }

    public static void SendCommand(string command, int port = DefaultPort, string host = DefaultHost)
    {
        _ = SendCommandAsync(command, port, host);
    }

    // Standard RetroArch Network Commands
    public static void PauseToggle() => SendCommand("PAUSE_TOGGLE");
    public static void SaveState() => SendCommand("SAVE_STATE");
    public static void LoadState() => SendCommand("LOAD_STATE");
    public static void SlotPlus() => SendCommand("STATE_SLOT_PLUS");
    public static void SlotMinus() => SendCommand("STATE_SLOT_MINUS");
    public static void TakeScreenshot() => SendCommand("SCREENSHOT");
    public static void ResetGame() => SendCommand("RESET");
    public static void FastForwardToggle() => SendCommand("FAST_FORWARD");
    public static void Rewind() => SendCommand("REWIND");
    public static void ShaderNext() => SendCommand("SHADER_NEXT");
    public static void ShaderPrev() => SendCommand("SHADER_PREV");
    public static void DiskEjectToggle() => SendCommand("DISK_EJECT_TOGGLE");
    public static void DiskNext() => SendCommand("DISK_NEXT");
    public static void DiskPrev() => SendCommand("DISK_PREV");
    public static void MuteAudio() => SendCommand("MUTE");
    public static void QuitRetroArch() => SendCommand("QUIT");
}
