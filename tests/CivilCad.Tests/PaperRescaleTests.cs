using CivilCad.App.Drawing;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

/// <summary>シートの縮尺の変更: 図面をそのまま (図寸固定) / 実寸を保つ (実寸固定)。</summary>
public class PaperRescaleTests
{
    private static (CadDocument Doc, LineEntity Line, TextEntity Text, SymbolEntity Mark) Sample()
    {
        var doc = new CadDocument { ScaleDenominator = 100 };
        doc.Entities.AddRange(DrawingFrame.Build(doc, new DrawingFrameOptions { Paper = "A3", ScaleDenominator = 100, Margin = 20 }, new Vec2(10, 20)));
        var line = new LineEntity(doc.CurrentLayer, new Vec2(12, 22), new Vec2(22, 22)); // 10m の線
        var text = new TextEntity(doc.CurrentLayer, new Vec2(15, 25), "No.1", doc.PaperMmToMeters(3.5)); // 用紙上 3.5mm の文字
        var mark = new SymbolEntity(doc.CurrentLayer, SymbolLibrary.NorthArrow, new Vec2(30, 30), 20, doc.PaperMmToMeters(20));
        doc.Entities.AddRange([line, text, mark]);
        return (doc, line, text, mark);
    }

    private static Box2 Outer(CadDocument doc) =>
        doc.Entities.OfType<PolylineEntity>().First(p => p.Tag == DrawingFrame.Tag && p.Layer.Name == DrawingFrame.LayerFrame).Bounds;

    [Fact]
    public void KeepPaper_ScalesWholeDrawing_MeasureFollowsScale()
    {
        var (doc, line, text, mark) = Sample();
        doc.History.Execute(PaperRescale.Command(doc, 250, ScaleChangeMode.KeepPaper));
        Assert.Equal(250, doc.ScaleDenominator);
        // 1:100 で 10m の線は 1:250 で 25m (用紙上は同じ 100mm)
        var l2 = doc.Entities.OfType<LineEntity>().Single(l => l.Tag == null);
        Assert.Equal(25, l2.Length, 9);
        Assert.True(l2.Start.DistanceTo(new Vec2(30, 55)) < 1e-9); // 原点 (0,0) を基準に 2.5 倍
        // 文字も一緒に (用紙上 3.5mm のまま = 図面上 0.875m)
        var t2 = doc.Entities.OfType<TextEntity>().Single(t => t.Text == "No.1");
        Assert.Equal(0.875, t2.Height, 9);
        Assert.Equal(doc.PaperMmToMeters(3.5), t2.Height, 9);
        // 図枠は 95m、表題欄の縮尺は 1:250
        Assert.Equal(95, Outer(doc).Width, 9);
        Assert.Contains(doc.Entities.OfType<TextEntity>(), t => t.Tag == DrawingFrame.Tag && t.Text == "1:250");
        // 記号は用紙上 20mm のまま
        var m2 = doc.Entities.OfType<SymbolEntity>().Single();
        Assert.Equal(20, m2.SizeMm, 9);
        Assert.Equal(doc.PaperMmToMeters(20), m2.Size, 9);

        // 元に戻すで一度に戻る
        doc.History.Undo();
        Assert.Equal(100, doc.ScaleDenominator);
        Assert.Contains(line, doc.Entities);
        Assert.Equal(10, line.Length, 9);
        Assert.Equal(0.35, text.Height, 9);
        Assert.Equal(38, Outer(doc).Width, 9);
        Assert.Equal(2, mark.Size, 9);
    }

    [Fact]
    public void KeepPaper_ScreenFixedText_KeepsPixels()
    {
        var doc = new CadDocument { ScaleDenominator = 100 };
        var t = new TextEntity(doc.CurrentLayer, new Vec2(4, 2), "A", 1) { ScreenHeightPx = 16 };
        doc.Entities.Add(t);
        doc.History.Execute(PaperRescale.Command(doc, 200, ScaleChangeMode.KeepPaper));
        var t2 = doc.Entities.OfType<TextEntity>().Single();
        Assert.Equal(16, t2.ScreenHeightPx);
        Assert.Equal(new Vec2(8, 4), t2.Position);
    }

    [Fact]
    public void KeepReal_RescalesFrameOnly()
    {
        var (doc, line, text, _) = Sample();
        doc.History.Execute(PaperRescale.Command(doc, 250, ScaleChangeMode.KeepReal));
        Assert.Equal(250, doc.ScaleDenominator);
        var outer = Outer(doc);
        Assert.Equal(95, outer.Width, 9);
        Assert.True(outer.Min.DistanceTo(new Vec2(10, 20)) < 1e-9); // 外枠の左下は動かない
        Assert.Contains(doc.Entities.OfType<TextEntity>(), t => t.Tag == DrawingFrame.Tag && t.Text == "1:250");
        // 線・文字はそのまま
        Assert.Contains(line, doc.Entities);
        Assert.Equal(10, line.Length, 9);
        Assert.Contains(text, doc.Entities);
        Assert.Equal(0.35, text.Height, 9);
    }
}
