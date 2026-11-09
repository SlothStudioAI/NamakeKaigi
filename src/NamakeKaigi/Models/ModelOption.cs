namespace NamakeKaigi.Models;

/// <summary>選べるモデル1つ。</summary>
/// <param name="Id">CLIの --model に渡す名前</param>
/// <param name="Label">画面に出す名前(分からなければIdと同じ)</param>
public sealed record ModelOption(string Id, string Label);

/// <summary>選べる労力(エフォート)1つ。Claude CLI・agy ともに --effort に渡す。</summary>
/// <param name="Id">CLIの --effort に渡す値(low / medium / high / xhigh / max)</param>
/// <param name="Label">画面に出す短い名前</param>
public sealed record EffortOption(string Id, string Label)
{
    /// <summary>既定の労力(中)。</summary>
    public const string DefaultId = "medium";

    /// <summary>画面で選べる5段階の労力一覧。</summary>
    public static IReadOnlyList<EffortOption> All { get; } = new[]
    {
        new EffortOption("low", "低"),
        new EffortOption("medium", "中"),
        new EffortOption("high", "高"),
        new EffortOption("xhigh", "超高"),
        new EffortOption("max", "最大"),
    };
}
