using System.Runtime.ExceptionServices;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

/// <summary>右クリックメニューの「一つ戻す」(StepBack)。</summary>
[Collection("SharedToolSettings")] // 共有の設定を書き換える
public class StepBackTests
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

    [Fact]
    public void Line_StepBackRemovesLastSegment() => RunSta((doc, canvas) =>
    {
        var tool = new LineTool();
        canvas.SetTool(tool);
        Assert.False(tool.CanStepBack);
        tool.OnPoint(new Vec2(0, 0));
        tool.OnPoint(new Vec2(10, 0));
        tool.OnPoint(new Vec2(10, 10));
        Assert.Equal(2, doc.Entities.Count);

        tool.StepBack(); // (10,10) とその線を取り消す
        Assert.Single(doc.Entities);
        Assert.Equal(new Vec2(10, 0), tool.BasePoint);

        tool.OnPoint(new Vec2(20, 5)); // 戻った点から続けて引ける
        Assert.Equal(new Vec2(10, 0), doc.Entities.OfType<LineEntity>().Last().Start);

        tool.StepBack();
        tool.StepBack();
        tool.StepBack(); // 始点まで戻すと線は無くなり、始点の指定からやり直し
        Assert.Empty(doc.Entities);
        Assert.Null(tool.BasePoint);
        Assert.False(tool.CanStepBack);
    });

    [Fact]
    public void Polyline_And_Hatch_StepBackRemovesLastPoint() => RunSta((doc, canvas) =>
    {
        var pl = new PolylineTool();
        canvas.SetTool(pl);
        pl.OnPoint(new Vec2(0, 0));
        pl.OnPoint(new Vec2(10, 0));
        pl.OnPoint(new Vec2(10, 10));
        pl.StepBack();
        pl.OnEnter();
        var poly = Assert.Single(doc.Entities.OfType<PolylineEntity>());
        Assert.Equal(2, poly.Points.Count);

        var hatch = new HatchTool();
        canvas.SetTool(hatch);
        hatch.OnPoint(new Vec2(100, 0));
        hatch.OnPoint(new Vec2(110, 0));
        hatch.OnPoint(new Vec2(110, 10));
        hatch.OnPoint(new Vec2(999, 999)); // 間違えた点
        hatch.StepBack();
        Assert.NotNull(hatch.ConfirmLabel);
        hatch.OnEnter();
        var h = Assert.Single(doc.Entities.OfType<HatchEntity>());
        Assert.Equal(3, h.Loops[0].Count);
    });

    [Fact]
    public void Circle_And_Arc_StepBack() => RunSta((doc, canvas) =>
    {
        DrawInputMemory.Radius = null;
        DrawInputMemory.ArcRadius = null;
        var circle = new CircleTool();
        canvas.SetTool(circle);
        circle.OnPoint(new Vec2(0, 0));
        Assert.True(circle.CanStepBack);
        circle.StepBack();
        circle.OnPoint(new Vec2(5, 5));   // 新しい中心
        circle.OnPoint(new Vec2(8, 5));   // 半径 3
        var c = Assert.Single(doc.Entities.OfType<CircleEntity>());
        Assert.Equal(new Vec2(5, 5), c.Center);

        var arc = new ArcTool();
        canvas.SetTool(arc);
        arc.OnPoint(new Vec2(-3, 0));
        arc.OnPoint(new Vec2(50, 50)); // 間違えた通過点
        arc.StepBack();
        arc.OnPoint(new Vec2(0, 3));
        arc.OnPoint(new Vec2(3, 0));
        Assert.Equal(3, Assert.Single(doc.Entities.OfType<ArcEntity>()).Radius, 9);
    });
}
