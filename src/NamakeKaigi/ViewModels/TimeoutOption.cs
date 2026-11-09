namespace NamakeKaigi.ViewModels;

/// <summary>制限時間の選択肢1つ分。</summary>
/// <param name="Seconds">制限時間(秒)</param>
public sealed record TimeoutOption(int Seconds)
{
    /// <summary>画面に出す文言(「⏱ 10分」「⏱ 90秒」など)。</summary>
    public string Label => Seconds % 60 == 0 ? $"⏱ {Seconds / 60}分" : $"⏱ {Seconds}秒";
}
