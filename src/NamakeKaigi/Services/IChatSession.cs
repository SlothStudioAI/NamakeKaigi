using NamakeKaigi.Models;

namespace NamakeKaigi.Services;

/// <summary>1回の会議の進行役(画面は、これだけを通して会議を動かす)。</summary>
public interface IChatSession
{
    /// <summary>会議ログのファイルパス。</summary>
    string LogPath { get; }

    /// <summary>会議の名前(log.md の1行目に書いてある名前。会議フォルダの名前にもなる。なければnull)。</summary>
    string? MeetingTitle => null;

    /// <summary>会議の名前を、会議ログの1行目に書く。発言の記録には触れない。</summary>
    /// <param name="title">会議の名前(空なら名前なし)</param>
    void SetMeetingTitle(string? title) { }

    /// <summary>会議フォルダを用意する(まだなければ作る)。発言の前に画像を貼ったときも、正しい会議フォルダに保存するために使う。</summary>
    /// <returns>会議フォルダのパス</returns>
    string EnsureMeetingFolder() => System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(LogPath)) ?? "";

    /// <summary>発言の先頭の呼びかけ(「ジェミ、〜」)で宛先を切り替える、呼び名の一覧。</summary>
    CallNameSettings CallNames => CallNameSettings.Default;

    /// <summary>過去の会議を開き直したときに復元された発言一覧(新規会議なら空)。</summary>
    IReadOnlyList<ChatEntry> RestoredEntries => Array.Empty<ChatEntry>();

    /// <summary>保存されている過去の会議一覧(新しい順)を返す。</summary>
    IReadOnlyList<SavedMeetingSummary> ListPastMeetings() => Array.Empty<SavedMeetingSummary>();

    /// <summary>指定した過去の会議フォルダを開き直して、新しい進行役を返す。</summary>
    /// <param name="folderPath">会議フォルダのパス</param>
    IChatSession? OpenPastMeeting(string folderPath) => null;

    /// <summary>AIごとの、モデル名の初期表示(識別名→表示)。</summary>
    IReadOnlyDictionary<string, string> ModelLabels { get; }

    /// <summary>そのAIに、いま選ばれているモデル。</summary>
    /// <param name="agentId">AIの識別名</param>
    /// <returns>CLIの --model に渡す名前(空なら指定なし)</returns>
    string ModelIdOf(string agentId);

    /// <summary>いまの作業フォルダ。</summary>
    string WorkDir => "";

    /// <summary>作業フォルダを切り替える(次の発言から、全員に効く)。</summary>
    /// <param name="workDir">作業フォルダ</param>
    void SetWorkDir(string workDir) { }

    /// <summary>作業フォルダの外にある添付ファイル・フォルダを読めるよう、追加フォルダ(--add-dir 用)を渡す。</summary>
    /// <param name="dirs">追加するフォルダの一覧</param>
    void SetExtraDirs(IReadOnlyList<string> dirs) { }

    /// <summary>そのAIが、ファイルを書き換える「書く担当」になれるか。</summary>
    /// <param name="agentId">AIの識別名</param>
    bool CanWrite(string agentId) => false;

    /// <summary>そのAIを「書く担当」にする・外す。次の発言から効く。作業が別々なら、両方を書く担当にしてよい。</summary>
    /// <param name="agentId">AIの識別名</param>
    /// <param name="isWriter">書く担当にするならtrue</param>
    void SetWriter(string agentId, bool isWriter) { }

    /// <summary>そのAIが、決まったコマンド(dotnet build など)も使える持ち物にできるか。</summary>
    /// <param name="agentId">AIの識別名</param>
    bool CanRunCommands(string agentId) => false;

    /// <summary>そのAIに、決まったコマンドも使わせる・やめる(書く担当のときだけ有効)。次の発言から効く。</summary>
    /// <param name="agentId">AIの識別名</param>
    /// <param name="enabled">使わせるならtrue</param>
    void SetCommands(string agentId, bool enabled) { }

    /// <summary>そのAIを「何でもできる」(道具をすべて使い、許可は自動判定)にできるか。</summary>
    /// <param name="agentId">AIの識別名</param>
    bool CanFull(string agentId) => false;

    /// <summary>そのAIを「何でもできる」にする・やめる(書く担当のときだけ有効)。次の発言から効く。</summary>
    /// <param name="agentId">AIの識別名</param>
    /// <param name="enabled">何でもできるにするならtrue</param>
    void SetFull(string agentId, bool enabled) { }

    /// <summary>そのAIの、いま有効なハーネス(ツール・許可コマンド)の説明文を返す。</summary>
    /// <param name="agentId">AIの識別名</param>
    string HarnessSummary(string agentId) => "";

    /// <summary>Claudeのいまの設定を返す(編集画面を開くため。なければnull)。</summary>
    ClaudeSettings? GetClaudeSettings() => null;

    /// <summary>Claudeでいま使える外部連携の一覧を取る(編集画面のチェック用。取れなければnull)。</summary>
    Task<IReadOnlyList<string>?> LoadClaudeConnectorsAsync() => Task.FromResult<IReadOnlyList<string>?>(null);

    /// <summary>Claudeのハーネス設定を更新し、保存する(次の発言から効く)。</summary>
    /// <param name="settings">新しい設定</param>
    void UpdateClaudeSettings(ClaudeSettings settings) { }

    /// <summary>agy(Gemini)のいまの設定を返す(編集画面を開くため。なければnull)。</summary>
    AgySettings? GetAgySettings() => null;

    /// <summary>agy(Gemini)のハーネス設定を更新し、保存する(次の発言から効く)。</summary>
    /// <param name="settings">新しい設定</param>
    void UpdateAgySettings(AgySettings settings) { }

    /// <summary>そのAIを書く担当にしたときに画面へ出す注意(なければ空)。</summary>
    /// <param name="agentId">AIの識別名</param>
    string WriterWarning(string agentId) => "";

    /// <summary>そのAIを「何でもできる」にしたときに画面へ出す注意(なければ空)。</summary>
    /// <param name="agentId">AIの識別名</param>
    string FullWarning(string agentId) => "";

    /// <summary>そのAIのモデルを切り替える(次の発言から効く)。</summary>
    /// <param name="agentId">AIの識別名</param>
    /// <param name="modelId">CLIの --model に渡す名前</param>
    void SetModel(string agentId, string modelId);

    /// <summary>
    /// そのAIのモデルを、この会議だけ切り替える(次の発言から効く)。設定ファイルには保存しない
    /// (ふだん使うモデルを書き換えないため。予約の実行用)。
    /// </summary>
    /// <param name="agentId">AIの識別名</param>
    /// <param name="modelId">CLIの --model に渡す名前(空なら指定なし)</param>
    void SetModelForThisMeeting(string agentId, string modelId) => SetModel(agentId, modelId);

    /// <summary>そのAIにいま選ばれている労力(low / medium / high / xhigh / max。空なら指定なし)。</summary>
    /// <param name="agentId">AIの識別名</param>
    string EffortIdOf(string agentId) => "";

    /// <summary>そのAIで、労力をモデルと別に選べるか。</summary>
    /// <param name="agentId">AIの識別名</param>
    bool SupportsEffort(string agentId) => true;

    /// <summary>そのAIの労力を切り替える(次の発言から効く)。</summary>
    /// <param name="agentId">AIの識別名</param>
    /// <param name="effortId">CLIの --effort に渡す値</param>
    void SetEffort(string agentId, string effortId) { }

    /// <summary>そのAIの、すぐ出せる選べるモデルの一覧。</summary>
    /// <param name="agentId">AIの識別名</param>
    /// <returns>モデルの一覧</returns>
    IReadOnlyList<ModelOption> ModelOptionsFor(string agentId);

    /// <summary>そのAIの、選べるモデルの一覧をCLIに聞いて最新にする。</summary>
    /// <param name="agentId">AIの識別名</param>
    /// <returns>モデルの一覧</returns>
    Task<IReadOnlyList<ModelOption>> LoadModelOptionsAsync(string agentId);

    /// <summary>ユーザーの発言を送り、宛先のAIからの途中経過と結果を、届くたびに通知する。</summary>
    /// <param name="text">ユーザーの発言</param>
    /// <param name="targetIds">宛先のAIの識別名</param>
    /// <param name="onProgress">途中経過・結果が届くたびに呼ぶ(呼び出し元の文脈=画面のスレッドで呼ばれる)</param>
    /// <returns>全員の返答が済んだら完了</returns>
    Task SendAsync(string text, IReadOnlyList<string> targetIds, Action<ChatProgress> onProgress);

    /// <summary>ユーザーの発言を送り、宛先のAIからの途中経過と結果を、届くたびに通知する(発言ごとの追加フォルダ指定付き)。</summary>
    /// <param name="text">ユーザーの発言</param>
    /// <param name="targetIds">宛先のAIの識別名</param>
    /// <param name="onProgress">途中経過・結果が届くたびに呼ぶ</param>
    /// <param name="extraDirs">この発言で作業フォルダの外も読めるよう追加するフォルダ</param>
    /// <returns>全員の返答が済んだら完了</returns>
    Task SendAsync(string text, IReadOnlyList<string> targetIds, Action<ChatProgress> onProgress,
        IReadOnlyList<string>? extraDirs) => SendAsync(text, targetIds, onProgress);

    /// <summary>いま実行中のAIの返答待ちをすべてキャンセル(停止)する。</summary>
    void CancelPending() { }

    /// <summary>会議を閉じる(返答待ちを止め、起動したままのCLIを終わらせる)。</summary>
    void Shutdown() { }

    /// <summary>いま選ばれている配色テーマID。</summary>
    string ThemeId => ThemeCatalog.DefaultThemeId;

    /// <summary>配色テーマを切り替え、設定に保存する。</summary>
    /// <param name="themeId">テーマID</param>
    void SetTheme(string themeId) { }

    /// <summary>CLI1回の制限時間(秒)。</summary>
    int TimeoutSec => 0;

    /// <summary>CLI1回の制限時間を変える(両方のAIの次の発言から効き、settings.json にも保存する)。</summary>
    /// <param name="timeoutSec">制限時間(秒)</param>
    void SetTimeout(int timeoutSec) { }

    /// <summary>「全員」宛ての答える順番(AllOrders の値)。</summary>
    string AllOrder => AllOrders.Parallel;

    /// <summary>「全員」宛ての答える順番を変える(次の発言から効き、settings.json にも保存する)。</summary>
    /// <param name="order">AllOrders の値</param>
    void SetAllOrder(string order) { }

    /// <summary>意見を頼むときの頼み書き(settings.json の relay)。</summary>
    RelaySettings Relay => new();

    /// <summary>いまの簡易表示モード・表示名の設定。</summary>
    AppearanceSettings Appearance => AppearanceSettings.Default;

    /// <summary>簡易表示モード・表示名の設定を更新し、settings.json にも保存する。</summary>
    /// <param name="appearance">新しい設定</param>
    void SetAppearance(AppearanceSettings appearance) { }
}
