using QRCoder;

namespace NamakeKaigi.Services.Remote;

/// <summary>リモートのURLを、スマホのカメラで読めるQRコード(PNG)にする。画面を知らない。</summary>
public static class RemoteQr
{
    /// <summary>文字列を、QRコードのPNG画像にする。</summary>
    /// <param name="text">QRコードに入れる文字列(URL)</param>
    /// <param name="pixelsPerModule">1マスの大きさ(ピクセル)</param>
    /// <returns>PNG画像のバイト列</returns>
    public static byte[] ToPng(string text, int pixelsPerModule = 8)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(data).GetGraphic(pixelsPerModule);
    }
}
