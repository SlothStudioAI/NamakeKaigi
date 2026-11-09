using NamakeKaigi.Models;
using NamakeKaigi.Services;

namespace NamakeKaigi.ViewModels;

/// <summary>
/// 返答の読み上げ(スイッチのON/OFFと、吹き出しを読み上げ向けの文にして順番待ちに足す処理)。
/// 状態が変わったら、変わったプロパティ名を、渡された通知の関数で知らせる(画面の表示は MainViewModel のプロパティが映す)。
/// </summary>
public sealed class ReadAloudController
{
    private readonly ISpeechService? _speech;
    private readonly VoiceSettings _voice;
    private readonly Action<string> _notify;
    private bool _on;

    /// <summary>読み上げの部品と設定を受け取る。</summary>
    /// <param name="speech">読み上げの部品(無ければnull。読み上げは使えない)</param>
    /// <param name="voice">読み上げの設定</param>
    /// <param name="notify">状態が変わったプロパティ名を知らせる関数(MainViewModel の Raise)</param>
    public ReadAloudController(ISpeechService? speech, VoiceSettings voice, Action<string> notify)
    {
        _speech = speech;
        _voice = voice;
        _notify = notify;
        _on = Available && _voice.AutoRead;
        if (_speech is not null)
        {
            _speech.SpeakingChanged += () =>
            {
                _notify("IsSpeaking");
                _notify("ReadAloudTooltip"); // 代わりの声で読んでいる、などの知らせが変わったとき
                _notify("ReadAloudAvailable"); // 準備を後回しにした部品が、使えないと分かったとき
                _notify("ReadAloudLabel");
            };
        }
    }

    /// <summary>読み上げの機能があるか(部品が渡されたか)。ないときは、画面に読み上げの部分を出さない。</summary>
    public bool Enabled => _speech is not null;

    /// <summary>読み上げを使えるか(使える日本語の声が見つかったか)。</summary>
    public bool Available => _speech is { IsAvailable: true };

    /// <summary>読み上げがONか。ONのあいだ、AIの返答が完成するたびに、順番に読む。OFFにすると、読んでいるものも止める。</summary>
    public bool On
    {
        get => _on;
        set
        {
            if (value) _speech?.Prepare(); // 準備を後回しにした部品は、ここで初めて使えるかが分かる
            if (value && !Available) return;
            if (_on == value) return;
            _on = value;
            _notify("ReadAloudOn");
            if (!value) _speech?.Stop();
            _notify("ReadAloudLabel");
        }
    }

    /// <summary>スイッチの表示。</summary>
    public string Label => !Available ? "🔇 読み上げ: 使えません" : _on ? "🔊 読み上げ: ON" : "🔈 読み上げ: OFF";

    /// <summary>スイッチにマウスを乗せたときの説明(使えないときは、理由と直し方)。</summary>
    public string Tooltip => Available
        ? "AIの返答を声で読み上げます(完成してから、順番に1人ずつ)。ONにすると以後の返答を読みます。吹き出しの🔊で、1つだけ読むこともできます"
          + (_speech!.Note.Length > 0 ? "\n" + _speech.Note : "")
        : _speech?.UnavailableReason ?? "読み上げの部品がありません。";

    /// <summary>読み上げているか(読んでいるあいだ、止めるボタンを出す)。</summary>
    public bool IsSpeaking => _speech?.IsSpeaking == true;

    /// <summary>読んでいるものを止める(順番待ちも空にする)。</summary>
    public void Stop() => _speech?.Stop();

    /// <summary>吹き出し1つを、声で読み上げる(スイッチがOFFでも読む)。</summary>
    /// <param name="message">読み上げる吹き出し</param>
    public void SpeakMessage(MessageViewModel message)
    {
        if (message is null || !message.CanSpeak) return;
        _speech?.Prepare();
        if (!Available) return;
        Speak(message);
    }

    /// <summary>吹き出しの本文を読み上げ向けの文にして、順番待ちに足す。</summary>
    /// <param name="message">読み上げる吹き出し</param>
    public void Speak(MessageViewModel message)
    {
        var name = _speech!.NeedsSpeakerNamePrefix ? message.DisplayName : ""; // 声で聞き分けられるなら、名前は言わない
        var text = SpeechTextFormatter.Format(message.Text, name, _voice.MaxChars);
        if (text.Length > 0) _speech.Enqueue(text, message.Speaker.Id);
    }
}
