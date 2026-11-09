using NamakeKaigi.Models;

namespace NamakeKaigi.Tests;

/// <summary>「過去の会議を開く」一覧に出す、会議の名前の試験。</summary>
public class PastMeetingNameTests
{
    private static SavedMeetingSummary Make(string folderName) =>
        new(folderName, "C:/m/" + folderName, "C:/m/" + folderName + "/log.md", new DateTime(2026, 11, 3, 22, 6, 0), "最初の発言", 3, true);

    [Fact]
    public void 新しい形式のフォルダなら_最後の名前が会議の名前になる()
    {
        var m = Make("20261103_204740/クロ");
        Assert.Equal("クロ", m.MeetingName);
        Assert.Equal("クロ · ", m.MeetingNamePrefix);
    }

    [Fact]
    public void 古い形式のフォルダは_名前がなく_何も足さない()
    {
        var m = Make("20261025_013000");
        Assert.Equal("", m.MeetingName);
        Assert.Equal("", m.MeetingNamePrefix);
    }

    [Fact]
    public void 実際の会議フォルダの一覧でも_名前が付く()
    {
        using var dir = new TempDir();
        var room = System.IO.Path.Combine(dir.Path, "20261103_204740", "修正確認");
        System.IO.Directory.CreateDirectory(room);
        System.IO.File.WriteAllText(System.IO.Path.Combine(room, "log.md"),
            "# 会議ログ: 修正確認\n\n## ユーザー → Claude (20:48:56)\n\nこんにちは\n");
        var list = NamakeKaigi.Services.MeetingStore.ListMeetings(dir.Path);
        Assert.Single(list);
        Assert.Equal("修正確認", list[0].MeetingName);
    }
}
