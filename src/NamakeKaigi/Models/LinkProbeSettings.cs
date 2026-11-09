namespace NamakeKaigi.Models;

/// <summary>返答の中のパスが実在するかを調べるときの設定。切断されたネットワークドライブなどで、画面が固まらないようにする。</summary>
/// <param name="TimeoutMs">ネットワーク・リムーバブルなどのドライブへの問い合わせを待つ長さ(ミリ秒)。省略時は300。これを超えたドライブは、しばらく調べない</param>
/// <param name="CacheSec">調べた結果を覚えておく長さ(秒)。流れている返答を200msごとに作り直しても、同じパスを何度も調べないため。省略時は5</param>
/// <param name="SlowDriveSec">時間切れになったドライブを、調べずに「リンクではない」とする長さ(秒)。省略時は60</param>
public sealed record LinkProbeSettings(
    int TimeoutMs = 300,
    int CacheSec = 5,
    int SlowDriveSec = 60);
