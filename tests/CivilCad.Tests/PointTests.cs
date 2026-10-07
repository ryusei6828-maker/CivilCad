using System.IO;
using System.Runtime.ExceptionServices;
using CivilCad.App.IO;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;
using SkiaSharp;

namespace CivilCad.Tests;

/// <summary>点 (PO): 作図・スナップ・編集・保存と読み込み・印刷。</summary>
public class PointTests
{
    private static void RunSta(Action<CadDocument, CadCanvas> action)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                var doc = new CadDocument { ScaleDenominator = 100 };
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
    public void Tool_PlacesPointsContinuously_StepBackRemovesLast() => RunSta((doc, canvas) =>
    {
        var tool = new PointTool();
        canvas.SetTool(tool);
        tool.OnPoint(new Vec2(1, 2));
        Assert.True(CoordinateInput.TryParse("@3,4", tool.BasePoint, out var rel)); // 直前の点からの相対座標
        tool.OnPoint(rel);
        Assert.True(CoordinateInput.TryParse("s10,20", tool.BasePoint, out var survey)); // 測量座標 X=北, Y=東
        tool.OnPoint(survey);
        Assert.Equal([new Vec2(1, 2), new Vec2(4, 6), new Vec2(20, 10)], doc.Entities.OfType<PointEntity>().Select(p => p.Position));
        Assert.True(tool.CanStepBack);
        tool.StepBack();
        Assert.Equal(2, doc.Entities.OfType<PointEntity>().Count());
        Assert.Equal(new Vec2(4, 6), tool.BasePoint);
        // Ctrl+Z でも 1 点ずつ戻せる
        doc.History.Undo();
        Assert.Single(doc.Entities.OfType<PointEntity>());
    });

    [Fact]
    public void Point_SnapsMovesAndSelects()
    {
        var layer = new Layer("0", RgbColor.White);
        var p = new PointEntity(layer, new Vec2(5, 5)) { LineWeight = 0.35 };
        Assert.Equal([new SnapPoint(new Vec2(5, 5), SnapKind.Node)], p.SnapPoints);
        Assert.True(p.IntersectsBox(new Box2(new Vec2(4, 4), new Vec2(6, 6))));
        Assert.False(p.IntersectsBox(new Box2(new Vec2(6, 6), new Vec2(7, 7))));
        Assert.Equal(1, p.DistanceTo(new Vec2(5, 6)), 12);
        p.Translate(new Vec2(1, 0));
        p.Rotate(new Vec2(0, 0), Math.PI / 2);
        Assert.True(p.Position.DistanceTo(new Vec2(-5, 6)) < 1e-12);
        p.Scale(Vec2.Zero, 2);
        Assert.True(p.Position.DistanceTo(new Vec2(-10, 12)) < 1e-12);
        var c = (PointEntity)p.Clone();
        Assert.Equal((p.Position, 0.35), (c.Position, c.LineWeight!.Value));
    }

    [Theory]
    [InlineData(".dxf")]
    [InlineData(".dwg")]
    [InlineData(".jww")]
    [InlineData(".sfc")]
    [InlineData(".p21")]
    public void SaveAndOpen_KeepsPoints(string ext)
    {
        var doc = new CadDocument { ScaleDenominator = 100 };
        var red = doc.GetOrCreateLayer("測点", new RgbColor(255, 0, 0));
        doc.Entities.Add(new LineEntity(doc.CurrentLayer, new Vec2(0, 0), new Vec2(10, 0)));
        doc.Entities.Add(new PointEntity(red, new Vec2(3.25, 4.5)));
        doc.Entities.Add(new PointEntity(red, new Vec2(-2, 7.75)));
        var path = Path.Combine(Path.GetTempPath(), "civilcad-test-" + Guid.NewGuid().ToString("N") + ext);
        try
        {
            DrawingFiles.Save(doc, path);
            var back = new CadDocument();
            DrawingFiles.Open(back, path);
            // SFC / P21 は用紙の左下が原点になるので、線の始点 (元は 0,0) からの位置で比べる
            var origin = back.Entities.OfType<LineEntity>().Single().Start;
            var pts = back.Entities.OfType<PointEntity>().Select(p => p.Position - origin).OrderBy(p => p.X).ToList();
            Assert.Equal(2, pts.Count);
            Assert.True(pts[0].DistanceTo(new Vec2(-2, 7.75)) < 1e-6, pts[0].ToString());
            Assert.True(pts[1].DistanceTo(new Vec2(3.25, 4.5)) < 1e-6, pts[1].ToString());
            Assert.All(back.Entities.OfType<PointEntity>(), p => Assert.Equal(new RgbColor(255, 0, 0), p.EffectiveColor));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Sfc_OtherMarkersKeepTheirCode()
    {
        // 見本の SFC の点マーカー 7 種 (1 asterisk 〜 7 x) は点になり、点 (3) 以外はコードを覚えておく
        var doc = new CadDocument();
        SfcIO.Load(doc, Path.Combine(AppContext.BaseDirectory, "testdata", "sxf", "shapes.sfc"));
        var markers = doc.Entities.OfType<PointEntity>().ToList();
        Assert.Equal(7, markers.Count);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7], markers.OrderBy(p => p.Position.X).Select(SfcIO.MarkerCode));
        Assert.Single(markers, p => p.Tag == null); // 3 (点)
    }

    [Fact]
    public void Pdf_PrintsPointAsDot()
    {
        var doc = new CadDocument { ScaleDenominator = 100 };
        doc.Entities.Add(new LineEntity(doc.CurrentLayer, new Vec2(-10, -5), new Vec2(10, 5)));
        doc.Entities.Add(new PointEntity(doc.CurrentLayer, new Vec2(5, -3)));
        var o = new PdfOptions { Paper = "A4", ScaleDenominator = 100 };
        var l = PdfExport.ComputeLayout(doc, o);
        const float pxPerMm = 8;
        using var bmp = new SKBitmap((int)(l.Width * pxPerMm), (int)(l.Height * pxPerMm));
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Scale(pxPerMm * 25.4f / 72f);
            PdfExport.Render(doc, canvas, l, o);
        }
        var mm = (new Vec2(5, -3) - l.Origin) / l.MetersPerMm;
        var c = bmp.GetPixel((int)Math.Round(mm.X * pxPerMm), (int)Math.Round((l.Height - mm.Y) * pxPerMm));
        Assert.True(c is { Red: < 80, Green: < 80, Blue: < 80 }, c.ToString()); // 白い点は黒で
        // φ0.5mm: 中心から 0.6mm 離れると白
        var off = bmp.GetPixel((int)Math.Round((mm.X + 0.6) * pxPerMm), (int)Math.Round((l.Height - mm.Y) * pxPerMm));
        Assert.True(off is { Red: > 240, Green: > 240, Blue: > 240 }, off.ToString());
    }
}
