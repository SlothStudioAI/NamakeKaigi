using System.IO;
using NamakeKaigi.Models;
using NamakeKaigi.Services;
using NamakeKaigi.ViewModels;

namespace NamakeKaigi.Tests;

/// <summary>会議室(タブ)の一覧の試験。画面なしで、部屋の追加・切り替え・閉じる・共有の設定の同期を確かめる。</summary>
public class MeetingRoomsTests
{
    private static (MeetingRooms Rooms, List<FakeSession> Sessions) Create(int maxRooms)
    {
        var sessions = new List<FakeSession>();
        var rooms = new MeetingRooms(() =>
        {
            var session = new FakeSession();
            sessions.Add(session);
            return new MainViewModel(() => session);
        }, maxRooms);
        return (rooms, sessions);
    }

    [Fact]
    public void 起動時は1部屋だけで_その部屋が画面に映る()
    {
        var (rooms, _) = Create(3);

        Assert.Single(rooms.Items);
        Assert.Same(rooms.Items[0], rooms.Current);
        Assert.Equal("会議室1", rooms.Current.RoomTitle);
        Assert.True(rooms.Current.IsCurrentRoom);
        Assert.False(rooms.CanCloseRoom);
    }

    [Fact]
    public void 部屋を増やすと_新しい部屋へ移り_上限で止まる()
    {
        var (rooms, sessions) = Create(2);
        var moved = new List<MainViewModel>();
        rooms.CurrentChanged += moved.Add;

        rooms.AddRoom();
        rooms.AddRoom(); // 上限なので増えない

        Assert.Equal(2, rooms.Items.Count);
        Assert.Equal(2, sessions.Count);
        Assert.Equal("会議室2", rooms.Current.RoomTitle);
        Assert.Single(moved);
        Assert.False(rooms.CanAddRoom);
        Assert.False(rooms.AddRoomCommand.CanExecute(null));
        Assert.False(rooms.Items[0].IsCurrentRoom);
        Assert.True(rooms.Items[1].IsCurrentRoom);
    }

    [Fact]
    public void 上限が0以下でも_1部屋は使える()
    {
        var (rooms, _) = Create(0);

        Assert.Single(rooms.Items);
        Assert.False(rooms.CanAddRoom);
    }

    [Fact]
    public void 部屋ごとに別の会議で_入力欄と発言が混ざらない()
    {
        var (rooms, _) = Create(3);
        var first = rooms.Current;
        first.InputText = "開発の話";

        rooms.AddRoom();
        rooms.Current.InputText = "動画の話";

        Assert.Equal("開発の話", first.InputText);
        Assert.Equal("動画の話", rooms.Current.InputText);
        Assert.NotSame(first, rooms.Current);
    }

    [Fact]
    public void タブの命令で部屋を切り替えられ_元の部屋の状態は残る()
    {
        var (rooms, _) = Create(3);
        var first = rooms.Current;
        first.InputText = "書きかけ";
        rooms.AddRoom();

        first.SelectRoomCommand.Execute(null);

        Assert.Same(first, rooms.Current);
        Assert.Equal("書きかけ", first.InputText);
        Assert.True(first.IsCurrentRoom);
    }

    [Fact]
    public void 部屋を閉じると_後片付けをして_隣の部屋へ移る_最後の1部屋は閉じない()
    {
        var (rooms, sessions) = Create(3);
        rooms.AddRoom();
        var second = rooms.Current;

        second.CloseRoomCommand.Execute(null);

        Assert.Single(rooms.Items);
        Assert.Same(rooms.Items[0], rooms.Current);
        Assert.Equal(1, sessions[1].ShutdownCount);

        rooms.Current.CloseRoomCommand.Execute(null); // 最後の1部屋
        Assert.Single(rooms.Items);
        Assert.Equal(0, sessions[0].ShutdownCount);
    }

    [Fact]
    public void 閉じたあとに部屋を増やすと_空いた番号の名前になる()
    {
        var (rooms, _) = Create(3);
        rooms.AddRoom();
        rooms.AddRoom();
        rooms.Close(rooms.Items[1]); // 会議室2を閉じる

        rooms.AddRoom();

        Assert.Equal(new[] { "会議室1", "会議室3", "会議室2" }, rooms.Items.Select(r => r.RoomTitle));
    }

    [Fact]
    public void 配色テーマは_ある部屋で変えると_ほかの部屋にも合う()
    {
        var (rooms, _) = Create(3);
        rooms.AddRoom();
        var first = rooms.Items[0];
        var second = rooms.Items[1];
        var other = ThemeCatalog.All.First(t => t.Id != first.SelectedThemeId).Id;

        second.SelectedThemeId = other;

        Assert.Equal(other, first.SelectedThemeId);
    }

    [Fact]
    public void 表示名の設定は_ある部屋で変えると_ほかの部屋にも合う()
    {
        var (rooms, sessions) = Create(3);
        rooms.AddRoom();
        var appearance = AppearanceSettings.Default with { IsBusinessMode = true };

        rooms.Items[1].UpdateAppearance(appearance);

        Assert.True(rooms.Items[0].IsBusinessMode);
        Assert.True(sessions[0].Appearance.IsBusinessMode);
    }

    [Fact]
    public void 単独で作った画面の状態は_部屋の一覧がなくても使える()
    {
        var vm = new MainViewModel(() => new FakeSession());

        Assert.Null(vm.Rooms);
        Assert.True(vm.IsCurrentRoom);
        Assert.False(vm.CloseRoomCommand.CanExecute(null));
    }

    [Fact]
    public void 同じ名前の会議を同じ起動で作っても_会議フォルダは別になる()
    {
        using var dir = new TempDir();
        var now = new DateTime(2026, 10, 30, 12, 0, 0);
        var session = MarkdownLogWriter.SessionDirFor(dir.Path, now);

        // どちらも、まだ発言がない(フォルダは作られていない)
        var a = new MarkdownLogWriter(session, now, "会議室1");
        var b = new MarkdownLogWriter(session, now, "会議室1");
        Assert.False(Directory.Exists(session));

        // 最初の発言で、それぞれのフォルダができる(あとの方は番号つき)
        var entry = new ChatEntry(Participants.UserId, "こんにちは", now);
        a.Append(entry);
        b.Append(entry);
        Assert.NotEqual(a.Path, b.Path);
        Assert.True(File.Exists(a.Path));
        Assert.True(File.Exists(b.Path));
        Assert.EndsWith(Path.Combine("会議室1 (2)", "log.md"), b.Path);
    }
}
