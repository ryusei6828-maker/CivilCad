using System.IO;
using System.Runtime.ExceptionServices;
using CivilCad.App.IO;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

public class JoinFillTests
{
    private static readonly Layer L = new("0", RgbColor.White);

    [Fact]
    public void Join_ShuffledReversedLines_MakeClosedPolyline()
    {
        // 正方形の 4 辺を、順番・向きをばらばらにして渡す (1 本はわずかな隙間あり)
        var lines = new Entity[]
        {
            new LineEntity(L, new Vec2(10, 10), new Vec2(10, 0)),
            new LineEntity(L, new Vec2(0, 0), new Vec2(10, 0)) { Color = RgbColor.Red, LineWeight = 0.5 },
            new LineEntity(L, new Vec2(0, 10), new Vec2(0, 0)),
            new LineEntity(L, new Vec2(0, 10.00001), new Vec2(10, 10)),
            new LineEntity(L, new Vec2(50, 50), new Vec2(60, 50)), // 離れた線は結合しない
        };
        var r = Joiner.Join(lines, 1e-3);
        var pl = Assert.Single(r.Joined);
        Assert.True(pl.IsClosed);
        Assert.Equal(4, pl.Points.Count);
        Assert.Equal(100, pl.Area, 2);
        Assert.Equal(4, r.Consumed.Count);
        Assert.DoesNotContain(lines[4], r.Consumed);
        Assert.Equal(1, r.ClosedCount);
    }

    [Fact]
    public void Join_OpenChain_WithArcAndPolyline()
    {
        var arc = new ArcEntity(L, new Vec2(10, 5), 5, -Math.PI / 2, Math.PI / 2); // (10,0) → (10,10) 反時計回り
        var entities = new Entity[]
        {
            new LineEntity(L, new Vec2(0, 0), new Vec2(10, 0)),
            arc,
            new PolylineEntity(L, [new Vec2(0, 20), new Vec2(5, 15), new Vec2(10, 10)]), // 逆向きにつながる
        };
        var pl = Assert.Single(Joiner.Join(entities, 1e-6).Joined);
        Assert.False(pl.IsClosed);
        Assert.Equal(new Vec2(0, 0), pl.Points[0]);
        Assert.Equal(new Vec2(0, 20), pl.Points[^1]);
        Assert.True(pl.Points.Count > 10); // 円弧は折れ線に
        Assert.Equal(10 + Math.PI * 5 + 2 * Math.Sqrt(50), pl.Length, 2);
    }

    [Fact]
    public void Fill_SelectedClosedShapes_WithHole_AndTransparency() => RunSta((doc, canvas) =>
    {
        var square = new PolylineEntity(doc.CurrentLayer, [new(0, 0), new(20, 0), new(20, 20), new(0, 20)], true);
        var hole = new CircleEntity(doc.CurrentLayer, new Vec2(10, 10), 3);
        var other = new PolylineEntity(doc.CurrentLayer, [new(50, 0), new(60, 0), new(60, 10)], true);
        doc.Entities.AddRange([square, hole, other]);
        doc.CurrentColor = RgbColor.Cyan;
        HatchSettings.TransparencyPercent = 40;
        try
        {
            canvas.SetSelection([square, hole, other]);
            canvas.SetTool(new HatchTool(fill: true));
            var fills = doc.Entities.OfType<HatchEntity>().ToList();
            Assert.Equal(2, fills.Count);                              // 正方形 (円の中抜き) と三角形
            var big = fills.Single(f => f.Loops.Count == 2);
            Assert.True(big.Solid);
            Assert.Equal(400 - Math.PI * 9, big.Area, 0);
            Assert.Equal(40, big.TransparencyPercent, 6);
            Assert.Equal(RgbColor.Cyan, big.Color);
            Assert.Empty(canvas.Selection);
        }
        finally
        {
            HatchSettings.TransparencyPercent = 0;
        }
    });

    [Fact]
    public void Transparency_Clone_And_DxfRoundTrip()
    {
        var doc = new CadDocument();
        var h = new HatchEntity(doc.CurrentLayer, [[new(0, 0), new(10, 0), new(10, 10)]], [], solid: true) { TransparencyPercent = 60 };
        Assert.Equal(0.4, h.Opacity, 9);
        Assert.Equal(60, ((HatchEntity)h.Clone()).TransparencyPercent, 6);
        doc.Entities.Add(h);
        var path = Path.Combine(Path.GetTempPath(), $"civilcad_{Guid.NewGuid():N}.dxf");
        try
        {
            DxfIO.Save(doc, path);
            var loaded = new CadDocument();
            DxfIO.Load(loaded, path);
            Assert.Equal(60, Assert.Single(loaded.Entities.OfType<HatchEntity>()).TransparencyPercent, 6);
        }
        finally
        {
            File.Delete(path);
        }
    }

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
}
