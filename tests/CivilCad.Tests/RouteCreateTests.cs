using System.Runtime.ExceptionServices;
using CivilCad.Alignment;
using CivilCad.App;
using CivilCad.App.Alignment;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

/// <summary>路線作成 (RL): 図面で IP をクリックして IP 表を作る。幅 (路肩線・幅杭) の作図。</summary>
public class RouteCreateTests
{
    private static void RunSta(Action<CadDocument, CadCanvas> action)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                var doc = new CadDocument { ScaleDenominator = 500 };
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
    public void Tool_ClickPoints_EnterConfirms() => RunSta((doc, canvas) =>
    {
        List<Vec2>? result = null;
        var tool = new RouteCreateTool(p => result = p);
        canvas.SetTool(tool);
        tool.OnPoint(new Vec2(0, 0));
        tool.OnEnter();                      // 1 点だけでは確定しない
        Assert.Null(result);
        tool.OnPoint(new Vec2(100, 0));
        tool.OnPoint(new Vec2(5, 5));        // 間違えた点
        tool.StepBack();
        tool.OnPoint(new Vec2(100, 0));      // 同じ点は続けて置かない
        tool.OnPoint(new Vec2(150, 100));
        Assert.Equal(3, tool.Points.Count);
        tool.OnEnter();
        Assert.Equal([new Vec2(0, 0), new Vec2(100, 0), new Vec2(150, 100)], result);
        Assert.IsType<SelectTool>(canvas.Tool);
    });

    [Fact]
    public void RouteFromPoints_MakesIpTable_InSurveyCoordinates()
    {
        var doc = new CadDocument();
        doc.Entities.Add(new LineEntity(doc.CurrentLayer, Vec2.Zero, Vec2.Zero) { Tag = "ALIGN:路線1" }); // 路線1 は使用中
        var previous = new AlignmentInput { Pitch = 20, TextHeightMm = 2.5, LeftWidths = "3.0", RightWidths = "3.0" };
        var input = MainWindow.RouteFromPoints(doc, [new Vec2(100, 200), new Vec2(150, 260), new Vec2(300, 260)], previous);
        Assert.Equal("路線2", input.Name);
        Assert.Equal(["BP", "IP1", "EP"], input.Rows.Select(r => r.Name));
        // 測量座標は X = 北 (図面の y), Y = 東 (図面の x)
        Assert.Equal((200.0, 100.0), (input.Rows[0].X, input.Rows[0].Y));
        Assert.Equal((260.0, 150.0), (input.Rows[1].X, input.Rows[1].Y));
        Assert.All(input.Rows, r => Assert.Equal(0, r.R)); // R・A はあとで窓で入れる
        Assert.Equal((20.0, 2.5, "3.0"), (input.Pitch, input.TextHeightMm, input.LeftWidths)); // 設定は引き継ぐ

        // 座標設定の向きがあれば、その向きで測量座標にする
        doc.SetSurveyFrame(Math.PI / 2, new Vec2(100, 200));
        var turned = MainWindow.RouteFromPoints(doc, [new Vec2(100, 200), new Vec2(110, 200)], previous);
        Assert.Equal(210, turned.Rows[1].X, 9); // 図面で東へ 10 → 測量座標では北へ 10
        Assert.Equal(100, turned.Rows[1].Y, 9);
    }

    [Fact]
    public void Widths_DrawShoulderLinesAndStakes()
    {
        var doc = new CadDocument { ScaleDenominator = 500 };
        // 北へまっすぐ 100m (測点間隔 20m)
        var input = new AlignmentInput
        {
            Name = "路線A",
            Pitch = 20,
            LeftWidths = "3.0, 5.5",
            RightWidths = "3.0",
            Rows = [new() { Name = "BP", X = 0, Y = 0 }, new() { Name = "EP", X = 100, Y = 0 }],
        };
        var al = input.Compute();
        doc.History.Execute(AlignmentDrawer.CreateDrawCommand(doc, al, input));

        // 路肩線: 幅ごとに中心線と平行 (左 3.0・5.5、右 3.0)
        var shoulders = doc.Entities.OfType<PolylineEntity>().Where(p => p.Layer.Name == AlignmentDrawer.LayerShoulder).ToList();
        Assert.Equal(3, shoulders.Count);
        var xs = shoulders.Select(p => Math.Round(p.Points[0].X, 6)).Order().ToList();
        Assert.Equal([-5.5, -3.0, 3.0], xs); // 北向きなので左は西 (x が負)
        Assert.All(shoulders, p => Assert.All(p.Points, q => Assert.Equal(p.Points[0].X, q.X, 6)));
        Assert.All(shoulders, p => Assert.Equal(100, p.Length, 6));

        // 幅杭: 測点ごと (No.0〜No.5 の 6 本) に左 5.5 〜 右 3.0 を結ぶ線と、両端の小さな円
        var stakeLines = doc.Entities.OfType<LineEntity>().Where(l => l.Layer.Name == AlignmentDrawer.LayerStake).ToList();
        Assert.Equal(6, stakeLines.Count);
        Assert.All(stakeLines, l => Assert.Equal(8.5, l.Length, 6));
        Assert.Equal(12, doc.Entities.OfType<CircleEntity>().Count(c => c.Layer.Name == AlignmentDrawer.LayerStake));
        Assert.All(doc.Entities.Where(e => e.Layer.Name is AlignmentDrawer.LayerShoulder or AlignmentDrawer.LayerStake), e => Assert.Equal(input.Tag, e.Tag));
    }

    [Fact]
    public void Widths_FollowCurve()
    {
        var doc = new CadDocument { ScaleDenominator = 500 };
        var input = new AlignmentInput
        {
            Pitch = 20,
            RightWidths = "4",
            DrawStakes = false,
            Rows =
            [
                new() { Name = "BP", X = 0, Y = 0 },
                new() { Name = "IP1", X = 100, Y = 0, R = 50 },
                new() { Name = "EP", X = 100, Y = 100 },
            ],
        };
        var al = input.Compute();
        doc.History.Execute(AlignmentDrawer.CreateDrawCommand(doc, al, input));
        var shoulder = doc.Entities.OfType<PolylineEntity>().Single(p => p.Layer.Name == AlignmentDrawer.LayerShoulder);
        // 中心線からの離れはどこでも 4m (円曲線の区間も)
        Assert.All(shoulder.Points, p => Assert.Equal(4, Math.Abs(al.Project(p).OffsetRight), 3));
        Assert.DoesNotContain(doc.Entities, e => e.Layer.Name == AlignmentDrawer.LayerStake);
    }

    [Fact]
    public void ParseWidths_Validates()
    {
        Assert.Equal([3.0, 5.5], AlignmentInput.ParseWidths("5.5、3.0", "左の幅"));
        Assert.Empty(AlignmentInput.ParseWidths("  ", "左の幅"));
        Assert.Throws<AlignmentException>(() => AlignmentInput.ParseWidths("3, abc", "左の幅"));
        Assert.Throws<AlignmentException>(() => AlignmentInput.ParseWidths("-2", "右の幅"));
    }
}
