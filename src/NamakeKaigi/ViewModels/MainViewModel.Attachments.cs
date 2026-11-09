using System.Collections.ObjectModel;
using System.Windows.Input;
using NamakeKaigi.Models;
using NamakeKaigi.Services;

namespace NamakeKaigi.ViewModels;

/// <summary>メイン画面の状態とコマンド(partial): 添付ファイル。</summary>
public sealed partial class MainViewModel
{
    /// <summary>入力欄に添付されたファイル・フォルダの一覧。</summary>
    public ObservableCollection<AttachmentItem> Attachments { get; } = new();

    /// <summary>添付が1件以上あるならtrue。</summary>
    public bool HasAttachments => Attachments.Count > 0;

    /// <summary>ファイルまたはフォルダのパスを添付一覧へ追加する(同じパスは重複して入れない)。</summary>
    /// <param name="paths">追加するパスの並び</param>
    public void AddAttachments(IEnumerable<string> paths)
    {
        foreach (var raw in paths)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var item = new AttachmentItem(raw.Trim());
            if (Attachments.All(a => !string.Equals(a.FullPath, item.FullPath, StringComparison.OrdinalIgnoreCase)))
            {
                Attachments.Add(item);
            }
        }
    }

    /// <summary>指定した添付を取り除く。</summary>
    /// <param name="item">取り除く添付項目</param>
    public void RemoveAttachment(AttachmentItem item) => Attachments.Remove(item);
}
