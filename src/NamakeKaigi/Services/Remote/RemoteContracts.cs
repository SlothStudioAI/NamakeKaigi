namespace NamakeKaigi.Services.Remote;

/// <summary>スマホに見せる発言1件。</summary>
/// <param name="Index">一覧での通し番号</param>
/// <param name="Speaker">話した人の識別名(user / claude / gemini / chappy)</param>
/// <param name="Name">表示名</param>
/// <param name="Time">時刻(HH:mm)</param>
/// <param name="Text">本文</param>
/// <param name="IsUser">ユーザーの発言か</param>
/// <param name="IsError">エラーの知らせか</param>
/// <param name="Streaming">返答の途中か</param>
/// <param name="Status">返答途中の作業の様子(なければ空)</param>
public sealed record RemoteMessage(
    int Index, string Speaker, string Name, string Time, string Text,
    bool IsUser, bool IsError, bool Streaming, string Status);

/// <summary>スマホに見せる、いまの会議室の状態。</summary>
/// <param name="Room">会議室の名前</param>
/// <param name="Busy">AIが返答中か</param>
/// <param name="Messages">発言(古い順)</param>
public sealed record RemoteSnapshot(string Room, bool Busy, IReadOnlyList<RemoteMessage> Messages);

/// <summary>リモートのサーバーが、会議(アプリ本体)を操作するための窓口。画面のスレッドへの受け渡しは、実装側が行う。</summary>
public interface IRemoteHost
{
    /// <summary>いまの会議室の状態を返す。</summary>
    /// <param name="maxMessages">返す発言の最大件数(新しい順に数える)</param>
    Task<RemoteSnapshot> GetSnapshotAsync(int maxMessages);

    /// <summary>いまの会議室へ発言を送る(返答の完了は待たない)。</summary>
    /// <param name="text">発言の本文</param>
    /// <returns>送れたらtrue</returns>
    Task<bool> SendAsync(string text);
}
