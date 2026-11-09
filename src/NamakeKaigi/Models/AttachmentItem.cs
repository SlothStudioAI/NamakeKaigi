using System.IO;

namespace NamakeKaigi.Models;

/// <summary>入力欄に添付されたファイルまたはフォルダ1件。</summary>
public sealed record AttachmentItem
{
    /// <summary>パスから添付項目を作る(絶対パスに正規化する)。</summary>
    /// <param name="path">ファイルまたはフォルダのパス</param>
    public AttachmentItem(string path)
    {
        FullPath = Path.GetFullPath(path).Replace('\\', '/');
        IsDirectory = Directory.Exists(FullPath);
        Name = Path.GetFileName(FullPath.TrimEnd('/'));
        if (string.IsNullOrEmpty(Name)) Name = FullPath;
    }

    /// <summary>正規化された絶対パス(区切りは /)。</summary>
    public string FullPath { get; }

    /// <summary>フォルダならtrue、ファイルならfalse。</summary>
    public bool IsDirectory { get; }

    /// <summary>画面のチップに出す短い名前。</summary>
    public string Name { get; }

    /// <summary>画面のチップに出すラベル(アイコン＋名前＋絶対パス)。</summary>
    public string ChipText => $"{(IsDirectory ? "📁" : "📄")} {Name} ({FullPath})";
}
