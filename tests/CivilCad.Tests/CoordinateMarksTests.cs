using CivilCad.App.Tools;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

public class CoordinateMarksTests
{
    private static (CadDocument Doc, PointEntity A, PointEntity B) Doc()
    {
        var doc = new CadDocument { ScaleDenominator = 500 };
        var a = new PointEntity(doc.CurrentLayer, new Vec2(10, 20)) { Name = "P1", Z = 12.5 };
        var b = new PointEntity(doc.CurrentLayer, new Vec2(30, 40)) { Name = "P2" };
        doc.Entities.AddRange([a, b]);
        return (doc, a, b);
    }

    [Fact]
    public void CrossCircle_WithName_SizeOnPaper()
    {
        var (doc, a, _) = Doc();
        var s = new PointMarkSettings { Kind = PointMarkKind.CrossCircle, SizeMm = 3, DrawName = true, TextHeightMm = 2.5 };
        doc.History.Execute(CoordinateMarks.CreateDrawCommand(doc, [a], s));
        var circle = Assert.Single(doc.Entities.OfType<CircleEntity>());
        Assert.Equal(0.75, circle.Radius, 9);                 // 1/500 で 3mm の直径 = 1.5m
        Assert.Equal(a.Position, circle.Center);
        Assert.Equal(2, doc.Entities.OfType<LineEntity>().Count());
        var name = Assert.Single(doc.Entities.OfType<TextEntity>());
        Assert.Equal("P1", name.Text);
        Assert.Equal(1.25, name.Height, 9);
        Assert.True(name.Position.X > a.Position.X && name.Position.Y > a.Position.Y); // 右上
        Assert.Equal(CoordinateMarks.NameLayer, name.Layer.Name);
        Assert.Equal(CoordinateMarks.MarkLayer, circle.Layer.Name);
        doc.History.Undo();
        Assert.Equal(2, doc.Entities.Count);
    }

    [Fact]
    public void DotCircle_NoName_RedrawReplaces_Erase()
    {
        var (doc, a, b) = Doc();
        doc.History.Execute(CoordinateMarks.CreateDrawCommand(doc, [a, b], new PointMarkSettings { Kind = PointMarkKind.CrossCircle }));
        Assert.Equal(2, doc.Entities.OfType<TextEntity>().Count());

        // 点＋〇・点名なしで描き直すと、前のマーク・点名は消えて置き換わる
        var s = new PointMarkSettings { Kind = PointMarkKind.DotCircle, DrawName = false };
        doc.History.Execute(CoordinateMarks.CreateDrawCommand(doc, [a, b], s));
        Assert.Empty(doc.Entities.OfType<TextEntity>());
        Assert.Empty(doc.Entities.OfType<LineEntity>());
        Assert.Equal(2, doc.Entities.OfType<CircleEntity>().Count());
        Assert.Equal(2, doc.Entities.OfType<HatchEntity>().Count(h => h.Solid)); // 黒丸

        // 標高も描く: 標高のある点だけ
        doc.History.Execute(CoordinateMarks.CreateDrawCommand(doc, [a, b], new PointMarkSettings { DrawName = true, DrawElevation = true }));
        Assert.Equal(["P1", "H=12.500", "P2"], doc.Entities.OfType<TextEntity>().Select(t => t.Text));

        // 消す: 点は残る
        doc.History.Execute(CoordinateMarks.CreateEraseCommand(doc, [a])!);
        Assert.DoesNotContain(doc.Entities, e => e.Tag == CoordinateMarks.TagOf(a));
        Assert.Contains(doc.Entities, e => e.Tag == CoordinateMarks.TagOf(b));
        Assert.Contains(a, doc.Entities);
        Assert.Null(CoordinateMarks.CreateEraseCommand(doc, [a]));
    }

    [Fact]
    public void AllKinds_BuildSomething()
    {
        var (doc, a, _) = Doc();
        var layer = doc.CurrentLayer;
        foreach (var kind in Enum.GetValues<PointMarkKind>())
        {
            var list = CoordinateMarks.Build(doc, a, new PointMarkSettings { Kind = kind, DrawName = false }, layer, layer);
            Assert.NotEmpty(list);
            Assert.All(list, e => Assert.True(e.Bounds.Center.DistanceTo(a.Position) < 1.5, kind.ToString()));
            Assert.False(string.IsNullOrEmpty(CoordinateMarks.Label(kind)));
        }
    }
}
