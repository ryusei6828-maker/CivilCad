using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CivilCad.App.IO;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;
using SkiaSharp;

namespace CivilCad.Tests;

/// <summary>画像を PDF に印刷したとき、色がくすまない (画像を低い画質で書かない)。</summary>
public class PdfImageQualityTests
{
    private static readonly SKColor[] Colors = [new(230, 20, 20), new(20, 200, 40), new(30, 60, 230), new(250, 210, 0)];

    /// <summary>4 色 (左上 赤・右上 緑・左下 青・右下 黄) の 200×200 画素の PNG。</summary>
    private static string MakePng()
    {
        var path = Path.Combine(Path.GetTempPath(), "civilcad-test-" + Guid.NewGuid().ToString("N") + ".png");
        using var bmp = new SKBitmap(200, 200);
        for (var x = 0; x < 200; x++)
            for (var y = 0; y < 200; y++)
                bmp.SetPixel(x, y, Colors[(y < 100 ? 0 : 2) + (x < 100 ? 0 : 1)]);
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Export_KeepsImageColors(bool lossless)
    {
        var png = MakePng();
        var pdf = Path.Combine(Path.GetTempPath(), "civilcad-test-" + Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            var doc = new CadDocument { ScaleDenominator = 100 };
            // 20m × 20m の画像 (1:100 で用紙上 200mm)
            doc.Entities.Add(new ImageEntity(doc.CurrentLayer, png, 200, 200, Vec2.Zero, new Vec2(20, 0), new Vec2(0, 20)));
            var o = new PdfOptions { Paper = "A3", ScaleDenominator = 100, LosslessImages = lossless };
            var l = PdfExport.Export(doc, pdf, o);

            // 書いた PDF を Windows の PDF 表示で画像にして、画像の 4 色を測る
            const double pxPerMm = 2;
            int w = (int)(l.Width * pxPerMm), h = (int)(l.Height * pxPerMm);
            var bmp = PdfRenderer.Render(pdf, 0, null, w, h);
            var conv = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[w * h * 4];
            conv.CopyPixels(pixels, w * 4, 0);
            SKColor At(Vec2 world)
            {
                var mm = (world - l.Origin) / l.MetersPerMm;
                int x = (int)Math.Round(mm.X * pxPerMm), y = (int)Math.Round((l.Height - mm.Y) * pxPerMm);
                var i = (y * w + x) * 4;
                return new SKColor(pixels[i + 2], pixels[i + 1], pixels[i]);
            }
            Vec2[] centers = [new(5, 15), new(15, 15), new(5, 5), new(15, 5)];
            for (var k = 0; k < 4; k++)
            {
                var c = At(centers[k]);
                var e = Colors[k];
                Assert.True(Math.Abs(c.Red - e.Red) <= 12 && Math.Abs(c.Green - e.Green) <= 12 && Math.Abs(c.Blue - e.Blue) <= 12,
                    $"色 {k}: 期待 {e}, 実際 {c}");
            }
        }
        finally
        {
            File.Delete(png);
            File.Delete(pdf);
        }
    }
}
