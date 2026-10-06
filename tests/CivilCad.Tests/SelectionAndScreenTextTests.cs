using System.IO;
using System.Runtime.ExceptionServices;
using CivilCad.App.IO;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

public class SelectionAndScreenTextTests
{
    private static void OnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    [Fact]
    public void Click_SelectsOnlyThatShape()
    {
        OnSta(() =>
        {
            var canvas = new CadCanvas();
            var doc = canvas.Document;
            var a = new LineEntity(doc.CurrentLayer, new Vec2(0, 0), new Vec2(10, 0));
            var b = new LineEntity(doc.CurrentLayer, new Vec2(0, 20), new Vec2(10, 20));
            doc.Entities.AddRange([a, b]);
            var tool = Assert.IsType<SelectTool>(canvas.Tool);
            void Click(Vec2 p)
            {
                tool.OnPress(p);
                tool.OnRelease(p);
            }

            Click(new Vec2(5, 0));
            Assert.Equal([a], canvas.Selection);
            // 別の図形をクリックすると、そちらだけが選ばれる (足されない)
            Click(new Vec2(5, 20));
            Assert.Equal([b], canvas.Selection);
            // 空き部分のクリックで解除
            Click(new Vec2(50, 50));
            Assert.Empty(canvas.Selection);
            // コマンド欄からの 2 点の範囲選択も、選び直しになる
            canvas.SetSelection([a]);
            tool.OnPoint(new Vec2(-1, 19));
            tool.OnPoint(new Vec2(11, 21));
            Assert.Equal([b], canvas.Selection);
        });
    }

    [Fact]
    public void ScreenFixedText_KeepsScreenSize_AndSavesAtPaperSize()
    {
        OnSta(() =>
        {
            var canvas = new CadCanvas();
            var doc = canvas.Document;
            doc.ScaleDenominator = 500;
            canvas.SetView(Vec2.Zero, 2); // 1m = 2px
            var wasFixed = TextTool.ScreenFixed;
            var wasPx = TextTool.DefaultScreenPx;
            try
            {
                TextTool.ScreenFixed = true;
                TextTool.DefaultScreenPx = 16;
                canvas.SetTool(new TextTool());
                canvas.Tool.OnPoint(new Vec2(0, 0));
                Assert.True(canvas.Tool.OnText("測点 No.1"));
                var t = Assert.Single(doc.Entities.OfType<TextEntity>());
                Assert.Equal(16, t.ScreenHeightPx);
                Assert.Equal(8, t.Height, 9); // 16px ÷ 2px/m

                // 拡大すると図面上の高さが小さくなり、画面上の大きさ (16px) は変わらない
                canvas.SetView(Vec2.Zero, 8);
                Assert.Equal(2, t.Height, 9);
                // 縮尺を変えても変わらない
                doc.ScaleDenominator = 1000;
                canvas.RedrawAll();
                Assert.Equal(2, t.Height, 9);

                // 保存すると用紙上 16px × 0.26mm を縮尺 1/1000 で換算した高さになる。図面の文字はそのまま
                var path = Path.Combine(Path.GetTempPath(), "civilcad-test-" + Guid.NewGuid().ToString("N") + ".dxf");
                try
                {
                    DrawingFiles.Save(doc, path);
                    Assert.Equal(2, t.Height, 9);
                    var loaded = new CadDocument();
                    DxfIO.Load(loaded, path);
                    var saved = Assert.Single(loaded.Entities.OfType<TextEntity>());
                    Assert.Equal(16 * 25.4 / 96 * 1000 / 1000, saved.Height, 6);
                }
                finally { File.Delete(path); }

                // 用紙 mm の文字 (切り替え): 縮尺で換算
                TextTool.ScreenFixed = false;
                TextTool.DefaultHeightMm = 3.5;
                canvas.Tool.OnPoint(new Vec2(10, 0));
                canvas.Tool.OnText("ふつう");
                var normal = doc.Entities.OfType<TextEntity>().Single(x => x.Text == "ふつう");
                Assert.Null(normal.ScreenHeightPx);
                Assert.Equal(3.5, normal.Height, 9); // 3.5mm × 1/1000
                canvas.SetView(Vec2.Zero, 2);
                Assert.Equal(3.5, normal.Height, 9);
            }
            finally
            {
                TextTool.ScreenFixed = wasFixed;
                TextTool.DefaultScreenPx = wasPx;
            }
        });
    }
}
