using System.IO;
using CivilCad.App.IO;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

/// <summary>図面を開くときに縮尺 (と DXF / DWG の座標の単位) を選んで読み込む。</summary>
public class ImportScaleTests
{
    private static string Temp(string ext) => Path.Combine(Path.GetTempPath(), "civilcad-test-" + Guid.NewGuid().ToString("N") + ext);

    /// <summary>1:100 で 10m の線と用紙上 3.5mm の文字を描いた図面。</summary>
    private static CadDocument Sample()
    {
        var doc = new CadDocument { ScaleDenominator = 100 };
        doc.Entities.Add(new LineEntity(doc.CurrentLayer, new Vec2(0, 0), new Vec2(10, 0)));
        doc.Entities.Add(new TextEntity(doc.CurrentLayer, new Vec2(0, 1), "No.1", doc.PaperMmToMeters(3.5)));
        return doc;
    }

    [Fact]
    public void Factor_ByFormatAndUnit()
    {
        var paper = new DrawingFiles.OpenResult("", null, 100, null, true);
        Assert.Equal(1, DrawingFiles.ImportFactor(paper, 100, ImportUnit.Meter), 12);
        Assert.Equal(2.5, DrawingFiles.ImportFactor(paper, 250, ImportUnit.Meter), 12);    // JWW / SFC は用紙上の大きさのまま
        var dxf = new DrawingFiles.OpenResult("", null, null, ImportUnit.Millimeter);
        Assert.Equal(0.001, DrawingFiles.ImportFactor(dxf, 250, ImportUnit.Millimeter), 12); // 実寸 mm → m
        Assert.Equal(0.01, DrawingFiles.ImportFactor(dxf, 250, ImportUnit.Centimeter), 12);
        Assert.Equal(1, DrawingFiles.ImportFactor(dxf, 250, ImportUnit.Meter), 12);          // 縮尺の書いていない実寸 m はそのまま
        Assert.Equal(0.25, DrawingFiles.ImportFactor(dxf, 250, ImportUnit.PaperMillimeter), 12); // 用紙上の mm × 1/250
        var dxfWithScale = new DrawingFiles.OpenResult("", null, 100, ImportUnit.Meter);
        Assert.Equal(2.5, DrawingFiles.ImportFactor(dxfWithScale, 250, ImportUnit.Meter), 12);
    }

    [Theory]
    [InlineData(".jww")]
    [InlineData(".sfc")]
    [InlineData(".dxf")]
    public void Open_AtOtherScale_KeepsPaperSize(string ext)
    {
        var path = Temp(ext);
        try
        {
            DrawingFiles.Save(Sample(), path);
            var doc = new CadDocument();
            var r = DrawingFiles.Open(doc, path);
            Assert.Equal(100, r.FileScale);
            if (ext == ".dxf") Assert.Equal(ImportUnit.Meter, r.FileUnit); // CivilCad の DXF は実寸 m と書く
            var (k, _) = DrawingFiles.ApplyImportScale(doc, r, 250, ImportUnit.Meter);
            Assert.Equal(2.5, k, 9);
            Assert.Equal(250, doc.ScaleDenominator);
            // 1:100 で 10m の線は 1:250 では 25m、文字は用紙上 3.5mm のまま
            Assert.Equal(25, doc.Entities.OfType<LineEntity>().Max(l => l.Length), 6);
            Assert.Equal(doc.PaperMmToMeters(3.5), doc.Entities.OfType<TextEntity>().Single(t => t.Text == "No.1").Height, 6);
            Assert.False(doc.History.CanUndo);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Open_SameScale_Unchanged()
    {
        var path = Temp(".jww");
        try
        {
            DrawingFiles.Save(Sample(), path);
            var doc = new CadDocument();
            var r = DrawingFiles.Open(doc, path);
            var (k, _) = DrawingFiles.ApplyImportScale(doc, r, 100, ImportUnit.Meter);
            Assert.Equal(1, k);
            Assert.Equal(10, doc.Entities.OfType<LineEntity>().Max(l => l.Length), 6);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
