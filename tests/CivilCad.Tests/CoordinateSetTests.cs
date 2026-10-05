using CivilCad.App.Tools;
using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

public class CoordinateSetTests
{
    private static void AssertNear(Vec2 expected, Vec2 actual, double tol = 1e-6) =>
        Assert.True(expected.DistanceTo(actual) < tol, $"expected {expected}, actual {actual}");

    [Fact]
    public void TwoPoints_MoveAndRotateWholeDrawing_OntoSurveyCoordinates()
    {
        // 図面: 基準点1 (0,0)、基準点2 (10,0) = 東向き 10m
        var doc = new CadDocument();
        var l = doc.CurrentLayer;
        var line = new LineEntity(l, new Vec2(0, 0), new Vec2(10, 0));
        var arc = new ArcEntity(l, new Vec2(5, 0), 2, 0, Math.PI / 2);
        var text = new TextEntity(l, new Vec2(10, 0), "BP", 1);
        var pl = new PolylineEntity(l, [new(0, 0), new(0, 5)]);
        var circle = new CircleEntity(l, new Vec2(0, 5), 1);
        doc.Entities.AddRange([line, arc, text, pl, circle]);

        // 測量座標: 基準点1 = X 1000 / Y 2000、基準点2 = X 1010 / Y 2000 (真北へ 10m)
        Assert.True(CoordinateSetTool.TryParseSurvey("1000,2000", out var s1));
        Assert.True(CoordinateSetTool.TryParseSurvey("s1010, 2000", out var s2));
        Assert.Equal(new Vec2(2000, 1000), s1); // CAD は x=東(Y), y=北(X)

        var r = CoordinateSetTool.ComputeTransform(Vec2.Zero, s1, new Vec2(10, 0), s2);
        Assert.Equal(Math.PI / 2, r.Angle, 9);  // 東向き → 北向き = 左に 90°
        Assert.Equal(1, r.ScaleRatio, 9);

        doc.History.Execute(new RotateTranslateCommand(doc.Entities.ToList(), Vec2.Zero, r.Angle, r.Delta));
        AssertNear(s1, line.Start);
        AssertNear(s2, line.End);                                   // 基準点2 も測量座標に一致
        AssertNear(new Vec2(2000, 1005), arc.Center);
        Assert.Equal(Math.PI / 2, arc.StartAngle, 9);
        AssertNear(s2, text.Position);
        Assert.Equal(Math.PI / 2, text.Rotation, 9);
        AssertNear(new Vec2(1995, 1000), pl.Points[1]);             // 北 5m → 西 5m に回る
        AssertNear(new Vec2(1995, 1000), circle.Center);

        // Undo で元の位置に戻る
        doc.History.Undo();
        AssertNear(Vec2.Zero, line.Start);
        AssertNear(new Vec2(10, 0), line.End);
        Assert.Equal(0, text.Rotation, 9);
        Assert.Equal(0, arc.StartAngle, 9);
    }

    [Fact]
    public void OnePoint_MovesOnly_AndReportsScaleMismatch()
    {
        var r1 = CoordinateSetTool.ComputeTransform(new Vec2(3, 4), new Vec2(100, 200), null, null);
        Assert.Equal(0, r1.Angle);
        Assert.Equal(new Vec2(97, 196), r1.Delta);

        // 図面 10m に対して測量 20m → 距離の比 2 (大きさは変えずに報告だけ)
        var r2 = CoordinateSetTool.ComputeTransform(Vec2.Zero, Vec2.Zero, new Vec2(10, 0), new Vec2(20, 0));
        Assert.Equal(2, r2.ScaleRatio, 9);
        Assert.Equal(0, r2.Angle, 9);
    }

    [Theory]
    [InlineData("s-95537.12,-60760.90", -60760.90, -95537.12)]
    [InlineData("S100,200", 200, 100)]
    public void CommandInput_AcceptsSurveyCoordinates(string text, double x, double y)
    {
        Assert.True(CoordinateInput.TryParse(text, null, out var p));
        AssertNear(new Vec2(x, y), p);
    }

    [Fact]
    public void LengthAngle_Constraint()
    {
        var b = new Vec2(10, 10);
        // 長さだけ固定: カーソル方向に 10m
        AssertNear(new Vec2(16, 18), LengthAngle.Apply(b, new Vec2(13, 14), 10, null));
        // 角度だけ固定 (北 = 90°): カーソルを北向きの線に投影
        AssertNear(new Vec2(10, 17), LengthAngle.Apply(b, new Vec2(15, 17), null, 90));
        // 両方固定: 東から 30° に 2m
        AssertNear(b + new Vec2(Math.Sqrt(3), 1), LengthAngle.Apply(b, new Vec2(-50, -50), 2, 30));
        // 何も固定しない
        AssertNear(new Vec2(1, 2), LengthAngle.Apply(b, new Vec2(1, 2), null, null));

        var (len, ang) = LengthAngle.Measure(b, new Vec2(10, 5));
        Assert.Equal(5, len, 9);
        Assert.Equal(270, ang, 9);
    }

    [Fact]
    public void SurveyParse_RejectsBadInput()
    {
        Assert.False(CoordinateSetTool.TryParseSurvey("abc", out _));
        Assert.False(CoordinateSetTool.TryParseSurvey("1,2,3", out _));
    }
}
