using System.Collections.ObjectModel;
using System.Windows.Input;
using NamakeKaigi.Models;
using NamakeKaigi.Services;

namespace NamakeKaigi.ViewModels;

/// <summary>メイン画面の状態とコマンド(partial): 返答の読み上げ。</summary>
public sealed partial class MainViewModel
{
    private readonly ReadAloudController _readAloud;

    /// <summary>読み上げの機能があるか(設定の voice.enabled)。ないときは、設定画面の読み上げの行と、吹き出しの🔊を出さない。</summary>
    public bool ReadAloudEnabled => _readAloud.Enabled;

    /// <summary>読み上げを使えるか(使える日本語の声が見つかったか)。</summary>
    public bool ReadAloudAvailable => _readAloud.Available;

    /// <summary>読み上げがONか。ONのあいだ、AIの返答が完成するたびに、順番に読む。OFFにすると、読んでいるものも止める。</summary>
    public bool ReadAloudOn
    {
        get => _readAloud.On;
        set => _readAloud.On = value;
    }

    /// <summary>スイッチの表示。</summary>
    public string ReadAloudLabel => _readAloud.Label;

    /// <summary>スイッチにマウスを乗せたときの説明(使えないときは、理由と直し方)。</summary>
    public string ReadAloudTooltip => _readAloud.Tooltip;

    /// <summary>読み上げているあいだ、止めるボタンを出す。</summary>
    public bool IsSpeaking => _readAloud.IsSpeaking;

    /// <summary>読み上げのスイッチを切り替えるコマンド。</summary>
    public ICommand ToggleReadAloudCommand { get; }

    /// <summary>読み上げを止めるコマンド。</summary>
    public ICommand StopSpeakingCommand { get; }

    /// <summary>吹き出し1つを、声で読み上げる(スイッチがOFFでも読む)。</summary>
    /// <param name="message">読み上げる吹き出し</param>
    public void SpeakMessage(MessageViewModel message) => _readAloud.SpeakMessage(message);
}
