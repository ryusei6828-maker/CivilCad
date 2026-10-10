using System.Runtime.ExceptionServices;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

public class GridSnapTests
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
    public void SnapsToGridIntersection_OnlyWhenEnabledAndVisible_ObjectSnapsFirst() => RunSta(() =>
    {
        var doc = new CadDocument();
        doc.Entities.Add(new LineEntity(doc.CurrentLayer, new Vec2(10.3, 20.2), new Vec2(30, 20.2)));
        var canvas = new CadCanvas { Document = doc, GridSpacing = 1 };
        canvas.SetView(Vec2.Zero, 100); // 1m = 100px → 許容 12px = 0.12m

        // 既定ではグリッドスナップはオフ
        Assert.DoesNotContain(SnapKind.Grid, SnapFinder.DefaultKinds);
        Assert.Null(canvas.FindSnap(new Vec2(5.05, 7.97)));

        canvas.SnapKinds.Add(SnapKind.Grid);
        var sp = canvas.FindSnap(new Vec2(5.05, 7.97));
        Assert.Equal(new SnapPoint(new Vec2(5, 8), SnapKind.Grid), sp);
        Assert.Null(canvas.FindSnap(new Vec2(5.5, 7.5)));             // 交点から遠い

        // 線の端点が近くにあればそちらが優先
        Assert.Equal(SnapKind.Endpoint, canvas.FindSnap(new Vec2(10.25, 20.15))!.Value.Kind);

        // グリッド非表示なら効かない
        canvas.GridVisible = false;
        Assert.Null(canvas.FindSnap(new Vec2(5.05, 7.97)));
        canvas.GridVisible = true;

        // 拡大率が小さく間引いているときは、表示中の間隔 (10m) の交点へ
        canvas.SetView(Vec2.Zero, 2);
        Assert.Equal(new Vec2(10, 0), canvas.FindSnap(new Vec2(12, 3))!.Value.Point);
    });
}
