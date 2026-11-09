namespace NamakeKaigi.Models;

/// <summary>メンバー名簿の設定。定義ファイルを探す場所と、探し方の上限。</summary>
/// <param name="Roots">探し始めるフォルダ(省略・空なら作業フォルダ)</param>
/// <param name="ExcludeDirs">中に入らないフォルダ名(省略時は DefaultExcludeDirs)</param>
/// <param name="MaxDepth">起点から何階層下まで探すか(省略時は6)</param>
/// <param name="IncludeUserDirs">個人用の置き場所(~/.claude/agents、~/.agents/skills、~/.codex/agents)も探すか(省略時は探す)</param>
public sealed record RosterSettings(
    IReadOnlyList<string>? Roots = null,
    IReadOnlyList<string>? ExcludeDirs = null,
    int MaxDepth = 6,
    bool IncludeUserDirs = true)
{
    /// <summary>既定の、中に入らないフォルダ名(退避したゴミ箱・ビルド出力など)。</summary>
    public static readonly IReadOnlyList<string> DefaultExcludeDirs =
        new[] { ".git", "node_modules", "bin", "obj", "_ゴミ箱" };

    /// <summary>探し始めるフォルダを決める。設定になければ作業フォルダを使う。</summary>
    /// <param name="workDir">作業フォルダ</param>
    /// <returns>探し始めるフォルダの一覧</returns>
    public IReadOnlyList<string> ResolveRoots(string workDir) =>
        Roots is { Count: > 0 } ? Roots : new[] { workDir };
}
