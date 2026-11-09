using System.Windows.Threading;
using NamakeKaigi.Services.Remote;

namespace NamakeKaigi.ViewModels;

/// <summary>
/// リモート(スマホ)からの要求を、いま画面に映している会議室へつなぐ。
/// 会議の状態は画面のスレッドが持つので、読み書きはすべてそのスレッドで行う。
/// </summary>
public sealed class RemoteHost : IRemoteHost
{
    private readonly MeetingRooms _rooms;
    private readonly Dispatcher _dispatcher;

    /// <summary>窓口を作る。</summary>
    /// <param name="rooms">会議室の一覧</param>
    /// <param name="dispatcher">画面のスレッドへの受け渡し</param>
    public RemoteHost(MeetingRooms rooms, Dispatcher dispatcher)
    {
        _rooms = rooms;
        _dispatcher = dispatcher;
    }

    /// <inheritdoc />
    public Task<RemoteSnapshot> GetSnapshotAsync(int maxMessages) =>
        _dispatcher.InvokeAsync(() =>
        {
            var room = _rooms.Current;
            var all = room.Messages.ToList();
            var skip = Math.Max(0, all.Count - maxMessages);
            var messages = all.Skip(skip).Select((m, i) => new RemoteMessage(
                skip + i, m.Speaker.Id, m.DisplayName, m.TimeText, m.Text,
                m.IsUser, m.IsError, m.IsStreaming, m.IsStreaming ? m.StatusLine : "")).ToList();
            return new RemoteSnapshot(room.RoomTitle, room.IsBusy, messages);
        }).Task;

    /// <inheritdoc />
    public Task<bool> SendAsync(string text) =>
        _dispatcher.InvokeAsync(() =>
        {
            // 返答の完了(数分かかる)は待たない。失敗は、画面側のエラーの吹き出しになる
            _ = _rooms.Current.SendRemoteAsync(text);
            return true;
        }).Task;
}
