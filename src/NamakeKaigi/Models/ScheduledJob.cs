namespace NamakeKaigi.Models;

/// <summary>定期実行する予約1件(時刻になったら、新しい会議室で、決まった相手にプロンプトを送る)。</summary>
/// <param name="Id">予約の識別名(履歴に残す。重複しない短い英数字)</param>
/// <param name="Name">画面に出す名前(会議室の名前にもなる)</param>
/// <param name="Cron">実行する時刻のcron式(分 時 日 月 曜日。例: 0 0,2 * * *)</param>
/// <param name="Agent">送る相手の識別名(claude / gemini / chappy)</param>
/// <param name="Level">任せる段階(read=読むだけ / partial=一部できる・書き換えできる / full=何でもできる)</param>
/// <param name="Prompt">送る本文(普段チャットに打つ文と同じ)</param>
/// <param name="Enabled">動かすか(falseなら時刻になっても動かない)</param>
/// <param name="Model">使うモデル。省略(null)=ふだんの設定のまま / 空文字=指定なし(CLIの既定) / モデル名=そのモデル。この予約の会議室だけで使い、ふだんの設定は書き換えない</param>
public sealed record ScheduledJob(string Id, string Name, string Cron, string Agent, string Level, string Prompt,
    bool Enabled = true, string? Model = null);

/// <summary>予約の保存ファイル(schedule.json)の中身。</summary>
/// <param name="Jobs">予約の一覧</param>
/// <param name="TickSec">時刻を見に行く間隔(秒)</param>
/// <param name="GraceMinutes">予定の時刻から、この分数までの遅れなら実行する(それより遅れたら見送る。スリープ明けなど)</param>
/// <param name="HistoryFile">実行の記録(1行1件のJSON)を書くファイルのパス(省略時は schedule.json と同じ場所の schedule-history.jsonl)</param>
public sealed record ScheduleFile(IReadOnlyList<ScheduledJob> Jobs, int TickSec = 30, int GraceMinutes = 5,
    string? HistoryFile = null)
{
    /// <summary>予約が1件もない状態。</summary>
    public static ScheduleFile Empty => new(Array.Empty<ScheduledJob>());
}

/// <summary>予約1回ぶんの実行結果(履歴に1行で残す)。</summary>
/// <param name="JobId">予約の識別名</param>
/// <param name="JobName">予約の名前</param>
/// <param name="ScheduledAt">予定の時刻(手動の「今すぐ実行」ならその時刻)</param>
/// <param name="StartedAt">実際に始めた時刻</param>
/// <param name="FinishedAt">終わった時刻</param>
/// <param name="Result">結果(ok=返答が届いた / error=エラーだった / skipped=見送った)</param>
/// <param name="Note">補足(エラーの内容、見送った理由など)</param>
/// <param name="Manual">手動の「今すぐ実行」ならtrue</param>
/// <param name="LogPath">その会議のログのパス(開ければ、何を話したか分かる。見送りなら空)</param>
public sealed record ScheduleRunRecord(string JobId, string JobName, DateTime ScheduledAt, DateTime StartedAt,
    DateTime FinishedAt, string Result, string Note, bool Manual, string LogPath)
{
    /// <summary>画面に出す結果の言葉(履歴ファイルには書かない)。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string ResultLabel => Result switch
    {
        Ok => "✔ 返答あり",
        Error => "✖ エラー",
        _ => "– 見送り",
    };

    /// <summary>結果: 返答が届いた。</summary>
    public const string Ok = "ok";

    /// <summary>結果: エラーだった。</summary>
    public const string Error = "error";

    /// <summary>結果: 見送った。</summary>
    public const string Skipped = "skipped";
}
