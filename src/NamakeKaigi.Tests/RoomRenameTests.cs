using System.IO;
using NamakeKaigi.Models;
using NamakeKaigi.Services;
using NamakeKaigi.ViewModels;

namespace NamakeKaigi.Tests;

/// <summary>会議室の名前の変更と、会議ログの置き場所(保存先/起動した時刻/会議名/log.md)の試験。</summary>
public class RoomRenameTests : IDisposable
{
    // _dir が保存先(meetings)。その下の _session が、起動した時刻のフォルダ
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "namake_rename_" + Guid.NewGuid().ToString("N"));
    private readonly string _session;

    public RoomRenameTests()
    {
        Directory.CreateDirectory(_dir);
        _session = MarkdownLogWriter.SessionDirFor(_dir, new DateTime(2026, 10, 31, 14, 15, 0));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static ChatEntry Entry(string speaker, string text) =>
        new(speaker, text, new DateTime(2026, 10, 31, 14, 20, 0), null);

    private MarkdownLogWriter NewLog(string? title = null) =>
        new(_session, new DateTime(2026, 10, 31, 14, 16, 0), title);

    [Fact]
    public void 起動した時刻のフォルダの下に_会議名のフォルダとlogmdを作る()
    {
        var log = NewLog("名簿づくり");
        log.Append(Entry(Participants.UserId, "こんにちは"));

        Assert.Equal(Path.Combine(_dir, "20261031_141500", "名簿づくり", "log.md"), log.Path);
        Assert.Equal(Path.Combine(_dir, "20261031_141500", "名簿づくり", "meeting.json"), log.StatePath);
        var text = File.ReadAllText(log.Path);
        Assert.StartsWith("# 会議ログ: 名簿づくり\n", text);
        Assert.Contains("こんにちは", text);
    }

    [Fact]
    public void 発言がないうちは_何も作らず_名前の予定だけが変わる()
    {
        var log = NewLog("会議室1");
        log.SetTitle("名簿づくり");

        Assert.False(Directory.Exists(_session)); // 起動した時刻のフォルダも、まだ作らない
        Assert.Equal(Path.Combine(_session, "名簿づくり", "log.md"), log.Path);
        Assert.Empty(MeetingStore.ListMeetings(_dir));

        log.Append(Entry(Participants.UserId, "最初の発言"));

        Assert.Equal(new[] { Path.Combine(_session, "名簿づくり") }, Directory.GetDirectories(_session));
        Assert.StartsWith("# 会議ログ: 名簿づくり\n", File.ReadAllText(log.Path));
    }

    [Fact]
    public void 最初の発言の前にフォルダを用意しても_2を付けず同じフォルダを使う()
    {
        var log = NewLog("名簿づくり");
        // 最初の発言より前に、ログボタンなどでフォルダを用意した
        var folder = log.EnsureFolder();

        log.Append(Entry(Participants.UserId, "最初の発言"));

        // (2) のフォルダは作られず、最初に用意したフォルダの中に log.md が作られる
        Assert.Equal(Path.Combine(folder, "log.md"), log.Path);
        Assert.Equal(new[] { Path.Combine(_session, "名簿づくり") }, Directory.GetDirectories(_session));
        Assert.True(File.Exists(log.Path));
    }

    [Fact]
    public void 名前を変えると_会議フォルダも付け替わり_logの中の添付のパスも書き換わる()
    {
        var log = NewLog("会議室1");
        var oldFolder = log.EnsureFolder();
        var image = Path.Combine(oldFolder, "attachments", "a.png");
        Directory.CreateDirectory(Path.GetDirectoryName(image)!);
        File.WriteAllText(image, "png");
        log.Append(Entry(Participants.UserId, $"[添付ファイル: {image.Replace('\\', '/')}]"));

        log.SetTitle("名簿づくり");

        var newFolder = Path.Combine(_session, "名簿づくり");
        Assert.Equal(newFolder, log.FolderPath);
        Assert.False(Directory.Exists(oldFolder));
        Assert.True(File.Exists(Path.Combine(newFolder, "attachments", "a.png"))); // 添付も一緒に動く
        var text = File.ReadAllText(log.Path);
        Assert.Contains(Path.Combine(newFolder, "attachments", "a.png").Replace('\\', '/'), text);
        Assert.DoesNotContain("会議室1/attachments", text);
        Assert.StartsWith("# 会議ログ: 名簿づくり\n", text);

        log.Append(Entry(Participants.ClaudeId, "付け替えたあとも、同じログに続く"));
        Assert.Contains("付け替えたあとも", File.ReadAllText(log.Path));
    }

    [Fact]
    public void 同じ名前の会議は_番号を付けて別のフォルダにする()
    {
        var first = NewLog("会議室1");
        var second = NewLog("会議室1"); // 同じ部屋で「新しい会議」を押したときなど

        first.Append(Entry(Participants.UserId, "1つ目"));
        second.Append(Entry(Participants.UserId, "2つ目"));

        Assert.Equal(Path.Combine(_session, "会議室1"), first.FolderPath);
        Assert.Equal(Path.Combine(_session, "会議室1 (2)"), second.FolderPath);

        second.SetTitle("会議室1"); // 自分の名前のままなら、動かない
        Assert.Equal(Path.Combine(_session, "会議室1 (2)"), second.FolderPath);

        var third = NewLog("別の名前");
        third.Append(Entry(Participants.UserId, "3つ目"));
        third.SetTitle("会議室1"); // すでにある名前に変えると、番号を付ける
        Assert.Equal(Path.Combine(_session, "会議室1 (3)"), third.FolderPath);
    }

    [Fact]
    public void 発言の前に画像を貼っても_その会議のフォルダに入り_ほかの会議と混ざらない()
    {
        var a = NewLog("会議室1");
        var b = NewLog("会議室1");

        var folderA = a.EnsureFolder(); // 画像を貼る前に用意する
        var folderB = b.EnsureFolder();
        a.Append(Entry(Participants.UserId, "Aの発言"));

        Assert.NotEqual(folderA, folderB);
        Assert.Equal(Path.Combine(folderA, "log.md"), a.Path);
        Assert.Equal(Path.Combine(_session, "会議室1 (2)"), folderB);
    }

    [Fact]
    public void 名前の改行は空白にし_使えない文字は_に置き換える()
    {
        var log = NewLog();
        log.Append(Entry(Participants.UserId, "発言"));

        log.SetTitle("A/B:C?\n二行目");

        Assert.Equal(Path.Combine(_session, "A_B_C_ 二行目"), log.FolderPath);
        Assert.Equal("A/B:C? 二行目", MarkdownLogWriter.ReadTitle(log.Path)); // 1行目の名前は、そのまま残す
        Assert.Equal("", MarkdownLogWriter.SafeFileName("..."));
    }

    [Fact]
    public void 名前を空にすると_見出しは名前なし_フォルダは会議に戻る()
    {
        var log = NewLog("最初の名前");
        log.Append(Entry(Participants.UserId, "一言目"));
        log.SetTitle("あとの名前");
        Assert.Equal(1, File.ReadAllText(log.Path).Split("# 会議ログ").Length - 1); // 見出しが重ならない

        log.SetTitle("  ");

        Assert.StartsWith("# 会議ログ\n", File.ReadAllText(log.Path));
        Assert.Null(log.Title);
        Assert.Equal(Path.Combine(_session, MarkdownLogWriter.DefaultFolderName), log.FolderPath);
    }

    [Fact]
    public void 開き直した会議は_書いてある名前を読み取り_名前を変えるとフォルダも付け替わる()
    {
        var first = NewLog("名簿づくり");
        first.Append(Entry(Participants.UserId, "発言"));

        var reopened = new MarkdownLogWriter(first.FolderPath, first.StartedAt, reopen: true);
        Assert.Equal("名簿づくり", reopened.Title);
        Assert.Equal(first.Path, reopened.Path);

        reopened.SetTitle("改名");
        Assert.Equal(Path.Combine(_session, "改名"), reopened.FolderPath);
    }

    [Fact]
    public void 名前つきの会議から_発言と開始時刻を復元できる()
    {
        var log = NewLog("名簿づくり");
        log.Append(Entry(Participants.UserId, "復元される発言"));

        var state = MeetingStore.LoadState(log.FolderPath); // meeting.json がないときは log.md から

        Assert.Equal(new DateTime(2026, 10, 31, 14, 15, 0), state.StartedAt); // 起動した時刻のフォルダ名から
        Assert.Equal("復元される発言", Assert.Single(state.Entries).Text);
        Assert.Single(MeetingStore.ParseMarkdownLog(log.Path, new DateTime(2026, 10, 31))); // 1行目の名前は、発言に数えない
    }

    [Fact]
    public void 古い形式の会議フォルダも_一覧に出て_開き直しても場所は動かさない()
    {
        var oldDir = Path.Combine(_dir, "20261030_100000");
        Directory.CreateDirectory(oldDir);
        File.WriteAllText(Path.Combine(oldDir, "log.md"), "# 会議ログ\n\n## ユーザー (10:00:05)\n\n昔の発言\n");
        var created = NewLog("新しい方");
        created.Append(Entry(Participants.UserId, "新しい形式の発言"));

        var list = MeetingStore.ListMeetings(_dir);

        Assert.Equal(new[] { created.FolderPath, oldDir }, list.Select(m => m.FolderPath));
        Assert.Equal(new[] { "20261031_141500/新しい方", "20261030_100000" }, list.Select(m => m.FolderName));

        var reopenedOld = new MarkdownLogWriter(oldDir, new DateTime(2026, 10, 30, 10, 0, 0), reopen: true);
        reopenedOld.Append(Entry(Participants.ClaudeId, "続き"));
        reopenedOld.SetTitle("古い方に名前"); // 古い形式は、1行目だけ書き換え、フォルダは動かさない
        Assert.Equal(Path.Combine(oldDir, "log.md"), reopenedOld.Path);
        Assert.Equal("古い方に名前", MarkdownLogWriter.ReadTitle(reopenedOld.Path));
    }

    [Fact]
    public void 見出しがなくなっていても_先頭に名前つきの見出しを足す()
    {
        var log = NewLog();
        log.EnsureFolder();
        File.WriteAllText(log.Path, "\n## ユーザー (14:00:00)\n\n本文\n"); // 手で見出しを消された

        log.SetTitle("復活");

        var text = File.ReadAllText(log.Path);
        Assert.StartsWith("# 会議ログ: 復活\n", text);
        Assert.Contains("本文", text);
    }

    [Fact]
    public void 部屋の名前は_最初から会議に渡し_変えるとタブと会議の名前が変わる()
    {
        var session = new FakeSession();
        var vm = new MainViewModel(() => session);
        Assert.Equal(new[] { "会議室1" }, session.TitleWrites); // 名前を付ける前は、タブの名前がフォルダ名

        vm.RenameRoom("  名簿 づくり ");

        Assert.Equal("名簿 づくり", vm.RoomTitle);
        Assert.Equal(new[] { "会議室1", "名簿 づくり" }, session.TitleWrites);
    }

    [Fact]
    public void 空の名前は無視し_長い名前は上限で切る()
    {
        var session = new FakeSession();
        var vm = new MainViewModel(() => session, roomTitleMaxChars: 5);

        vm.RenameRoom("   ");
        Assert.Equal("会議室1", vm.RoomTitle);
        Assert.Equal(new[] { "会議室1" }, session.TitleWrites);

        vm.RenameRoom("123456789");
        Assert.Equal("12345", vm.RoomTitle);
    }

    [Fact]
    public void その場の書き換えは_Enterで確定し_Escでやめる()
    {
        var session = new FakeSession();
        var vm = new MainViewModel(() => session);

        vm.BeginRenameRoom();
        Assert.True(vm.IsRenamingRoom);
        Assert.Equal("会議室1", vm.RoomTitleEdit); // 入力欄には、いまの名前が入る
        vm.RoomTitleEdit = "やめる名前";
        vm.CancelRenameRoom();
        Assert.False(vm.IsRenamingRoom);
        Assert.Equal("会議室1", vm.RoomTitle);

        vm.BeginRenameRoom();
        vm.RoomTitleEdit = "決める名前";
        vm.CommitRenameRoom();
        vm.CommitRenameRoom(); // 入力欄を離れたときの2回目は、何もしない
        Assert.Equal("決める名前", vm.RoomTitle);
        Assert.Equal(new[] { "会議室1", "決める名前" }, session.TitleWrites);
    }

    [Fact]
    public void 新しい会議には_いまのタブの名前を渡す()
    {
        var sessions = new List<FakeSession>();
        var vm = new MainViewModel(() =>
        {
            var s = new FakeSession();
            sessions.Add(s);
            return s;
        });

        vm.StartNewMeeting();
        Assert.Equal(new[] { "会議室1" }, sessions[1].TitleWrites);

        vm.RenameRoom("名簿づくり");
        vm.StartNewMeeting();
        Assert.Equal(new[] { "名簿づくり" }, sessions[2].TitleWrites);
    }

    [Fact]
    public void 過去の会議を開き直すと_書いてある名前をタブに戻す()
    {
        var reopened = new FakeSession { MeetingTitle = "昔の会議" };
        var first = new FakeSession { OnOpenPastMeeting = _ => reopened };
        var vm = new MainViewModel(() => first);

        vm.OpenPastMeeting("C:/meetings/20261030_100000");

        Assert.Equal("昔の会議", vm.RoomTitle);
        Assert.Empty(reopened.TitleWrites); // 昔の名前を書き直さない
    }

    [Fact]
    public void 名前のない過去の会議を開き直しても_タブの名前とログは変えない()
    {
        var reopened = new FakeSession();
        var first = new FakeSession { OnOpenPastMeeting = _ => reopened };
        var vm = new MainViewModel(() => first);
        vm.RenameRoom("今の名前");

        vm.OpenPastMeeting("C:/meetings/20261030_100000");

        Assert.Equal("今の名前", vm.RoomTitle);
        Assert.Empty(reopened.TitleWrites);
    }

    [Fact]
    public void 名前を変えたタブと_書き換え中のタブが_画像に描ける()
    {
        var rooms = new MeetingRooms(() => new MainViewModel(() => new FakeSession()), maxRooms: 3);
        rooms.AddRoom();
        rooms.Items[0].RenameRoom("名簿づくり");

        var renamed = MainWindowRenderTests.Render(rooms.Current, "main_window_room_renamed.png");
        rooms.Current.BeginRenameRoom(); // 2つ目の部屋を、書き換え中にする
        var editing = MainWindowRenderTests.Render(rooms.Current, "main_window_room_renaming.png");

        Assert.True(new FileInfo(renamed).Length > 10_000);
        Assert.True(new FileInfo(editing).Length > 10_000);
    }
}
