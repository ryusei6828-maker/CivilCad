using System.Runtime.ExceptionServices;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Editing;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

/// <summary>面取り (CHA)。</summary>
public class ChamferTests
{
    private static readonly Layer L = new("0", RgbColor.White);

    private static void Near(Vec2 expected, Vec2 actual, double eps = 1e-6) =>
        Assert.True(expected.DistanceTo(actual) < eps, $"期待 {expected} / 実際 {actual}");

    private static T New<T>(Chamfer.Result r, Entity old) where T : Entity => Assert.IsType<T>(r.Replaced.Single(p => p.Old == old).New);

    [Fact]
    public void TwoLines_DifferentDistances()
    {
        var a = new LineEntity(L, new Vec2(10, 0), new Vec2(0, 0));
        var b = new LineEntity(L, new Vec2(0, 0), new Vec2(0, 10));
        var r = Chamfer.Create(a, new Vec2(5, 0), b, new Vec2(0, 5), 2, 3, out var error);
        Assert.Null(error);
        Near(new Vec2(2, 0), New<LineEntity>(r!, a).End);
        Near(new Vec2(0, 3), New<LineEntity>(r!, b).Start);
        Near(new Vec2(2, 0), r!.Cut!.Start);
        Near(new Vec2(0, 3), r.Cut.End);
    }

    [Fact]
    public void LinesThatDoNotReach_AreExtended_AndZeroMakesSharpCorner()
    {
        var a = new LineEntity(L, new Vec2(10, 0), new Vec2(4, 0));
        var b = new LineEntity(L, new Vec2(0, 3), new Vec2(0, 10));
        var r = Chamfer.Create(a, new Vec2(8, 0), b, new Vec2(0, 8), 0, 0, out var error);
        Assert.Null(error);
        Assert.Null(r!.Cut);
        Near(Vec2.Zero, New<LineEntity>(r, a).End);
        Near(Vec2.Zero, New<LineEntity>(r, b).Start);
    }

    [Fact]
    public void PolylineVertex_DistanceFollowsPickOrder()
    {
        var pl = new PolylineEntity(L, [new Vec2(10, 0), new Vec2(0, 0), new Vec2(0, 10)]);
        var r = Chamfer.Create(pl, new Vec2(0, 5), pl, new Vec2(5, 0), 2, 3, out var error); // 1 本目は縦の線分
        Assert.Null(error);
        Assert.Null(r!.Cut);
        var n = New<PolylineEntity>(r, pl);
        Assert.Equal(4, n.Points.Count);
        Near(new Vec2(3, 0), n.Points[1]);
        Near(new Vec2(0, 2), n.Points[2]);
    }

    [Fact]
    public void PolylineEnd_MovesOnlyEndPoint()
    {
        var pl = new PolylineEntity(L, [new Vec2(5, 5), new Vec2(5, 0), new Vec2(1, 0)]);
        var line = new LineEntity(L, new Vec2(0, 1), new Vec2(0, 10));
        var r = Chamfer.Create(pl, new Vec2(3, 0), line, new Vec2(0, 5), 1, 1, out var error);
        Assert.Null(error);
        var n = New<PolylineEntity>(r!, pl);
        Assert.Equal(3, n.Points.Count);
        Near(new Vec2(5, 5), n.Points[0]);
        Near(new Vec2(1, 0), n.Points[2]);
        Near(new Vec2(0, 1), New<LineEntity>(r!, line).Start);
    }

    [Fact]
    public void Arc_Parallel_And_TooLong_Fail()
    {
        var line = new LineEntity(L, new Vec2(0, 0), new Vec2(5, 0));
        var arc = new ArcEntity(L, Vec2.Zero, 5, 0, 1);
        Assert.Null(Chamfer.Create(line, new Vec2(4, 0), arc, new Vec2(3, 4), 1, 1, out var arcError));
        Assert.Contains("面取りできません", arcError);

        var parallel = new LineEntity(L, new Vec2(0, 2), new Vec2(5, 2));
        Assert.Null(Chamfer.Create(line, new Vec2(4, 0), parallel, new Vec2(4, 2), 1, 1, out var parallelError));
        Assert.Contains("平行", parallelError);

        var other = new LineEntity(L, new Vec2(0, 0), new Vec2(0, 5));
        Assert.Null(Chamfer.Create(line, new Vec2(4, 0), other, new Vec2(0, 4), 9, 1, out var tooLong));
        Assert.Contains("大きすぎ", tooLong);
    }

    [Fact]
    public void Tool_TwoDistancesFromCommandLine_AndUndo()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var canvas = new CadCanvas();
                var doc = canvas.Document;
                var a = new LineEntity(doc.CurrentLayer, new Vec2(10, 0), new Vec2(0, 0));
                var b = new LineEntity(doc.CurrentLayer, new Vec2(0, 0), new Vec2(0, 10));
                doc.Entities.AddRange([a, b]);

                canvas.SetTool(new ChamferTool());
                Assert.True(canvas.Tool.OnText("2,3"));
                Assert.False(canvas.Tool.OnText("abc"));
                canvas.Tool.OnPoint(new Vec2(5, 0));
                canvas.Tool.OnPoint(new Vec2(0, 5));
                Assert.Equal(3, doc.Entities.Count);
                var cut = doc.Entities.OfType<LineEntity>().Single(l => l.Start.DistanceTo(new Vec2(2, 0)) < 1e-9);
                Near(new Vec2(0, 3), cut.End);

                doc.History.Undo(); // 1 回で元に戻る
                Assert.Equal([a, b], doc.Entities);
                canvas.Tool.OnText("1"); // ほかのテストのために既定に戻す
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
