using System.Runtime.InteropServices;

namespace NamakeKaigi.Services;

/// <summary>Windows標準の音声入力(Win + H)を呼び出す。</summary>
public static class VoiceInputHelper
{
    private const byte VkLWin = 0x5B;
    private const byte VkH = 0x48;
    private const uint KeyeventfKeyup = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    /// <summary>Win + H キーを送って、Windows標準の音声入力バーを開く。</summary>
    public static void LaunchWindowsVoiceTyping()
    {
        keybd_event(VkLWin, 0, 0, UIntPtr.Zero);
        keybd_event(VkH, 0, 0, UIntPtr.Zero);
        keybd_event(VkH, 0, KeyeventfKeyup, UIntPtr.Zero);
        keybd_event(VkLWin, 0, KeyeventfKeyup, UIntPtr.Zero);
    }
}
