using System.Runtime.ExceptionServices;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

/// <summary>塗りつぶし (塗りのハッチング・塗りつぶしの折れ線) をクリックで選ぶ。</summary>
public class FillSelectTests
{
    private static void RunSta(Action<CadDocument, CadCanvas> action)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                var doc = new CadDocument();
                action(doc, new CadCanvas { Document = doc });
            }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    private static readonly Vec2[] Square = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)];

    [Fact]
    public void ClickInside_SelectsFills_LinesOnTopFirst() => RunSta((doc, canvas) =>
    {
        var hatch = new HatchEntity(doc.CurrentLayer, [Square], [], solid: true);
        var poly = new PolylineEntity(doc.CurrentLayer, Square.Select(p => p + new Vec2(20, 0)), isClosed: true) { Filled = true };
        var line = new LineEntity(doc.CurrentLayer, new Vec2(2, 5), new Vec2(8, 5)); // 塗りの上の線
        doc.Entities.AddRange([hatch, poly, line]);

        Assert.Same(hatch, canvas.HitTest(new Vec2(3, 2)));   // 塗りのハッチングの中
        Assert.Same(poly, canvas.HitTest(new Vec2(25, 5)));   // 塗りつぶしの折れ線の中
        Assert.Same(line, canvas.HitTest(new Vec2(5, 5)));    // 塗りの上の線は線を選ぶ
        Assert.Null(canvas.HitTest(new Vec2(15, 5)));         // 塗りの外

        // 塗りつぶしでない閉じた折れ線は、中をクリックしても選ばない (今までどおり)
        poly.Filled = false;
        Assert.Null(canvas.HitTest(new Vec2(25, 5)));
        poly.Filled = true;

        // ロックしたレイヤの塗りは選ばない
        var locked = doc.GetOrCreateLayer("ロック", RgbColor.White);
        locked.IsLocked = true;
        poly.Layer = locked;
        Assert.Null(canvas.HitTest(new Vec2(25, 5)));
    });

    [Fact]
    public void CrossingBox_InsideFill_SelectsFilledPolyline()
    {
        var doc = new CadDocument();
        var poly = new PolylineEntity(doc.CurrentLayer, Square, isClosed: true) { Filled = true };
        Assert.True(poly.IntersectsBox(new Box2(new Vec2(4, 4), new Vec2(6, 6))));   // 塗りの中の小さな範囲
        poly.Filled = false;
        Assert.False(poly.IntersectsBox(new Box2(new Vec2(4, 4), new Vec2(6, 6))));
    }
}
