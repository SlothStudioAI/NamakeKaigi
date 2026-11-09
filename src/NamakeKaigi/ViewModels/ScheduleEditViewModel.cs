using System.Collections.ObjectModel;
using NamakeKaigi.Models;
using NamakeKaigi.Services;

namespace NamakeKaigi.ViewModels;

/// <summary>編集窓の「相手」の選択肢1つ。</summary>
/// <param name="Id">識別名(claude / gemini / chappy)</param>
/// <param name="Label">画面に出す名前</param>
public sealed record AgentChoice(string Id, string Label);

/// <summary>編集窓の「モデル」の選択肢1つ。</summary>
/// <param name="Value">予約に書く値(null=ふだんの設定のまま / 空文字=指定なし / モデル名)</param>
/// <param name="Label">画面に出す名前</param>
public sealed record ModelChoice(string? Value, string Label);

/// <summary>
/// 予約の追加・編集の窓の状態。入力の検査(名前・cron式・本文)と、cron式の「次の3回」の表示を持つ。
/// 保存は、窓を開いた側が Build() の結果を受け取って行う。
/// </summary>
public sealed class ScheduleEditViewModel : ViewModelBase
{
    /// <summary>「次の何回分」を見せるか。</summary>
    private const int PreviewCount = 3;

    private readonly ScheduledJob? _original;
    private readonly Func<DateTime> _clock;
    private readonly Func<string, IReadOnlyList<ModelOption>> _modelOptionsFor;
    private string _name;
    private string _cron;
    private string _prompt;
    private string _selectedAgentId;
    private string _selectedLevelId;
    private ModelChoice _selectedModel;
    private string _cronPreview = "";

    /// <summary>編集の状態を作る。</summary>
    /// <param name="original">編集する予約(新しく足すならnull)</param>
    /// <param name="clock">いまの時刻を返す関数(「次の3回」の計算に使う)</param>
    /// <param name="modelOptionsFor">相手の識別名から、選べるモデルの一覧を返す関数</param>
    public ScheduleEditViewModel(ScheduledJob? original, Func<DateTime> clock,
        Func<string, IReadOnlyList<ModelOption>> modelOptionsFor)
    {
        _original = original;
        _clock = clock;
        _modelOptionsFor = modelOptionsFor;
        _name = original?.Name ?? "";
        _cron = original?.Cron ?? "0 0 * * *";
        _prompt = original?.Prompt ?? "";
        Enabled = original?.Enabled ?? false; // 新しい予約は、確かめてからONにできるよう、OFFで始める
        _selectedAgentId = original?.Agent ?? Participants.GeminiId;
        _selectedLevelId = original?.Level ?? PermissionLevelOption.PartialId;
        LevelOptions = new ObservableCollection<PermissionLevelOption>();
        ModelChoices = new ObservableCollection<ModelChoice>();
        _selectedModel = null!;
        RefreshLevels();
        RefreshModels(original?.Model, keep: true);
        RefreshPreview();
    }

    /// <summary>窓の見出し。</summary>
    public string Title => _original is null ? "予約を追加" : "予約を編集";

    /// <summary>相手の選択肢(クロ・ジェミ・チャッピー)。</summary>
    public IReadOnlyList<AgentChoice> AgentOptions { get; } = Participants.All
        .Where(p => p.Id != Participants.UserId).Select(p => new AgentChoice(p.Id, p.DisplayName)).ToList();

    /// <summary>名前(会議室の名前にもなる)。</summary>
    public string Name
    {
        get => _name;
        set
        {
            if (Set(ref _name, value ?? "")) Raise(nameof(ValidationError));
        }
    }

    /// <summary>cron式。</summary>
    public string Cron
    {
        get => _cron;
        set
        {
            if (!Set(ref _cron, value ?? "")) return;
            RefreshPreview();
            Raise(nameof(ValidationError));
        }
    }

    /// <summary>cron式の下に出す説明(読めれば「次の3回」、読めなければ理由)。</summary>
    public string CronPreview
    {
        get => _cronPreview;
        private set => Set(ref _cronPreview, value);
    }

    /// <summary>送る本文。</summary>
    public string Prompt
    {
        get => _prompt;
        set
        {
            if (Set(ref _prompt, value ?? "")) Raise(nameof(ValidationError));
        }
    }

    /// <summary>保存したあと、すぐ動かすか。</summary>
    public bool Enabled { get; set; }

    /// <summary>選ばれている相手の識別名。変えると、段階とモデルの選択肢が、その相手のものに変わる。</summary>
    public string SelectedAgentId
    {
        get => _selectedAgentId;
        set
        {
            if (string.IsNullOrEmpty(value) || !Set(ref _selectedAgentId, value)) return;
            RefreshLevels();
            RefreshModels(null, keep: false);
            Raise(nameof(CanChooseModel));
        }
    }

    /// <summary>選べる段階の一覧(相手によって名前が変わる)。</summary>
    public ObservableCollection<PermissionLevelOption> LevelOptions { get; }

    /// <summary>選ばれている段階(read / partial / full)。</summary>
    public string SelectedLevelId
    {
        get => _selectedLevelId;
        set
        {
            if (!string.IsNullOrEmpty(value)) Set(ref _selectedLevelId, value);
        }
    }

    /// <summary>選べるモデルの一覧(先頭は「ふだんの設定のまま」)。</summary>
    public ObservableCollection<ModelChoice> ModelChoices { get; }

    /// <summary>選ばれているモデル。</summary>
    public ModelChoice SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (value is not null) Set(ref _selectedModel, value);
        }
    }

    /// <summary>この相手は、モデルを選べるか(チャッピーは固定)。</summary>
    public bool CanChooseModel => _selectedAgentId != Participants.ChappyId;

    /// <summary>入力の誤り(なければ空)。保存ボタンの可否に使う。</summary>
    public string ValidationError
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_name)) return "名前を入れてください。";
            if (!CronExpression.TryParse(_cron, out _, out var error)) return error;
            if (string.IsNullOrWhiteSpace(_prompt)) return "プロンプト(送る本文)を入れてください。";
            return "";
        }
    }

    /// <summary>入力から予約を作る(誤りがあるときは例外)。編集なら識別名を引き継ぎ、新規なら新しく付ける。</summary>
    /// <returns>予約</returns>
    /// <exception cref="InvalidOperationException">入力に誤りがあるとき</exception>
    public ScheduledJob Build()
    {
        var error = ValidationError;
        if (error.Length > 0) throw new InvalidOperationException(error);
        var model = CanChooseModel ? _selectedModel.Value : null;
        return new ScheduledJob(_original?.Id ?? "job-" + Guid.NewGuid().ToString("N")[..8], _name.Trim(), _cron.Trim(),
            _selectedAgentId, _selectedLevelId, _prompt.Trim(), Enabled, model);
    }

    /// <summary>相手に合わせて、段階の選択肢を作り直す(いまの段階が選べなければ、読むだけに戻す)。</summary>
    private void RefreshLevels()
    {
        LevelOptions.Clear();
        foreach (var o in PermissionLevelOption.For(canFull: true, partialIsEditOnly: _selectedAgentId == Participants.GeminiId))
            LevelOptions.Add(o);
        if (LevelOptions.All(o => o.Id != _selectedLevelId)) SelectedLevelId = PermissionLevelOption.ReadId;
        Raise(nameof(SelectedLevelId));
    }

    /// <summary>相手に合わせて、モデルの選択肢を作り直す。</summary>
    /// <param name="value">選んでおくモデルの値(null=ふだんのまま)</param>
    /// <param name="keep">指定の値を選び直すか(相手を変えたときは、ふだんのままに戻す)</param>
    private void RefreshModels(string? value, bool keep)
    {
        ModelChoices.Clear();
        var usual = new ModelChoice(null, "ふだんの設定のまま");
        ModelChoices.Add(usual);
        if (CanChooseModel)
        {
            if (_selectedAgentId == Participants.GeminiId) ModelChoices.Add(new ModelChoice("", "指定なし(agyの既定)"));
            foreach (var o in _modelOptionsFor(_selectedAgentId)) ModelChoices.Add(new ModelChoice(o.Id, o.Label));
            // 一覧にないモデル(古い名前など)が書かれていても、消えないよう選択肢に足す
            if (keep && value is { Length: > 0 } && ModelChoices.All(c => c.Value != value))
                ModelChoices.Add(new ModelChoice(value, value + "(一覧にありません)"));
        }
        _selectedModel = (keep ? ModelChoices.FirstOrDefault(c => c.Value == value) : null) ?? usual;
        Raise(nameof(SelectedModel));
    }

    /// <summary>cron式の下の説明を作り直す。</summary>
    private void RefreshPreview()
    {
        if (!CronExpression.TryParse(_cron, out var expr, out var error))
        {
            CronPreview = "⚠ " + error;
            return;
        }
        var times = new List<string>();
        var t = _clock();
        for (var i = 0; i < PreviewCount; i++)
        {
            var next = expr!.Next(t);
            if (next is null) break;
            times.Add(next.Value.ToString("M/d(ddd) HH:mm"));
            t = next.Value;
        }
        CronPreview = times.Count == 0 ? "(次の予定はありません)" : "次の" + times.Count + "回: " + string.Join(" / ", times);
    }
}
