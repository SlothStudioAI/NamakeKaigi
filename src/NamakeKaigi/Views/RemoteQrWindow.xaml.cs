using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using NamakeKaigi.Services.Remote;

namespace NamakeKaigi.Views;

/// <summary>リモートのURLを、QRコードと文字で見せる窓。スマホのカメラで読んでつなぐ。</summary>
public partial class RemoteQrWindow : Window
{
    private readonly string _url;

    /// <summary>窓を作る。</summary>
    /// <param name="url">リモートのURL(合言葉つき)</param>
    /// <param name="note">上に出す説明(なければ既定の説明)</param>
    public RemoteQrWindow(string url, string? note = null)
    {
        InitializeComponent();
        _url = url;
        UrlText.Text = url;
        NoteText.Text = note ?? "Tailscaleをオンにしたスマホで読むと、ブラウザでこの会議に参加できます。";
        QrImage.Source = ToBitmap(RemoteQr.ToPng(url));
    }

    /// <summary>PNGのバイト列を、画面に出せる画像にする。</summary>
    /// <param name="png">PNG画像</param>
    /// <returns>画像</returns>
    private static BitmapImage ToBitmap(byte[] png)
    {
        var image = new BitmapImage();
        using var stream = new MemoryStream(png);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    /// <summary>「URLをコピー」: URLをクリップボードに入れる。</summary>
    private void OnCopy(object sender, RoutedEventArgs e) => Clipboard.SetText(_url);

    /// <summary>「閉じる」: 窓を閉じる。</summary>
    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
