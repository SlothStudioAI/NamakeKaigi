using System.Diagnostics;
using System.IO;

namespace NamakeKaigi.Services;

/// <summary>クリックされたリンクを開く。実行できるファイルは、実行せず、エクスプローラーで場所を示すだけにする(WBS 11.32)。</summary>
public static class LinkOpener
{
    /// <summary>リンクを開く。開けないとき(消えた・権限がない)は、何もしない。</summary>
    /// <param name="target">URL またはフルパス</param>
    public static void Open(string target)
    {
        try
        {
            switch (LinkDetector.Classify(target))
            {
                case LinkKind.Web:
                case LinkKind.Folder:
                case LinkKind.File:
                    Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                    break;
                case LinkKind.RevealFile:
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{target}\"") { UseShellExecute = true });
                    break;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            Debug.WriteLine($"リンクを開けませんでした: {target} ({ex.Message})");
        }
    }
}
