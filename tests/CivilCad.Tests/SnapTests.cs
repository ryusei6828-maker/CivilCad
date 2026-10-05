using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

public class SnapTests
{
    private static void AssertPoints(IEnumerable<Vec2> actual, params Vec2[] expected)
    {
        var list = actual.ToList();
        Assert.Equal(expected.Length, list.Count);
        foreach (var e in expected)
            Assert.Contains(list, p => p.DistanceTo(e) < 1e-9);
    }

    [Fact]
    public void Intersections()
    {
        var a = new SegmentCurve(new(0, 0), new(10, 10));
        var b = new SegmentCurve(new(0, 10), new(10, 0));
        AssertPoints(SnapGeometry.Intersections(a, b), new Vec2(5, 5));
        AssertPoints(SnapGeometry.Intersections(a, new SegmentCurve(new(1, 0), new(11, 10))));   // 平行
        AssertPoints(SnapGeometry.Intersections(a, new SegmentCurve(new(20, 0), new(20, 5))));   // 届かない

        var circle = new CircleCurve(new(0, 0), 5);
        AssertPoints(SnapGeometry.Intersections(new SegmentCurve(new(-10, 0), new(10, 0)), circle), new Vec2(-5, 0), new Vec2(5, 0));

        // 上半分の円弧: y=3 の線とは 2 点、y=-3 とは交わらない
        var upper = new CircleCurve(new(0, 0), 5, 0, Math.PI, IsFull: false);
        AssertPoints(SnapGeometry.Intersections(new SegmentCurve(new(-10, 3), new(10, 3)), upper), new Vec2(-4, 3), new Vec2(4, 3));
        AssertPoints(SnapGeometry.Intersections(new SegmentCurve(new(-10, -3), new(10, -3)), upper));

        // 円どうし: 中心間 6、半径 5 → (3, ±4)
        AssertPoints(SnapGeometry.Intersections(circle, new CircleCurve(new(6, 0), 5)), new Vec2(3, 4), new Vec2(3, -4));
    }

    [Fact]
    public void PerpendicularTangentNearest()
    {
        var seg = new SegmentCurve(new(0, 0), new(10, 0));
        AssertPoints(SnapGeometry.PerpendicularFeet(seg, new(5, 5)), new Vec2(5, 0));
        AssertPoints(SnapGeometry.PerpendicularFeet(seg, new(20, 5)));              // 線分の外
        var circle = new CircleCurve(new(0, 0), 5);
        AssertPoints(SnapGeometry.PerpendicularFeet(circle, new(10, 0)), new Vec2(5, 0), new Vec2(-5, 0));

        // (10,0) から半径 5 の円への接点: 中心角 ±60°
        AssertPoints(SnapGeometry.TangentPoints(circle, new(10, 0)),
            new Vec2(2.5, 5 * Math.Sin(Math.PI / 3)), new Vec2(2.5, -5 * Math.Sin(Math.PI / 3)));
        AssertPoints(SnapGeometry.TangentPoints(circle, new(1, 0)));               // 円の内側からは無し

        Assert.Equal(new Vec2(3, 0), SnapGeometry.Nearest(seg, new(3, 7)));
        var upper = new CircleCurve(new(0, 0), 5, 0, Math.PI, IsFull: false);
        Assert.True(SnapGeometry.Nearest(upper, new(0, 9)).DistanceTo(new Vec2(0, 5)) < 1e-9);
        Assert.True(SnapGeometry.Nearest(upper, new(3, -9)).DistanceTo(new Vec2(5, 0)) < 1e-9); // 端点に寄る
    }

    [Fact]
    public void Finder_PicksRightKind()
    {
        var layer = new Layer("0", RgbColor.White);
        Entity[] entities =
        [
            new LineEntity(layer, new(0, 0), new(10, 10)),
            new LineEntity(layer, new(0, 10), new(10, 0)),
            new CircleEntity(layer, new(30, 0), 5),
        ];
        var kinds = SnapFinder.DefaultKinds;

        // 交点
        var s = SnapFinder.Find(entities, new(5.2, 4.9), 0.5, null, kinds);
        Assert.Equal(SnapKind.Intersection, s!.Value.Kind);
        Assert.Equal(new Vec2(5, 5), s.Value.Point);

        // 端点
        Assert.Equal(SnapKind.Endpoint, SnapFinder.Find(entities, new(0.1, 0.1), 0.5, null, kinds)!.Value.Kind);

        // 垂線の足: 基準点 (10,0) から線 (0,0)-(10,10) へ → (5,5) は交点と同じ位置なので、別の線で確認
        var line = new LineEntity(layer, new(0, -20), new(10, -20));
        var perp = SnapFinder.Find([line], new(4.1, -20.2), 0.5, new Vec2(4, -10), kinds);
        Assert.Equal(SnapKind.Perpendicular, perp!.Value.Kind);
        Assert.Equal(new Vec2(4, -20), perp.Value.Point);

        // 接点: 基準点 (40,0) から円 (30,0) r5 へ
        var tan = SnapFinder.Find(entities, new(32.5, 4.3), 0.5, new Vec2(40, 0), kinds);
        Assert.Equal(SnapKind.Tangent, tan!.Value.Kind);

        // 近接点は既定では無効。有効にすると他に候補が無いときだけ使う
        Assert.Null(SnapFinder.Find([line], new(7.3, -20.1), 0.5, null, kinds));
        var withNearest = new HashSet<SnapKind>(kinds) { SnapKind.Nearest };
        var near = SnapFinder.Find([line], new(7.3, -20.1), 0.5, null, withNearest);
        Assert.Equal(SnapKind.Nearest, near!.Value.Kind);
        Assert.Equal(new Vec2(7.3, -20), near.Value.Point);
        Assert.Equal(SnapKind.Midpoint, SnapFinder.Find([line], new(5.1, -20.1), 0.5, null, withNearest)!.Value.Kind);

        // 無効にした種類は使わない
        var noEnd = new HashSet<SnapKind>(kinds);
        noEnd.Remove(SnapKind.Endpoint);
        Assert.Null(SnapFinder.Find([line], new(0.1, -20.1), 0.5, null, noEnd));
    }
}
