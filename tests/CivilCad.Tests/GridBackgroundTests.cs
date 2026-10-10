using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CivilCad.App.Settings;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

public class GridBackgroundTests
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

    [Fact]
    public void GridSpacing_AutoAndFixed_ThinnedWhenTooDense() => RunSta(() =>
    {
        var canvas = new CadCanvas { Document = new CadDocument() };
        canvas.SetView(Vec2.Zero, 2);                   // 1m = 2px
        Assert.Equal(20, canvas.EffectiveGridSpacing, 9); // 自動: 25px 以上になる 1・2・5×10ⁿ → 20m (40px)
        canvas.SetView(Vec2.Zero, 100);
        Assert.Equal(0.5, canvas.EffectiveGridSpacing, 9);

        canvas.GridSpacing = 1;
        Assert.Equal(1, canvas.EffectiveGridSpacing, 9); // 1m = 100px
        canvas.SetView(Vec2.Zero, 2);
        Assert.Equal(10, canvas.EffectiveGridSpacing, 9); // 1m = 2px は細かすぎるので 10 倍ずつ間引く
        canvas.GridSpacing = -3;
        Assert.Null(canvas.GridSpacing);                  // 0 以下は自動
    });

    [Fact]
    public void LightBackground_ShowsWhiteAsBlack() => RunSta(() =>
    {
        var canvas = new CadCanvas { Document = new CadDocument() };
        Assert.False(canvas.IsLightBackground);
        Assert.Equal(RgbColor.White, canvas.ForDisplay(RgbColor.White));
        Assert.Equal(RgbColor.White, canvas.ForDisplay(new RgbColor(0, 0, 0)));   // 暗い背景で黒い線は白
        canvas.BackgroundColor = RgbColor.White;
        Assert.True(canvas.IsLightBackground);
        Assert.Equal(new RgbColor(0, 0, 0), canvas.ForDisplay(RgbColor.White));  // 白い背景で白い線は黒
        Assert.Equal(RgbColor.Red, canvas.ForDisplay(RgbColor.Red));              // 他の色はそのまま
    });

    [Fact]
    public void SettingsParsing()
    {
        Assert.Equal(new RgbColor(0x1E, 0x2F, 0xAB), SettingsWindow.ParseHex("#1e2fab"));
        Assert.Null(SettingsWindow.ParseHex("white"));
        Assert.True(SettingsWindow.TryParseSpacing("自動", out var auto) && auto == null);
        Assert.True(SettingsWindow.TryParseSpacing("5m", out var five) && five == 5);
        Assert.False(SettingsWindow.TryParseSpacing("0", out _));
        Assert.Equal("自動", SettingsWindow.FormatSpacing(null));
        Assert.Equal("0.5", SettingsWindow.FormatSpacing(0.5));
        Assert.True(new AppSettings().GridVisible);
    }

    [Fact]
    public void Renders_GridOnDarkAndLight() => RunSta(() =>
    {
        var doc = new CadDocument();
        doc.Entities.Add(new LineEntity(doc.CurrentLayer, new Vec2(0, 0), new Vec2(30, 20)));
        doc.Entities.Add(new CircleEntity(doc.CurrentLayer, new Vec2(15, 10), 5) { Color = RgbColor.Red });
        var canvas = new CadCanvas { Document = doc };
        canvas.Measure(new Size(600, 400));
        canvas.Arrange(new Rect(0, 0, 600, 400));
        var dir = Path.GetTempPath();
        foreach (var (bg, name) in new[] { (CadCanvas.DefaultBackground, "dark"), (RgbColor.White, "light") })
        {
            canvas.BackgroundColor = bg;
            canvas.SetView(new Vec2(15, 10), 15);
            canvas.RedrawAll();
            var r = new RenderTargetBitmap(600, 400, 96, 96, PixelFormats.Pbgra32);
            r.Render(canvas);
            // 背景色の画素がある (グリッド線の無いところ)
            var px = new byte[4];
            r.CopyPixels(new Int32Rect(3, 3, 1, 1), px, 4, 0);
            Assert.InRange(Math.Abs(px[2] - bg.R), 0, 0x30);
        }
        // 非表示・極端な拡大率でも描ける
        canvas.GridVisible = false;
        canvas.RedrawAll();
        canvas.GridVisible = true;
        foreach (var k in new[] { 1e-4, 1e5 })
        {
            canvas.SetView(new Vec2(-60000, -95000), k);
            canvas.RedrawAll();
        }
    });
}
