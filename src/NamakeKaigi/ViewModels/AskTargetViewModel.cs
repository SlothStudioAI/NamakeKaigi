using NamakeKaigi.Models;

namespace NamakeKaigi.ViewModels;

/// <summary>吹き出しの「🔍 〇〇に聞く」ボタン1つ分(意見を聞く相手1人)。</summary>
public sealed class AskTargetViewModel : ViewModelBase
{
    private string _displayName;
    private bool _available = true;

    /// <summary>ボタンを作る。</summary>
    /// <param name="owner">ボタンが載っている吹き出し</param>
    /// <param name="id">意見を聞く相手の識別名</param>
    public AskTargetViewModel(MessageViewModel owner, string id)
    {
        Owner = owner;
        Id = id;
        _displayName = Participants.Get(id).DisplayName;
    }

    /// <summary>ボタンが載っている吹き出し。</summary>
    public MessageViewModel Owner { get; }

    /// <summary>意見を聞く相手の識別名。</summary>
    public string Id { get; }

    /// <summary>相手の表示名(簡易表示モードや名前変更に連動する)。</summary>
    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (!Set(ref _displayName, value)) return;
            Raise(nameof(Label));
            Raise(nameof(Tooltip));
        }
    }

    /// <summary>相手が、この会議に参加しているか(参加していなければ、ボタンを出さない)。</summary>
    public bool Available
    {
        get => _available;
        set
        {
            if (Set(ref _available, value)) Raise(nameof(CanAsk));
        }
    }

    /// <summary>ボタンを出すならtrue(完成したAIの返答で、相手が参加しているとき)。</summary>
    public bool CanAsk => Owner.CanAsk && _available;

    /// <summary>ボタンの文言。</summary>
    public string Label => $"🔍 {_displayName}に聞く";

    /// <summary>ボタンにマウスを乗せたときの説明。</summary>
    public string Tooltip => $"この返答について、{_displayName}の意見を聞きます";

    /// <summary>吹き出しの状態が変わったとき、ボタンを出すかを知らせ直す。</summary>
    internal void RefreshCanAsk() => Raise(nameof(CanAsk));
}
