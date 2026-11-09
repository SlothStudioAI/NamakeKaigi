namespace NamakeKaigi.ViewModels;

/// <summary>「全員」宛ての答える順番の選択肢1つ。</summary>
/// <param name="Id">順番の識別名(AllOrders の値)</param>
/// <param name="Label">画面に出す文言(「同時」「クローディ→ジェミリー」など)</param>
/// <param name="Description">選択欄のツールチップに出す説明</param>
public sealed record AllOrderOption(string Id, string Label, string Description);
