using NamakeKaigi.Models;
using NamakeKaigi.Services;

namespace NamakeKaigi.ViewModels;

/// <summary>予約1回ぶんの実行結果(部屋が返す)。</summary>
/// <param name="Ok">返答が届いたらtrue(エラーがあればfalse)</param>
/// <param name="Note">補足(失敗の理由など。成功なら空)</param>
public sealed record ScheduleOutcome(bool Ok, string Note);

/// <summary>メイン画面の状態とコマンド(partial): 予約(定期実行)として、決まった相手に決まった段階で本文を送る。</summary>
public sealed partial class MainViewModel
{
    /// <summary>この部屋が、予約の実行のために自動で開かれた部屋ならtrue(上限のとき、古いものから閉じて場所を空ける対象になる)。</summary>
    public bool IsScheduledRoom { get; internal set; }

    /// <summary>
    /// 予約の本文を、決まった相手に、決まった段階で送り、返答が済むまで待つ。
    /// 相手が見つからない・段階が使えないときは、黙って別の段階で動かさず、失敗として返す。
    /// </summary>
    /// <param name="job">実行する予約</param>
    /// <returns>結果(エラーの吹き出しが出たら失敗)</returns>
    public async Task<ScheduleOutcome> RunScheduledAsync(ScheduledJob job)
    {
        var member = Members.FirstOrDefault(m => m.Participant.Id == job.Agent);
        if (member is null || member.Participant.Id == Participants.UserId)
            return new ScheduleOutcome(false, $"相手「{job.Agent}」が見つかりません(claude / gemini / chappy のどれかを書いてください)。");
        if (!member.IsPresent)
            return new ScheduleOutcome(false, $"{member.Participant.DisplayName}は、この会議室に参加していません。");
        if (member.LevelOptions.All(o => o.Id != job.Level))
            return new ScheduleOutcome(false, $"{member.Participant.DisplayName}には、段階「{job.Level}」がありません。");

        member.SelectedLevelId = job.Level;
        if (member.SelectedLevelId != job.Level) // 設定でその段階が使えない(例: 何でもできるの引数が未設定)ときは、気づけるよう止める
            return new ScheduleOutcome(false, $"{member.Participant.DisplayName}を段階「{job.Level}」にできませんでした(設定で使えない段階です)。");

        // モデルの指定があれば、この会議室だけで切り替える(設定ファイルのふだんのモデルは、書き換えない)
        if (job.Model is not null)
        {
            if (!member.CanChooseModel)
                return new ScheduleOutcome(false, $"{member.Participant.DisplayName}は、モデルを選べません(予約のモデル指定を外してください)。");
            _session.SetModelForThisMeeting(job.Agent, job.Model);
            member.SetOptions(_session.ModelOptionsFor(job.Agent), job.Model);
            member.ModelText = LabelOf(member, job.Model) ?? (job.Model.Length == 0 ? AgyAgent.UnspecifiedModelLabel : job.Model);
        }

        Target = ToChatTarget(job.Agent);
        var before = Messages.ToHashSet();
        var modelText = job.Model is null ? "" : $"・モデル: {(job.Model.Length == 0 ? "指定なし" : job.Model)}";
        var display = $"⏰ 予約「{job.Name}」を実行します({member.Participant.DisplayName}・{member.LevelLabel}{modelText})。\n\n{job.Prompt}";
        await SendCoreAsync(display, job.Prompt, new[] { job.Agent });

        var errors = Messages.Where(m => !before.Contains(m) && m.IsError).Select(m => m.Text).ToList();
        return errors.Count == 0
            ? new ScheduleOutcome(true, "")
            : new ScheduleOutcome(false, string.Join(" / ", errors));
    }
}
