using NamakeKaigi.Models;

namespace NamakeKaigi.Services;

/// <summary>設定から、新しい会議または既存の会議(ChatSession)を作る。</summary>
public sealed class ChatSessionFactory
{
    private AppSettings _settings;
    private readonly IProcessRunner _runner;
    private readonly Action<string>? _saveWorkDir;
    private readonly Action<ClaudeSettings>? _saveClaude;
    private readonly Action<AgySettings>? _saveAgy;
    private readonly Action<string, string, string>? _saveModelEffort;
    private readonly Action<string>? _saveTheme;
    private readonly Action<AppearanceSettings>? _saveAppearance;
    private readonly Action<int>? _saveTimeout;
    private readonly Action<string>? _saveAllOrder;
    private string _workDir; // 直前に選ばれた作業フォルダ(新しい会議も、これで始める)
    private readonly DateTime _launchedAt = DateTime.Now; // アプリを起動した時刻(この起動で作る会議は、この時刻のフォルダにまとめる)

    /// <summary>工場を作る。</summary>
    /// <param name="settings">アプリ設定</param>
    /// <param name="runner">プロセス実行</param>
    /// <param name="saveWorkDir">作業フォルダが変わったときに保存する処理</param>
    /// <param name="saveClaude">Claudeのハーネス設定が変わったときに保存する処理</param>
    /// <param name="saveAgy">agyのハーネス設定が変わったときに保存する処理</param>
    /// <param name="saveModelEffort">モデル・労力が変わったときに保存する処理(agentSection, modelId, effortId)</param>
    /// <param name="saveTheme">配色テーマが変わったときに保存する処理</param>
    /// <param name="saveAppearance">簡易表示モード・表示名設定が変わったときに保存する処理</param>
    /// <param name="saveTimeout">CLIの制限時間(秒)が変わったときに保存する処理</param>
    /// <param name="saveAllOrder">「全員」宛ての答える順番が変わったときに保存する処理</param>
    public ChatSessionFactory(AppSettings settings, IProcessRunner runner, Action<string>? saveWorkDir = null,
        Action<ClaudeSettings>? saveClaude = null, Action<AgySettings>? saveAgy = null,
        Action<string, string, string>? saveModelEffort = null,
        Action<string>? saveTheme = null,
        Action<AppearanceSettings>? saveAppearance = null,
        Action<int>? saveTimeout = null,
        Action<string>? saveAllOrder = null)
    {
        _saveTimeout = saveTimeout;
        _saveAllOrder = saveAllOrder;
        _settings = settings;
        _runner = runner;
        _saveWorkDir = saveWorkDir;
        _saveClaude = saveClaude;
        _saveAgy = saveAgy;
        _saveModelEffort = saveModelEffort;
        _saveTheme = saveTheme;
        _saveAppearance = saveAppearance;
        _workDir = settings.WorkDir;
    }

    /// <summary>新しい会議を始める(会議フォルダとログを作る)。</summary>
    /// <returns>会議の進行役</returns>
    public IChatSession Create()
    {
        // 置き場所は 保存先/起動した時刻/会議名/(フォルダは最初の発言のときに作る)
        var log = new MarkdownLogWriter(MarkdownLogWriter.SessionDirFor(_settings.MeetingsDir, _launchedAt), DateTime.Now);
        return CreateCore(log, restoredState: null);
    }

    /// <summary>保存されている過去の会議一覧(新しい順)を返す。</summary>
    public IReadOnlyList<SavedMeetingSummary> ListPastMeetings() =>
        MeetingStore.ListMeetings(_settings.MeetingsDir);

    /// <summary>過去の会議を開き直して、続きから話せる進行役を作る。</summary>
    /// <param name="folderPath">会議の保存場所(新しい形式はログ(.md)のパス、古い形式は会議フォルダのパス)</param>
    public IChatSession OpenExisting(string folderPath)
    {
        var restored = MeetingStore.LoadState(folderPath);
        if (!string.IsNullOrWhiteSpace(restored.WorkDir) && System.IO.Directory.Exists(restored.WorkDir))
        {
            _workDir = restored.WorkDir;
        }
        var log = new MarkdownLogWriter(folderPath, restored.StartedAt, reopen: true);
        return CreateCore(log, restored);
    }

    private IChatSession CreateCore(MarkdownLogWriter log, MeetingState? restoredState)
    {
        var timeout = TimeSpan.FromSeconds(_settings.TimeoutSec);
        IChatAgent[] agents =
        {
            new ClaudeAgent(_settings.Claude, _runner, _workDir, timeout) { KeepAlive = _settings.KeepAlive },
            new AgyAgent(_settings.Agy, _runner, _workDir, timeout)
            {
                KeepAlive = _settings.KeepAlive,
                ModelCachePath = ModelListCache.DefaultAgyPath(),
            },
            new CodexAgent(_settings.Codex ?? new CodexSettings(), _runner, _workDir, timeout)
            {
                ModelCachePath = ModelListCache.DefaultCodexPath(),
            },
        };
        return new ChatSession(agents, log, _settings.MaxInjectChars, workDir: _workDir,
            onWorkDirChanged: dir =>
            {
                _workDir = dir;
                _saveWorkDir?.Invoke(dir);
            },
            onClaudeHarnessChanged: claude =>
            {
                _settings = _settings with { Claude = claude };
                _saveClaude?.Invoke(claude);
            },
            onAgyHarnessChanged: agy =>
            {
                _settings = _settings with { Agy = agy };
                _saveAgy?.Invoke(agy);
            },
            restoredState: restoredState,
            listPastMeetings: ListPastMeetings,
            openPastMeeting: OpenExisting,
            onModelOrEffortChanged: (agentId, modelId, effortId) =>
            {
                if (agentId == Participants.ClaudeId)
                {
                    _settings = _settings with { Claude = _settings.Claude with { Model = modelId, Effort = effortId } };
                    _saveModelEffort?.Invoke("claude", modelId, effortId);
                }
                else if (agentId == Participants.GeminiId)
                {
                    _settings = _settings with { Agy = _settings.Agy with { Model = modelId, Effort = effortId } };
                    _saveModelEffort?.Invoke("agy", modelId, effortId);
                }
                else if (agentId == Participants.ChappyId)
                {
                    var codex = _settings.Codex ?? new CodexSettings();
                    _settings = _settings with { Codex = codex with { Model = modelId, Effort = effortId } };
                    _saveModelEffort?.Invoke("codex", modelId, effortId);
                }
            },
            themeId: _settings.Theme ?? ThemeCatalog.DefaultThemeId,
            onThemeChanged: themeId =>
            {
                _settings = _settings with { Theme = themeId };
                _saveTheme?.Invoke(themeId);
            },
            appearance: _settings.Appearance ?? AppearanceSettings.Default,
            onAppearanceChanged: appearance =>
            {
                _settings = _settings with { Appearance = appearance };
                _saveAppearance?.Invoke(appearance);
            },
            timeoutSec: _settings.TimeoutSec,
            onTimeoutChanged: sec =>
            {
                _settings = _settings with { TimeoutSec = sec };
                _saveTimeout?.Invoke(sec);
            },
            allOrder: AllOrders.Normalize(_settings.AllOrder),
            onAllOrderChanged: order =>
            {
                _settings = _settings with { AllOrder = order };
                _saveAllOrder?.Invoke(order);
            },
            relay: _settings.Relay,
            callNames: _settings.CallNames);
    }
}
