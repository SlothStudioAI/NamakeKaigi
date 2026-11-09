namespace NamakeKaigi.Models;

/// <summary>設定画面の「場所」で変えられるパスの一覧(空は「既定の値を使う」)。</summary>
/// <param name="WorkDir">新しい会議室の作業フォルダ(各AIが作業する場所)</param>
/// <param name="MeetingsDir">会議ログの保存先(空なら exe の隣の meetings)</param>
/// <param name="ScheduleFile">予約(定期実行)の保存ファイル(空なら自動実行を使わない)</param>
/// <param name="ClaudeExecutable">Claude Code の CLI の場所(空なら PATH から探す)</param>
/// <param name="AgyExecutable">Antigravity の agy の場所(空なら既定の場所と PATH から探す)</param>
/// <param name="CodexExecutable">Codex の CLI の場所(空なら PATH から探す)</param>
public sealed record PathSettings(
    string WorkDir,
    string MeetingsDir,
    string ScheduleFile,
    string ClaudeExecutable,
    string AgyExecutable,
    string CodexExecutable);
