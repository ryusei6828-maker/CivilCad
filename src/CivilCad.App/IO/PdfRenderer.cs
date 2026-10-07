using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace CivilCad.App.IO;

/// <summary>PDF の 1 ページの大きさ (用紙上の mm)。</summary>
public sealed record PdfPageInfo(int Index, double WidthMm, double HeightMm)
{
    public string Label => $"{Index + 1} ページ ({WidthMm:0}×{HeightMm:0}mm{PaperName})";

    private string PaperName
    {
        get
        {
            var (l, s) = (Math.Max(WidthMm, HeightMm), Math.Min(WidthMm, HeightMm));
            foreach (var (name, pl, ps) in new[] { ("A0", 1189, 841), ("A1", 841, 594), ("A2", 594, 420), ("A3", 420, 297), ("A4", 297, 210) })
                if (Math.Abs(l - pl) < 3 && Math.Abs(s - ps) < 3) return $", {name} {(WidthMm >= HeightMm ? "横" : "縦")}";
            return "";
        }
    }
}

/// <summary>
/// PDF のページを画像にする (Windows 標準の PDF 機能)。ページの一部だけを好きな細かさで描けるので、
/// 大きな図面の PDF でも、拡大したとき見えている範囲だけを細かく描き直せる。
/// 画像と同じに扱うため、ページを PixelsPerMm (600dpi 相当) の画素の画像とみなす。
/// </summary>
public static class PdfRenderer
{
    /// <summary>PDF を画像とみなすときの細かさ (600dpi 相当)。</summary>
    public const double PixelsPerMm = 600 / 25.4;

    private const double DipPerMm = 96 / 25.4;

    public static bool IsPdf(string path) => Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    private static PdfDocument Open(string path)
    {
        var file = StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)).AsTask().GetAwaiter().GetResult();
        return PdfDocument.LoadFromFileAsync(file).AsTask().GetAwaiter().GetResult();
    }

    /// <summary>全ページの大きさ (パスワード付きなどで開けなければ例外)。</summary>
    public static List<PdfPageInfo> ReadPages(string path)
    {
        var doc = Open(path);
        var list = new List<PdfPageInfo>();
        for (var i = 0; i < doc.PageCount; i++)
        {
            using var page = doc.GetPage((uint)i);
            list.Add(new PdfPageInfo(i, page.Size.Width / DipPerMm, page.Size.Height / DipPerMm));
        }
        return list;
    }

    /// <summary>ページを画像とみなしたときの画素数。</summary>
    public static (int Width, int Height) PixelSize(PdfPageInfo p) =>
        (Math.Max(1, (int)Math.Round(p.WidthMm * PixelsPerMm)), Math.Max(1, (int)Math.Round(p.HeightMm * PixelsPerMm)));

    /// <summary>
    /// ページの region (PixelsPerMm の画素での範囲、null なら全体) を outW × outH 画素で描く。
    /// 背景は白。戻り値は凍結済み (別スレッドで作って画面側で使える)。
    /// </summary>
    public static BitmapSource Render(string path, int pageIndex, Int32Rect? region, int outW, int outH)
    {
        var doc = Open(path);
        using var page = doc.GetPage((uint)Math.Clamp(pageIndex, 0, (int)doc.PageCount - 1));
        var options = new PdfPageRenderOptions
        {
            DestinationWidth = (uint)Math.Max(1, outW),
            DestinationHeight = (uint)Math.Max(1, outH),
            BackgroundColor = global::Windows.UI.Color.FromArgb(255, 255, 255, 255),
            BitmapEncoderId = global::Windows.Graphics.Imaging.BitmapEncoder.BmpEncoderId,
        };
        if (region is Int32Rect r)
        {
            var k = DipPerMm / PixelsPerMm; // 画素 → ページの DIP
            options.SourceRect = new global::Windows.Foundation.Rect(r.X * k, r.Y * k, r.Width * k, r.Height * k);
        }
        using var stream = new InMemoryRandomAccessStream();
        page.RenderToStreamAsync(stream, options).AsTask().GetAwaiter().GetResult();
        stream.Seek(0);
        using var s = stream.AsStreamForRead();
        var frame = BitmapDecoder.Create(s, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad).Frames[0];
        var result = new WriteableBitmap(frame);
        result.Freeze();
        return result;
    }
}
