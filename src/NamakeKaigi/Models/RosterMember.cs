namespace NamakeKaigi.Models;

/// <summary>名簿に載せるメンバー1人分(定義ファイルから読んだ内容)。</summary>
/// <param name="Group">所属グループの識別名(claude / gemini / codex)</param>
/// <param name="Team">置かれているチームの場所(探す起点からの相対パス。起点の直下なら「共通」)</param>
/// <param name="Name">呼び出し用の識別名(例: software-engineer)</param>
/// <param name="DisplayName">画面に出す名前(例: 実装担当 (ツクル))。定義になければ識別名</param>
/// <param name="Description">役割の説明(なければ空)</param>
/// <param name="Model">定義に書かれた担当モデル(なければnull)</param>
/// <param name="Rules">行動ルール(定義ファイルの本文)</param>
/// <param name="SourcePath">定義ファイルのフルパス</param>
public sealed record RosterMember(
    string Group,
    string Team,
    string Name,
    string DisplayName,
    string Description,
    string? Model,
    string Rules,
    string SourcePath);
