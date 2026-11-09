namespace NamakeKaigi.Models;

/// <summary>AIに任せる段階(読むだけ / 一部できる / 何でもできる)の選択肢1つ。</summary>
/// <param name="Id">識別名(read / partial / full)</param>
/// <param name="Label">画面に出す名前</param>
public sealed record PermissionLevelOption(string Id, string Label)
{
    /// <summary>読むだけ(ファイルを読むだけ。書き換え・コマンドはしない)。</summary>
    public const string ReadId = "read";

    /// <summary>一部できる(「ツール・許可の設定」で選んだ道具と、許可したコマンドだけ)。</summary>
    public const string PartialId = "partial";

    /// <summary>何でもできる(普段のCLIと同じく、道具はすべて。許可は自動判定)。</summary>
    public const string FullId = "full";

    private static readonly IReadOnlyList<PermissionLevelOption> ThreeLevels = new[]
    {
        new PermissionLevelOption(ReadId, "読む"),
        new PermissionLevelOption(PartialId, "読み書き"),
        new PermissionLevelOption(FullId, "全部"),
    };

    private static readonly IReadOnlyList<PermissionLevelOption> TwoLevels = new[]
    {
        new PermissionLevelOption(ReadId, "読む"),
        new PermissionLevelOption(PartialId, "読み書き"),
    };

    private static readonly IReadOnlyList<PermissionLevelOption> EditThreeLevels = new[]
    {
        new PermissionLevelOption(ReadId, "読む"),
        new PermissionLevelOption(PartialId, "読み書き"),
        new PermissionLevelOption(FullId, "全部"),
    };

    /// <summary>AIが選べる段階の一覧を返す。</summary>
    /// <param name="canFull">「全部」にできるAIか(できないAIは、読むだけ / 書き換えできる の2段階)</param>
    /// <param name="partialIsEditOnly">真ん中の段階が、道具を選べず「書き換えだけ」のAIか(Gemini)。真なら「読み書き」と呼ぶ</param>
    /// <returns>選択肢の一覧</returns>
    public static IReadOnlyList<PermissionLevelOption> For(bool canFull, bool partialIsEditOnly = false) =>
        canFull ? (partialIsEditOnly ? EditThreeLevels : ThreeLevels) : TwoLevels;
}
