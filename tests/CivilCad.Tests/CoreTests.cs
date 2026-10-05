using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

public class CoreTests
{
    [Fact]
    public void Arc_From3Points_KeepsPassingPoint()
    {
        var layer = new Layer("0", RgbColor.White);
        // 時計回りに指定しても通過点を含む円弧になる
        var arc = ArcEntity.From3Points(layer, new Vec2(10, 0), new Vec2(0, -10), new Vec2(-10, 0))!;
        Assert.True(arc.DistanceTo(new Vec2(0, -10)) < 1e-9);
        Assert.True(arc.DistanceTo(new Vec2(0, 10)) > 1);
        Assert.Equal(Math.PI * 10, arc.Length, 9);
    }

    [Fact]
    public void Polyline_Area_Coordinates()
    {
        var pl = new PolylineEntity(new Layer("0", RgbColor.White), [new(0, 0), new(10, 0), new(10, 5), new(0, 5)], true);
        Assert.Equal(50, pl.Area, 9);
    }

    [Fact]
    public void UndoRedo_AddRemoveMoveChangeLayer()
    {
        var doc = new CadDocument();
        var l1 = doc.GetOrCreateLayer("A", RgbColor.Red);
        var a = new LineEntity(doc.CurrentLayer, Vec2.Zero, new Vec2(1, 0));
        var b = new LineEntity(doc.CurrentLayer, Vec2.Zero, new Vec2(0, 1));

        doc.History.Execute(new AddEntitiesCommand([a, b]));
        doc.History.Execute(new TranslateEntitiesCommand([a], new Vec2(5, 5)));
        doc.History.Execute(new ChangeLayerCommand([b], l1));
        doc.History.Execute(new RemoveEntitiesCommand([a]));
        Assert.Equal([b], doc.Entities);

        doc.History.Undo();
        Assert.Equal([a, b], doc.Entities);
        doc.History.Undo();
        Assert.Equal("0", b.Layer.Name);
        doc.History.Undo();
        Assert.Equal(Vec2.Zero, a.Start);
        doc.History.Redo();
        Assert.Equal(new Vec2(5, 5), a.Start);
    }

    [Fact]
    public void ReplaceEntities_KeepsOrder_AndUndoes()
    {
        var doc = new CadDocument();
        var a = new LineEntity(doc.CurrentLayer, Vec2.Zero, new Vec2(1, 0));
        var b = new CircleEntity(doc.CurrentLayer, Vec2.Zero, 1);
        var c = new LineEntity(doc.CurrentLayer, Vec2.Zero, new Vec2(0, 1));
        doc.Entities.AddRange([a, b, c]);

        var b2 = (CircleEntity)b.Clone();
        b2.Radius = 5;
        b2.LineWeight = 0.5;
        doc.History.Execute(new ReplaceEntitiesCommand([(b, b2)]));
        Assert.Equal([a, b2, c], doc.Entities);

        doc.History.Undo();
        Assert.Equal([a, b, c], doc.Entities);
        Assert.Equal(1, b.Radius);
        doc.History.Redo();
        Assert.Same(b2, doc.Entities[1]);
    }

    [Fact]
    public void LockedAndOnlyCurrentLayer_AreNotEditable()
    {
        var doc = new CadDocument();
        var other = doc.GetOrCreateLayer("B", RgbColor.Green);
        var e0 = new LineEntity(doc.CurrentLayer, Vec2.Zero, new Vec2(1, 0));
        var e1 = new LineEntity(other, Vec2.Zero, new Vec2(1, 0));
        Assert.True(doc.IsEditable(e1));

        other.IsLocked = true;
        Assert.False(doc.IsEditable(e1));
        other.IsLocked = false;

        doc.OnlyCurrentLayerEditable = true;
        Assert.True(doc.IsEditable(e0));
        Assert.False(doc.IsEditable(e1));

        // 現在レイヤはロックできない
        doc.CurrentLayer.IsLocked = true;
        Assert.False(doc.CurrentLayer.IsLocked);
    }
}
