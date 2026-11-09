using System.Collections.ObjectModel;
using System.Windows.Input;
using NamakeKaigi.Models;
using NamakeKaigi.Services;

namespace NamakeKaigi.ViewModels;

/// <summary>メイン画面の状態とコマンド(partial): ハーネス設定・書く担当・権限・モデルの選択。</summary>
public sealed partial class MainViewModel
{
    /// <summary>作業フォルダを切り替える(次の発言から、両方のAIに効く)。</summary>
    /// <param name="workDir">新しい作業フォルダ</param>
    public void ChangeWorkDir(string workDir)
    {
        if (string.IsNullOrWhiteSpace(workDir)) return;
        _session.SetWorkDir(workDir);
        Raise(nameof(WorkDir));
        Raise(nameof(WorkDirDisplay));
        Raise(nameof(WorkDirName));
    }

    /// <summary>Claudeのいまの設定を取り出す(ハーネス編集画面を開くため)。</summary>
    public ClaudeSettings? GetClaudeSettings() => _session.GetClaudeSettings();

    /// <summary>Claudeでいま使える外部連携の一覧を取る(ハーネス編集画面のチェック用。取れなければnull)。</summary>
    public Task<IReadOnlyList<string>?> LoadClaudeConnectorsAsync() => _session.LoadClaudeConnectorsAsync();

    /// <summary>Claudeのハーネス設定を更新し、ツールチップと権限表示を最新にする。</summary>
    /// <param name="settings">新しいClaude設定</param>
    public void UpdateClaudeHarness(ClaudeSettings settings)
    {
        _session.UpdateClaudeSettings(settings);
        ClaudeMember.CanWrite = _session.CanWrite(Participants.ClaudeId);
        ClaudeMember.CanRunCommands = _session.CanRunCommands(Participants.ClaudeId);
        ClaudeMember.CanFull = _session.CanFull(Participants.ClaudeId);
        RefreshHarnessTooltips();
    }

    /// <summary>agy(Gemini)のいまの設定を取り出す(ハーネス編集画面を開くため)。</summary>
    public AgySettings? GetAgySettings() => _session.GetAgySettings();

    /// <summary>agy(Gemini)の更新を確認する(ジェミの設定画面の「更新を確認」から。意図した操作なので、黒い窓が一瞬出てよい)。</summary>
    /// <param name="ct">キャンセル(画面を閉じたとき)</param>
    /// <returns>確認の結果</returns>
    public Task<AgyUpdateResult> CheckAgyUpdateAsync(CancellationToken ct)
    {
        var settings = _session.GetAgySettings()
            ?? throw new InvalidOperationException("ジェミの設定がありません。");
        return new AgyUpdater(settings).CheckAsync(ct);
    }

    /// <summary>agy(Gemini)のハーネス設定を更新し、ツールチップと注意文を最新にする。</summary>
    /// <param name="settings">新しいagy設定</param>
    public void UpdateAgyHarness(AgySettings settings)
    {
        _session.UpdateAgySettings(settings);
        GeminiMember.CanWrite = _session.CanWrite(Participants.GeminiId);
        GeminiMember.WriterWarning = _session.WriterWarning(Participants.GeminiId);
        GeminiMember.FullWarning = _session.FullWarning(Participants.GeminiId);
        GeminiMember.CanFull = _session.CanFull(Participants.GeminiId);
        RefreshHarnessTooltips();
    }

    /// <summary>「書く担当」の選択欄とハーネスの説明を、会議の設定から反映する(新しい会議は、全員が読み取り専用から始まる)。</summary>
    private void ApplyWritePermissions()
    {
        foreach (var m in Members.Where(m => m.Participant.Id != Participants.UserId))
        {
            m.CanWrite = _session.CanWrite(m.Participant.Id);
            m.WriterWarning = _session.WriterWarning(m.Participant.Id);
            m.FullWarning = _session.FullWarning(m.Participant.Id);
            m.CanRunCommands = _session.CanRunCommands(m.Participant.Id);
            m.CanFull = _session.CanFull(m.Participant.Id);
            m.SetWriterSilently(false);
        }
        RefreshHarnessTooltips();
    }

    /// <summary>AI全員のハーネス説明(ツールチップ文)を、いまの状態に合わせて更新する。</summary>
    private void RefreshHarnessTooltips()
    {
        foreach (var m in Members.Where(m => m.Participant.Id != Participants.UserId))
        {
            m.HarnessTooltip = _session.HarnessSummary(m.Participant.Id);
        }
    }

    /// <summary>画面で「書く担当」が選ばれた・外されたとき。会議に伝える(次の発言から効く)。作業が別々なら、両方を書く担当にしてよい。</summary>
    private void OnWriterChosen(string agentId, bool isWriter)
    {
        _session.SetWriter(agentId, isWriter);
        RefreshHarnessTooltips();
    }

    /// <summary>画面で「何でもできる」が選ばれた・外されたとき。会議に伝える(次の発言から効く)。</summary>
    private void OnFullChosen(string agentId, bool enabled)
    {
        _session.SetFull(agentId, enabled);
        RefreshHarnessTooltips();
    }

    /// <summary>画面で「コマンドも使う」が選ばれた・外されたとき。会議に伝える(次の発言から効く)。</summary>
    private void OnCommandsChosen(string agentId, bool enabled)
    {
        _session.SetCommands(agentId, enabled);
        RefreshHarnessTooltips();
    }

    /// <summary>モデルの選択欄と、モデル名の初期表示を、会議の設定から反映する(実際のモデル名が分かれば、後で置き換わる)。</summary>
    private void ApplyModelLabels()
    {
        foreach (var (id, fallbackLabel) in _session.ModelLabels)
        {
            var member = Member(id);
            var modelId = _session.ModelIdOf(id);
            member.SetOptions(_session.ModelOptionsFor(id), modelId);
            member.SetEffortSilently(_session.EffortIdOf(id));
            member.CanChooseEffort = _session.SupportsEffort(id);
            member.ModelText = LabelOf(member, modelId) ?? fallbackLabel;
        }
    }

    /// <summary>選べるモデルの一覧を、CLIに聞いて最新にする(起動時に1回。聞けなくても、設定にある一覧のまま使える)。</summary>
    private async Task RefreshModelOptionsAsync()
    {
        var session = _session;
        foreach (var member in Members.Where(m => m.CanChooseModel))
        {
            try
            {
                var options = await session.LoadModelOptionsAsync(member.Participant.Id);
                if (session != _session) return; // 会議が新しくなっていたら、古い結果は捨てる
                var modelId = session.ModelIdOf(member.Participant.Id);
                var wasShowingConfigured = member.ModelText == (LabelOf(member, modelId) ?? modelId);
                member.SetOptions(options, modelId);
                if (wasShowingConfigured) member.ModelText = LabelOf(member, modelId) ?? modelId;
            }
            catch (Exception)
            {
                // 一覧が取れなくても、設定にある一覧のまま使えるので、何もしない
            }
        }
    }

    /// <summary>画面でモデルが選ばれたとき。次の発言から効くよう会議に伝え、表示も切り替える。</summary>
    private void OnModelChosen(string agentId, string modelId)
    {
        _session.SetModel(agentId, modelId);
        var member = Member(agentId);
        member.ModelText = LabelOf(member, modelId) ?? modelId;
    }

    /// <summary>画面で労力(エフォート)が選ばれたとき。次の発言から効くよう会議に伝える。</summary>
    private void OnEffortChosen(string agentId, string effortId)
    {
        _session.SetEffort(agentId, effortId);
    }

    /// <summary>モデルの名前から、画面に出す名前を探す(一覧になければnull)。</summary>
    private static string? LabelOf(MemberViewModel member, string modelId) =>
        member.ModelOptions.FirstOrDefault(o => o.Id == modelId)?.Label;
}
