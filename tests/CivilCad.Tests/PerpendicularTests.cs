using System.Runtime.ExceptionServices;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

public class PerpendicularTests
{
    private static void RunSta(Action<CadDocument, CadCanvas> action)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                var doc = new CadDocument();
                var canvas = new CadCanvas { Document = doc };
                canvas.SetView(Vec2.Zero, 10);
                action(doc, canvas);
            }
            catch (Exception ex) { error = ex; }
            finally { PerpendicularTool.FixedLength = null; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    [Fact]
    public void FromPointOnSlantedLine() => RunSta((doc, canvas) =>
    {
        // 45° の線 (0,0)-(10,10)
        doc.Entities.Add(new LineEntity(doc.CurrentLayer, Vec2.Zero, new Vec2(10, 10)));
        var tool = new PerpendicularTool();
        canvas.SetTool(tool);
        tool.OnPoint(new Vec2(2, 2.05));                  // 基準の線を選ぶ
        tool.OnPoint(tool.ConstrainCursor(new Vec2(5, 4))); // 線上の点 → 足は (4.5, 4.5)
        Assert.Equal(new Vec2(4.5, 4.5), tool.BasePoint!.Value with { X = Math.Round(tool.BasePoint.Value.X, 9), Y = Math.Round(tool.BasePoint.Value.Y, 9) });
        tool.OnPoint(new Vec2(2, 8));                     // 左上の方向へ
        var perp = doc.Entities.OfType<LineEntity>().Last();
        var d = perp.End - perp.Start;
        Assert.Equal(0, d.Dot(new Vec2(1, 1)), 9);         // 基準の線と直交
        Assert.True(perp.Start.DistanceTo(new Vec2(4.5, 4.5)) < 1e-9);
        Assert.Equal(6 / Math.Sqrt(2), d.Length, 9);       // (2,8)-(4.5,4.5) = (-2.5,3.5) を法線 (-1,1)/√2 へ投影した長さ
    });

    [Fact]
    public void FixedLength_OnCircle_IsRadial() => RunSta((doc, canvas) =>
    {
        doc.Entities.Add(new CircleEntity(doc.CurrentLayer, Vec2.Zero, 5));
        PerpendicularTool.FixedLength = 2;
        var tool = new PerpendicularTool();
        canvas.SetTool(tool);
        tool.OnPoint(new Vec2(0, 5.02));
        tool.OnPoint(tool.ConstrainCursor(new Vec2(0.1, 6)));  // 足は円周上 (0.1,6) の向き
        tool.OnPoint(tool.ConstrainCursor(new Vec2(0, 0)));    // 内側をクリック → 中心向きに 2m
        var perp = doc.Entities.OfType<LineEntity>().Single();
        Assert.Equal(5, perp.Start.Length, 9);
        Assert.Equal(3, perp.End.Length, 9);
        Assert.Equal(2, perp.Start.DistanceTo(perp.End), 9);

        // 続けて同じ円に引ける / 一つ戻すで足 → 基準の線の順に戻る
        tool.OnPoint(tool.ConstrainCursor(new Vec2(9, 0)));
        tool.StepBack();
        Assert.Null(tool.BasePoint);
        Assert.True(tool.CanStepBack);
        tool.StepBack();
        Assert.False(tool.CanStepBack);
    });
}
