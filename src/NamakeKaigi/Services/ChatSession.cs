using NamakeKaigi.Models;

namespace NamakeKaigi.Services;

/// <summary>会議の進行。未読発言を添えて各AIに聞き、会話IDを引き継ぎ、ログと状態ファイルを保存する。</summary>
public sealed class ChatSession : IChatSession
{
    private readonly Dictionary<string, IChatAgent> _agents;
    private readonly Dictionary<string, SemaphoreSlim> _gates = new();
    private readonly Dictionary<string, string> _conversationIds = new();
    private readonly MeetingTranscript _transcript = new();
    private readonly MarkdownLogWriter _log;
    private readonly int _maxInjectChars;
    private readonly Func<DateTime> _clock;
    private readonly object _notifyLock = new();
    private readonly object _stateLock = new();
    private readonly Action<string>? _onWorkDirChanged;
    private readonly Action<ClaudeSettings>? _onClaudeHarnessChanged;
    private readonly Action<AgySettings>? _onAgyHarnessChanged;
    private readonly Action<string, string, string>? _onModelOrEffortChanged;
    private readonly Action<string>? _onThemeChanged;
    private readonly Action<AppearanceSettings>? _onAppearanceChanged;
    private readonly Func<IReadOnlyList<SavedMeetingSummary>>? _listPastMeetings;
    private readonly Func<string, IChatSession>? _openPastMeeting;
    private readonly List<CancellationTokenSource> _activeCts = new();

    /// <summary>会議を始める。</summary>
    /// <param name="agents">参加するAI</param>
    /// <param name="log">ログの保存先</param>
    /// <param name="maxInjectChars">未読発言を添える上限(文字)</param>
    /// <param name="clock">現在時刻の取得(試験で差し替える。省略時は現在時刻)</param>
    /// <param name="workDir">最初の作業フォルダ(表示用)</param>
    /// <param name="onWorkDirChanged">作業フォルダが切り替わったときに呼ぶ処理</param>
    /// <param name="onClaudeHarnessChanged">Claudeのハーネス設定が変更されたときに呼ぶ処理</param>
    /// <param name="onAgyHarnessChanged">agyのハーネス設定が変更されたときに呼ぶ処理</param>
    /// <param name="restoredState">過去の会議を開き直したときの状態(新規ならnull)</param>
    /// <param name="listPastMeetings">過去の会議一覧を取る処理</param>
    /// <param name="openPastMeeting">過去の会議を開き直す処理</param>
    /// <param name="onModelOrEffortChanged">モデルまたは労力が変更されたときに呼ぶ処理(agentId, modelId, effortId)</param>
    /// <param name="themeId">初期の配色テーマID</param>
    /// <param name="onThemeChanged">配色テーマが変更されたときに呼ぶ処理</param>
    /// <param name="appearance">初期の簡易表示モード・表示名設定</param>
    /// <param name="onAppearanceChanged">簡易表示モード・表示名設定が変更されたときに呼ぶ処理</param>
    /// <param name="timeoutSec">CLI1回の制限時間(秒)</param>
    /// <param name="onTimeoutChanged">制限時間が変更されたときに呼ぶ処理</param>
    /// <param name="allOrder">「全員」宛ての答える順番(AllOrders の値)</param>
    /// <param name="onAllOrderChanged">答える順番が変更されたときに呼ぶ処理</param>
    /// <param name="relay">順番のとき、2人目に添える頼み書き(省略時は標準の文)</param>
    /// <param name="callNames">呼びかけで宛先を切り替える呼び名の一覧(省略時は標準)</param>
    public ChatSession(IEnumerable<IChatAgent> agents, MarkdownLogWriter log, int maxInjectChars,
        Func<DateTime>? clock = null, string workDir = "", Action<string>? onWorkDirChanged = null,
        Action<ClaudeSettings>? onClaudeHarnessChanged = null, Action<AgySettings>? onAgyHarnessChanged = null,
        MeetingState? restoredState = null,
        Func<IReadOnlyList<SavedMeetingSummary>>? listPastMeetings = null,
        Func<string, IChatSession>? openPastMeeting = null,
        Action<string, string, string>? onModelOrEffortChanged = null,
        string themeId = ThemeCatalog.DefaultThemeId,
        Action<string>? onThemeChanged = null,
        AppearanceSettings? appearance = null,
        Action<AppearanceSettings>? onAppearanceChanged = null,
        int timeoutSec = 0,
        Action<int>? onTimeoutChanged = null,
        string allOrder = AllOrders.Parallel,
        Action<string>? onAllOrderChanged = null,
        RelaySettings? relay = null,
        CallNameSettings? callNames = null)
    {
        CallNames = callNames ?? CallNameSettings.Default;
        WorkDir = workDir;
        AllOrder = AllOrders.Normalize(allOrder);
        _onAllOrderChanged = onAllOrderChanged;
        _relay = relay ?? new RelaySettings();
        TimeoutSec = timeoutSec;
        _onTimeoutChanged = onTimeoutChanged;
        ThemeId = ThemeCatalog.GetOrDefault(themeId).Id;
        Appearance = appearance ?? AppearanceSettings.Default;
        _onWorkDirChanged = onWorkDirChanged;
        _onClaudeHarnessChanged = onClaudeHarnessChanged;
        _onAgyHarnessChanged = onAgyHarnessChanged;
        _onModelOrEffortChanged = onModelOrEffortChanged;
        _onThemeChanged = onThemeChanged;
        _onAppearanceChanged = onAppearanceChanged;
        _listPastMeetings = listPastMeetings;
        _openPastMeeting = openPastMeeting;
        _agents = agents.ToDictionary(a => a.Id);
        foreach (var id in _agents.Keys) _gates[id] = new SemaphoreSlim(1, 1);
        _log = log;
        _maxInjectChars = maxInjectChars;
        _clock = clock ?? (() => DateTime.Now);

        if (restoredState is not null)
        {
            RestoredEntries = restoredState.Entries;
            _transcript.Restore(restoredState.Entries, restoredState.SeenPositions);
            foreach (var (id, convId) in restoredState.ConversationIds)
            {
                if (!string.IsNullOrWhiteSpace(convId)) _conversationIds[id] = convId;
            }
            foreach (var (id, modelId) in restoredState.Models)
            {
                if (_agents.TryGetValue(id, out var a) && !string.IsNullOrWhiteSpace(modelId)) a.SetModel(modelId);
            }
            foreach (var (id, effortId) in restoredState.Efforts)
            {
                if (_agents.TryGetValue(id, out var a) && !string.IsNullOrWhiteSpace(effortId)) a.SetEffort(effortId);
            }
        }
    }

    /// <inheritdoc />
    public string LogPath => _log.Path;

    /// <inheritdoc />
    public string? MeetingTitle => _log.Title;

    /// <inheritdoc />
    public void SetMeetingTitle(string? title) => _log.SetTitle(title);

    /// <inheritdoc />
    public string EnsureMeetingFolder() => _log.EnsureFolder();

    /// <inheritdoc />
    public IReadOnlyList<ChatEntry> RestoredEntries { get; } = Array.Empty<ChatEntry>();

    /// <inheritdoc />
    public IReadOnlyList<SavedMeetingSummary> ListPastMeetings() =>
        _listPastMeetings?.Invoke() ?? Array.Empty<SavedMeetingSummary>();

    /// <inheritdoc />
    public IChatSession? OpenPastMeeting(string folderPath) =>
        _openPastMeeting?.Invoke(folderPath);

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> ModelLabels =>
        _agents.ToDictionary(a => a.Key, a => a.Value.ModelLabel);

    /// <inheritdoc />
    public string WorkDir { get; private set; } = "";

    /// <inheritdoc />
    public string ThemeId { get; private set; } = ThemeCatalog.DefaultThemeId;

    /// <inheritdoc />
    public int TimeoutSec { get; private set; }

    /// <inheritdoc />
    public void SetTimeout(int timeoutSec)
    {
        if (timeoutSec <= 0 || timeoutSec == TimeoutSec) return;
        TimeoutSec = timeoutSec;
        foreach (var agent in _agents.Values) agent.SetTimeout(TimeSpan.FromSeconds(timeoutSec));
        _onTimeoutChanged?.Invoke(timeoutSec);
    }

    private readonly Action<int>? _onTimeoutChanged;

    /// <inheritdoc />
    public CallNameSettings CallNames { get; }

    /// <inheritdoc />
    public string AllOrder { get; private set; }

    /// <inheritdoc />
    public void SetAllOrder(string order)
    {
        var normalized = AllOrders.Normalize(order);
        if (normalized == AllOrder) return;
        AllOrder = normalized;
        _onAllOrderChanged?.Invoke(normalized);
    }

    private readonly Action<string>? _onAllOrderChanged;
    private readonly RelaySettings _relay;

    /// <inheritdoc />
    public RelaySettings Relay => _relay;

    /// <inheritdoc />
    public void SetTheme(string themeId)
    {
        ThemeId = ThemeCatalog.GetOrDefault(themeId).Id;
        _onThemeChanged?.Invoke(ThemeId);
    }

    /// <inheritdoc />
    public AppearanceSettings Appearance { get; private set; } = AppearanceSettings.Default;

    /// <inheritdoc />
    public void SetAppearance(AppearanceSettings appearance)
    {
        Appearance = appearance;
        _onAppearanceChanged?.Invoke(appearance);
    }

    /// <inheritdoc />
    public void SetWorkDir(string workDir)
    {
        WorkDir = workDir;
        foreach (var agent in _agents.Values) agent.SetWorkDir(workDir);
        _onWorkDirChanged?.Invoke(workDir);
        SaveState();
    }

    /// <inheritdoc />
    public void SetExtraDirs(IReadOnlyList<string> dirs)
    {
        foreach (var agent in _agents.Values) agent.SetExtraDirs(dirs);
    }

    /// <inheritdoc />
    public bool CanWrite(string agentId) => _agents[agentId].CanWrite;

    /// <inheritdoc />
    public void SetWriter(string agentId, bool isWriter) => _agents[agentId].WriteMode = isWriter;

    /// <inheritdoc />
    public bool CanRunCommands(string agentId) => _agents[agentId].CanRunCommands;

    /// <inheritdoc />
    public void SetCommands(string agentId, bool enabled) => _agents[agentId].CommandMode = enabled;

    /// <inheritdoc />
    public bool CanFull(string agentId) => _agents[agentId].CanFull;

    /// <inheritdoc />
    public void SetFull(string agentId, bool enabled) => _agents[agentId].FullMode = enabled;

    /// <inheritdoc />
    public string HarnessSummary(string agentId) =>
        _agents.TryGetValue(agentId, out var a) ? a.HarnessSummary : "";

    /// <inheritdoc />
    public ClaudeSettings? GetClaudeSettings() =>
        _agents.TryGetValue(Participants.ClaudeId, out var a) && a is ClaudeAgent c ? c.Settings : null;

    /// <inheritdoc />
    public Task<IReadOnlyList<string>?> LoadClaudeConnectorsAsync() =>
        _agents.TryGetValue(Participants.ClaudeId, out var a) && a is ClaudeAgent c
            ? c.LoadConnectorsAsync(CancellationToken.None)
            : Task.FromResult<IReadOnlyList<string>?>(null);

    /// <inheritdoc />
    public void UpdateClaudeSettings(ClaudeSettings settings)
    {
        if (_agents.TryGetValue(Participants.ClaudeId, out var a) && a is ClaudeAgent c)
            c.UpdateSettings(settings);
        _onClaudeHarnessChanged?.Invoke(settings);
    }

    /// <inheritdoc />
    public AgySettings? GetAgySettings() =>
        _agents.TryGetValue(Participants.GeminiId, out var a) && a is AgyAgent g ? g.Settings : null;

    /// <inheritdoc />
    public void UpdateAgySettings(AgySettings settings)
    {
        if (_agents.TryGetValue(Participants.GeminiId, out var a) && a is AgyAgent g)
            g.UpdateSettings(settings);
        _onAgyHarnessChanged?.Invoke(settings);
    }

    /// <inheritdoc />
    public string WriterWarning(string agentId) => _agents[agentId].WriterWarning;

    /// <inheritdoc />
    public string FullWarning(string agentId) => _agents[agentId].FullWarning;

    /// <inheritdoc />
    public string ModelIdOf(string agentId) => _agents[agentId].ModelId;

    /// <inheritdoc />
    public void SetModel(string agentId, string modelId)
    {
        var agent = _agents[agentId];
        agent.SetModel(modelId);
        _onModelOrEffortChanged?.Invoke(agentId, agent.ModelId, agent.EffortId);
        SaveState();
    }

    /// <inheritdoc />
    public void SetModelForThisMeeting(string agentId, string modelId)
    {
        _agents[agentId].SetModel(modelId); // 保存の知らせ(_onModelOrEffortChanged)は呼ばない
        SaveState();
    }

    /// <inheritdoc />
    public string EffortIdOf(string agentId) =>
        _agents.TryGetValue(agentId, out var a) ? a.EffortId : "";

    /// <inheritdoc />
    public bool SupportsEffort(string agentId) => _agents.TryGetValue(agentId, out var agent) && agent.SupportsEffort;

    /// <inheritdoc />
    public void SetEffort(string agentId, string effortId)
    {
        if (_agents.TryGetValue(agentId, out var a))
        {
            a.SetEffort(effortId);
            _onModelOrEffortChanged?.Invoke(agentId, a.ModelId, a.EffortId);
            SaveState();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ModelOption> ModelOptionsFor(string agentId) => _agents[agentId].ModelOptions;

    /// <inheritdoc />
    public Task<IReadOnlyList<ModelOption>> LoadModelOptionsAsync(string agentId) =>
        _agents[agentId].LoadModelOptionsAsync(CancellationToken.None);

    /// <inheritdoc />
    public void CancelPending()
    {
        CancellationTokenSource[] snapshot;
        lock (_activeCts)
        {
            snapshot = _activeCts.ToArray();
        }
        foreach (var cts in snapshot)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // すでに完了・破棄済みなら無視
            }
        }
    }

    /// <inheritdoc />
    public void Shutdown()
    {
        CancelPending();
        foreach (var agent in _agents.Values) agent.Shutdown();
    }

    /// <inheritdoc />
    public Task SendAsync(string text, IReadOnlyList<string> targetIds, Action<ChatProgress> onProgress) =>
        SendAsync(text, targetIds, onProgress, null);

    /// <inheritdoc />
    public async Task SendAsync(string text, IReadOnlyList<string> targetIds, Action<ChatProgress> onProgress,
        IReadOnlyList<string>? extraDirs)
    {
        Record(Participants.UserId, text, targetIds);

        var context = SynchronizationContext.Current;
        void Notify(ChatProgress progress)
        {
            if (context is null)
            {
                lock (_notifyLock) onProgress(progress);
            }
            else
            {
                context.Send(_ => onProgress(progress), null);
            }
        }

        using var cts = new CancellationTokenSource();
        lock (_activeCts) _activeCts.Add(cts);
        try
        {
            var order = AllOrder; // 送った時点の順番で進める(途中で選び直しても、この発言には効かない)
            if (AllOrders.IsSequential(order, targetIds))
            {
                await AskInTurnAsync(AllOrders.Arrange(order, targetIds), Notify, cts.Token, extraDirs);
            }
            else
            {
                await Task.WhenAll(targetIds.Select(id => AskOneAsync(id, Notify, cts.Token, extraDirs: extraDirs)));
            }
        }
        finally
        {
            lock (_activeCts) _activeCts.Remove(cts);
        }
    }

    /// <summary>
    /// 先の人から聞く。1人目が答えてから、残りの人に同時に聞く(会議風)。残りの人は、未読の発言として1人目の答えを読める
    /// (残りの人どうしは、互いの今回の答えを見ない)。残りの人には、頼み書きを添える。1人目が失敗・停止したら、残りの人には聞かない。
    /// </summary>
    /// <param name="orderedIds">答える順の宛先(先頭が1人目)</param>
    /// <param name="notify">途中経過の通知</param>
    /// <param name="ct">停止の合図</param>
    /// <param name="extraDirs">追加のフォルダ(あれば)</param>
    private async Task AskInTurnAsync(IReadOnlyList<string> orderedIds, Action<ChatProgress> notify, CancellationToken ct,
        IReadOnlyList<string>? extraDirs = null)
    {
        var firstId = orderedIds[0];
        var followers = orderedIds.Skip(1).ToList();
        var answered = await AskOneAsync(firstId, notify, ct, extraDirs: extraDirs);
        if (!answered)
        {
            var reason = ct.IsCancellationRequested
                ? "回答を停止しました。"
                : $"{_agents[firstId].Name}が答えられなかったので、聞きませんでした。";
            foreach (var id in followers) notify(new ChatProgress(id, ChatProgressKind.Error, reason));
            return;
        }
        await Task.WhenAll(followers.Select(id => AskOneAsync(id, notify, ct, RelayNoteFor(id, firstId, followers.Count), extraDirs)));
    }

    /// <summary>
    /// 1人目のあとに答える人に添える頼み書きを選ぶ。あとに答える人が1人で、書き換えできる段階なら「続きの作業」、
    /// それ以外は「意見」(あとに答える人が2人以上だと、同時に作業して同じファイルを触りかねないため、作業は頼まない)。
    /// </summary>
    /// <param name="id">あとに答える人の識別名</param>
    /// <param name="firstId">1人目の識別名</param>
    /// <param name="followerCount">あとに答える人の数</param>
    /// <returns>頼み書き(空なら添えない)</returns>
    public string RelayNoteFor(string id, string firstId, int followerCount = 1)
    {
        var note = _agents[id].WriteMode && followerCount <= 1 ? _relay.WorkNote : _relay.ReviewNote;
        return (note ?? "").Replace("{first}", _agents[firstId].Name, StringComparison.Ordinal);
    }

    /// <summary>AI1人に聞く。保持している会話IDがCLI側で消えていて失敗した場合は、過去のログを先頭に添えて新しい会話として1回だけ自動再開する。</summary>
    /// <param name="id">聞くAIの識別名</param>
    /// <param name="notify">途中経過の通知</param>
    /// <param name="ct">停止の合図</param>
    /// <param name="relayNote">本文の最後に添える頼み書き(順番の2人目だけ)</param>
    /// <param name="extraDirs">この発言で作業フォルダの外も読めるよう追加するフォルダ</param>
    /// <returns>答えが届いたらtrue(失敗・停止ならfalse)</returns>
    private async Task<bool> AskOneAsync(string id, Action<ChatProgress> notify, CancellationToken ct, string? relayNote = null,
        IReadOnlyList<string>? extraDirs = null)
    {
        var agent = _agents[id];
        var gate = _gates[id];
        notify(new ChatProgress(id, ChatProgressKind.Started, ""));

        // 発言ごとの時間の記録(WBS 11.51): 順番待ち・最初の知らせ・最初の文字・完了までを測る
        var timer = System.Diagnostics.Stopwatch.StartNew();
        long queueMs = 0, firstOutputMs = -1, firstTextMs = -1;
        var outcome = "error";
        var replyChars = 0;
        bool? reusedCli = null;
        string? restartReason = null;
        var retried = false;
        var acquired = false;
        var workStarted = false; // 返答の文字・作業・承認のどれかが届いたら true(作業が進んでいるので、やり直さない)
        void OnUpdate(AgentUpdate update)
        {
            if (update.Kind is AgentUpdateKind.Delta or AgentUpdateKind.Activity or AgentUpdateKind.Approval) workStarted = true;
            var sinceAsked = timer.ElapsedMilliseconds - queueMs;
            Interlocked.CompareExchange(ref firstOutputMs, sinceAsked, -1);
            if (ToProgressKind(update.Kind) == ChatProgressKind.Delta)
                Interlocked.CompareExchange(ref firstTextMs, sinceAsked, -1);
            notify(new ChatProgress(id, ToProgressKind(update.Kind), update.Text, update.Approval));
        }

        try
        {
            await gate.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            notify(new ChatProgress(id, ChatProgressKind.Error, "回答を停止しました。"));
            return false;
        }
        acquired = true;
        queueMs = timer.ElapsedMilliseconds;
        agent.SetExtraDirs(extraDirs ?? Array.Empty<string>());

        try
        {
            var existingConvId = _conversationIds.GetValueOrDefault(id);
            var (unseen, upTo) = _transcript.PeekUnseen(id, _maxInjectChars);
            var prompt = PromptComposer.Compose(unseen, agent.Name, relayNote);
            AgentReply reply;
            try
            {
                reply = await agent.AskAsync(prompt, existingConvId, OnUpdate, ct);
            }
            catch (Exception ex) when (ShouldRetryWithNewConversation(ex, existingConvId, workStarted, ct))
            {
                // 保険: CLI側の会話が消えていた場合、会話IDを外して既読位置を0に戻し、過去のやりとりを添えて新しい会話でやり直す
                retried = true;
                workStarted = false;
                _conversationIds.Remove(id);
                _transcript.ResetSeen(id);
                (unseen, upTo) = _transcript.PeekUnseen(id, _maxInjectChars);
                prompt = PromptComposer.Compose(unseen, agent.Name, relayNote);
                reply = await agent.AskAsync(prompt, null, OnUpdate, ct);
            }

            reusedCli = agent.LastTurnReusedCli;
            restartReason = retried ? "retry+" + agent.LastTurnRestartReason : agent.LastTurnRestartReason;
            replyChars = reply.Text.Length;
            if (!string.IsNullOrEmpty(reply.ConversationId)) _conversationIds[id] = reply.ConversationId;
            _transcript.MarkSeen(id, upTo);
            Record(id, reply.Text);
            if (!string.IsNullOrEmpty(reply.Model)) notify(new ChatProgress(id, ChatProgressKind.Model, reply.Model));
            notify(new ChatProgress(id, ChatProgressKind.Done, reply.Text));
            outcome = "ok";
            return true;
        }
        catch (OperationCanceledException)
        {
            outcome = "stopped";
            notify(new ChatProgress(id, ChatProgressKind.Error, "回答を停止しました。"));
            return false;
        }
        catch (Exception ex)
        {
            notify(new ChatProgress(id, ChatProgressKind.Error, ex.Message));
            return false;
        }
        finally
        {
            if (acquired)
            {
                TurnTimingLog.Append(System.IO.Path.GetDirectoryName(_log.StatePath), new TurnTiming(
                    _clock(), id, outcome, queueMs, firstOutputMs, firstTextMs,
                    timer.ElapsedMilliseconds - queueMs, reusedCli, replyChars, restartReason));
            }
            gate.Release();
        }
    }

    /// <summary>AIからの知らせの種類を、画面へ通知する種類に変える。</summary>
    /// <summary>
    /// 会話IDを外して新しい会話でやり直すかを決める。会話が消えていた失敗だけを救うため、
    /// 時間切れ・停止・作業が進んだあとの失敗では、やり直さない(同じ依頼が二重に実行されるのを防ぐ)。
    /// </summary>
    /// <param name="ex">1回目の失敗</param>
    /// <param name="conversationId">1回目に使った会話ID(なければ、そもそもやり直す対象がない)</param>
    /// <param name="workStarted">1回目で、返答の文字・作業・承認の更新が届いたか</param>
    /// <param name="ct">停止の取り消し</param>
    /// <returns>やり直すならtrue</returns>
    public static bool ShouldRetryWithNewConversation(Exception ex, string? conversationId, bool workStarted, CancellationToken ct) =>
        !string.IsNullOrEmpty(conversationId)
        && ex is not OperationCanceledException and not TimeoutException
        && !ct.IsCancellationRequested
        && !workStarted;

    private static ChatProgressKind ToProgressKind(AgentUpdateKind kind) => kind switch
    {
        AgentUpdateKind.Model => ChatProgressKind.Model,
        AgentUpdateKind.Activity => ChatProgressKind.Activity,
        AgentUpdateKind.Usage => ChatProgressKind.Usage,
        AgentUpdateKind.Approval => ChatProgressKind.Approval,
        _ => ChatProgressKind.Delta,
    };

    /// <summary>発言を一覧とログに記録し、状態ファイルも更新する。</summary>
    private void Record(string speakerId, string text, IReadOnlyList<string>? targetIds = null)
    {
        var entry = new ChatEntry(speakerId, text, _clock(), targetIds);
        _transcript.Add(entry);
        _log.Append(entry);
        SaveState();
    }

    private void SaveState()
    {
        var entries = _transcript.GetEntriesSnapshot();
        if (entries.Count == 0) return; // まだ1件も発言がない会議は状態ファイルを作らない
        lock (_stateLock)
        {
            var state = new MeetingState
            {
                StartedAt = _log.StartedAt,
                WorkDir = WorkDir,
                ConversationIds = new Dictionary<string, string>(_conversationIds),
                SeenPositions = _transcript.GetSeenSnapshot(),
                Models = _agents.ToDictionary(kv => kv.Key, kv => kv.Value.ModelId),
                Efforts = _agents.ToDictionary(kv => kv.Key, kv => kv.Value.EffortId),
                Entries = entries,
            };
            MeetingStore.SaveState(_log.StatePath, state);
        }
    }
}
