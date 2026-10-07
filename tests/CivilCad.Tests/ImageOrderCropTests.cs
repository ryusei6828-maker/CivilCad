using System.IO;
using System.Runtime.ExceptionServices;
using CivilCad.App.IO;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;
using SkiaSharp;

namespace CivilCad.Tests;

/// <summary>画像の最前面 / 最背面、トリミング、中のクリックでの選択と削除。</summary>
public class ImageOrderCropTests
{
    private static void RunSta(Action<CadDocument, CadCanvas> action)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                var doc = new CadDocument { ScaleDenominator = 100 };
                action(doc, new CadCanvas { Document = doc });
            }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    /// <summary>左半分が赤、右半分が青の 40×20 画素の PNG。</summary>
    private static string MakePng()
    {
        var path = Path.Combine(Path.GetTempPath(), "civilcad-test-" + Guid.NewGuid().ToString("N") + ".png");
        using var bmp = new SKBitmap(40, 20);
        for (var x = 0; x < 40; x++)
            for (var y = 0; y < 20; y++)
                bmp.SetPixel(x, y, x < 20 ? SKColors.Red : SKColors.Blue);
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    /// <summary>幅 4m × 高さ 2m、左下 (0,0) の画像。</summary>
    private static ImageEntity Image(Layer layer, string path = "x.png") => new(layer, path, 40, 20, Vec2.Zero, new Vec2(4, 0), new Vec2(0, 2));

    [Fact]
    public void Crop_ChangesVisibleArea()
    {
        var img = Image(new Layer("0", RgbColor.White));
        img.SetCrop(0.75, 1, 0.25, 0.5); // 順番が逆でもよい: 左 0.25〜右 0.75、上 0.5〜下 1 (= 下半分)
        Assert.True(img.IsCropped);
        Assert.Equal((0.25, 0.5, 0.75, 1.0), (img.CropLeft, img.CropTop, img.CropRight, img.CropBottom));
        Assert.Equal(new Box2(new Vec2(1, 0), new Vec2(3, 1)), img.Bounds);
        Assert.True(img.Contains(new Vec2(2, 0.5)));
        Assert.False(img.Contains(new Vec2(0.5, 0.5))); // 隠した所
        Assert.False(img.Contains(new Vec2(2, 1.5)));
        Assert.Equal(new Vec2(4, 0), img.FullCorners[1]);
        var copy = (ImageEntity)img.Clone();
        Assert.Equal(img.Bounds, copy.Bounds);
        Assert.Throws<ArgumentException>(() => img.SetCrop(1.5, 0, 2, 1)); // 画像にかかっていない
    }

    [Fact]
    public void DrawOrder_FrontAndBack_Undo()
    {
        var doc = new CadDocument();
        var line = new LineEntity(doc.CurrentLayer, Vec2.Zero, new Vec2(1, 1));
        var a = Image(doc.CurrentLayer, "a.png");
        var b = Image(doc.CurrentLayer, "b.png");
        doc.Entities.AddRange([a, line, b]);
        doc.History.Execute(new DrawOrderCommand([a], toFront: true));
        Assert.Equal([line, b, a], doc.Entities);
        Assert.True(a.InFront); // 画像は線より上に描く
        doc.History.Execute(new DrawOrderCommand([a, line], toFront: false)); // 選んだ図形どうしの順番 (線 → 画像) は保つ
        Assert.Equal([line, a, b], doc.Entities);
        Assert.False(a.InFront);
        doc.History.Undo();
        Assert.Equal([line, b, a], doc.Entities);
        Assert.True(a.InFront);
        doc.History.Undo();
        Assert.Equal([a, line, b], doc.Entities);
        Assert.False(a.InFront);
    }

    [Fact]
    public void HitTest_InsideImage_SelectsImage_LinesFirst() => RunSta((doc, canvas) =>
    {
        var img = Image(doc.CurrentLayer);
        var line = new LineEntity(doc.CurrentLayer, new Vec2(0, 1), new Vec2(4, 1));
        doc.Entities.AddRange([img, line]);
        Assert.Same(line, canvas.HitTest(new Vec2(2, 1)));     // 近くに線があれば線
        Assert.Same(img, canvas.HitTest(new Vec2(1, 0.4)));    // 無ければ画像の中で画像
        Assert.Null(canvas.HitTest(new Vec2(10, 10)));
        // 削除モードで画像の中をクリックすると消える
        var erase = new EraseTool();
        canvas.SetTool(erase);
        erase.OnPress(new Vec2(1, 0.4));
        erase.OnRelease(new Vec2(1, 0.4));
        Assert.DoesNotContain(img, doc.Entities);
        Assert.Contains(line, doc.Entities);
    });

    [Fact]
    public void CropTool_SelectedImage_TwoPoints_ThenReset() => RunSta((doc, canvas) =>
    {
        var img = Image(doc.CurrentLayer);
        doc.Entities.Add(img);
        canvas.SetSelection([img]);
        var tool = new ImageCropTool();
        canvas.SetTool(tool);
        tool.OnPoint(new Vec2(1, 0.5));
        tool.OnPoint(new Vec2(3, 5)); // 画像の外まで取っても画像の中だけ
        var cropped = Assert.Single(doc.Entities.OfType<ImageEntity>());
        Assert.Equal(new Box2(new Vec2(1, 0.5), new Vec2(3, 2)), cropped.Bounds);
        doc.History.Undo();
        Assert.False(Assert.Single(doc.Entities.OfType<ImageEntity>()).IsCropped);
        doc.History.Redo();

        // 画像をクリックして選び、R でトリミングを解除
        canvas.ClearSelection();
        var reset = new ImageCropTool();
        canvas.SetTool(reset);
        reset.OnPoint(new Vec2(2, 1));
        Assert.True(reset.OnText("r"));
        Assert.False(Assert.Single(doc.Entities.OfType<ImageEntity>()).IsCropped);
    });

    [Fact]
    public void Dxf_KeepsCropAndFront()
    {
        var png = MakePng();
        var path = Path.Combine(Path.GetTempPath(), "civilcad-test-" + Guid.NewGuid().ToString("N") + ".dxf");
        try
        {
            var doc = new CadDocument();
            var img = Image(doc.CurrentLayer, png);
            img.SetCrop(0.1, 0.2, 0.6, 0.9);
            img.InFront = true;
            doc.Entities.Add(img);
            DxfIO.Save(doc, path);
            var back = new CadDocument();
            DxfIO.Load(back, path);
            var r = Assert.Single(back.Entities.OfType<ImageEntity>());
            Assert.Equal(0.1, r.CropLeft, 9);
            Assert.Equal(0.2, r.CropTop, 9);
            Assert.Equal(0.6, r.CropRight, 9);
            Assert.Equal(0.9, r.CropBottom, 9);
            Assert.True(r.InFront);
        }
        finally
        {
            File.Delete(png);
            File.Delete(path);
        }
    }

    [Fact]
    public void Pdf_PrintsOnlyCroppedArea_FrontImageOverLines()
    {
        var png = MakePng();
        try
        {
            var doc = new CadDocument { ScaleDenominator = 100 };
            var img = Image(doc.CurrentLayer, png);
            img.SetCrop(0, 0, 0.5, 1); // 赤い左半分だけ
            var line = new LineEntity(doc.CurrentLayer, new Vec2(-1, 1), new Vec2(5, 1)) { LineWeight = 0.5 };
            doc.Entities.AddRange([img, line]);
            var o = new PdfOptions { Paper = "A4", ScaleDenominator = 100 };
            SKColor At(Vec2 w)
            {
                var l = PdfExport.ComputeLayout(doc, o);
                const float px = 8;
                using var bmp = new SKBitmap((int)(l.Width * px), (int)(l.Height * px));
                using (var canvas = new SKCanvas(bmp))
                {
                    canvas.Scale(px * 25.4f / 72f);
                    PdfExport.Render(doc, canvas, l, o);
                }
                var mm = (w - l.Origin) / l.MetersPerMm;
                return bmp.GetPixel((int)Math.Round(mm.X * px), (int)Math.Round((l.Height - mm.Y) * px));
            }
            Assert.True(At(new Vec2(1, 0.5)) is { Red: > 200, Blue: < 60 });                 // 残した所
            Assert.True(At(new Vec2(3, 0.5)) is { Red: > 240, Green: > 240, Blue: > 240 });  // 隠した所は白
            Assert.True(At(new Vec2(1, 1)) is { Red: < 80, Green: < 80, Blue: < 80 });      // 線は画像の上 (黒)
            img.InFront = true;
            Assert.True(At(new Vec2(1, 1)) is { Red: > 200, Blue: < 60 });                   // 最前面の画像は線の上
        }
        finally
        {
            File.Delete(png);
        }
    }
}
