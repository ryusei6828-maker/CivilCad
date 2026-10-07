using CivilCad.App.Drawing;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

/// <summary>シートの縮尺を変えたとき、図枠・文字を新しい縮尺に合わせる。</summary>
public class PaperRescaleTests
{
    private static (CadDocument Doc, LineEntity Line, TextEntity Text) Sample()
    {
        var doc = new CadDocument { ScaleDenominator = 100 };
        doc.Entities.AddRange(DrawingFrame.Build(doc, new DrawingFrameOptions { Paper = "A3", ScaleDenominator = 100, Margin = 20 }, new Vec2(10, 20)));
        var line = new LineEntity(doc.CurrentLayer, new Vec2(12, 22), new Vec2(32, 22)); // 実寸 20m の線
        var text = new TextEntity(doc.CurrentLayer, new Vec2(15, 25), "No.1", doc.PaperMmToMeters(3.5)); // 3.5mm の文字
        doc.Entities.AddRange([line, text]);
        return (doc, line, text);
    }

    private static Box2 Outer(CadDocument doc) =>
        doc.Entities.OfType<PolylineEntity>().First(p => p.Tag == DrawingFrame.Tag && p.Layer.Name == DrawingFrame.LayerFrame).Bounds;

    [Fact]
    public void ChangeScale_KeepsPaperSize_OfFrameAndText()
    {
        var (doc, line, text) = Sample();
        doc.History.Execute(PaperRescale.Command(doc, 250, keepPaperSize: true));
        Assert.Equal(250, doc.ScaleDenominator);
        // A3 の外枠: 1:100 で 38m → 1:250 で 95m。左下は動かない
        var outer = Outer(doc);
        Assert.Equal(95, outer.Width, 9);
        Assert.Equal(64.25, outer.Height, 9);
        Assert.True(outer.Min.DistanceTo(new Vec2(10, 20)) < 1e-9);
        // 表題欄の縮尺の文字も書き換わる
        Assert.Contains(doc.Entities.OfType<TextEntity>(), t => t.Tag == DrawingFrame.Tag && t.Text == "1:250");
        Assert.DoesNotContain(doc.Entities.OfType<TextEntity>(), t => t.Text == "1:100");
        // 文字は用紙上 3.5mm のまま (図面上 0.35m → 0.875m)、置いた点は同じ
        var t2 = doc.Entities.OfType<TextEntity>().Single(t => t.Text == "No.1");
        Assert.Equal(0.875, t2.Height, 9);
        Assert.Equal(new Vec2(15, 25), t2.Position);
        // 実寸の線は変わらない (計測の値も同じ)
        Assert.Contains(line, doc.Entities);
        Assert.Equal(20, line.Length, 9);

        // 元に戻すで一度に戻る
        doc.History.Undo();
        Assert.Equal(100, doc.ScaleDenominator);
        Assert.Equal(38, Outer(doc).Width, 9);
        Assert.Contains(text, doc.Entities);
        Assert.Equal(0.35, text.Height, 9);
    }

    [Fact]
    public void ChangeScale_WithoutKeep_OnlyChangesScale()
    {
        var (doc, _, text) = Sample();
        doc.History.Execute(PaperRescale.Command(doc, 250, keepPaperSize: false));
        Assert.Equal(250, doc.ScaleDenominator);
        Assert.Equal(38, Outer(doc).Width, 9);
        Assert.Equal(0.35, text.Height, 9);
    }
}
