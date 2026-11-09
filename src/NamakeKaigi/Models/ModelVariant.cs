using System.Text.RegularExpressions;

namespace NamakeKaigi.Models;

/// <summary>
/// 「モデル名 (段階)」の形のモデル1つを、モデル名と段階に分けたもの(agy の一覧は、モデル×段階の組み合わせで並ぶため)。
/// </summary>
/// <param name="ModelId">CLIの --model に渡す名前(例: gemini-3.8-flash-medium)</param>
/// <param name="Family">モデル名(例: Gemini 3.8 Flash)</param>
/// <param name="LevelId">段階の識別名(例: medium)</param>
public sealed record ModelVariant(string ModelId, string Family, string LevelId)
{
    // 表示名の末尾の「(段階)」を取り出す。例: "Gemini 3.8 Flash (Medium)"
    private static readonly Regex LabelPattern = new(@"^(?<family>.+?)\s*\((?<level>[^()]+)\)\s*$");

    /// <summary>段階の並び順(低→高)。知らない段階は後ろに回す。</summary>
    private static readonly string[] LevelOrder = { "low", "medium", "high" };

    /// <summary>モデル1つを、モデル名と段階に分ける。</summary>
    /// <param name="option">モデル</param>
    /// <returns>分けた結果。表示名が「名前 (段階)」の形でなければ null</returns>
    public static ModelVariant? Parse(ModelOption option)
    {
        var m = LabelPattern.Match(option.Label);
        if (!m.Success) return null;
        return new ModelVariant(option.Id, m.Groups["family"].Value.Trim(), m.Groups["level"].Value.Trim().ToLowerInvariant());
    }

    /// <summary>一覧の全部を分ける。1つでも分けられなければ、空を返す(そのときは、分けずに1つの欄で選ぶ)。</summary>
    /// <param name="options">モデルの一覧</param>
    /// <returns>分けた結果の一覧(元の並び順のまま)</returns>
    public static IReadOnlyList<ModelVariant> SplitAll(IEnumerable<ModelOption> options)
    {
        var result = new List<ModelVariant>();
        foreach (var o in options)
        {
            if (Parse(o) is not { } v) return Array.Empty<ModelVariant>();
            result.Add(v);
        }
        return result;
    }

    /// <summary>あるモデル名で選べる段階を、低→高の順で返す。</summary>
    /// <param name="variants">分けた一覧</param>
    /// <param name="family">モデル名</param>
    /// <returns>段階の一覧(画面に出す名前つき)</returns>
    public static IReadOnlyList<EffortOption> LevelsOf(IEnumerable<ModelVariant> variants, string family) =>
        variants.Where(v => v.Family == family)
            .Select(v => v.LevelId).Distinct()
            .OrderBy(id => Array.IndexOf(LevelOrder, id) is var i && i >= 0 ? i : LevelOrder.Length)
            .Select(id => new EffortOption(id, LevelLabel(id)))
            .ToList();

    /// <summary>段階の識別名を、画面に出す短い名前にする(低/中/高。知らないものは、そのまま)。</summary>
    /// <param name="levelId">段階の識別名</param>
    /// <returns>画面に出す名前</returns>
    public static string LevelLabel(string levelId) => levelId switch
    {
        "low" => "低",
        "medium" => "中",
        "high" => "高",
        _ => levelId,
    };
}
