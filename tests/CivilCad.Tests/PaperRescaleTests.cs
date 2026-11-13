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

    [Fact]
    public void KeepPaper_AfterCoordinateSet_ScalesAboutSurveyPivot_AndKeepsRegisteredCoordinatesAndRoute()
    {
        var doc = new CadDocument { ScaleDenominator = 100 };
        var pivot = new Vec2(-70857.888, -127271.935);
        doc.SetSurveyFrame(0, pivot); // 座標設定 (向き 0 でも基準点は残る)
        var body = new LineEntity(doc.CurrentLayer, pivot + new Vec2(1, 2), pivot + new Vec2(11, 2));
        var point = new PointEntity(doc.CurrentLayer, pivot + new Vec2(5, 5)) { Name = "A1" };
        var mark = new CircleEntity(doc.CurrentLayer, point.Position, 0.15) { Tag = "POINT:A1" };
        var tagged = new TextEntity(doc.CurrentLayer, point.Position + new Vec2(0.2, 0.1), "A1", 0.25) { Tag = "POINT:A1" };
        var legacy = new TextEntity(doc.CurrentLayer, point.Position + new Vec2(0.2, 0.1), "A1", 0.25); // Tag なし (点の近く)
        var other = new TextEntity(doc.CurrentLayer, pivot + new Vec2(300, 0), "A1", 0.25); // 点から遠い同名の文字は図面の物
        var routeLine = new LineEntity(doc.CurrentLayer, pivot + new Vec2(-3, 0), pivot + new Vec2(40, 9)) { Tag = "ALIGN:CD" };
        var routeText = new TextEntity(doc.CurrentLayer, pivot + new Vec2(20, 3), "No.1", 0.35) { Tag = "ALIGN:CD" };
        doc.Entities.AddRange([body, point, mark, tagged, legacy, other, routeLine, routeText]);

        doc.History.Execute(PaperRescale.Command(doc, 1000, ScaleChangeMode.KeepPaper));
        Assert.Equal(1000, doc.ScaleDenominator);

        // 図形は作り直される (並びは同じ) ので、いまの図形を取り直す
        var now = doc.Entities.ToList();
        var (nb, np, nm, nt, nl, no, nrl, nrt) = ((LineEntity)now[0], (PointEntity)now[1], (CircleEntity)now[2], (TextEntity)now[3], (TextEntity)now[4], (TextEntity)now[5], (LineEntity)now[6], (TextEntity)now[7]);

        // 図面は基準点を中心に 10 倍 (基準点との位置関係が保たれる)
        Assert.True(nb.Start.DistanceTo(pivot + new Vec2(10, 20)) < 1e-6);
        Assert.True(nb.End.DistanceTo(pivot + new Vec2(110, 20)) < 1e-6);
        Assert.True(no.Position.DistanceTo(pivot + new Vec2(3000, 0)) < 1e-6);
        // 登録した座標の点・路線の線は動かない
        Assert.Equal(pivot + new Vec2(5, 5), np.Position);
        Assert.Equal(pivot + new Vec2(-3, 0), nrl.Start);
        Assert.Equal(pivot + new Vec2(40, 9), nrl.End);
        // マーク・点名は点の位置を基準に 10 倍 (用紙上の大きさのまま)
        Assert.Equal(np.Position, nm.Center);
        Assert.Equal(1.5, nm.Radius, 9);
        Assert.True(nt.Position.DistanceTo(np.Position + new Vec2(2, 1)) < 1e-6);
        Assert.Equal(2.5, nt.Height, 9);
        Assert.True(nl.Position.DistanceTo(np.Position + new Vec2(2, 1)) < 1e-6);
        // 路線の文字は位置そのまま・大きさだけ用紙上のまま
        Assert.Equal(pivot + new Vec2(20, 3), nrt.Position);
        Assert.Equal(3.5, nrt.Height, 9);

        doc.History.Undo();
        Assert.Equal(100, doc.ScaleDenominator);
        Assert.True(body.Start.DistanceTo(pivot + new Vec2(1, 2)) < 1e-6);
        Assert.Equal(0.15, mark.Radius, 9);
        Assert.Equal(0.35, routeText.Height, 9);
    }

    [Fact]
    public void KeepPaper_NoCoordinateSet_StillScalesAboutOrigin_ButKeepsRegisteredPoints()
    {
        var doc = new CadDocument { ScaleDenominator = 100 };
        var line = new LineEntity(doc.CurrentLayer, new Vec2(1, 2), new Vec2(11, 2));
        var point = new PointEntity(doc.CurrentLayer, new Vec2(5, 5)) { Name = "A1" };
        var unnamed = new PointEntity(doc.CurrentLayer, new Vec2(5, 5));
        doc.Entities.AddRange([line, point, unnamed]);
        doc.History.Execute(PaperRescale.Command(doc, 200, ScaleChangeMode.KeepPaper));
        Assert.Equal(new Vec2(2, 4), doc.Entities.OfType<LineEntity>().Single().Start);
        Assert.Equal(new Vec2(5, 5), doc.Entities.OfType<PointEntity>().Single(p => p.Name == "A1").Position);
        Assert.Equal(new Vec2(10, 10), doc.Entities.OfType<PointEntity>().Single(p => p.Name == "").Position);
    }
}
