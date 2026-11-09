namespace NamakeKaigi.Models;

/// <summary>返答の読み上げの設定(settings.json の voice)。</summary>
/// <param name="VoiceName">使う声の名前(例: Microsoft Haruka Desktop)。省略・空なら、日本語の声のうち最初に見つかったもの</param>
/// <param name="Rate">読む速さ(-10〜10。0が標準、大きいほど速い)</param>
/// <param name="MaxChars">1回の返答で読む文字数の上限(超えたら、途中までを読んで「続きは画面で見てください」で終える。0以下なら全部読む)</param>
/// <param name="AutoRead">起動したときに、読み上げをONにするか(省略時はfalse)</param>
/// <param name="Engine">声の元(windows=Windows標準、voicevox=VOICEVOX。省略時は windows)</param>
/// <param name="Voicevox">VOICEVOXの設定(Engine が voicevox のときだけ使う。省略時は標準)</param>
/// <param name="Enabled">読み上げの機能を使うか(省略時はfalse)。false のときは、声の部品を作らず、画面の読み上げの行・吹き出しの🔊・止めるボタンも出さない(公開版では引き算。WBS 11.62)</param>
public sealed record VoiceSettings(string? VoiceName = null, int Rate = 0, int MaxChars = 400, bool AutoRead = false,
    string? Engine = null, VoicevoxSettings? Voicevox = null, bool Enabled = false);
