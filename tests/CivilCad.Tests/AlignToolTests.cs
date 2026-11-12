using System.Runtime.ExceptionServices;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

[Collection("SharedToolSettings")] // 共有の設定を書き換える
public class AlignToolTests
{
    private static void RunSta(Action action)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
            finally { AlignTool.ScaleToFit = true; AlignTool.KeepSelectionUpright = true; }
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
        AlignTool.KeepSelectionUpright = false;
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

    private static (CadDocument Doc, CadCanvas Canvas, AlignTool Tool, LineEntity Route, LineEntity Sel) Setup()
    {
        var doc = new CadDocument();
        doc.SetSurveyFrame(0.3, new Vec2(100, 50));
        var route = new LineEntity(doc.CurrentLayer, new Vec2(0, 0), new Vec2(100, 0));
        var sel = new LineEntity(doc.CurrentLayer, new Vec2(10, 10), new Vec2(20, 10));
        doc.Entities.AddRange([route, sel]);
        var canvas = new CadCanvas { Document = doc };
        canvas.SetSelection([sel]);
        var tool = new AlignTool();
        canvas.SetTool(tool);
        return (doc, canvas, tool, route, sel);
    }

    private static void Run4(AlignTool tool, Vec2 s1, Vec2 d1, Vec2 s2, Vec2 d2)
    {
        tool.OnPoint(s1);
        tool.OnPoint(d1);
        tool.OnPoint(s2);
        tool.OnPoint(d2);
    }

    [Fact]
    public void SurveyFrameMath_KeepsSurveyCoordinatesOfRotatedOthers()
    {
        var rnd = new Random(7);
        for (var i = 0; i < 200; i++)
        {
            var phi = (rnd.NextDouble() - 0.5) * 6;
            var pivot = new Vec2(rnd.NextDouble() * 2000 - 1000, rnd.NextDouble() * 2000 - 1000);
            var q = new Vec2(rnd.NextDouble() * 2000 - 1000, rnd.NextDouble() * 2000 - 1000);
            var theta = (rnd.NextDouble() - 0.5) * 6;
            var before = new CadDocument();
            before.SetSurveyFrame(phi, pivot);
            var (rot, piv) = CadDocument.SurveyFrameAfterRotatingOthers(before.SurveyRotation, before.SurveyPivot, q, theta);
            var after = new CadDocument();
            after.SetSurveyFrame(rot, piv);
            for (var j = 0; j < 5; j++)
            {
                var p = new Vec2(rnd.NextDouble() * 2000 - 1000, rnd.NextDouble() * 2000 - 1000);
                var p2 = q + (p - q).Rotate(-theta);
                var a = before.ToSurveyCad(p);
                var b = after.ToSurveyCad(p2);
                Assert.True(a.DistanceTo(b) < 1e-9, $"{a} vs {b}");
            }
        }
        // 回転が 0 に戻る場合
        var d = new CadDocument();
        var (r0, p0) = CadDocument.SurveyFrameAfterRotatingOthers(0, Vec2.Zero, new Vec2(5, 5), 1);
        d.SetSurveyFrame(r0, p0);
        Assert.Equal(1, d.SurveyRotation, 12);
    }

    [Fact]
    public void KeepUpright_RotatesOthersAndFrame_SelectionOnlyMoves_UndoRestores() => RunSta(() =>
    {
        AlignTool.ScaleToFit = false;
        var (doc, canvas, tool, route, sel) = Setup();
        var q = new Vec2(50, 50);
        var routeSurveyBefore = new[] { doc.ToSurvey(route.Start), doc.ToSurvey(route.End) };
        Run4(tool, new Vec2(10, 10), q, new Vec2(20, 10), new Vec2(50, 60)); // θ = 90°

        var moved = doc.Entities.OfType<LineEntity>().Single(l => l != route);
        Near(new Vec2(50, 50), moved.Start);
        Near(new Vec2(60, 50), moved.End); // 向きは水平のまま、移動だけ
        // 路線は Q を中心に -90° 回り、合わせ先の 2 点目の回転後の位置に選んだ図形の 2 点目が来る
        Near(q + (new Vec2(0, 0) - q).Rotate(-Math.PI / 2), route.Start);
        Near(q + (new Vec2(100, 0) - q).Rotate(-Math.PI / 2), route.End);
        Near(q + (new Vec2(50, 60) - q).Rotate(-Math.PI / 2), moved.End);
        // 路線の測量座標は変わらない
        var after = new[] { doc.ToSurvey(route.Start), doc.ToSurvey(route.End) };
        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(routeSurveyBefore[i].X, after[i].X, 6);
            Assert.Equal(routeSurveyBefore[i].Y, after[i].Y, 6);
        }
        Assert.Equal(0.3 + Math.PI / 2, doc.SurveyRotation, 9);

        doc.History.Undo(); // 1 回で全部戻る
        Near(new Vec2(0, 0), route.Start);
        Near(new Vec2(100, 0), route.End);
        var back = doc.Entities.OfType<LineEntity>().Single(l => l != route);
        Near(new Vec2(10, 10), back.Start);
        Near(new Vec2(20, 10), back.End);
        Assert.Equal(0.3, doc.SurveyRotation, 12);
        Near(new Vec2(100, 50), doc.SurveyPivot);
    });

    [Fact]
    public void KeepUpright_ImageOnlySelection_StillRotatesImage() => RunSta(() =>
    {
        AlignTool.ScaleToFit = false;
        var doc = new CadDocument();
        var img = new ImageEntity(doc.CurrentLayer, "photo.jpg", 100, 50, Vec2.Zero, new Vec2(100, 0), new Vec2(0, 50));
        var route = new LineEntity(doc.CurrentLayer, new Vec2(0, 0), new Vec2(100, 0));
        doc.Entities.AddRange([img, route]);
        var canvas = new CadCanvas { Document = doc };
        canvas.SetSelection([img]);
        var tool = new AlignTool();
        canvas.SetTool(tool);
        Run4(tool, Vec2.Zero, new Vec2(100, 100), new Vec2(100, 0), new Vec2(100, 300));
        Assert.Equal(Math.PI / 2, doc.Entities.OfType<ImageEntity>().Single().U.Angle, 9);
        Near(new Vec2(100, 0), route.End); // ほかの図形は動かない
        Assert.Equal(0, doc.SurveyRotation);
    });

    [Fact]
    public void KeepUprightOff_RotatesSelectionAsBefore() => RunSta(() =>
    {
        AlignTool.ScaleToFit = false;
        AlignTool.KeepSelectionUpright = false;
        var (doc, canvas, tool, route, sel) = Setup();
        Run4(tool, new Vec2(10, 10), new Vec2(50, 50), new Vec2(20, 10), new Vec2(50, 60));
        var moved = doc.Entities.OfType<LineEntity>().Single(l => l != route);
        Near(new Vec2(50, 50), moved.Start);
        Near(new Vec2(50, 60), moved.End); // 回った
        Near(new Vec2(100, 0), route.End);
        Assert.Equal(0.3, doc.SurveyRotation, 12);
    });
}
