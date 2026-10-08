using System.Runtime.ExceptionServices;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

public class RotateStepTests
{
    private static void RunSta(Action action)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
            finally { DrawInputMemory.AngleStepDeg = null; }
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
    public void Rotate_CenterThenReferenceThenTargetDirection() => RunSta(() =>
    {
        var doc = new CadDocument();
        // 北東向きの線を、中心 (10,10) で北東 → 北西 (90° 回転) に
        var line = new LineEntity(doc.CurrentLayer, new Vec2(10, 10), new Vec2(20, 20));
        doc.Entities.Add(line);
        var canvas = new CadCanvas { Document = doc };
        canvas.SetSelection([line]);
        var tool = new RotateTool();
        canvas.SetTool(tool);
        tool.OnPoint(new Vec2(10, 10));   // 中心点
        tool.OnPoint(new Vec2(20, 20));   // 基準の方向点 (線の先 = 北東)
        Assert.Same(tool, canvas.Tool);  // まだ回らない
        Near(new Vec2(20, 20), line.End);
        tool.OnMouseMove(new Vec2(0, 25));
        tool.OnPoint(new Vec2(5, 15));    // 回転先の方向点 (北西。長さは関係ない)
        Near(new Vec2(0, 20), line.End);  // 北東を向いていた先が北西へ
        Near(new Vec2(10, 10), line.Start);

        // 一つ戻すで基準の方向点を選び直せる
        canvas.SetSelection([line]);
        var t2 = new RotateTool();
        canvas.SetTool(t2);
        t2.OnPoint(new Vec2(10, 10));
        t2.OnPoint(new Vec2(99, 99));
        t2.StepBack();
        t2.OnPoint(new Vec2(0, 20));      // 今の向き (北西)
        t2.OnPoint(new Vec2(10, 0));      // 南へ
        Near(new Vec2(10, 10 - Math.Sqrt(200)), line.End);

        Assert.Equal(-Math.PI / 2, RotateTool.Angle(Vec2.Zero, new Vec2(0, 1), new Vec2(1, 0)), 9);
        Assert.Equal(Math.PI / 2, RotateTool.Angle(Vec2.Zero, new Vec2(0, -1), new Vec2(1, 0)), 9);
    });

    [Fact]
    public void LineAngleStep_SnapsDirection_ButFixedAngleAndObjectSnapWin() => RunSta(() =>
    {
        // 関数そのもの: 15° 刻みで 38° → 45°、長さはカーソルを方向に投影
        var p = LengthAngle.Apply(Vec2.Zero, Vec2.FromAngle(38 * Math.PI / 180) * 10, null, null, 15);
        Assert.Equal(45, p.Angle * 180 / Math.PI, 6);
        Assert.Equal(10 * Math.Cos(7 * Math.PI / 180), p.Length, 6);
        Assert.Equal(0, LengthAngle.Apply(Vec2.Zero, new Vec2(10, 3), null, null, 90).Y, 9);   // 90° 刻み = 水平・垂直
        Assert.Equal(30, LengthAngle.Apply(Vec2.Zero, new Vec2(10, 3), null, 30, 90).Angle * 180 / Math.PI, 6); // 角度の固定が優先
        Assert.Equal(new Vec2(10, 3), LengthAngle.Apply(Vec2.Zero, new Vec2(10, 3), null, null, null));          // 刻みなし

        // 線ツール: 45° 刻みで描くと 45° の線になる
        DrawInputMemory.AngleStepDeg = 45;
        var doc = new CadDocument();
        var canvas = new CadCanvas { Document = doc };
        var tool = new LineTool();
        canvas.SetTool(tool);
        tool.OnPoint(Vec2.Zero);
        var c = tool.ConstrainCursor(new Vec2(10, 8));
        Assert.Equal(45, c.Angle * 180 / Math.PI, 6);
        tool.OnPoint(c);
        var line = Assert.Single(doc.Entities.OfType<LineEntity>());
        Assert.Equal(line.End.X, line.End.Y, 9);
        Assert.Equal(["刻みなし", "90°刻み", "45°刻み", "15°刻み"], LengthAngle.Steps.Select(LengthAngle.StepName));
    });
}
