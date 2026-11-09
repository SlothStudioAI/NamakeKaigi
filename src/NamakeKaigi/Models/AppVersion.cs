using System.Reflection;

namespace NamakeKaigi.Models;

/// <summary>アプリのバージョン(正本は NamakeKaigi.csproj の Version)。</summary>
public static class AppVersion
{
    /// <summary>実行中のアプリのバージョン(例: 「0.9.0」)。取れなければ空。</summary>
    public static string Current { get; } = Normalize(
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>画面に出す形(例: 「v0.9.0」)。取れなければ空。</summary>
    public static string Label => ToLabel(Current);

    /// <summary>ビルド情報の「+」以降(コミットIDなど)を除いて、版の数字だけにする。</summary>
    /// <param name="raw">埋め込まれている版の文字列(null可)</param>
    /// <returns>版の数字(例: 「0.9.0」)。空・nullなら空</returns>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var plus = raw.IndexOf('+');
        return (plus >= 0 ? raw[..plus] : raw).Trim();
    }

    /// <summary>版の数字に「v」を付けた表示用の文字にする。</summary>
    /// <param name="version">版の数字(例: 「0.9.0」)</param>
    /// <returns>「v0.9.0」。空なら空</returns>
    public static string ToLabel(string version) => version.Length == 0 ? "" : "v" + version;
}
