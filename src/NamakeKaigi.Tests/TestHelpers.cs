using System.IO;
using NamakeKaigi.Models;
using NamakeKaigi.Services;

namespace NamakeKaigi.Tests;

/// <summary>試験用の偽のAI。呼ばれた内容を記録し、決めた返答を返す。</summary>
internal sealed class FakeAgent : IChatAgent
{
    private readonly Func<string, string?, int, AgentReply> _reply;

    /// <summary>偽のAIを作る。</summary>
    /// <param name="id">識別名</param>
    /// <param name="name">プロンプトに書く名前</param>
    /// <param name="reply">(本文, 会話ID, 何回目か)から返答を作る関数。例外を投げれば失敗になる</param>
    public FakeAgent(string id, string name, Func<string, string?, int, AgentReply> reply)
    {
        Id = id;
        Name = name;
        _reply = reply;
    }

    public string Id { get; }
    public string Name { get; }
    public string ModelLabel => ModelId.Length == 0 ? "未指定" : ModelId;
    public string ModelId { get; private set; } = "fake-model";
    public IReadOnlyList<ModelOption> ModelOptions { get; init; } = new[] { new ModelOption("fake-model", "fake-model") };

    /// <inheritdoc />
    public void SetModel(string modelId) => ModelId = modelId;

    /// <summary>最後に渡された制限時間(まだなければnull)。</summary>
    public TimeSpan? Timeout { get; private set; }

    /// <inheritdoc />
    public void SetTimeout(TimeSpan timeout) => Timeout = timeout;

    /// <summary>最後に渡された追加の読み取り許可フォルダ一覧。</summary>
    public IReadOnlyList<string> ExtraDirs { get; private set; } = Array.Empty<string>();

    /// <inheritdoc />
    public void SetExtraDirs(IReadOnlyList<string> dirs) => ExtraDirs = dirs;

    /// <inheritdoc />
    public Task<IReadOnlyList<ModelOption>> LoadModelOptionsAsync(CancellationToken ct) => Task.FromResult(ModelOptions);

    /// <summary>直近の発言で、起動したままのCLIを使い回したか(時間の記録の試験用。既定はnull)。</summary>
    public bool? LastTurnReusedCli { get; init; }

    /// <summary>返答の前に、途中の知らせとして流す内容。</summary>
    public IReadOnlyList<AgentUpdate> Updates { get; init; } = Array.Empty<AgentUpdate>();

    /// <summary>呼ばれた記録(本文, 渡された会話ID)。</summary>
    public List<(string Prompt, string? ConversationId)> Calls { get; } = new();

    /// <summary>いま「書く担当」か(順番の2人目への頼み書きの選び分けを試す)。</summary>
    public bool WriteMode { get; set; }

    /// <inheritdoc />
    public Task<AgentReply> AskAsync(string prompt, string? conversationId, Action<AgentUpdate>? onUpdate,
        CancellationToken ct)
    {
        Calls.Add((prompt, conversationId));
        foreach (var u in Updates) onUpdate?.Invoke(u);
        return Task.FromResult(_reply(prompt, conversationId, Calls.Count));
    }
}

/// <summary>試験用の偽のプロセス実行。渡された内容を記録し、決めた出力の行を流す。</summary>
internal sealed class FakeRunner : IProcessRunner
{
    /// <summary>標準出力として流す行。</summary>
    public List<string> Lines { get; } = new();

    /// <summary>行を流したあとに投げる例外(終了コードが0以外の場合を真似る)。</summary>
    public Exception? ThrowAfter { get; set; }

    public string? LastFile { get; private set; }
    public IReadOnlyList<string> LastArgs { get; private set; } = Array.Empty<string>();

    /// <inheritdoc />
    public Task RunLinesAsync(string fileName, IReadOnlyList<string> args, string workDir,
        TimeSpan timeout, Action<string> onLine, CancellationToken ct)
    {
        LastFile = fileName;
        LastArgs = args;
        foreach (var line in Lines) onLine(line);
        return ThrowAfter is null ? Task.CompletedTask : Task.FromException(ThrowAfter);
    }

    /// <summary>標準入力に渡された文字(RunWithInputAsync。使われなければnull)。</summary>
    public string? LastInput { get; private set; }

    /// <summary>起動のときに投げる例外(実行ファイルが見つからない場合などを真似る)。</summary>
    public Exception? ThrowOnStart { get; set; }

    /// <inheritdoc />
    public Task RunWithInputAsync(string fileName, IReadOnlyList<string> args, string workDir, TimeSpan timeout,
        string input, Action<string> onLine, CancellationToken ct)
    {
        if (ThrowOnStart is not null) return Task.FromException(ThrowOnStart);
        LastInput = input;
        return RunLinesAsync(fileName, args, workDir, timeout, onLine, ct);
    }

    /// <summary>やりとりの形で最初に送られた1行(使われなければnull)。</summary>
    public string? FirstInput { get; private set; }

    /// <summary>やりとりの形で、標準入力へ送られた行(最初の1行を除く)。</summary>
    public List<string> Sent { get; } = new();

    /// <summary>標準入力が閉じられたか。</summary>
    public bool InputClosed { get; private set; }

    /// <summary>タイムアウトを止めた回数。</summary>
    public int PauseCount { get; private set; }

    /// <inheritdoc />
    public async Task RunConversationAsync(string fileName, IReadOnlyList<string> args, string workDir,
        TimeSpan timeout, string firstInput, Func<string, IProcessChannel, Task> onLine, CancellationToken ct)
    {
        LastFile = fileName;
        LastArgs = args;
        FirstInput = firstInput;
        var channel = new FakeChannel(this);
        foreach (var line in Lines) await onLine(line, channel);
        if (ThrowAfter is not null) throw ThrowAfter;
    }

    /// <summary>この偽の実行役に記録する窓口を返す(ほかの偽物から使う)。</summary>
    public IProcessChannel AsChannel() => new FakeChannel(this);

    /// <summary>送った行・閉じたこと・止めた回数を、偽の実行役に記録する窓口。</summary>
    private sealed class FakeChannel(FakeRunner owner) : IProcessChannel
    {
        public void Send(string line) => owner.Sent.Add(line);
        public void CloseInput() => owner.InputClosed = true;

        public IDisposable PauseTimeout()
        {
            owner.PauseCount++;
            return new Nothing();
        }

        private sealed class Nothing : IDisposable
        {
            public void Dispose() { }
        }
    }
}

/// <summary>試験用の一時フォルダ(終わったら消す)。</summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "namake_kaigi_test_" + Guid.NewGuid().ToString("N"));

    /// <summary>一時フォルダを作る。</summary>
    public TempDir() => Directory.CreateDirectory(Path);

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            // git が作る読み取り専用のファイルも消せるよう、属性を外してから消す
            foreach (var f in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* 消せなくても試験は成功扱い */ }
    }
}

/// <summary>試験用の偽の会議。送られた内容を記録し、決めた途中経過・結果を返す。</summary>
internal sealed class FakeSession : IChatSession
{
    private readonly List<TaskCompletionSource> _waiting = new();
    private Action<ChatProgress>? _lastCallback;

    /// <summary>送られた(本文, 宛先)の記録。</summary>
    public List<(string Text, IReadOnlyList<string> Targets)> Sent { get; } = new();

    /// <summary>送られたときに返す途中経過・結果。nullなら返さず、Push / Finish で後から返す。</summary>
    public Func<string, ChatProgress[]>? Script { get; set; }

    /// <summary>意見を頼むときの頼み書き(試験で差し替える)。</summary>
    public RelaySettings Relay { get; init; } = new();

    public string LogPath { get; init; } = "fake-log.md";

    /// <summary>会議の名前(過去の会議を開き直したときは、最初から入れておける)。</summary>
    public string? MeetingTitle { get; set; }

    /// <summary>会議の名前を書くよう頼まれた記録(頼まれた順)。</summary>
    public List<string?> TitleWrites { get; } = new();

    public void SetMeetingTitle(string? title)
    {
        MeetingTitle = title;
        TitleWrites.Add(title);
    }

    /// <summary>Shutdown(起動したままのCLIを終わらせる)が呼ばれた回数。</summary>
    public int ShutdownCount { get; private set; }

    public void Shutdown() => ShutdownCount++;

    public IReadOnlyList<ChatEntry> RestoredEntries { get; init; } = Array.Empty<ChatEntry>();

    public IReadOnlyList<SavedMeetingSummary> PastMeetings { get; init; } = Array.Empty<SavedMeetingSummary>();

    public Func<string, IChatSession?>? OnOpenPastMeeting { get; init; }

    public IReadOnlyList<SavedMeetingSummary> ListPastMeetings() => PastMeetings;

    public IChatSession? OpenPastMeeting(string folderPath) => OnOpenPastMeeting?.Invoke(folderPath);

    /// <summary>作業フォルダ(画面の表示の確認用)。</summary>
    public string WorkDir { get; private set; } = "C:/Work/NamakeKaigi";

    public void SetWorkDir(string workDir) => WorkDir = workDir;

    /// <summary>最後に渡された追加の読み取り許可フォルダ一覧。</summary>
    public IReadOnlyList<string> ExtraDirs { get; private set; } = Array.Empty<string>();

    public void SetExtraDirs(IReadOnlyList<string> dirs) => ExtraDirs = dirs;

    /// <summary>Claudeだけが、書く担当になれる(本物と同じ)。</summary>
    public bool CanWrite(string agentId) => agentId == Participants.ClaudeId;

    public string HarnessSummary(string agentId) => $"ハーネス説明({agentId})";

    public IReadOnlyDictionary<string, string> ModelLabels { get; init; } = new Dictionary<string, string>
    {
        [Participants.ClaudeId] = "sonnet",
        [Participants.GeminiId] = "未指定(agyの既定)",
    };

    /// <summary>AIごとの、いま選ばれているモデル(Geminiは指定なし)。</summary>
    public Dictionary<string, string> CurrentModels { get; } = new()
    {
        [Participants.ClaudeId] = "sonnet",
        [Participants.GeminiId] = "",
    };

    /// <summary>モデルを切り替えるよう頼まれた記録(AIの識別名, モデル名)。</summary>
    public List<(string AgentId, string ModelId)> ModelChanges { get; } = new();

    /// <summary>起動時に、すぐ出せる一覧。</summary>
    public Dictionary<string, IReadOnlyList<ModelOption>> InitialOptions { get; } = new()
    {
        [Participants.ClaudeId] = new[] { new ModelOption("sonnet", "sonnet"), new ModelOption("opus", "opus") },
        [Participants.GeminiId] = Array.Empty<ModelOption>(),
    };

    /// <summary>CLIに聞いたあとに出す一覧(なければ、起動時の一覧のまま)。</summary>
    public Dictionary<string, IReadOnlyList<ModelOption>> LoadedOptions { get; } = new();

    public string ModelIdOf(string agentId) => CurrentModels[agentId];

    public void SetModel(string agentId, string modelId)
    {
        CurrentModels[agentId] = modelId;
        ModelChanges.Add((agentId, modelId));
    }

    public Dictionary<string, string> CurrentEfforts { get; } = new()
    {
        [Participants.ClaudeId] = "",
        [Participants.GeminiId] = "",
    };

    public List<(string AgentId, string EffortId)> EffortChanges { get; } = new();

    public string EffortIdOf(string agentId) => CurrentEfforts.GetValueOrDefault(agentId, "");

    public void SetEffort(string agentId, string effortId)
    {
        CurrentEfforts[agentId] = effortId;
        EffortChanges.Add((agentId, effortId));
    }

    public IReadOnlyList<ModelOption> ModelOptionsFor(string agentId) => InitialOptions[agentId];

    public Task<IReadOnlyList<ModelOption>> LoadModelOptionsAsync(string agentId) =>
        Task.FromResult(LoadedOptions.GetValueOrDefault(agentId, InitialOptions[agentId]));

    /// <inheritdoc />
    public Task SendAsync(string text, IReadOnlyList<string> targetIds, Action<ChatProgress> onProgress) =>
        SendAsync(text, targetIds, onProgress, null);

    /// <inheritdoc />
    public async Task SendAsync(string text, IReadOnlyList<string> targetIds, Action<ChatProgress> onProgress,
        IReadOnlyList<string>? extraDirs)
    {
        if (extraDirs is not null) ExtraDirs = extraDirs;
        Sent.Add((text, targetIds));
        _lastCallback = onProgress;
        if (Script is not null)
        {
            foreach (var r in Script(text)) onProgress(r);
            return;
        }
        var tcs = new TaskCompletionSource();
        _waiting.Add(tcs);
        await tcs.Task;
    }

    public string ThemeId { get; private set; } = ThemeCatalog.DefaultThemeId;

    public List<string> ThemeChanges { get; } = new();

    public int TimeoutSec { get; set; } = 600;

    /// <summary>呼びかけで宛先を切り替える呼び名の一覧。</summary>
    public CallNameSettings CallNames { get; set; } = CallNameSettings.Default;

    /// <summary>「全員」宛ての答える順番。</summary>
    public string AllOrder { get; set; } = AllOrders.Parallel;

    /// <summary>順番を変えるよう頼まれた記録。</summary>
    public List<string> AllOrderChanges { get; } = new();

    public void SetAllOrder(string order)
    {
        AllOrder = order;
        AllOrderChanges.Add(order);
    }

    /// <summary>労力を別に選べないAIの識別名。</summary>
    public HashSet<string> NoEffortAgents { get; } = new();

    public bool SupportsEffort(string agentId) => !NoEffortAgents.Contains(agentId);

    public List<int> TimeoutChanges { get; } = new();

    public void SetTimeout(int timeoutSec)
    {
        TimeoutSec = timeoutSec;
        TimeoutChanges.Add(timeoutSec);
    }

    public void SetTheme(string themeId)
    {
        ThemeId = themeId;
        ThemeChanges.Add(themeId);
    }

    public AppearanceSettings Appearance { get; private set; } = AppearanceSettings.Default;

    public void SetAppearance(AppearanceSettings appearance) => Appearance = appearance;

    public int CancelCallCount { get; private set; }

    public void CancelPending()
    {
        CancelCallCount++;
        _lastCallback?.Invoke(new ChatProgress(Participants.ClaudeId, ChatProgressKind.Error, "回答を停止しました。"));
        foreach (var w in _waiting) w.TrySetResult();
        _waiting.Clear();
    }

    /// <summary>止めておいた送信に、途中経過を1つ返す(送信は終わらせない)。</summary>
    /// <param name="progress">返す内容</param>
    public void Push(ChatProgress progress) => _lastCallback!(progress);

    /// <summary>止めておいた送信に、結果(完成した返答)を返して、送信を終わらせる。</summary>
    /// <param name="progress">返す内容(DoneかError)</param>
    public void Finish(ChatProgress progress)
    {
        _lastCallback!(progress);
        foreach (var w in _waiting) w.TrySetResult();
        _waiting.Clear();
    }
}
