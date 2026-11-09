using System.ComponentModel;
using System.Text.Json;
using NamakeKaigi.Models;

namespace NamakeKaigi.Services;

/// <summary>Codex CLI(codex exec --json)の出力を、1行ずつ読み取って状態にためる。</summary>
public sealed class CodexStreamState
{
    public string ThreadId { get; private set; } = "";
    public string Response { get; private set; } = "";
    public string Error { get; private set; } = "";

    /// <summary>この発言の答えが終わったか(成功・失敗とも)。</summary>
    public bool HasResult { get; private set; }

    /// <summary>1行を読み取る。JSONでない行は無視する。</summary>
    /// <param name="line">標準出力の1行</param>
    /// <param name="onUpdate">途中の知らせ(不要ならnull)</param>
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
                case "thread.started":
                    ThreadId = Str(root, "thread_id");
                    break;
                case "item.started" when root.TryGetProperty("item", out var started):
                    // コマンドを使い始めたら、いまの作業として知らせる
                    if (Str(started, "type") == "command_execution")
                        onUpdate?.Invoke(new AgentUpdate(AgentUpdateKind.Activity, "コマンドを実行しています"));
                    break;
                case "item.completed" when root.TryGetProperty("item", out var item):
                    if (Str(item, "type") == "agent_message")
                    {
                        // Codexは文字を少しずつは流さないので、言い終わった文をまとめて出す
                        var text = Str(item, "text");
                        Response = Response.Length > 0 ? Response + "\n\n" + text : text;
                        onUpdate?.Invoke(new AgentUpdate(AgentUpdateKind.Delta, Response.Length == text.Length ? text : "\n\n" + text));
                    }
                    else if (Str(item, "type") == "command_execution")
                    {
                        onUpdate?.Invoke(new AgentUpdate(AgentUpdateKind.Activity, "コマンドを実行しました"));
                    }
                    break;
                case "turn.completed":
                    HasResult = true;
                    break;
                case "turn.failed":
                    HasResult = true;
                    Error = root.TryGetProperty("error", out var err) ? Str(err, "message") : "失敗しました";
                    break;
                case "error":
                    Error = Str(root, "message");
                    break;
            }
        }
    }

    /// <summary>ためた状態から、完成した返答を作る。結果が届いていない・エラー・空なら例外にする。</summary>
    /// <returns>返答</returns>
    public AgentReply ToReply()
    {
        if (Error.Length > 0) throw new InvalidOperationException($"ChatGPT(Codex)がエラーを返しました: {Error}");
        if (!HasResult) throw new InvalidOperationException("ChatGPT(Codex)から返答が届きませんでした。");
        if (string.IsNullOrWhiteSpace(Response)) throw new InvalidOperationException("ChatGPT(Codex)の返答が空でした。");
        return new AgentReply(ReplyTextCleaner.StripLeadingRoleHeader(Response), ThreadId);
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}

/// <summary>
/// チャッピー(Codex CLI)。基本の参加者。段階(読む/読み書き/全部)とモデルを選べ、発言ごとに起動する。
/// 続きの会話は、会話ID(thread_id)で codex exec resume する。
/// </summary>
public sealed class CodexAgent : IChatAgent
{
    private readonly CodexSettings _settings;
    private readonly IProcessRunner _runner;
    private volatile string _workDir;
    private IReadOnlyList<string> _extraDirs = Array.Empty<string>();
    private TimeSpan _timeout;
    private bool _writeMode;
    private bool _fullMode;
    private string _model;
    private string _effort;

    /// <summary>モデルを指定しないとき(Codexの設定 ~/.codex/config.toml の既定を使う)の表示。</summary>
    public const string DefaultModelLabel = "Codexの既定";

    /// <summary>チャッピーを作る。</summary>
    /// <param name="settings">Codexの設定</param>
    /// <param name="runner">プロセス実行</param>
    /// <param name="workDir">作業フォルダ</param>
    /// <param name="timeout">タイムアウト</param>
    public CodexAgent(CodexSettings settings, IProcessRunner runner, string workDir, TimeSpan timeout)
    {
        _settings = settings;
        _runner = runner;
        _workDir = workDir;
        _timeout = timeout;
        _model = settings.Model?.Trim() ?? "";
        _effort = settings.Effort?.Trim() ?? "";
    }

    /// <summary>最後に取れたモデル一覧を残すファイル(nullなら残さない)。</summary>
    public string? ModelCachePath { get; init; }

    /// <inheritdoc />
    public string Id => Participants.ChappyId;

    /// <inheritdoc />
    public string Name => "ChatGPT";

    /// <inheritdoc />
    public bool CanWrite => true;

    /// <inheritdoc />
    public bool WriteMode
    {
        get => _writeMode;
        set
        {
            _writeMode = value;
            if (!value) _fullMode = false; // 書く担当でなくなったら、「何でもできる」も外す
        }
    }

    /// <inheritdoc />
    public string WriterWarning => "作業フォルダの中のファイルを書き換えられます(コマンドも、その中だけ。ネット接続・フォルダの外への書き込みはできません)";

    /// <inheritdoc />
    public bool CanFull => true;

    /// <inheritdoc />
    public bool FullMode
    {
        get => _fullMode;
        set => _fullMode = value && _writeMode; // 「何でもできる」は、書く担当のときだけ有効(クローディ・ジェミリーと同じ)
    }

    /// <inheritdoc />
    public string FullWarning => "制限なしです。コマンド・ネット接続・フォルダの外の操作も、確認なしでできます。戻せない操作に気をつけてください";

    /// <inheritdoc />
    public string ModelId => _model;

    /// <inheritdoc />
    public string ModelLabel => _model.Length > 0 ? _model : DefaultModelLabel;

    /// <inheritdoc />
    public void SetModel(string modelId) => _model = modelId.Trim();

    /// <inheritdoc />
    public string EffortId => _effort;

    /// <inheritdoc />
    public void SetEffort(string effortId) => _effort = effortId.Trim();

    /// <inheritdoc />
    /// <remarks>思考の深さは -c model_reasoning_effort=... で渡す(空なら渡さず、Codexの設定の既定を使う)。</remarks>
    public bool SupportsEffort => true;

    /// <inheritdoc />
    public void SetTimeout(TimeSpan timeout) => _timeout = timeout;

    /// <inheritdoc />
    public void SetWorkDir(string workDir) => _workDir = workDir;

    /// <inheritdoc />
    public void SetExtraDirs(IReadOnlyList<string> dirs) => _extraDirs = dirs;

    /// <inheritdoc />
    public string HarnessSummary => HarnessInfo.DescribeCodex(_settings, _writeMode, _fullMode);

    /// <inheritdoc />
    /// <remarks>先頭は「Codexの既定」。最後に取れた一覧があれば、その後ろに並べる。</remarks>
    public IReadOnlyList<ModelOption> ModelOptions => WithDefaultAndCurrent(ModelListCache.Load(ModelCachePath));

    /// <inheritdoc />
    /// <remarks>codex debug models で、アカウントで選べるモデルを取る(画面に出す扱いの visibility=list だけ)。取れなければ、いまの一覧のまま。</remarks>
    public async Task<IReadOnlyList<ModelOption>> LoadModelOptionsAsync(CancellationToken ct)
    {
        var json = new System.Text.StringBuilder();
        try
        {
            await _runner.RunLinesAsync(_settings.Executable, _settings.ModelListArgs ?? CodexSettings.DefaultModelListArgs,
                _workDir, _timeout, line => json.AppendLine(line), ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return ModelOptions; // 取れなかったときは、前に取れた一覧(なければ既定だけ)
        }
        var found = ParseModelList(json.ToString());
        if (found.Count == 0) return ModelOptions;
        ModelListCache.Save(ModelCachePath, found); // 次の起動と、取れなかったときのために残す
        return WithDefaultAndCurrent(found);
    }

    /// <summary>codex debug models の JSON から、画面に出す扱い(visibility が list)のモデルを、並び順(priority)で取り出す。</summary>
    /// <param name="json">codex debug models の出力</param>
    /// <returns>モデルの一覧(読めなければ空)</returns>
    public static IReadOnlyList<ModelOption> ParseModelList(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("models", out var models) || models.ValueKind != System.Text.Json.JsonValueKind.Array)
                return Array.Empty<ModelOption>();
            return models.EnumerateArray()
                .Where(m => Str(m, "visibility") == "list" && Str(m, "slug").Length > 0)
                .OrderBy(m => m.TryGetProperty("priority", out var p) && p.TryGetInt32(out var n) ? n : int.MaxValue)
                .Select(m => new ModelOption(Str(m, "slug"), Str(m, "display_name") is { Length: > 0 } name ? name : Str(m, "slug")))
                .ToList();
        }
        catch (System.Text.Json.JsonException)
        {
            return Array.Empty<ModelOption>();
        }

        static string Str(System.Text.Json.JsonElement e, string key) =>
            e.TryGetProperty(key, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() ?? "" : "";
    }

    /// <summary>先頭に「Codexの既定」を置き、いま選んでいるモデルが一覧になければ足す。</summary>
    private IReadOnlyList<ModelOption> WithDefaultAndCurrent(IReadOnlyList<ModelOption> found)
    {
        var options = new List<ModelOption> { new("", DefaultModelLabel) };
        options.AddRange(found.Where(o => o.Id.Length > 0));
        if (_model.Length > 0 && options.All(o => o.Id != _model)) options.Add(new ModelOption(_model, _model));
        return options;
    }

    /// <summary>いまの段階(何でもできる / 一部できる / 読むだけ)の、起動の追加の引数。古い書き方(--sandbox)は -c sandbox_mode=... に直す。</summary>
    private IReadOnlyList<string> ExtraArgs => NormalizeSandbox(
        _fullMode ? _settings.FullExtraArgs ?? CodexSettings.DefaultFullExtraArgs
        : _writeMode ? _settings.WriterExtraArgs ?? CodexSettings.DefaultWriterExtraArgs
        : _settings.ExtraArgs ?? CodexSettings.DefaultExtraArgs);

    /// <summary>
    /// 「--sandbox 値」(または「-s 値」)を「-c sandbox_mode=値」に置き換える。
    /// `codex exec resume`(2回目以降の発言)は --sandbox を受け付けず、-c なら初回も2回目も通るため。
    /// </summary>
    /// <param name="args">設定の引数</param>
    /// <returns>置き換えた引数</returns>
    public static IReadOnlyList<string> NormalizeSandbox(IReadOnlyList<string> args)
    {
        var result = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            if ((args[i] is "--sandbox" or "-s") && i + 1 < args.Count)
            {
                result.Add("-c");
                result.Add("sandbox_mode=" + args[i + 1]);
                i++;
            }
            else
            {
                result.Add(args[i]);
            }
        }
        return result;
    }

    /// <inheritdoc />
    public async Task<AgentReply> AskAsync(string prompt, string? conversationId, Action<AgentUpdate>? onUpdate,
        CancellationToken ct)
    {
        var state = new CodexStreamState();
        try
        {
            // 本文は標準入力で渡す(npm で入れた codex は .cmd なので、引数の改行や記号が壊れやすいため)
            await _runner.RunWithInputAsync(_settings.Executable, BuildArgs(conversationId), _workDir, _timeout,
                BuildInput(prompt), line => state.Feed(line, onUpdate), ct);
        }
        catch (Win32Exception)
        {
            throw new InvalidOperationException(
                $"Codex CLI({_settings.Executable})が見つかりません。インストールとログイン(codex login)が済んでいるか確かめてください。");
        }
        catch (InvalidOperationException) when (state.HasResult && state.Error.Length == 0)
        {
            // 答えまで届いていれば、終了コードの違いは無視して、結果の内容で判断する
        }
        return state.ToReply();
    }

    /// <summary>標準入力に渡す本文(先頭に、いまの状態の知らせを添える)。</summary>
    /// <param name="prompt">送る本文</param>
    /// <returns>渡す文字</returns>
    public string BuildInput(string prompt) => (
        _fullMode ? _settings.FullNote ?? CodexSettings.DefaultFullNote
        : _writeMode ? _settings.WriterNote ?? CodexSettings.DefaultWriterNote
        : _settings.Note ?? CodexSettings.DefaultNote) + prompt;

    /// <summary>CLIに渡す引数を組み立てる(本文は「-」=標準入力から読む)。続きの会話なら codex exec resume を使う。</summary>
    /// <param name="conversationId">続きの会話ID(なければnull)</param>
    /// <returns>引数の並び</returns>
    public IReadOnlyList<string> BuildArgs(string? conversationId)
    {
        var args = new List<string> { "exec" };
        if (!string.IsNullOrEmpty(conversationId)) args.AddRange(new[] { "resume", conversationId });
        args.Add("--json");
        if (ModelId.Length > 0) args.AddRange(new[] { "--model", ModelId });
        // 値に引用符を付けない(Windowsの .cmd 経由で崩れるのを避ける。TOMLとして読めない値は、そのまま文字列として使われる)
        if (_effort.Length > 0) args.AddRange(new[] { "-c", $"model_reasoning_effort={_effort}" });
        args.AddRange(ExtraArgs);
        foreach (var dir in _extraDirs) args.AddRange(new[] { "--add-dir", dir });
        args.Add("-");
        return args;
    }
}
