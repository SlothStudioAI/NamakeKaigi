using System.Collections.ObjectModel;
using NamakeKaigi.Models;

namespace NamakeKaigi.ViewModels;

/// <summary>左のサイドバーに並ぶ参加者1人分(名前・アバター・モデル・状態)。</summary>
public sealed class MemberViewModel : ViewModelBase
{
    private readonly Action<string, string>? _onModelChosen;
    private readonly Action<string, string>? _onEffortChosen;
    private int _pendingCount;
    private bool _isAnswering;
    private string _modelText = "";
    private string _activityText = "";
    private string _harnessTooltip = "";
    private string? _selectedModelId;
    private string _selectedEffortId = EffortOption.DefaultId;
    private bool _settingOptions;

    /// <summary>参加者の表示を作る。</summary>
    /// <param name="participant">参加者</param>
    /// <param name="onModelChosen">画面でモデルが選ばれたときに呼ぶ処理(引数: AIの識別名, モデル名)。人間には不要</param>
    /// <param name="onWriterChanged">「書く担当」の指名が変わったときに呼ぶ処理(引数: AIの識別名, 書く担当になったか)</param>
    /// <param name="onCommandsChanged">「コマンドも使う」が変わったときに呼ぶ処理(引数: AIの識別名, 使うか)</param>
    /// <param name="onEffortChosen">画面で労力が選ばれたときに呼ぶ処理(引数: AIの識別名, 労力ID)</param>
    /// <param name="onFullChanged">「何でもできる」が変わったときに呼ぶ処理(引数: AIの識別名, 何でもできるか)</param>
    /// <param name="onPresenceChanged">「参加する」が画面で変えられたときに呼ぶ処理(引数: AIの識別名, 参加するか)</param>
    public MemberViewModel(Participant participant, Action<string, string>? onModelChosen = null,
        Action<string, bool>? onWriterChanged = null, Action<string, bool>? onCommandsChanged = null,
        Action<string, string>? onEffortChosen = null, Action<string, bool>? onFullChanged = null,
        Action<string, bool>? onPresenceChanged = null)
    {
        Participant = participant;
        _displayName = participant.DisplayName;
        _onModelChosen = onModelChosen;
        _onWriterChanged = onWriterChanged;
        _onCommandsChanged = onCommandsChanged;
        _onEffortChosen = onEffortChosen;
        _onFullChanged = onFullChanged;
        _onPresenceChanged = onPresenceChanged;
    }

    private readonly Action<string, bool>? _onPresenceChanged;
    private bool _isPresent = true;
    private bool _canChangePresence = true;

    /// <summary>このAIのCLIが、このPCで見つかったか(見つからなければ、カードを出さず、会議にも参加させない)。</summary>
    public bool IsInstalled { get; init; } = true;

    /// <summary>「参加する」の欄を出すか(ユーザー本人には出さない。AIとゲストだけ)。</summary>
    public bool CanChoosePresence => Participant.Id != Participants.UserId;

    /// <summary>この会議に参加しているか(ユーザー本人は常に参加)。外すと、宛先・「全員」・査読の相手から外れる。</summary>
    public bool IsPresent
    {
        get => _isPresent;
        set
        {
            if (!Set(ref _isPresent, value)) return;
            Raise(nameof(StatusText));
            _onPresenceChanged?.Invoke(Participant.Id, value);
        }
    }

    /// <summary>参加の切り替えを受け付けるか(会議が始まったあと・最後の1人のときは変えられない)。</summary>
    public bool CanChangePresence
    {
        get => _canChangePresence;
        set => Set(ref _canChangePresence, value);
    }

    private string _presenceTooltip = DefaultPresenceTooltip;

    /// <summary>通常の「参加する」の説明。</summary>
    public const string DefaultPresenceTooltip =
        "チェックを外すと、この会議には参加しません(宛先・「全員」・査読の相手から外れます)。会議を始める前だけ選べます";

    /// <summary>「参加する」の欄にマウスを乗せたときの説明(変えられない理由があれば、その理由)。</summary>
    public string PresenceTooltip
    {
        get => _presenceTooltip;
        set => Set(ref _presenceTooltip, value);
    }

    /// <summary>参加の状態を、会議に伝えずに切り替える(会議の切り替えや、1人も参加しない状態に戻すとき)。</summary>
    /// <param name="value">参加するならtrue</param>
    public void SetPresentSilently(bool value)
    {
        if (!Set(ref _isPresent, value, nameof(IsPresent))) return;
        Raise(nameof(StatusText));
    }

    private readonly Action<string, bool>? _onFullChanged;
    private bool _canFull;
    private bool _isFull;

    /// <summary>「何でもできる」にできるか(できないAIは、読むだけ / 書き換えできる の2段階)。</summary>
    public bool CanFull
    {
        get => _canFull;
        set
        {
            if (!Set(ref _canFull, value)) return;
            Raise(nameof(LevelOptions));
            if (!value && _isFull) SetFull(false);
        }
    }

    /// <summary>いま「何でもできる」か。</summary>
    public bool IsFull => _isFull;

    /// <summary>選べる段階の一覧(読むだけ / 一部できる / 何でもできる)。</summary>
    public IReadOnlyList<PermissionLevelOption> LevelOptions => PermissionLevelOption.For(_canFull, Participant.Id == Participants.GeminiId);

    /// <summary>画面で選ばれている段階(read / partial / full)。選び直すと、次の発言から効く。</summary>
    public string SelectedLevelId
    {
        get => _isFull ? PermissionLevelOption.FullId
            : _isWriter ? PermissionLevelOption.PartialId
            : PermissionLevelOption.ReadId;
        set
        {
            if (string.IsNullOrEmpty(value) || value == SelectedLevelId) return;
            // 先に書く担当を決めてから、何でもできるを決める(何でもできるは、書く担当のときだけ効くため)
            IsWriter = value != PermissionLevelOption.ReadId;
            SetFull(value == PermissionLevelOption.FullId && _canFull);
            Raise(nameof(SelectedLevelId));
        }
    }

    /// <summary>いまの段階の名前(読むだけ / 一部できる / 書き換えできる / 何でもできる)。</summary>
    public string LevelLabel =>
        LevelOptions.FirstOrDefault(o => o.Id == SelectedLevelId)?.Label ?? LevelOptions[0].Label;

    /// <summary>「何でもできる」を切り替えて、会議に伝える。</summary>
    /// <param name="value">何でもできるにするならtrue</param>
    private void SetFull(bool value)
    {
        if (!Set(ref _isFull, value, nameof(IsFull))) return;
        Raise(nameof(DisplayWarning));
        Raise(nameof(ShowWriterWarning));
        Raise(nameof(ShowCommandChoice));
        Raise(nameof(SelectedLevelId));
        _onFullChanged?.Invoke(Participant.Id, value);
    }

    private readonly Action<string, bool>? _onCommandsChanged;
    private bool _canRunCommands;
    private bool _commandsOn;

    /// <summary>決まったコマンドも使える持ち物にできるか。</summary>
    public bool CanRunCommands
    {
        get => _canRunCommands;
        set
        {
            if (!Set(ref _canRunCommands, value)) return;
            Raise(nameof(ShowCommandChoice));
        }
    }

    /// <summary>「コマンドも使う」か(書く担当のときだけ選べる。次の発言から効く)。</summary>
    public bool CommandsOn
    {
        get => _commandsOn;
        set
        {
            if (!Set(ref _commandsOn, value)) return;
            _onCommandsChanged?.Invoke(Participant.Id, value);
        }
    }

    /// <summary>「コマンドも使う」の選択欄を見せるか(一部できるで、コマンドを使える持ち物があるとき)。</summary>
    public bool ShowCommandChoice => _canRunCommands && _isWriter && !_isFull;

    private readonly Action<string, bool>? _onWriterChanged;
    private bool _canWrite;
    private bool _isWriter;

    /// <summary>「書く担当」になれるか(なれないAIには、選ぶ欄を出さない)。</summary>
    public bool CanWrite
    {
        get => _canWrite;
        set => Set(ref _canWrite, value);
    }

    private string _writerWarning = "";

    /// <summary>書く担当にしたときの注意(例: 作業フォルダの外にも書ける)。なければ空。</summary>
    public string WriterWarning
    {
        get => _writerWarning;
        set
        {
            if (!Set(ref _writerWarning, value)) return;
            Raise(nameof(DisplayWarning));
            Raise(nameof(ShowWriterWarning));
        }
    }

    private string _fullWarning = "";

    /// <summary>「何でもできる」にしたときの注意(判定がなく、止まらないこと)。なければ空。</summary>
    public string FullWarning
    {
        get => _fullWarning;
        set
        {
            if (!Set(ref _fullWarning, value)) return;
            Raise(nameof(DisplayWarning));
            Raise(nameof(ShowWriterWarning));
        }
    }

    /// <summary>いまの段階に合わせて画面に出す注意(何でもできるなら、その注意。そうでなければ書く担当の注意)。</summary>
    public string DisplayWarning => _isFull && _fullWarning.Length > 0 ? _fullWarning : _writerWarning;

    /// <summary>書く担当になっていて、見せる注意があるなら、注意を見せる。</summary>
    public bool ShowWriterWarning => _isWriter && DisplayWarning.Length > 0;

    /// <summary>「書く担当」か(ファイルを書き換えられる。次の発言から効く)。</summary>
    public bool IsWriter
    {
        get => _isWriter;
        set
        {
            if (!Set(ref _isWriter, value)) return;
            Raise(nameof(ShowWriterWarning));
            Raise(nameof(ShowCommandChoice));
            Raise(nameof(SelectedLevelId));
            _onWriterChanged?.Invoke(Participant.Id, value);
            if (value) return;
            CommandsOn = false; // 書く担当をやめたら、コマンドも「何でもできる」も使わせない
            SetFull(false);
        }
    }

    /// <summary>「書く担当」の指名を、通知を出さずに入れ替える(会議の切り替えや、他のAIが指名されたとき)。</summary>
    /// <param name="value">書く担当か</param>
    public void SetWriterSilently(bool value)
    {
        if (!Set(ref _isWriter, value, nameof(IsWriter))) return;
        Raise(nameof(ShowWriterWarning));
        Raise(nameof(ShowCommandChoice));
        if (!value)
        {
            Set(ref _commandsOn, false, nameof(CommandsOn));
            Set(ref _isFull, false, nameof(IsFull));
        }
        Raise(nameof(SelectedLevelId));
    }

    /// <summary>いま有効なハーネス(使えるツール・許可コマンド)のツールチップ説明文。</summary>
    public string HarnessTooltip
    {
        get => _harnessTooltip;
        set => Set(ref _harnessTooltip, value);
    }

    /// <summary>ハーネス(ツール・許可の範囲)を確認・編集できる参加者か(AIのみ)。</summary>
    public bool CanEditHarness => Participant.Id != Participants.UserId && Participant.HasControls;

    private string _displayName;
    private bool _isBusinessMode;

    public Participant Participant { get; }

    /// <summary>画面に出す名前(簡易表示モードや名前変更に連動する)。</summary>
    public string DisplayName
    {
        get => _displayName;
        private set
        {
            if (!Set(ref _displayName, value)) return;
            Raise(nameof(InitialText));
        }
    }

    public string SubTitle => Participant.SubTitle;
    public string AvatarUri => Participant.AvatarUri;

    /// <summary>簡易表示モード(キャラ絵を隠して頭文字バッジにする)ならtrue。</summary>
    public bool IsBusinessMode
    {
        get => _isBusinessMode;
        private set
        {
            if (!Set(ref _isBusinessMode, value)) return;
            Raise(nameof(ShowImageAvatar));
            Raise(nameof(ShowInitialAvatar));
        }
    }

    // 絵がない参加者(ゲストのチャッピーなど)は、ふだんも頭文字のバッジで出す
    public bool ShowImageAvatar => !_isBusinessMode && Participant.HasAvatar;
    public bool ShowInitialAvatar => !ShowImageAvatar;
    public string InitialText => AppearanceSettings.ResolveInitial(Participant.Id, _displayName);

    /// <summary>簡易表示モードや名前変更の設定を反映する。</summary>
    public void ApplyAppearance(AppearanceSettings appearance)
    {
        DisplayName = appearance.ResolveParticipantName(Participant.Id);
        IsBusinessMode = appearance.IsBusinessMode;
    }

    /// <summary>選べるモデルの一覧(AIだけ)。</summary>
    public ObservableCollection<ModelOption> ModelOptions { get; } = new();

    /// <summary>選べる労力の一覧(低/中/高/超高/最大。AIだけ)。</summary>
    public IReadOnlyList<EffortOption> EffortOptions => EffortOption.All;

    /// <summary>画面で選ばれている労力(CLIの --effort に渡す値)。選び直すと、次の発言から効く。</summary>
    public string SelectedEffortId
    {
        get => _selectedEffortId;
        set
        {
            if (string.IsNullOrEmpty(value) || !Set(ref _selectedEffortId, value)) return;
            Raise(nameof(ModelSummary));
            if (!_settingOptions) _onEffortChosen?.Invoke(Participant.Id, value);
        }
    }

    /// <summary>労力の選択状態を、通知を出さずに反映する。</summary>
    /// <param name="effortId">労力ID(空なら既定の medium)</param>
    public void SetEffortSilently(string effortId)
    {
        var id = string.IsNullOrWhiteSpace(effortId) ? EffortOption.DefaultId : effortId.Trim();
        Set(ref _selectedEffortId, id, nameof(SelectedEffortId));
        Raise(nameof(ModelSummary));
    }

    /// <summary>モデルを選べるか(AIだけ。人間は選べない)。</summary>
    public bool CanChooseModel => Participant.Id != Participants.UserId && Participant.HasControls;

    private bool _canChooseEffort = true;

    /// <summary>労力をモデルと別に選べるか(ジェミリーはモデル名に労力が含まれるので、選べない)。</summary>
    public bool CanChooseEffort
    {
        get => _canChooseEffort;
        set
        {
            if (!Set(ref _canChooseEffort, value)) return;
            Raise(nameof(ModelSummary));
            Raise(nameof(ShowVariantChoice));
            Raise(nameof(ShowSingleModelChoice));
        }
    }

    /// <summary>画面で選ばれているモデルの名前(CLIの --model に渡す名前)。選び直すと、次の発言から効く。</summary>
    public string? SelectedModelId
    {
        get => _selectedModelId;
        set
        {
            if (!Set(ref _selectedModelId, value)) return;
            SyncVariantSelection();
            if (!_settingOptions && !string.IsNullOrEmpty(value)) _onModelChosen?.Invoke(Participant.Id, value);
        }
    }

    /// <summary>選べるモデルの一覧を入れ替え、いま選ばれているモデルを選んだ状態にする(選び直しの通知は出さない)。</summary>
    /// <param name="options">モデルの一覧</param>
    /// <param name="currentId">いま選ばれているモデルの名前(空なら、選んでいない状態)</param>
    public void SetOptions(IEnumerable<ModelOption> options, string currentId)
    {
        _settingOptions = true;
        try
        {
            ModelOptions.Clear();
            foreach (var o in options) ModelOptions.Add(o);
            _variants = ModelVariant.SplitAll(ModelOptions);
            ModelFamilies.Clear();
            foreach (var f in _variants.Select(v => v.Family).Distinct()) ModelFamilies.Add(f);
            Raise(nameof(ShowVariantChoice));
            Raise(nameof(ShowSingleModelChoice));
            SelectedModelId = currentId.Length == 0 ? null : currentId;
            SyncVariantSelection();
        }
        finally
        {
            _settingOptions = false;
        }
    }

    private IReadOnlyList<ModelVariant> _variants = Array.Empty<ModelVariant>();
    private string? _selectedFamily;
    private string? _selectedVariantLevelId;

    /// <summary>
    /// モデル名と段階の2つの欄で選ぶか(ジェミリーのように、一覧が「モデル名 (段階)」の組み合わせで並ぶとき。
    /// 労力を別に選べるAIは、今までどおり1つの欄)。
    /// </summary>
    public bool ShowVariantChoice => _variants.Count > 0 && !CanChooseEffort;

    /// <summary>モデルを1つの欄で選ぶか(2つの欄に分けないとき)。</summary>
    public bool ShowSingleModelChoice => !ShowVariantChoice;

    /// <summary>2つの欄で選ぶときの、モデル名の一覧(例: Gemini 4 Argon、Gemini 3.8 Flash)。</summary>
    public ObservableCollection<string> ModelFamilies { get; } = new();

    /// <summary>2つの欄で選ぶときの、選んだモデル名で選べる段階(低/中/高)。</summary>
    public ObservableCollection<EffortOption> VariantLevels { get; } = new();

    /// <summary>2つの欄で選ぶときの、選ばれているモデル名。選び直すと、同じ段階(なければ中)のモデルに切り替わる。</summary>
    public string? SelectedFamily
    {
        get => _selectedFamily;
        set
        {
            if (value is null || !Set(ref _selectedFamily, value)) return;
            RefreshVariantLevels();
            var levels = VariantLevels.Select(l => l.Id).ToList();
            var level = _selectedVariantLevelId is { } current && levels.Contains(current) ? current
                : levels.Contains(EffortOption.DefaultId) ? EffortOption.DefaultId
                : levels.FirstOrDefault();
            ChooseVariant(value, level);
        }
    }

    /// <summary>2つの欄で選ぶときの、選ばれている段階。選び直すと、そのモデルに切り替わる。</summary>
    public string? SelectedVariantLevelId
    {
        get => _selectedVariantLevelId;
        set
        {
            if (value is null || !Set(ref _selectedVariantLevelId, value)) return;
            if (_selectedFamily is { } family) ChooseVariant(family, value);
        }
    }

    /// <summary>モデル名と段階から、モデルを選ぶ(該当がなければ何もしない)。</summary>
    /// <param name="family">モデル名</param>
    /// <param name="levelId">段階</param>
    private void ChooseVariant(string family, string? levelId)
    {
        var variant = _variants.FirstOrDefault(v => v.Family == family && v.LevelId == levelId);
        if (variant is not null) SelectedModelId = variant.ModelId;
    }

    /// <summary>選んだモデル名で選べる段階の一覧を、入れ替える。</summary>
    private void RefreshVariantLevels()
    {
        var levels = _selectedFamily is null ? Array.Empty<EffortOption>() : ModelVariant.LevelsOf(_variants, _selectedFamily);
        if (VariantLevels.Select(l => l.Id).SequenceEqual(levels.Select(l => l.Id))) return;
        VariantLevels.Clear();
        foreach (var l in levels) VariantLevels.Add(l);
    }

    /// <summary>いま選ばれているモデルに合わせて、モデル名と段階の欄をそろえる(選び直しの通知は出さない)。</summary>
    private void SyncVariantSelection()
    {
        var variant = _variants.FirstOrDefault(v => v.ModelId == _selectedModelId);
        if (variant is null) return;
        _selectedFamily = variant.Family;
        RefreshVariantLevels();
        _selectedVariantLevelId = variant.LevelId;
        // 一覧を入れ替えると、値が前と同じでも欄が空になることがあるので、毎回知らせる
        Raise(nameof(SelectedFamily));
        Raise(nameof(SelectedVariantLevelId));
    }

    /// <summary>使っているモデル名の表示(AIだけ。人間は空)。実際の名前が分かれば置き換わる。</summary>
    public string ModelText
    {
        get => _modelText;
        set
        {
            if (!Set(ref _modelText, value)) return;
            Raise(nameof(HasModel));
            Raise(nameof(ShowModelLine));
            Raise(nameof(ModelSummary));
        }
    }

    public bool HasModel => _modelText.Length > 0;

    /// <summary>「モデル · 労力」の行を出すか(モデル名が分かっている、または選べるAI)。</summary>
    public bool ShowModelLine => HasModel || CanChooseModel;

    /// <summary>
    /// カードに出す「いまのモデル · 労力」の1行(例: claude-opus-5-5 · 中)。
    /// 労力がモデル名に含まれるAI(ジェミリー)は、モデル名だけ。押すと、その場でモデルと労力を選べる。
    /// </summary>
    public string ModelSummary
    {
        get
        {
            var model = HasModel ? _modelText : "モデルを選ぶ";
            if (!CanChooseEffort) return model;
            var effort = EffortOption.All.FirstOrDefault(e => e.Id == _selectedEffortId)?.Label ?? _selectedEffortId;
            return $"{model} · {effort}";
        }
    }

    private string _usageText = "";
    private string _usageTooltip = "";
    private bool _isUsageHigh;

    /// <summary>使用量の枠の表示(例: 枠: 5時間 3% · 7日 16%)。Claudeだけ返答のたびに更新される。</summary>
    public string UsageText
    {
        get => _usageText;
        private set
        {
            if (!Set(ref _usageText, value)) return;
            Raise(nameof(HasUsage));
        }
    }

    /// <summary>使用量の枠のツールチップ(リセット時刻つき)。</summary>
    public string UsageTooltip
    {
        get => _usageTooltip;
        private set => Set(ref _usageTooltip, value);
    }

    /// <summary>使用量の枠が80%以上ならtrue(黄色で強調する)。</summary>
    public bool IsUsageHigh
    {
        get => _isUsageHigh;
        private set => Set(ref _isUsageHigh, value);
    }

    /// <summary>使用量の枠が分かっているならtrue。</summary>
    public bool HasUsage => _usageText.Length > 0;

    /// <summary>通知された使用量の枠を反映する。</summary>
    /// <param name="encoded">UsageQuota.Encode() の文字列</param>
    public void ApplyUsage(string encoded)
    {
        var (shortText, isHigh, tooltip) = UsageQuota.Decode(encoded);
        UsageText = shortText;
        IsUsageHigh = isHigh;
        UsageTooltip = tooltip;
    }

    /// <summary>返答待ちの数(0なら待機中)。</summary>
    public int PendingCount
    {
        get => _pendingCount;
        set
        {
            if (!Set(ref _pendingCount, value)) return;
            Raise(nameof(IsThinking));
            Raise(nameof(StatusText));
        }
    }

    /// <summary>返答が流れ始めているならtrue(考え中から、回答中へ変わる)。</summary>
    public bool IsAnswering
    {
        get => _isAnswering;
        set
        {
            if (!Set(ref _isAnswering, value)) return;
            Raise(nameof(StatusText));
        }
    }

    /// <summary>いまの作業(「AI_MEMORY.md を読んでいます」「…を読みました」など)。作業していなければ空。</summary>
    public string ActivityText
    {
        get => _activityText;
        set
        {
            if (!Set(ref _activityText, value)) return;
            Raise(nameof(StatusText));
        }
    }

    public bool IsThinking => _pendingCount > 0;

    /// <summary>状態の文言。人間(あなた)は常に「参加中」、AIは不参加・待機中・作業中・考え中・回答中。</summary>
    public string StatusText =>
        Participant.Id == Participants.UserId ? "参加中"
        : !_isPresent ? "不参加"
        : !IsThinking ? "待機中"
        : _activityText.Length > 0 ? (_activityText.EndsWith("います", StringComparison.Ordinal) ? _activityText + "…" : _activityText)
        : IsAnswering ? "回答中…"
        : "考え中…";
}
