using CivilCad.Alignment;
using CivilCad.App.Alignment;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

/// <summary>路線の幅 (路肩線・幅杭) の作図。</summary>
public class RouteCreateTests
{
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
