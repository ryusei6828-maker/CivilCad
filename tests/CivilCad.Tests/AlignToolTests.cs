using System.Runtime.ExceptionServices;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

public class AlignToolTests
{
    private static void RunSta(Action action)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
            finally { AlignTool.ScaleToFit = true; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    private static void Near(Vec2 expected, Vec2 actual)
    {
        Assert.Equal(expected.X, actual.X, 6);
        Assert.Equal(expected.Y, actual.Y, 6);
    }

    [Fact]
    public void ImageTwoPoints_MatchDrawingTwoPoints() => RunSta(() =>
    {
        var doc = new CadDocument();
        var img = new ImageEntity(doc.CurrentLayer, "photo.jpg", 100, 50, Vec2.Zero, new Vec2(100, 0), new Vec2(0, 50));
        var line = new LineEntity(doc.CurrentLayer, new Vec2(10, 10), new Vec2(30, 10)); // 画像の上に描いた線もいっしょに
        doc.Entities.AddRange([img, line]);
        var canvas = new CadCanvas { Document = doc };
        canvas.SetSelection([img, line]);
        var tool = new AlignTool();
        canvas.SetTool(tool);

        // 画像の (10,10) → 図面の (60000, -95000)、画像の (30,10) → 図面の (60000, -94960): 90° 回転・2 倍
        var s1 = new Vec2(10, 10);
        var s2 = new Vec2(30, 10);
        var d1 = new Vec2(60000, -95000);
        var d2 = new Vec2(60000, -94960);
        tool.OnPoint(s1);
        tool.OnPoint(d1);
        tool.OnPoint(s2);
        tool.OnPoint(d2);

        var moved = doc.Entities.OfType<ImageEntity>().Single();
        // 画像の中の位置 (s, t) で、元の s1 / s2 にあった所が合わせ先に来ている
        var (a1, b1) = (0.1, 1 - 0.2); // (10,10) は 幅 100 の 0.1、高さ 50 の下から 0.2
        var (a2, b2) = (0.3, 1 - 0.2);
        Near(d1, moved.Map(a1, b1));
        Near(d2, moved.Map(a2, b2));
        Assert.Equal(200, moved.Width, 6);
        Assert.Equal(100, moved.Height, 6);
        Assert.Equal(Math.PI / 2, moved.U.Angle, 9);
        var l2 = doc.Entities.OfType<LineEntity>().Single();
        Near(d1, l2.Start);
        Near(d2, l2.End);
        Assert.Equal(2, canvas.Selection.Count);
        Assert.Same(tool, canvas.Tool); // 続けて合わせ直せる

        // 元に戻すで元の位置
        doc.History.Undo();
        Near(Vec2.Zero, doc.Entities.OfType<ImageEntity>().Single().Origin);
        Assert.Equal(100, doc.Entities.OfType<ImageEntity>().Single().Width, 9);
    });

    [Fact]
    public void WithoutScale_RotatesAndMovesOnly_AndStepBack() => RunSta(() =>
    {
        AlignTool.ScaleToFit = false;
        var doc = new CadDocument();
        var img = new ImageEntity(doc.CurrentLayer, "photo.jpg", 100, 50, Vec2.Zero, new Vec2(100, 0), new Vec2(0, 50));
        doc.Entities.Add(img);
        var canvas = new CadCanvas { Document = doc };
        canvas.SetSelection([img]);
        var tool = new AlignTool();
        canvas.SetTool(tool);

        tool.OnPoint(Vec2.Zero);
        tool.OnPoint(new Vec2(5, 5));        // 間違えた点
        tool.StepBack();                      // 一つ戻す
        tool.OnPoint(new Vec2(100, 100));
        tool.OnPoint(new Vec2(100, 0));
        tool.OnPoint(new Vec2(100, 100));    // 合わせ先の 2 点目が 1 点目と同じ → 受け付けない
        Assert.Equal(0, img.Origin.X);
        tool.OnPoint(new Vec2(100, 300));    // 真上 (長さは 200 だが大きさは変えない)

        var moved = doc.Entities.OfType<ImageEntity>().Single();
        Near(new Vec2(100, 100), moved.Origin);
        Assert.Equal(100, moved.Width, 9);   // 大きさはそのまま
        Assert.Equal(Math.PI / 2, moved.U.Angle, 9);
    });

}
