using System.Runtime.ExceptionServices;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

[Collection("SharedToolSettings")] // 共有の設定を書き換える
public class ArcRadiusTests
{
    private static readonly Layer L = new("0", RgbColor.White);
    private static readonly Vec2 S = new(-3, 0), E = new(3, 0);

    // 弦 6m・半径 5m: 中心は (0, ±4)。短い側の中心角 = 2·asin(3/5)
    private static readonly double Minor = 2 * Math.Asin(0.6);

    [Theory]
    [InlineData(0, 1, 0, -4, false)]  // 弦のすぐ上 → 下に中心がある短い円弧 (頂点 y=1)
    [InlineData(0, -1, 0, 4, false)]  // 弦のすぐ下 → 上に中心がある短い円弧
    [InlineData(0, 9, 0, 4, true)]    // ずっと上 → 上に中心がある大きい円弧 (頂点 y=9)
    [InlineData(0, -9, 0, -4, true)]  // ずっと下 → 下に中心がある大きい円弧
    public void FromChordAndRadius_PicksArcNearestToClick(double px, double py, double cx, double cy, bool major)
    {
        var arc = ArcEntity.FromChordAndRadius(L, S, E, 5, new Vec2(px, py))!;
        Assert.True(arc.Center.DistanceTo(new Vec2(cx, cy)) < 1e-9, $"center {arc.Center}");
        Assert.Equal(5, arc.Radius, 9);
        Assert.Equal(major ? 2 * Math.PI - Minor : Minor, arc.Sweep, 9);
        Assert.True(arc.DistanceTo(new Vec2(px, py)) < 1e-9);       // クリック位置を通る
        Assert.True(arc.DistanceTo(S) < 1e-9 && arc.DistanceTo(E) < 1e-9); // 始点・終点を通る
    }

    [Fact]
    public void FromChordAndRadius_TooFar_ReturnsNull() =>
        Assert.Null(ArcEntity.FromChordAndRadius(L, new Vec2(-6, 0), new Vec2(6, 0), 5, Vec2.Zero));

    [Fact]
    public void ArcTool_WithRadius_StartEndSide()
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                var doc = new CadDocument();
                var canvas = new CadCanvas { Document = doc };
                var tool = new ArcTool();
                canvas.SetTool(tool);
                tool.FixedRadius = 5;
                tool.OnPoint(S);
                tool.OnPoint(new Vec2(20, 0));   // 直径より遠い → 受け付けない
                Assert.Empty(doc.Entities);
                tool.OnPoint(E);
                tool.OnPoint(new Vec2(0.2, 1.1)); // 上に膨らむ側
                var arc = Assert.Single(doc.Entities.OfType<ArcEntity>());
                Assert.True(arc.Center.DistanceTo(new Vec2(0, -4)) < 1e-9);
                Assert.Equal(5, arc.Radius, 9);

                // 半径を空欄に戻すと 3 点円弧
                var tool2 = new ArcTool();
                canvas.SetTool(tool2);
                tool2.FixedRadius = null;
                tool2.OnPoint(S);
                tool2.OnPoint(new Vec2(0, 3));
                tool2.OnPoint(E);
                Assert.Equal(2, doc.Entities.OfType<ArcEntity>().Count());
            }
            catch (Exception ex) { error = ex; }
            finally { DrawInputMemory.ArcRadius = null; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
