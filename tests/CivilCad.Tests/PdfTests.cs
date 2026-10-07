using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CivilCad.App.IO;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

public class PdfTests
{
    /// <summary>
    /// テスト用の PDF: 1 ページ目 A3 横 (左半分が赤)、2 ページ目 A4 縦 (白)。
    /// </summary>
    private static string MakePdf()
    {
        var path = Path.Combine(Path.GetTempPath(), $"civilcad_pdf_{Guid.NewGuid():N}.pdf");
        const double a3w = 420 / 25.4 * 72, a3h = 297 / 25.4 * 72, a4w = 210 / 25.4 * 72, a4h = 297 / 25.4 * 72;
        var content = FormattableString.Invariant($"1 0 0 rg 0 0 {a3w / 2:0.##} {a3h:0.##} re f");
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 5 0 R] /Count 2 >>",
            FormattableString.Invariant($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {a3w:0.##} {a3h:0.##}] /Contents 4 0 R >>"),
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            FormattableString.Invariant($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {a4w:0.##} {a4h:0.##}] >>"),
        };
        var sb = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(sb.ToString()));
            sb.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = Encoding.ASCII.GetByteCount(sb.ToString());
        sb.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) sb.Append($"{o:D10} 00000 n \n");
        sb.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        File.WriteAllText(path, sb.ToString(), Encoding.ASCII);
        return path;
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    private static T WaitFor<T>(Func<T?> get, int timeoutMs = 20000) where T : class
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (get() is T v) return v;
            Thread.Sleep(20);
        }
        throw new TimeoutException();
    }

    private static (byte B, byte G, byte R) Pixel(BitmapSource bmp, int x, int y)
    {
        var px = new byte[4];
        new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0).CopyPixels(new Int32Rect(x, y, 1, 1), px, 4, 0);
        return (px[0], px[1], px[2]);
    }

    [Fact]
    public void ReadsPages_AndRendersPreviewAndRegion()
    {
        var path = MakePdf();
        try
        {
            var pages = PdfRenderer.ReadPages(path);
            Assert.Equal(2, pages.Count);
            Assert.Equal(420, pages[0].WidthMm, 0);
            Assert.Equal(297, pages[0].HeightMm, 0);
            Assert.Contains("A3 横", pages[0].Label);
            Assert.Contains("A4 縦", pages[1].Label);
            var (w, h) = PdfRenderer.PixelSize(pages[0]);
            Assert.InRange(w, 9920, 9922); // 420mm を 600dpi 相当で

            // 全体の縮小版 (裏で読む): 左が赤、右が白
            var entry = ImageCache.Get(path, 0);
            var preview = WaitFor(() => entry.Preview);
            Assert.Equal(ImageCache.PreviewMax, preview.PixelWidth);
            var left = Pixel(preview, 100, 100);
            var right = Pixel(preview, preview.PixelWidth - 100, 100);
            Assert.True(left.R > 200 && left.G < 60, $"左 {left}");
            Assert.True(right.R > 200 && right.G > 200, $"右 {right}");

            // 一部を細かく描く: 赤と白の境目 (横の真ん中) をまたぐ範囲
            ImageCache.RequestDetail(entry, new Int32Rect(w / 2 - 200, 1000, 400, 300), 400, 300);
            var detail = WaitFor(() => entry.Detail is { } d ? d.Bitmap : null);
            Assert.Equal((400, 300), (detail.PixelWidth, detail.PixelHeight));
            Assert.True(Pixel(detail, 50, 150).R > 200 && Pixel(detail, 50, 150).G < 60);
            Assert.True(Pixel(detail, 350, 150).G > 200);

            // 2 ページ目は別の画像
            var p2 = WaitFor(() => ImageCache.Get(path, 1).Preview);
            Assert.True(p2.PixelHeight > p2.PixelWidth);
        }
        finally
        {
            ImageCache.Forget(path);
            File.Delete(path);
        }
    }

    [Fact]
    public void PlacedAtPaperSize_AndPageSavedInDxf() => RunSta(() =>
    {
        var path = MakePdf();
        var dxf = Path.ChangeExtension(path, ".dxf");
        try
        {
            var doc = new CadDocument { ScaleDenominator = 500 };
            var canvas = new CadCanvas { Document = doc };
            var page = PdfRenderer.ReadPages(path)[1];
            var (w, h) = PdfRenderer.PixelSize(page);
            var tool = new ImagePlaceTool(path, w, h, page.Index, page.WidthMm);
            canvas.SetTool(tool);
            tool.OnPoint(new Vec2(-1000, 2000));
            tool.OnEnter(); // 原寸: A4 縦 210mm × 1:500 = 105m
            var img = Assert.Single(doc.Entities.OfType<ImageEntity>());
            Assert.Equal(105, img.Width, 1);
            Assert.Equal(148.5, img.Height, 1);
            Assert.Equal(1, img.Page);

            DxfIO.Save(doc, dxf);
            var loaded = new CadDocument();
            DxfIO.Load(loaded, dxf);
            var back = Assert.Single(loaded.Entities.OfType<ImageEntity>());
            Assert.Equal(1, back.Page);
            Assert.Equal(path, back.Path);
            Assert.Equal(img.Width, back.Width, 6);
        }
        finally
        {
            ImageCache.Forget(path);
            File.Delete(path);
            if (File.Exists(dxf)) File.Delete(dxf);
        }
    });

    [Fact]
    public void BrokenPdf_Throws_AndCacheReportsError()
    {
        var path = Path.Combine(Path.GetTempPath(), $"civilcad_bad_{Guid.NewGuid():N}.pdf");
        File.WriteAllText(path, "not a pdf");
        try
        {
            Assert.ThrowsAny<Exception>(() => PdfRenderer.ReadPages(path));
            var entry = ImageCache.Get(path);
            WaitFor(() => entry.Loading ? null : entry);
            Assert.Null(entry.Preview);
            Assert.Contains("PDFを読み込めません", entry.Error);
        }
        finally
        {
            ImageCache.Forget(path);
            File.Delete(path);
        }
    }
}
