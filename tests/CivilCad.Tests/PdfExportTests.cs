using System.IO;
using System.Text;
using CivilCad.App.IO;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;
using SkiaSharp;

namespace CivilCad.Tests;

public class PdfExportTests
{
    private const float PxPerMm = 4;

    /// <summary>縮尺 1:100 の A4 横に印刷する図面。図形全体の中心が用紙の中心に来る。</summary>
    private static (CadDocument Doc, PdfOptions Options) Sample()
    {
        var doc = new CadDocument { ScaleDenominator = 500 };
        var red = doc.GetOrCreateLayer("赤", new RgbColor(255, 0, 0));
        var hidden = doc.GetOrCreateLayer("非表示", RgbColor.White);
        hidden.IsVisible = false;
        doc.Entities.Add(new LineEntity(red, new Vec2(-10, -5), new Vec2(10, -5)) { LineWeight = 0.5 });                // 赤い線
        doc.Entities.Add(new LineEntity(doc.CurrentLayer, new Vec2(-10, 5), new Vec2(10, 5)) { LineWeight = 0.5 });     // 白 → 黒
        doc.Entities.Add(new LineEntity(hidden, new Vec2(-10, 0), new Vec2(10, 0)) { LineWeight = 0.5 });               // 印刷しない
        doc.Entities.Add(new ArcEntity(doc.CurrentLayer, new Vec2(0, 0), 2, 0, Math.PI) { LineWeight = 0.5 });          // 上半分の円弧
        doc.Entities.Add(new HatchEntity(doc.CurrentLayer, [[new Vec2(5, -1), new Vec2(7, -1), new Vec2(7, 1), new Vec2(5, 1)]], [], solid: true)
        {
            Color = new RgbColor(0, 0, 255),
        });
        doc.Entities.Add(new TextEntity(doc.CurrentLayer, new Vec2(-8, 1), "測点 No.1", 1)); // 図面 1m = 用紙 10mm
        return (doc, new PdfOptions { Paper = "A4", ScaleDenominator = 100 });
    }

    private static SKBitmap Raster(CadDocument doc, PdfOptions o, out PdfExport.Layout layout)
    {
        layout = PdfExport.ComputeLayout(doc, o);
        var bmp = new SKBitmap((int)(layout.Width * PxPerMm), (int)(layout.Height * PxPerMm));
        using var canvas = new SKCanvas(bmp);
        canvas.Scale(PxPerMm * 25.4f / 72f);
        PdfExport.Render(doc, canvas, layout, o);
        return bmp;
    }

    /// <summary>図面の点の、画像上の画素の近く (±3px) にある色。</summary>
    private static IEnumerable<SKColor> Near(SKBitmap bmp, PdfExport.Layout l, Vec2 w)
    {
        var mm = (w - l.Origin) / l.MetersPerMm;
        int cx = (int)Math.Round(mm.X * PxPerMm), cy = (int)Math.Round((l.Height - mm.Y) * PxPerMm);
        for (var dy = -3; dy <= 3; dy++)
            for (var dx = -3; dx <= 3; dx++)
                if (cx + dx >= 0 && cy + dy >= 0 && cx + dx < bmp.Width && cy + dy < bmp.Height)
                    yield return bmp.GetPixel(cx + dx, cy + dy);
    }

    private static bool IsWhite(SKColor c) => c.Red > 240 && c.Green > 240 && c.Blue > 240;

    [Fact]
    public void Layout_A4Landscape_CenteredOnShapes()
    {
        var (doc, o) = Sample();
        var l = PdfExport.ComputeLayout(doc, o);
        Assert.Equal("A4", l.PaperName);
        Assert.Equal((297.0, 210.0), (l.Width, l.Height));
        Assert.Equal(0.1, l.MetersPerMm, 12); // 1:100 → 用紙 1mm = 0.1m
        // 図形 (x -10〜10, y -5〜5) の中心 (0,0) が用紙の中心
        Assert.True((l.Origin + new Vec2(297, 210) / 2 * 0.1).DistanceTo(Vec2.Zero) < 1e-9);

        o.Portrait = true;
        var p = PdfExport.ComputeLayout(doc, o);
        Assert.Equal((210.0, 297.0), (p.Width, p.Height));
        // 用紙を指定しないときは、収まる最小の A 判
        o.Paper = null;
        o.Portrait = false;
        o.ScaleDenominator = 50; // 図形 20m × 10m → 用紙 400mm × 200mm + 余白 → A2
        Assert.Equal("A2", PdfExport.ComputeLayout(doc, o).PaperName);
    }

    [Fact]
    public void Render_ColorsArcsTextAndHiddenLayer()
    {
        var (doc, o) = Sample();
        using var bmp = Raster(doc, o, out var l);
        Assert.Contains(Near(bmp, l, new Vec2(0, -5)), c => c.Red > 200 && c.Green < 60 && c.Blue < 60);       // 赤い線
        Assert.Contains(Near(bmp, l, new Vec2(0, 5)), c => c.Red < 60 && c.Green < 60 && c.Blue < 60);          // 白い線は黒で
        Assert.All(Near(bmp, l, new Vec2(-5, 0)), c => Assert.True(IsWhite(c)));                                // 非表示のレイヤは印刷しない
        Assert.Contains(Near(bmp, l, new Vec2(0, 2)), c => !IsWhite(c));                                        // 円弧は上半分 (反時計回り)
        Assert.All(Near(bmp, l, new Vec2(0, -2)), c => Assert.True(IsWhite(c)));
        Assert.Contains(Near(bmp, l, new Vec2(6, 0)), c => c.Blue > 200 && c.Red < 60);                         // 塗りつぶし
        // 文字 (高さ 1m = 10mm、左下が (-8,1)): 文字の枠の中に黒い画素がある
        var inText = false;
        for (var x = -7.5; x < -4 && !inText; x += 0.25)
            inText = Near(bmp, l, new Vec2(x, 1.5)).Any(c => c.Red < 100 && c.Green < 100);
        Assert.True(inText, "文字が描かれていない");
    }

    [Fact]
    public void Render_Monochrome()
    {
        var (doc, o) = Sample();
        o.Monochrome = true;
        using var bmp = Raster(doc, o, out var l);
        Assert.Contains(Near(bmp, l, new Vec2(0, -5)), c => c.Red < 60 && c.Green < 60 && c.Blue < 60); // 赤い線も黒
        Assert.Contains(Near(bmp, l, new Vec2(6, 0)), c => c.Red == c.Green && c.Green == c.Blue && !IsWhite(c)); // 塗りは灰色
    }

    [Fact]
    public void Export_WritesPdf_TextAsOutlinesByDefault()
    {
        var (doc, o) = Sample();
        var path = Path.Combine(Path.GetTempPath(), "civilcad-test-" + Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            PdfExport.Export(doc, path, o);
            var bytes = File.ReadAllBytes(path);
            var text = Encoding.Latin1.GetString(bytes);
            Assert.StartsWith("%PDF-", text);
            Assert.Contains("/MediaBox [0 0 841.9", text); // A4 横 297mm = 841.89pt
            Assert.EndsWith("%%EOF", text.TrimEnd());
            // 文字は形で描くので、フォントは埋め込まない (ファイルが小さい)
            Assert.DoesNotContain("/FontFile", text);
            Assert.True(bytes.Length < 200_000, $"{bytes.Length} bytes");

            // フォントを埋め込む設定 (PDF の中で文字を検索できる)
            o.EmbedFonts = true;
            PdfExport.Export(doc, path, o);
            Assert.Contains("/FontFile", Encoding.Latin1.GetString(File.ReadAllBytes(path)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Render_OutlinedTextMatchesEmbeddedText()
    {
        // 形で描いた文字と、フォントで描いた文字は同じ位置・大きさ
        var (doc, o) = Sample();
        using var outlined = Raster(doc, o, out var l);
        o.EmbedFonts = true;
        using var embedded = Raster(doc, o, out _);
        int Dark(SKBitmap b)
        {
            var n = 0;
            for (var x = 0; x < b.Width; x += 2)
                for (var y = 0; y < b.Height; y += 2)
                    if (b.GetPixel(x, y) is var c && c.Red < 128 && c.Green < 128 && c.Blue < 128) n++;
            return n;
        }
        var (a, e) = (Dark(outlined), Dark(embedded));
        Assert.True(Math.Abs(a - e) <= Math.Max(20, e / 20), $"outlined {a}, embedded {e}");
    }
}
