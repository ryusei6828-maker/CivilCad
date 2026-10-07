using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CivilCad.App.IO;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

public class ImageTests
{
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

    /// <summary>左半分が赤・右半分が青のテスト画像を JPEG で書く。</summary>
    private static string MakeJpeg(int w, int h)
    {
        var path = Path.Combine(Path.GetTempPath(), $"civilcad_img_{Guid.NewGuid():N}.jpg");
        var stride = w * 3;
        var pixels = new byte[stride * h];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var i = y * stride + x * 3;
                if (x < w / 2) pixels[i + 2] = 255; else pixels[i] = 255; // BGR
            }
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr24, null, pixels, stride);
        var enc = new JpegBitmapEncoder { QualityLevel = 80 };
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
        return path;
    }

    private static void Delete(string path)
    {
        foreach (var f in WorldFile.Candidates(path).Append(path))
            if (File.Exists(f)) File.Delete(f);
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

    [Fact]
    public void WorldFile_PlacesImageCorners()
    {
        var w = WorldFile.Parse("0.5\n0\n0\n-0.5\n100.25\n199.75\n")!.Value;
        var (origin, u, v) = ImageEntity.FromWorldFile(w, 200, 100);
        // 左上の画素の中心 (100.25, 199.75) → 左上の角 (100, 200)。200×100 画素 × 0.5m = 100m × 50m
        Assert.Equal(new Vec2(100, 150), origin);
        Assert.Equal(new Vec2(100, 0), u);
        Assert.Equal(new Vec2(0, 50), v);
        Assert.Null(WorldFile.Parse("abc"));
        Assert.Contains(WorldFile.Candidates(@"C:\a\photo.jpg"), c => c.EndsWith("photo.jgw"));
        Assert.Contains(WorldFile.Candidates(@"C:\a\ortho.tif"), c => c.EndsWith("ortho.tfw"));
    }

    [Fact]
    public void Image_SelectedByFrame_MapUnmap_Rotate()
    {
        var layer = new Layer("0", RgbColor.White);
        var img = new ImageEntity(layer, "x.jpg", 200, 100, new Vec2(10, 20), new Vec2(100, 0), new Vec2(0, 50));
        Assert.Equal(25, img.DistanceTo(new Vec2(60, 45)));   // 中をクリックしても選ばれない (枠まで 25m)
        Assert.Equal(0, img.DistanceTo(new Vec2(10, 30)), 9);   // 枠の上
        Assert.False(img.IntersectsBox(new Box2(new Vec2(50, 40), new Vec2(60, 50)))); // 中だけを囲んでも選ばれない
        Assert.Equal(new Vec2(10, 70), img.Map(0, 0));          // 左上
        var (s, t) = img.Unmap(new Vec2(35, 57.5));
        Assert.Equal(0.25, s, 9);
        Assert.Equal(0.25, t, 9);
        img.Rotate(new Vec2(10, 20), Math.PI / 2);
        Assert.Equal(10, img.Map(1, 1).X, 9);                    // 右下 (110, 20) が真上へ回る
        Assert.Equal(120, img.Map(1, 1).Y, 9);
    }

    [Fact]
    public void ReadInfo_UsesWorldFile()
    {
        string? path = null;
        RunSta(() => path = MakeJpeg(40, 20));
        try
        {
            Assert.Null(ImageCache.ReadInfo(path!).Placement);
            File.WriteAllText(Path.ChangeExtension(path!, ".jgw"), "1\n0\n0\n-1\n-500.5\n2000.5\n");
            var info = ImageCache.ReadInfo(path!);
            Assert.Equal((40, 20), (info.PixelWidth, info.PixelHeight));
            Assert.Equal(new Vec2(-501, 1981), info.Placement!.Value.Origin);
            Assert.Equal(new Vec2(40, 0), info.Placement!.Value.U);
        }
        finally
        {
            Delete(path!);
        }
    }

    [Fact]
    public void LargeImage_LoadsInBackground_AndDetailReadsRegion()
    {
        string? path = null;
        RunSta(() => path = MakeJpeg(6000, 4000));
        try
        {
            var sw = Stopwatch.StartNew();
            var entry = ImageCache.Get(path!);
            Assert.True(sw.ElapsedMilliseconds < 200, $"Get が {sw.ElapsedMilliseconds}ms 待った"); // 読み込みを待たない
            var preview = WaitFor(() => entry.Preview);
            Assert.Equal(ImageCache.PreviewMax, preview.PixelWidth); // 縮小版だけ持つ
            Assert.True(preview.IsFrozen);

            // 右下の 600×400 画素を 300×200 で読み直す → 右半分なので青
            ImageCache.RequestDetail(entry, new Int32Rect(5000, 3000, 600, 400), 300, 200);
            var detail = WaitFor(() => entry.Detail is { } d ? (object)d : null);
            var (region, bmp, scale) = ((Int32Rect, BitmapSource, double))detail;
            Assert.Equal(5000, region.X);
            Assert.Equal((300, 200), (bmp.PixelWidth, bmp.PixelHeight));
            Assert.Equal(0.5, scale, 9);
            var px = new byte[4];
            new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0).CopyPixels(new Int32Rect(150, 100, 1, 1), px, 4, 0);
            Assert.True(px[0] > 200 && px[2] < 60, $"B={px[0]} R={px[2]}");
        }
        finally
        {
            ImageCache.Forget(path!);
            Delete(path!);
        }
    }

    [Fact]
    public void MissingFile_ReportsError_WithoutThrowing()
    {
        var entry = ImageCache.Get(Path.Combine(Path.GetTempPath(), $"nothing_{Guid.NewGuid():N}.jpg"));
        WaitFor(() => entry.Loading ? null : entry);
        Assert.Null(entry.Preview);
        Assert.Contains("見つかりません", entry.Error);
    }

    [Fact]
    public void Canvas_DrawsImage_AndPlaceTool() => RunSta(() =>
    {
        var path = MakeJpeg(300, 200);
        try
        {
            var doc = new CadDocument { ScaleDenominator = 500 };
            var canvas = new CadCanvas { Document = doc };
            canvas.Measure(new Size(800, 600));
            canvas.Arrange(new Rect(0, 0, 800, 600));
            var tool = new ImagePlaceTool(path, 300, 200);
            canvas.SetTool(tool);
            tool.OnPoint(new Vec2(0, 0));
            Assert.True(tool.OnText("60")); // 幅 60m
            var img = Assert.Single(doc.Entities.OfType<ImageEntity>());
            Assert.Equal(60, img.Width, 9);
            Assert.Equal(40, img.Height, 9); // 縦横比は画像のまま
            canvas.ZoomTo(img.Bounds);
            canvas.RedrawAll(); // 読込中の表示
            WaitFor(() => ImageCache.Get(path).Preview);
            canvas.RedrawAll(); // 画像の表示
        }
        finally
        {
            ImageCache.Forget(path);
            Delete(path);
        }
    });

    [Fact]
    public void SavedToDxf_AndSkippedInSfc()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"civilcad_imgdxf_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var doc = new CadDocument { ScaleDenominator = 500 };
            var imgPath = Path.Combine(dir, "ortho.jpg");
            var rot = new ImageEntity(doc.CurrentLayer, imgPath, 400, 300, new Vec2(-60000.5, -95000.25),
                new Vec2(40, 0).Rotate(0.3), new Vec2(0, 30).Rotate(0.3)) { TransparencyPercent = 30 };
            doc.Entities.Add(rot);
            doc.Entities.Add(new LineEntity(doc.CurrentLayer, new Vec2(0, 0), new Vec2(1, 1)));

            var dxf = Path.Combine(dir, "a.dxf");
            DxfIO.Save(doc, dxf);
            var loaded = new CadDocument();
            DxfIO.Load(loaded, dxf);
            var img = Assert.Single(loaded.Entities.OfType<ImageEntity>());
            Assert.Equal(imgPath, img.Path);
            Assert.Equal((400, 300), (img.PixelWidth, img.PixelHeight));
            Assert.Equal(rot.Origin.X, img.Origin.X, 6);
            Assert.Equal(rot.Origin.Y, img.Origin.Y, 6);
            Assert.Equal(rot.Map(1, 0).X, img.Map(1, 0).X, 6);
            Assert.Equal(rot.Map(1, 0).Y, img.Map(1, 0).Y, 6);
            Assert.Equal(30, img.TransparencyPercent);

            var sfc = Path.Combine(dir, "a.sfc");
            SfcWriter.Save(doc, sfc);
            var fromSfc = new CadDocument();
            SfcIO.Load(fromSfc, sfc);
            Assert.Empty(fromSfc.Entities.OfType<ImageEntity>());
            Assert.Single(fromSfc.Entities.OfType<LineEntity>());
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
