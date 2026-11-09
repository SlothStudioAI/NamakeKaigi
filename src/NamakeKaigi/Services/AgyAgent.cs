using System.Text.Json;
using NamakeKaigi.Models;

namespace NamakeKaigi.Services;

/// <summary>agyの流れてくる出力(stream-json)を、1行ずつ読み取って状態にためる。</summary>
public sealed class AgyStreamState
{
    public string ConversationId { get; private set; } = "";
    public string Response { get; private set; } = "";
    public string Status { get; private set; } = "";
    public string Error { get; private set; } = "";

    private string _lastActivity = "";

    /// <summary>最終結果の行まで届いたか。</summary>
    public bool HasResult { get; private set; }

    /// <summary>1行を読み取る。JSONでない行は無視する。</summary>
    /// <param name="line">標準出力の1行</param>
    /// <param name="onUpdate">続きの文字が届いたら知らせる処理(不要ならnull)</param>
    public void Feed(string line, Action<AgentUpdate>? onUpdate)
    {
        if (!line.StartsWith('{')) return;
        JsonDocument doc;
        try { doc = JsonDocument.Parse(line); }
        catch (JsonException) { return; }

        using (doc)
        {
            var root = doc.RootElement;
            switch (Str(root, "event"))
            {
                case "init":
                    ConversationId = Str(root, "conversation_id");
                    break;
                case "step_update":
                    if (!root.TryGetProperty("step_update", out var su)) break;
                    switch (Str(su, "step_type"))
                    {
                        case "agent_response":
                            var delta = Str(su, "text_delta");
                            if (delta.Length > 0) onUpdate?.Invoke(new AgentUpdate(AgentUpdateKind.Delta, delta));
                            break;
                        case "tool":
                            // 道具(ファイルを読む等)の開始と終了を、いまの作業として知らせる
                            if (Str(su, "state") == "ACTIVE")
                            {
                                string? target = null;
                                if (su.TryGetProperty("tool_info", out var info) && info.TryGetProperty("parameters", out var p))
                                    target = ActivityText.FindTarget(p);
                                _lastActivity = ActivityText.Describe(Str(su, "tool_name"), target);
                                onUpdate?.Invoke(new AgentUpdate(AgentUpdateKind.Activity, _lastActivity));
                            }
                            else if (Str(su, "state") == "DONE")
                            {
                                // 「〜しました」にして残す(次の動きまで、表示が消えないように)
                                onUpdate?.Invoke(new AgentUpdate(AgentUpdateKind.Activity, ActivityText.ToPast(_lastActivity)));
                            }
                            break;
                    }
                    break;
                case "result" when root.TryGetProperty("result", out var r):
                    HasResult = true;
                    Response = Str(r, "response");
                    Status = Str(r, "status");
                    Error = Str(r, "error");
                    var id = Str(r, "conversation_id");
                    if (id.Length > 0) ConversationId = id;
                    break;
            }
        }
    }

    /// <summary>ためた状態から、完成した返答を作る。結果が届いていない・エラー・空なら例外にする。</summary>
    /// <returns>返答</returns>
    public AgentReply ToReply()
    {
        if (!HasResult) throw new InvalidOperationException("Geminiから返答が届きませんでした。");
        if (Status == "ERROR" || Error.Length > 0)
            throw new InvalidOperationException($"Geminiがエラーを返しました: {Error}");
        if (string.IsNullOrWhiteSpace(Response))
            throw new InvalidOperationException("Geminiの返答が空でした(ツールの権限が拒否された可能性があります)。");
        var cleaned = ReplyTextCleaner.StripLeadingRoleHeader(Response);
        return new AgentReply(cleaned, ConversationId);
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}

/// <summary>agy(Antigravity CLI)で答えるAI(Gemini側)。</summary>
public sealed class AgyAgent : IChatAgent
{
    /// <summary>モデルを指定していないときの表示。</summary>
    public const string UnspecifiedModelLabel = "未指定(agyの既定)";

    private AgySettings _settings;
    private readonly IProcessRunner _runner;
    private volatile string _workDir;
    private IReadOnlyList<string> _extraDirs = Array.Empty<string>();
    private volatile bool _writeMode;
    private volatile bool _fullMode;
    private TimeSpan _timeout;
    private volatile string _model;
    private volatile string _effort;

    /// <summary>Gemini用のAIを作る。</summary>
    /// <param name="settings">agyの設定</param>
    /// <param name="runner">プロセス実行</param>
    /// <param name="workDir">作業フォルダ</param>
    /// <param name="timeout">タイムアウト</param>
    public AgyAgent(AgySettings settings, IProcessRunner runner, string workDir, TimeSpan timeout)
    {
        _settings = settings;
        _runner = runner;
        _workDir = workDir;
        _timeout = timeout;
        _model = settings.Model?.Trim() ?? "";
        _effort = settings.Effort?.Trim() ?? "";
        _kept = new KeptCli(runner);
    }

    private readonly KeptCli _kept;

    /// <inheritdoc />
    public void SetTimeout(TimeSpan timeout) => _timeout = timeout;

    /// <summary>いまのagyの設定。</summary>
    public AgySettings Settings => _settings;

    /// <summary>ハーネス(起動引数・注意文)の設定を更新する(次の発言から効く)。</summary>
    /// <param name="settings">新しい設定</param>
    public void UpdateSettings(AgySettings settings) => _settings = settings;

    /// <inheritdoc />
    public string HarnessSummary => HarnessInfo.DescribeAgy(_settings, _writeMode, _fullMode);

    /// <inheritdoc />
    public void SetWorkDir(string workDir) => _workDir = workDir;

    /// <inheritdoc />
    public void SetExtraDirs(IReadOnlyList<string> dirs) => _extraDirs = dirs;

    /// <inheritdoc />
    public bool CanWrite => _settings.WriterExtraArgs is { Count: > 0 };

    /// <inheritdoc />
    public bool WriteMode
    {
        get => _writeMode;
        set
        {
            _writeMode = value && CanWrite;
            if (!_writeMode) _fullMode = false; // 書く担当でなくなったら、「何でもできる」も外す
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
    public string WriterWarning => _settings.WriterWarning ?? "";

    /// <inheritdoc />
    public string FullWarning => _settings.FullWarning ?? "";

    /// <inheritdoc />
    public string Id => Participants.GeminiId;

    /// <inheritdoc />
    public string Name => "Gemini";

    /// <inheritdoc />
    public string ModelId => _model;

    /// <inheritdoc />
    public string ModelLabel => _model.Length == 0 ? UnspecifiedModelLabel : _model;

    /// <inheritdoc />
    public void SetModel(string modelId) => _model = modelId.Trim();

    /// <inheritdoc />
    public string EffortId => _effort;

    /// <inheritdoc />
    public void SetEffort(string effortId) => _effort = effortId.Trim();

    /// <inheritdoc />
    public bool SupportsEffort => false;

    /// <summary>最後に取れたモデル一覧を残すファイル(nullなら残さず、設定にある一覧だけを使う)。</summary>
    public string? ModelCachePath { get; init; }

    /// <inheritdoc />
    /// <remarks>最後に取れた一覧(表示名つき)があれば、それを使う。なければ設定にある一覧。</remarks>
    public IReadOnlyList<ModelOption> ModelOptions
    {
        get
        {
            var cached = ModelListCache.Load(ModelCachePath);
            if (cached.Count > 0) return WithCurrent(cached.ToList());
            return (_settings.Models ?? Array.Empty<string>()).Append(_model)
                .Where(m => m.Length > 0).Distinct().Select(m => new ModelOption(m, m)).ToList();
        }
    }

    /// <summary>一覧に、いま選んでいるモデルがなければ足す。</summary>
    private List<ModelOption> WithCurrent(List<ModelOption> options)
    {
        if (_model.Length > 0 && options.All(o => o.Id != _model)) options.Add(new ModelOption(_model, _model));
        return options;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ModelOption>> LoadModelOptionsAsync(CancellationToken ct)
    {
        var found = new List<ModelOption>();
        try
        {
            // "agy models" は、1行ごとに「名前(タブ)表示名」を出す。最初の「Fetching…」の行は読み飛ばす
            await _runner.RunLinesAsync(_settings.Executable, new[] { "models" }, _workDir, _timeout, line =>
            {
                var parts = line.Split('\t', 2);
                if (parts.Length == 2 && parts[0].Trim().Length > 0)
                    found.Add(new ModelOption(parts[0].Trim(), parts[1].Trim()));
            }, ct);
        }
        catch (Exception) when (found.Count == 0)
        {
            // 取れなかったときは、設定にある一覧を使う
        }
        if (found.Count == 0) return ModelOptions;
        ModelListCache.Save(ModelCachePath, found); // 次の起動と、取れなかったときのために残す
        return WithCurrent(found);
    }

    /// <inheritdoc />
    public async Task<AgentReply> AskAsync(string prompt, string? conversationId, Action<AgentUpdate>? onUpdate,
        CancellationToken ct)
    {
        var state = new AgyStreamState();
        var kept = KeepAlive && _runner.SupportsSessions;
        try
        {
            if (kept)
            {
                // 起動したままのagyに、標準入力で発言を送る(起動のたびに出る黒い画面も、最初の1回だけになる)
                await _kept.RunTurnAsync(_settings.Executable, BuildArgs(prompt, null, viaInput: true),
                    new[] { "--conversation", conversationId ?? "" }, _workDir, conversationId,
                    BuildUserMessage(Note + prompt), _timeout,
                    (line, _) =>
                    {
                        state.Feed(line, onUpdate);
                        return Task.FromResult(state.HasResult);
                    }, ct);
            }
            else
            {
                await _runner.RunLinesAsync(_settings.Executable, BuildArgs(prompt, conversationId),
                    _workDir, _timeout, line => state.Feed(line, onUpdate), ct);
            }
        }
        catch (InvalidOperationException) when (state.HasResult)
        {
            // 結果の行まで届いていれば、終了コードの違いは無視して、結果の内容で判断する
        }
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

    /// <summary>発言を、agyの標準入力へ送る1行(stream-json)にする。</summary>
    /// <param name="text">発言の本文</param>
    /// <returns>送る1行(例: {"event":"user","message":{"role":"user","content":"…"}})</returns>
    public static string BuildUserMessage(string text) =>
        JsonSerializer.Serialize(new { @event = "user", message = new { role = "user", content = text } },
            new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    /// <summary>いまの段階に合わせて、本文の先頭に添える頼み(読み取り専用・書き換えの範囲など)。</summary>
    private string Note =>
        _fullMode && !string.IsNullOrEmpty(_settings.FullNote) ? _settings.FullNote
        : _writeMode ? _settings.WriterNote ?? "" : _settings.NoToolNote;

    /// <summary>CLIに渡す引数を組み立てる(本文の先頭に「ツールは使わない」の添え書きを付ける)。</summary>
    /// <param name="prompt">送る本文</param>
    /// <param name="conversationId">続きの会話ID(なければnull)</param>
    /// <param name="viaInput">本文を引数でなく標準入力で送るならtrue(起動したままにするとき。本文は引数に入れない)</param>
    /// <returns>引数の並び</returns>
    public IReadOnlyList<string> BuildArgs(string prompt, string? conversationId, bool viaInput = false)
    {
        // 読み取り専用のときは「ツールは使わない」、書く担当のときは書く範囲の頼み(あれば)を、本文の先頭に添える。
        // 標準入力で送るときは、-p の値を空にする(-p の直後の引数は本文と見なされるため)
        var args = viaInput
            ? new List<string> { "-p=", "--input-format", "stream-json", "--output-format", "stream-json" }
            : new List<string> { "-p", Note + prompt, "--output-format", "stream-json" };
        if (_model.Length > 0)
        {
            args.Add("--model");
            args.Add(_model);
        }
        // 労力は渡さない(agyのモデル名に、すでに労力が含まれているため。例: gemini-3.8-flash-medium)
        // 何でもできる=許可の確認をすべて飛ばす / 書き換えできる=--mode accept-edits / 読むだけ=--mode plan
        args.AddRange(_fullMode ? _settings.FullExtraArgs!
            : _writeMode ? _settings.WriterExtraArgs! : _settings.ExtraArgs);
        foreach (var dir in _extraDirs)
        {
            args.Add("--add-dir");
            args.Add(dir);
        }
        if (!string.IsNullOrEmpty(conversationId))
        {
            args.Add("--conversation");
            args.Add(conversationId);
        }
        return args;
    }
}
