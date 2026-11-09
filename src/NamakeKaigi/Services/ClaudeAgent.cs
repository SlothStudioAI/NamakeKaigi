using System.Text.Json;
using NamakeKaigi.Models;

namespace NamakeKaigi.Services;

/// <summary>Claude CLIの流れてくる出力(stream-json)を、1行ずつ読み取って状態にためる。</summary>
public sealed class ClaudeStreamState
{
    public string Model { get; private set; } = "";
    public string SessionId { get; private set; } = "";
    public string ResultText { get; private set; } = "";
    public bool IsError { get; private set; }

    private string _lastActivity = "";

    /// <summary>最終結果の行まで届いたか。</summary>
    public bool HasResult { get; private set; }

    /// <summary>1行を読み取る。JSONでない行は無視する。</summary>
    /// <param name="line">標準出力の1行</param>
    /// <param name="onUpdate">モデル名・続きの文字が分かったら知らせる処理(不要ならnull)</param>
    public void Feed(string line, Action<AgentUpdate>? onUpdate)
    {
        if (!line.StartsWith('{')) return;
        JsonDocument doc;
        try { doc = JsonDocument.Parse(line); }
        catch (JsonException) { return; }

        using (doc)
        {
            var root = doc.RootElement;
            switch (Str(root, "type"))
            {
                case "system" when Str(root, "subtype") == "init":
                    Model = Str(root, "model");
                    SessionId = Str(root, "session_id");
                    if (Model.Length > 0) onUpdate?.Invoke(new AgentUpdate(AgentUpdateKind.Model, Model));
                    break;
                case "stream_event":
                    if (root.TryGetProperty("event", out var ev) && Str(ev, "type") == "content_block_delta"
                        && ev.TryGetProperty("delta", out var delta) && Str(delta, "type") == "text_delta")
                        onUpdate?.Invoke(new AgentUpdate(AgentUpdateKind.Delta, Str(delta, "text")));
                    break;
                case "assistant":
                    // 道具(ファイルを読む等)を使い始めたら、いまの作業として知らせる
                    foreach (var block in Blocks(root).Where(b => Str(b, "type") == "tool_use"))
                    {
                        var target = block.TryGetProperty("input", out var input) ? ActivityText.FindTarget(input) : null;
                        _lastActivity = ActivityText.Describe(Str(block, "name"), target);
                        onUpdate?.Invoke(new AgentUpdate(AgentUpdateKind.Activity, _lastActivity));
                    }
                    break;
                case "user":
                    // 道具の結果が戻ったら、「〜しました」にして残す(次の動きまで、表示が消えないように)
                    if (Blocks(root).Any(b => Str(b, "type") == "tool_result"))
                        onUpdate?.Invoke(new AgentUpdate(AgentUpdateKind.Activity, ActivityText.ToPast(_lastActivity)));
                    break;
                case "rate_limit_event":
                    var quota = UsageQuota.TryParse(root);
                    if (quota is not null)
                        onUpdate?.Invoke(new AgentUpdate(AgentUpdateKind.Usage, quota.Encode()));
                    break;
                case "result":
                    HasResult = true;
                    ResultText = Str(root, "result");
                    var id = Str(root, "session_id");
                    if (id.Length > 0) SessionId = id;
                    IsError = root.TryGetProperty("is_error", out var e) && e.ValueKind == JsonValueKind.True;
                    break;
            }
        }
    }

    /// <summary>ためた状態から、完成した返答を作る。結果が届いていない・エラーなら例外にする。</summary>
    /// <returns>返答</returns>
    public AgentReply ToReply()
    {
        if (!HasResult) throw new InvalidOperationException("Claudeから返答が届きませんでした。");
        if (IsError) throw new InvalidOperationException($"Claudeがエラーを返しました: {ResultText}");
        var cleaned = ReplyTextCleaner.StripLeadingRoleHeader(ResultText);
        return new AgentReply(cleaned, SessionId, Model.Length > 0 ? Model : null);
    }

    /// <summary>メッセージの中身(message.content)の要素を取り出す。</summary>
    private static List<JsonElement> Blocks(JsonElement root) =>
        root.TryGetProperty("message", out var m) && m.TryGetProperty("content", out var c)
        && c.ValueKind == JsonValueKind.Array
            ? c.EnumerateArray().ToList()
            : new List<JsonElement>();

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}

/// <summary>Claude CLI(claude -p)で答えるAI。</summary>
public sealed class ClaudeAgent : IChatAgent
{
    private static readonly string[] DefaultModels = { "sonnet", "opus", "haiku" };

    private ClaudeSettings _settings;
    private readonly IProcessRunner _runner;
    private volatile string _workDir;
    private IReadOnlyList<string> _extraDirs = Array.Empty<string>();
    private TimeSpan _timeout;
    private volatile string _model;
    private volatile string _effort;
    private volatile bool _writeMode;
    private volatile bool _commandMode;
    private volatile bool _fullMode;
    private volatile string _lastSentNote = "";
    private readonly ConnectorCatalog _connectors;
    private readonly KeptCli _kept;

    /// <summary>Claude用のAIを作る。</summary>
    /// <param name="settings">Claudeの設定</param>
    /// <param name="runner">プロセス実行</param>
    /// <param name="workDir">作業フォルダ</param>
    /// <param name="timeout">タイムアウト</param>
    public ClaudeAgent(ClaudeSettings settings, IProcessRunner runner, string workDir, TimeSpan timeout)
    {
        _settings = settings;
        _runner = runner;
        _workDir = workDir;
        _timeout = timeout;
        _model = settings.Model;
        _effort = settings.Effort?.Trim() ?? "";
        _connectors = new ConnectorCatalog(runner);
        _kept = new KeptCli(runner);
    }

    /// <inheritdoc />
    public void SetTimeout(TimeSpan timeout) => _timeout = timeout;

    /// <summary>いまのClaudeの設定。</summary>
    public ClaudeSettings Settings => _settings;

    /// <summary>ハーネス(許可するツール・コマンド)の設定を更新する(次の発言から効く)。</summary>
    /// <param name="settings">新しい設定</param>
    public void UpdateSettings(ClaudeSettings settings) => _settings = settings;

    /// <inheritdoc />
    public string HarnessSummary => HarnessInfo.DescribeClaude(_settings, _writeMode, _commandMode, _fullMode);

    /// <inheritdoc />
    public string Id => Participants.ClaudeId;

    /// <inheritdoc />
    public string Name => "Claude";

    /// <inheritdoc />
    public bool CanWrite => _settings.WriterExtraArgs is { Count: > 0 };

    /// <inheritdoc />
    public bool WriteMode
    {
        get => _writeMode;
        set
        {
            _writeMode = value && CanWrite;
            if (_writeMode) return;
            _commandMode = false; // 書く担当でなくなったら、コマンドも「何でもできる」も使えなくする
            _fullMode = false;
        }
    }

    /// <inheritdoc />
    public bool CanFull => CanWrite && _settings.FullExtraArgs is { Count: > 0 };

    /// <inheritdoc />
    public bool FullMode
    {
        get => _fullMode;
        set => _fullMode = value && CanFull && _writeMode; // 「何でもできる」は、書く担当のときだけ有効
    }

    /// <inheritdoc />
    public bool CanRunCommands => CanWrite && _settings.CommandExtraArgs is { Count: > 0 };

    /// <inheritdoc />
    public bool CommandMode
    {
        get => _commandMode;
        set => _commandMode = value && CanRunCommands && _writeMode; // コマンドは、書く担当のときだけ有効
    }

    /// <inheritdoc />
    public void SetWorkDir(string workDir) => _workDir = workDir;

    /// <inheritdoc />
    public void SetExtraDirs(IReadOnlyList<string> dirs) => _extraDirs = dirs;

    /// <inheritdoc />
    public string ModelId => _model;

    /// <inheritdoc />
    public string ModelLabel => _model;

    /// <inheritdoc />
    public void SetModel(string modelId) => _model = modelId;

    /// <inheritdoc />
    public string EffortId => _effort;

    /// <inheritdoc />
    public void SetEffort(string effortId) => _effort = effortId.Trim();

    /// <inheritdoc />
    public IReadOnlyList<ModelOption> ModelOptions =>
        (_settings.Models is { Count: > 0 } ? _settings.Models : DefaultModels)
        .Append(_settings.Model).Append(_model)
        .Distinct().Select(m => new ModelOption(m, m)).ToList();

    /// <inheritdoc />
    public Task<IReadOnlyList<ModelOption>> LoadModelOptionsAsync(CancellationToken ct) =>
        Task.FromResult(ModelOptions); // Claude CLI にはモデルの一覧を出す命令がないので、設定の一覧を使う

    /// <summary>いまある外部連携の一覧を取る(設定の画面で、ON/OFFのチェックを並べるため)。</summary>
    /// <param name="ct">キャンセル</param>
    /// <returns>連携の名前。取れなかったらnull</returns>
    public Task<IReadOnlyList<string>?> LoadConnectorsAsync(CancellationToken ct) =>
        _connectors.LoadAsync(_settings.Executable, _workDir, TimeSpan.FromSeconds(_settings.ConnectorListTimeoutSec), 0, ct);

    /// <inheritdoc />
    public async Task<AgentReply> AskAsync(string prompt, string? conversationId, Action<AgentUpdate>? onUpdate,
        CancellationToken ct)
    {
        var state = new ClaudeStreamState();
        var status = CurrentStatusNote();
        // 「何でもできる」で連携を1つ以上ONにしているときだけ、いまある連携の一覧を取る(OFFの連携を止めるため)
        IReadOnlyList<string>? known = null;
        if (_fullMode && _settings.EnabledConnectors is { Count: > 0 })
        {
            known = await _connectors.LoadAsync(_settings.Executable, _workDir,
                TimeSpan.FromSeconds(_settings.ConnectorListTimeoutSec), _settings.ConnectorListCacheSec, ct);
        }
        var kept = KeepAlive && _runner.SupportsSessions;
        try
        {
            if (kept)
            {
                // 起動したままのCLIに、標準入力で発言を送る(段階・モデルなどが変わったときだけ起動し直す)
                var input = ClaudeControl.BuildUserMessage(StatusPrefix(conversationId) + prompt);
                await _kept.RunTurnAsync(_settings.Executable, BuildArgs(prompt, null, known, viaInput: true),
                    new[] { "--resume", conversationId ?? "" }, _workDir, conversationId, input, _timeout,
                    (line, channel) => OnConversationLineAsync(line, channel, state, onUpdate, ct), ct);
            }
            else if (UsesApproval)
            {
                // 一部できる: 許可リストの外の操作は、CLIが問い合わせてくるので、ユーザーに承認を求めて答える
                var args = BuildArgs(prompt, conversationId, known, viaInput: true);
                var firstInput = ClaudeControl.BuildUserMessage(StatusPrefix(conversationId) + prompt);
                await _runner.RunConversationAsync(_settings.Executable, args, _workDir, _timeout, firstInput,
                    async (line, channel) =>
                    {
                        // 答えが済んだら入力を閉じる(閉じないと、CLIが次の発言を待ち続ける)
                        if (await OnConversationLineAsync(line, channel, state, onUpdate, ct)) channel.CloseInput();
                    }, ct);
            }
            else
            {
                await _runner.RunLinesAsync(_settings.Executable, BuildArgs(prompt, conversationId, known),
                    _workDir, _timeout, line => state.Feed(line, onUpdate), ct);
            }
        }
        catch (InvalidOperationException) when (state.HasResult)
        {
            // 結果の行まで届いていれば、終了コードの違いは無視して、結果の内容で判断する
        }
        _lastSentNote = status; // 届いたので、同じ状態のあいだは次から添えない
        var reply = state.ToReply();
        if (kept) _kept.MarkConversation(reply.ConversationId);
        LastTurnReusedCli = kept ? !_kept.LastTurnStartedNew : null;
        return reply;
    }

    /// <inheritdoc />
    public bool? LastTurnReusedCli { get; private set; }

    /// <inheritdoc />
    public string? LastTurnRestartReason => _kept.LastStartReason;

    /// <summary>CLIを起動したままにして、2回目からの発言を速くするか(settings.json の keepAlive。既定はtrue)。</summary>
    public bool KeepAlive { get; set; } = true;

    /// <summary>起動したままのCLI(試験・確認用に公開)。</summary>
    public KeptCli Kept => _kept;

    /// <inheritdoc />
    public void Shutdown() => _kept.Stop();

    /// <summary>いまの段階が「一部できる」で、許可リストの外の操作をユーザーの承認で通すか。</summary>
    public bool UsesApproval => _writeMode && !_fullMode;

    /// <summary>
    /// やりとりの形で起動したときの、標準出力の1行を処理する。
    /// 許可の問い合わせなら、ユーザーの答えを待って返す(待つ間はタイムアウトを止める)。
    /// </summary>
    /// <param name="line">標準出力の1行</param>
    /// <param name="channel">プロセスへの窓口</param>
    /// <param name="state">返答をためる状態</param>
    /// <param name="onUpdate">途中の知らせ(nullなら、問い合わせはすべて断る)</param>
    /// <param name="ct">キャンセル</param>
    /// <returns>結果の行が届いて、この発言の答えが済んだらtrue</returns>
    private static async Task<bool> OnConversationLineAsync(string line, IProcessChannel channel, ClaudeStreamState state,
        Action<AgentUpdate>? onUpdate, CancellationToken ct)
    {
        if (!ClaudeControl.TryParseRequest(line, out var requestId, out var subtype))
        {
            state.Feed(line, onUpdate);
            return state.HasResult;
        }

        var request = ClaudeControl.TryParseToolRequest(line);
        if (request is null)
        {
            channel.Send(ClaudeControl.BuildUnsupportedResponse(requestId, subtype));
            return false;
        }

        var allow = false;
        if (onUpdate is not null)
        {
            var approval = new ToolApproval(request.ToolName, request.Detail);
            using (channel.PauseTimeout())
            {
                onUpdate(new AgentUpdate(AgentUpdateKind.Approval, approval.Text, approval));
                allow = await approval.Result.WaitAsync(ct);
            }
        }
        channel.Send(ClaudeControl.BuildToolResponse(request, allow));
        return false;
    }

    /// <summary>いまの段階でできること・できないことを伝える文(添えないときは空)。</summary>
    private string CurrentStatusNote() =>
        HarnessInfo.BuildClaudeStatusNote(_settings, _writeMode, _commandMode, _fullMode);

    /// <summary>
    /// 本文の先頭に添える、いまの段階の知らせ(制限そのものは、起動の引数が決める)。
    /// 会話は続きから話すので、新しい会話の最初と、段階が変わった直後だけ添える(毎回は添えない)。
    /// </summary>
    /// <param name="conversationId">続きの会話ID(なければnull)</param>
    /// <returns>添える文(末尾に空行つき。添えないときは空)</returns>
    private string StatusPrefix(string? conversationId)
    {
        var status = CurrentStatusNote();
        var sendNote = status.Length > 0 && (string.IsNullOrEmpty(conversationId) || status != _lastSentNote);
        return sendNote ? status + "\n\n" : "";
    }

    /// <summary>CLIに渡す引数を組み立てる(流れてくる形式で受け取る)。</summary>
    /// <param name="prompt">送る本文</param>
    /// <param name="conversationId">続きの会話ID(なければnull)</param>
    /// <param name="knownConnectors">いまある外部連携の名前(「何でもできる」のときだけ使う。分からなければnull)</param>
    /// <param name="viaInput">本文を引数でなく標準入力で送るならtrue(本文は引数に入れない。一部できるなら、許可の問い合わせも受ける)</param>
    /// <returns>引数の並び</returns>
    public IReadOnlyList<string> BuildArgs(string prompt, string? conversationId, IReadOnlyList<string>? knownConnectors = null,
        bool viaInput = false)
    {
        var args = viaInput
            ? new List<string>
            {
                "-p", "--input-format", "stream-json",
                "--output-format", "stream-json", "--verbose", "--include-partial-messages", "--model", _model,
            }
            : new List<string>
            {
                "-p", StatusPrefix(conversationId) + prompt, "--output-format", "stream-json", "--verbose",
                "--include-partial-messages", "--model", _model,
            };
        if (viaInput && UsesApproval)
        {
            // 一部できる: 許可リストの外の操作は、CLIが標準出力で問い合わせてくる(承認ボタンで答える)
            args.Add("--permission-prompt-tool");
            args.Add("stdio");
        }
        if (_effort.Length > 0)
        {
            args.Add("--effort");
            args.Add(_effort);
        }
        // 段階に応じて、許す道具と許可の範囲を切り替える
        // 何でもできる=道具を絞らず自動判定 / 一部できる=設定の道具(コマンドは許可リストに載せたものだけ) / 読むだけ
        args.AddRange(_fullMode ? _settings.FullExtraArgs!
            : _commandMode ? _settings.CommandExtraArgs!
            : _writeMode ? _settings.WriterExtraArgs! : _settings.ExtraArgs);
        if (_fullMode)
        {
            // 外部連携は、ONにしたものだけを使う(ほかの段階は、もともと連携を読み込まない)
            args.AddRange(ConnectorCatalog.BuildArgs(_settings.EnabledConnectors, knownConnectors));
        }
        foreach (var dir in _extraDirs)
        {
            args.Add("--add-dir");
            args.Add(dir);
        }
        if (!string.IsNullOrEmpty(conversationId))
        {
            args.Add("--resume");
            args.Add(conversationId);
        }
        return args;
    }
}
