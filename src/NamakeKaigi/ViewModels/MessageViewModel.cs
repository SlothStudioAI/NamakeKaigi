using System.Windows.Input;
using NamakeKaigi.Models;

namespace NamakeKaigi.ViewModels;

/// <summary>チャット画面の吹き出し1つ分。流れてくる返答は、本文が増えていく。</summary>
public sealed class MessageViewModel : ViewModelBase
{
    private string _text;
    private bool _isStreaming;
    private string _displayName;
    private bool _isBusinessMode;
    private string _statusLine = "";
    private string _statusNote = "";

    /// <summary>吹き出しを作る。</summary>
    /// <param name="speaker">発言者</param>
    /// <param name="text">本文</param>
    /// <param name="time">発言の時刻</param>
    /// <param name="isError">AIの失敗の知らせならtrue</param>
    /// <param name="targetIds">あなたの発言の宛先(別々に見る画面で、どちらの列に出すかに使う)</param>
    public MessageViewModel(Participant speaker, string text, DateTime time, bool isError = false,
        IReadOnlyList<string>? targetIds = null)
    {
        Speaker = speaker;
        _text = text;
        Time = time;
        IsError = isError;
        TargetIds = targetIds ?? Array.Empty<string>();
        _displayName = speaker.DisplayName;
        // 意見を聞く相手は、発言者以外のAI全員(参加していない人のボタンは、あとで隠す)
        AskTargets = IsUser || IsError
            ? Array.Empty<AskTargetViewModel>()
            : Participants.All
                .Where(p => p.Id != Participants.UserId && p.Id != speaker.Id)
                .Select(p => new AskTargetViewModel(this, p.Id))
                .ToList();
    }

    public Participant Speaker { get; }
    public DateTime Time { get; }
    public bool IsError { get; }
    public IReadOnlyList<string> TargetIds { get; }

    /// <summary>本文(流れてくる間は、増えていく)。</summary>
    public string Text
    {
        get => _text;
        set
        {
            if (!Set(ref _text, value)) return;
            Raise(nameof(HasText));
            Raise(nameof(CanSpeak));
        }
    }

    /// <summary>本文が1文字でもあればtrue(考え中の仮の吹き出しでは、本文の欄を隠す)。</summary>
    public bool HasText => !string.IsNullOrEmpty(_text);

    /// <summary>返答の途中に、吹き出しの下に小さく出す作業の様子(「考え中…」「○○を読んでいます…」)。</summary>
    public string StatusLine
    {
        get => _statusLine;
        set
        {
            if (Set(ref _statusLine, value)) Raise(nameof(ShowStatus));
        }
    }

    /// <summary>作業の前にAIがつぶやいた一言(「確認します」など)。本文には入れず、作業の様子の欄に出す。</summary>
    public string StatusNote
    {
        get => _statusNote;
        set
        {
            if (!Set(ref _statusNote, value)) return;
            Raise(nameof(HasStatusNote));
            Raise(nameof(ShowStatus));
        }
    }

    /// <summary>つぶやきの一言があればtrue。</summary>
    public bool HasStatusNote => _statusNote.Length > 0;

    /// <summary>作業の様子の欄を出すならtrue(返答の途中で、出す文言があるとき)。</summary>
    public bool ShowStatus => _isStreaming && (_statusLine.Length > 0 || _statusNote.Length > 0);

    private ToolApproval? _approval;

    /// <summary>いま答えを待っている、承認の問い合わせ(なければnull)。</summary>
    public ToolApproval? Approval
    {
        get => _approval;
        set
        {
            if (!Set(ref _approval, value)) return;
            Raise(nameof(HasApproval));
            Raise(nameof(ApprovalText));
        }
    }

    /// <summary>承認の問い合わせを出しているならtrue(吹き出しに、許可・拒否のボタンを出す)。</summary>
    public bool HasApproval => _approval is not null;

    /// <summary>承認の問い合わせの文(例: 「PowerShell: git status」)。</summary>
    public string ApprovalText => _approval?.Text ?? "";

    /// <summary>問い合わせに答えて、ボタンを消す。</summary>
    /// <param name="allow">許可ならtrue</param>
    public void AnswerApproval(bool allow)
    {
        var approval = _approval;
        if (approval is null) return;
        Approval = null;
        StatusLine = allow ? "許可しました" : "拒否しました";
        approval.Answer(allow);
    }

    /// <summary>許可のボタン。</summary>
    public ICommand AllowCommand => _allowCommand ??= new RelayCommand(() => AnswerApproval(true));

    /// <summary>拒否のボタン。</summary>
    public ICommand DenyCommand => _denyCommand ??= new RelayCommand(() => AnswerApproval(false));

    private ICommand? _allowCommand;
    private ICommand? _denyCommand;

    /// <summary>返答が流れてくる途中ならtrue(完成したらfalse)。</summary>
    public bool IsStreaming
    {
        get => _isStreaming;
        set
        {
            if (!Set(ref _isStreaming, value)) return;
            RefreshAskTargets();
            Raise(nameof(CanSpeak));
            Raise(nameof(ShowStatus));
        }
    }

    public bool IsUser => Speaker.Id == Participants.UserId;

    private bool _speakEnabled = true;

    /// <summary>読み上げの機能があるか(設定の voice.enabled。false なら🔊を出さない)。</summary>
    public bool SpeakEnabled
    {
        get => _speakEnabled;
        set
        {
            if (Set(ref _speakEnabled, value)) Raise(nameof(CanSpeak));
        }
    }

    /// <summary>完成したAIの返答で、本文があり、声で読み上げられる状態ならtrue(吹き出しの🔊ボタンを出す)。</summary>
    public bool CanSpeak => _speakEnabled && !IsUser && !IsError && !IsStreaming && HasText;

    /// <summary>吹き出しの「🔍 〇〇に聞く」ボタン(発言者以外のAI1人につき1つ)。あなたの発言・失敗の知らせには出さない。</summary>
    public IReadOnlyList<AskTargetViewModel> AskTargets { get; }

    /// <summary>完成したAIの返答で、意見を聞ける状態ならtrue(相手ごとの参加の有無は、ボタンの側で見る)。</summary>
    public bool CanAsk => !IsUser && !IsError && !IsStreaming;

    /// <summary>相手が会議に参加しているかを、ボタンに反映する。</summary>
    /// <param name="isPresent">識別名から、参加しているかを返す関数</param>
    public void ApplyPresence(Func<string, bool> isPresent)
    {
        foreach (var target in AskTargets) target.Available = isPresent(target.Id);
    }

    private void RefreshAskTargets()
    {
        foreach (var target in AskTargets) target.RefreshCanAsk();
    }

    /// <summary>画面に出す発言者名(簡易表示モードや名前変更に連動する)。</summary>
    public string DisplayName
    {
        get => _displayName;
        private set
        {
            if (!Set(ref _displayName, value)) return;
            Raise(nameof(InitialText));
        }
    }

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
    public bool ShowImageAvatar => !_isBusinessMode && Speaker.HasAvatar;
    public bool ShowInitialAvatar => !ShowImageAvatar;
    public string InitialText => AppearanceSettings.ResolveInitial(Speaker.Id, _displayName);

    public string AvatarUri => Speaker.AvatarUri;
    public string TimeText => Time.ToString("HH:mm");

    /// <summary>簡易表示モードや名前変更の設定を吹き出しに反映する。</summary>
    public void ApplyAppearance(AppearanceSettings appearance)
    {
        DisplayName = appearance.ResolveParticipantName(Speaker.Id);
        foreach (var target in AskTargets) target.DisplayName = appearance.ResolveParticipantName(target.Id);
        IsBusinessMode = appearance.IsBusinessMode;
    }
}
