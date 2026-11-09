namespace NamakeKaigi.Models;

/// <summary>リモート(スマホのブラウザから会議に参加する)機能の設定。既定はオフ。</summary>
/// <param name="Enabled">リモート機能を使うか(省略時はfalse)</param>
/// <param name="Port">待ち受けるポート番号(省略時は8765)</param>
/// <param name="Token">合言葉(アクセスキー)。空のまま有効にすると、起動時に自動で作って保存する</param>
/// <param name="BindAddress">待ち受けるIPアドレス。空ならTailscaleのアドレスを自動で探し、なければ自分のPC内(127.0.0.1)だけにする</param>
/// <param name="MaxMessages">スマホへ返す発言の最大件数(新しい順に数える。省略時は60)</param>
/// <param name="MaxTextChars">スマホから送れる発言の最大文字数(省略時は4000)</param>
/// <param name="PollMs">スマホのページが、状態を取りに来る間隔(ミリ秒。省略時は2000)</param>
/// <param name="FailDelayMs">合言葉を間違えたときの返答を遅らせる時間(ミリ秒。総当たりを遅くする。省略時は500)</param>
public sealed record RemoteSettings(
    bool Enabled = false,
    int Port = 8765,
    string? Token = null,
    string? BindAddress = null,
    int MaxMessages = 60,
    int MaxTextChars = 4000,
    int PollMs = 2000,
    int FailDelayMs = 500);
