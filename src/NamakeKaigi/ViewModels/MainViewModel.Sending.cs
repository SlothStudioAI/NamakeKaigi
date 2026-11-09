using System.Collections.ObjectModel;
using System.Windows.Input;
using NamakeKaigi.Models;
using NamakeKaigi.Services;

namespace NamakeKaigi.ViewModels;

/// <summary>メイン画面の状態とコマンド(partial): 送信と、流れてくる返答・発言リストの管理。</summary>
public sealed partial class MainViewModel
{
    private string _inputText = "";

    /// <summary>入力欄の文字。</summary>
    public string InputText
    {
        get => _inputText;
        set
        {
            if (Set(ref _inputText, value)) ((RelayCommand)SendCommand).RaiseCanExecuteChanged();
        }
    }

    /// <summary>入力欄の発言を送り、途中経過が届くたびに画面を更新する。待っている間も次の発言を入力できる。</summary>
    public Task SendAsync() => SendCoreFromAsync(InputText.Trim(), Attachments.ToList(), fromInput: true);

    /// <summary>リモート(スマホ)から届いた発言を送る。PCの入力欄の下書きと添付には触れない。</summary>
    /// <param name="text">発言の本文</param>
    public Task SendRemoteAsync(string text) =>
        SendCoreFromAsync(text.Trim(), new List<AttachmentItem>(), fromInput: false);

    /// <summary>発言を送る共通の処理(呼びかけの解釈、宛先の切り替え、記録、送信)。</summary>
    /// <param name="text">発言の本文</param>
    /// <param name="attached">添付</param>
    /// <param name="fromInput">入力欄からの送信ならtrue(送ったあと、入力欄と添付を空にする)</param>
    private async Task SendCoreFromAsync(string text, List<AttachmentItem> attached, bool fromInput)
    {
        if (text.Length == 0 && attached.Count == 0) return;

        // 先頭の呼びかけ(「ジェミ、〜」)で、送る直前に宛先を切り替える。AIに渡す本文からは呼びかけを除く(画面と記録には、言った言葉のまま残す)
        var call = CallParser.Parse(text, _session.CallNames);
        var body = text;
        if (call.IsCalled && IsPresent(call.TargetKey ?? "")) // 参加していない人への呼びかけは、普通の本文として扱う
        {
            Target = ToChatTarget(call.TargetKey ?? "");
            NormalizeTarget();
            body = call.Body;
            if (body.Length == 0 && attached.Count == 0)
            {
                if (fromInput) InputText = ""; // 呼びかけだけ: 宛先を切り替えるだけで、何も送らない
                return;
            }
        }
        if (fromInput)
        {
            InputText = "";
            Attachments.Clear();
        }
        _readAloud.Stop(); // 新しい発言を送ったら、読んでいるものは止める

        var targets = TargetIds();
        var displayText = AttachmentComposer.FormatForDisplayAndLog(text, attached);
        var promptText = AttachmentComposer.FormatForPrompt(body, attached);
        var extraDirs = AttachmentComposer.ResolveExternalDirectories(WorkDir, attached);

        await SendCoreAsync(displayText, promptText, targets, extraDirs);
    }

    /// <summary>AIの吹き出しについて、ほかのAI1人に、1クリックで意見を聞く(査読ではなく、別の視点からの意見)。</summary>
    /// <param name="message">意見を聞く対象のAI発言</param>
    /// <param name="targetId">意見を聞く相手の識別名</param>
    public async Task AskOpinionAsync(MessageViewModel message, string targetId)
    {
        if (message is null || !message.CanAsk) return;
        var target = message.AskTargets.FirstOrDefault(t => t.Id == targetId);
        if (target is null || !IsPresent(targetId)) return;
        Target = ToChatTarget(targetId);

        var displayText = $"🔍 {message.DisplayName}の発言について、{target.DisplayName}に意見を聞きます。";
        var excerpt = message.Text.Length > 2000 ? message.Text[..2000] + "\n…(省略)" : message.Text;
        var note = (_session.Relay.OpinionNote ?? "").Replace("{speaker}", message.DisplayName, StringComparison.Ordinal);
        var body = $"【対象の発言（{message.DisplayName}）】\n{excerpt}";
        var promptText = note.Trim().Length > 0 ? $"{note}\n\n{body}" : body; // 頼む文を空にした設定なら、対象の発言だけを送る

        await SendCoreAsync(displayText, promptText, new[] { targetId });
    }

    private async Task SendCoreAsync(string displayText, string promptText, IReadOnlyList<string> targets,
        IReadOnlyList<string>? extraDirs = null)
    {
        if (targets.Count == 0) return; // 宛先が0人(使えるAIがいない)なら送らない
        var session = _session;
        AddMessage(new MessageViewModel(Participants.User, displayText, _clock(), targetIds: targets));
        // 順番のときは、1人目の仮の吹き出しだけを出す(2人目は、番が来たときに Started で出す)
        var inTurn = AllOrders.IsSequential(_session.AllOrder, targets);
        var firstId = AllOrders.Arrange(_session.AllOrder, targets)[0];
        foreach (var id in targets)
        {
            Member(id).PendingCount++;
            if (inTurn && id != firstId)
            {
                Member(id).ActivityText = WaitingTurnText;
                continue;
            }
            AddPlaceholder(id);
        }
        NotifyBusyStateChanged();

        await session.SendAsync(promptText, targets, progress =>
        {
            if (session != _session) return; // 会議が新しくなったら、古い会議の知らせは捨てる
            OnProgress(progress);
        }, extraDirs);
    }

    /// <summary>送った瞬間に、考え中の仮の吹き出しを出す(最初の文字が届くまで、チャット欄が止まって見えないように)。</summary>
    /// <param name="id">AIの識別名</param>
    private void AddPlaceholder(string id)
    {
        if (_streaming.ContainsKey(id)) return;
        var placeholder = new MessageViewModel(Participants.Get(id), "", _clock())
        {
            IsStreaming = true,
            StatusLine = ToStatusLine(""),
        };
        _streaming[id] = placeholder;
        _rawStreamingText[id] = "";
        AddMessage(placeholder);
    }

    /// <summary>AIからの途中経過・結果を、画面に反映する。</summary>
    private void OnProgress(ChatProgress progress)
    {
        var id = progress.AgentId;
        var member = Member(id);
        switch (progress.Kind)
        {
            case ChatProgressKind.Started:
                // 順番の2人目は、ここで番が来る(順番待ちの一言を消して、仮の吹き出しを出す)
                if (member.ActivityText == WaitingTurnText) member.ActivityText = "";
                AddPlaceholder(id);
                break;

            case ChatProgressKind.Model:
                member.ModelText = progress.Text;
                break;

            case ChatProgressKind.Activity:
                member.ActivityText = progress.Text;
                if (_streaming.TryGetValue(id, out var working))
                {
                    // 作業の前のつぶやき(「確認します」など)は、本文から外して作業の様子の欄へ移す。
                    // 本文には最後の回答だけが流れるので、完成時に文字が消えて置き換わって見えない
                    var said = ReplyTextCleaner.StripLeadingRoleHeader(_rawStreamingText.GetValueOrDefault(id, "")).Trim();
                    if (progress.Text.Length > 0 && said.Length > 0)
                    {
                        working.StatusNote = said.Length > StatusNoteMaxChars ? said[..StatusNoteMaxChars] + "…" : said;
                        _rawStreamingText[id] = "";
                        working.Text = "";
                    }
                    working.StatusLine = ToStatusLine(progress.Text);
                }
                break;

            case ChatProgressKind.Usage:
                member.ApplyUsage(progress.Text);
                break;

            case ChatProgressKind.Approval:
                if (progress.Approval is null) break;
                AddPlaceholder(id);
                var asking = _streaming[id];
                asking.AnswerApproval(false); // 前の問い合わせが残っていたら断る(CLIは1件ずつ聞くので、ふつうは起きない)
                asking.Approval = progress.Approval;
                asking.StatusLine = ToStatusLine(ApprovalWaitingText);
                member.ActivityText = ApprovalWaitingText;
                break;

            case ChatProgressKind.Delta:
                member.ActivityText = ""; // 文字が流れ始めたら、読み込みなどの作業は終わっている
                if (!_streaming.TryGetValue(id, out var streaming))
                {
                    // 最初の文字が届いたとき、吹き出しを作る(以降は同じ吹き出しに足していく)
                    streaming = new MessageViewModel(Participants.Get(id), "", _clock()) { IsStreaming = true };
                    _streaming[id] = streaming;
                    _rawStreamingText[id] = "";
                    AddMessage(streaming);
                }
                member.IsAnswering = true;
                streaming.StatusLine = "";
                _rawStreamingText[id] = _rawStreamingText.GetValueOrDefault(id, "") + progress.Text;
                streaming.Text = ReplyTextCleaner.StripLeadingRoleHeader(_rawStreamingText[id]);
                break;

            case ChatProgressKind.Done:
                _rawStreamingText.Remove(id);
                var cleanedDone = ReplyTextCleaner.StripLeadingRoleHeader(progress.Text);
                MessageViewModel completed;
                if (_streaming.Remove(id, out var finished))
                {
                    finished.AnswerApproval(false); // 答えが済んだのに残っている問い合わせは、もう意味がないので閉じる
                    finished.Text = cleanedDone; // 流れてきた文字を、完成した全文で置き換える(つぶやきは外してあるので、ほぼ同じ文)
                    finished.IsStreaming = false;
                    MoveAboveStreaming(finished);
                    completed = finished;
                }
                else
                {
                    completed = new MessageViewModel(Participants.Get(id), cleanedDone, _clock());
                    AddMessage(completed);
                }
                Finish(member);
                if (ReadAloudOn) _readAloud.Speak(completed); // 完成してから、順番に読む(エラー・途中の文字は読まない)
                break;

            case ChatProgressKind.Error:
                _rawStreamingText.Remove(id);
                if (_streaming.Remove(id, out var broken))
                {
                    broken.AnswerApproval(false); // 停止・失敗したら、承認のボタンも消す
                    broken.IsStreaming = false;
                    if (broken.HasText) MoveAboveStreaming(broken);
                    else RemoveMessage(broken); // 1文字も届かなかった仮の吹き出しは消す
                }
                AddMessage(new MessageViewModel(Participants.Get(id), progress.Text, _clock(), isError: true));
                Finish(member);
                break;
        }
    }

    /// <summary>1件の返答が済んだ(成功・失敗とも)ので、状態を戻す。</summary>
    private void Finish(MemberViewModel member)
    {
        member.PendingCount = Math.Max(0, member.PendingCount - 1);
        member.IsAnswering = false;
        member.ActivityText = "";
        NotifyBusyStateChanged();
    }

    private void NotifyBusyStateChanged()
    {
        Raise(nameof(IsBusy));
        ((RelayCommand)CancelCommand).RaiseCanExecuteChanged();
    }

    /// <summary>吹き出しを、載るべき一覧に入れる(見た目の反映と、参加状況の反映もここで行う)。</summary>
    private void AddMessage(MessageViewModel message)
    {
        message.ApplyAppearance(Appearance);
        message.ApplyPresence(IsPresent);
        message.SpeakEnabled = _readAloud.Enabled;
        var wasEmpty = Messages.Count == 0;
        _lists.Insert(message);
        if (wasEmpty) RefreshPresenceLock(); // 最初の発言で、参加者の選び直しを締め切る
    }

    /// <summary>吹き出しを、すべての一覧から外す。</summary>
    private void RemoveMessage(MessageViewModel message) => _lists.Remove(message);

    /// <summary>返答が済んだ吹き出しを、まだ返答途中の吹き出しより上へ動かす(済んだ順に並ぶように)。</summary>
    private void MoveAboveStreaming(MessageViewModel message) => _lists.MoveAboveStreaming(message);

    /// <summary>作業の様子の欄の文言。作業がなければ「考え中…」。</summary>
    /// <param name="activity">作業の説明(「○○を読んでいます」など。空なら考え中)</param>
    private static string ToStatusLine(string activity) =>
        activity.Length == 0 ? "💭 考え中…"
        : activity.EndsWith("います", StringComparison.Ordinal) ? "⏳ " + activity + "…"
        : "✔ " + activity;
}
