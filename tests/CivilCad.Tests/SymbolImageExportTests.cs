using System.IO;
using CivilCad.App.IO;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;
using SkiaSharp;

namespace CivilCad.Tests;

/// <summary>プリセット (記号) と画像を、DWG・JWW・P21・PDF に書くときの扱い。</summary>
public class SymbolImageExportTests
{
    private static string Temp(string ext) => Path.Combine(Path.GetTempPath(), "civilcad-test-" + Guid.NewGuid().ToString("N") + ext);

    /// <summary>左半分が赤、右半分が青の 40×20 画素の PNG。</summary>
    private static string MakePng()
    {
        var path = Temp(".png");
        using var bmp = new SKBitmap(40, 20);
        for (var x = 0; x < 40; x++)
            for (var y = 0; y < 20; y++)
                bmp.SetPixel(x, y, x < 20 ? SKColors.Red : SKColors.Blue);
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    private static CadDocument Sample(string png)
    {
        var doc = new CadDocument { ScaleDenominator = 100 };
        doc.Entities.Add(new LineEntity(doc.CurrentLayer, new Vec2(0, 0), new Vec2(10, 0)));
        doc.Entities.Add(new SymbolEntity(doc.CurrentLayer, SymbolLibrary.NorthArrow, new Vec2(5, 5), 20, doc.PaperMmToMeters(20)));
        // 幅 4m × 高さ 2m の画像。左下が (-6, -2)
        doc.Entities.Add(new ImageEntity(doc.CurrentLayer, png, 40, 20, new Vec2(-6, -2), new Vec2(4, 0), new Vec2(0, 2)));
        return doc;
    }

    [Theory]
    [InlineData(".jww")]
    [InlineData(".p21")]
    [InlineData(".dwg")]
    public void Save_ExplodesSymbols_SkipsImages(string ext)
    {
        var png = MakePng();
        var path = Temp(ext);
        try
        {
            var doc = Sample(png);
            DrawingFiles.Save(doc, path);
            var back = new CadDocument();
            DrawingFiles.Open(back, path);
            Assert.Empty(back.Entities.OfType<ImageEntity>());
            Assert.Empty(back.Entities.OfType<SymbolEntity>());
            // 方位記号は線・円・塗り・文字 (N) になって残る
            Assert.Contains(back.Entities.OfType<TextEntity>(), t => t.Text == "N");
            Assert.True(back.Entities.Count > 3, $"{back.Entities.Count} 個");
            Assert.Contains("画像 1 枚は保存していません", DrawingFiles.ImageNote(doc, path));
        }
        finally
        {
            File.Delete(png);
            File.Delete(path);
        }
    }

    [Fact]
    public void ImageNote_DxfKeepsImages()
    {
        var doc = new CadDocument();
        Assert.Null(DrawingFiles.ImageNote(doc, "a.jww"));
        doc.Entities.Add(new ImageEntity(doc.CurrentLayer, "x.png", 1, 1, Vec2.Zero, new Vec2(1, 0), new Vec2(0, 1)));
        Assert.Contains("場所を記録", DrawingFiles.ImageNote(doc, "a.dxf"));
    }

    [Fact]
    public void Pdf_DrawsImagesAndSymbols()
    {
        var png = MakePng();
        try
        {
            var doc = Sample(png);
            var o = new PdfOptions { Paper = "A4" };
            var l = PdfExport.ComputeLayout(doc, o);
            const float pxPerMm = 4;
            using var bmp = new SKBitmap((int)(l.Width * pxPerMm), (int)(l.Height * pxPerMm));
            using (var canvas = new SKCanvas(bmp))
            {
                canvas.Scale(pxPerMm * 25.4f / 72f);
                PdfExport.Render(doc, canvas, l, o);
            }
            SKColor At(Vec2 w)
            {
                var mm = (w - l.Origin) / l.MetersPerMm;
                return bmp.GetPixel((int)Math.Round(mm.X * pxPerMm), (int)Math.Round((l.Height - mm.Y) * pxPerMm));
            }
            // 画像の左半分は赤、右半分は青 (上下・左右が図面と同じ向き)
            Assert.True(At(new Vec2(-5, -1)) is { Red: > 200, Blue: < 60 }, At(new Vec2(-5, -1)).ToString());
            Assert.True(At(new Vec2(-3, -1)) is { Blue: > 200, Red: < 60 }, At(new Vec2(-3, -1)).ToString());
            Assert.True(At(new Vec2(-7, -1)) is { Red: > 240, Green: > 240, Blue: > 240 }); // 画像の外は白

            // 方位記号: 記号の範囲に黒い画素がある
            var sym = doc.Entities.OfType<SymbolEntity>().Single().Bounds;
            var dark = false;
            for (var x = sym.Min.X; x < sym.Max.X && !dark; x += 0.05)
                for (var y = sym.Min.Y; y < sym.Max.Y && !dark; y += 0.05)
                    dark = At(new Vec2(x, y)) is { Red: < 100, Green: < 100, Blue: < 100 };
            Assert.True(dark, "方位記号が描かれていない");

            // 白黒では画像も灰色
            o.Monochrome = true;
            using var mono = new SKBitmap(bmp.Width, bmp.Height);
            using (var canvas = new SKCanvas(mono))
            {
                canvas.Scale(pxPerMm * 25.4f / 72f);
                PdfExport.Render(doc, canvas, l, o);
            }
            var mm2 = (new Vec2(-5, -1) - l.Origin) / l.MetersPerMm;
            var c = mono.GetPixel((int)Math.Round(mm2.X * pxPerMm), (int)Math.Round((l.Height - mm2.Y) * pxPerMm));
            Assert.True(Math.Abs(c.Red - c.Green) <= 2 && Math.Abs(c.Green - c.Blue) <= 2, c.ToString());
        }
        finally
        {
            File.Delete(png);
        }
    }

    [Fact]
    public void Scale_ScreenFixedText_ScalesPixels()
    {
        var t = new TextEntity(new Layer("0", RgbColor.White), Vec2.Zero, "A", 1) { ScreenHeightPx = 16 };
        t.Scale(Vec2.Zero, 2);
        Assert.Equal(32, t.ScreenHeightPx);
    }
}
