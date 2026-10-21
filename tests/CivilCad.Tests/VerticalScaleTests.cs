using System.IO;
using System.Text;
using CivilCad.App.Alignment;
using CivilCad.App.IO;
using CivilCad.App.Tools;
using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

/// <summary>縦横で縮尺が違うシート (縦断図: 横 1:1000・縦 1:100 など)。</summary>
public class VerticalScaleTests
{
    private static CadDocument Profile()
    {
        var doc = new CadDocument { ScaleDenominator = 1000, VerticalScaleDenominator = 100 };
        return doc;
    }

    [Fact]
    public void Document_FactorAndText()
    {
        var doc = Profile();
        Assert.True(doc.HasVerticalScale);
        Assert.Equal(0.1, doc.VerticalFactor, 12);           // 図面上 10m の縦 = 実際 1m
        Assert.Equal(new Vec2(20, 1), doc.ToRealDelta(new Vec2(20, 10)));
        Assert.Equal("H=1:1000 V=1:100", doc.ScaleText);
        doc.VerticalScaleDenominator = 1000;                  // 横と同じなら縦横同じ扱い
        Assert.False(doc.HasVerticalScale);
        Assert.Equal("1:1000", doc.ScaleText);
        doc.Clear();
        Assert.Null(doc.VerticalScaleDenominator);
    }

    [Fact]
    public void Dimension_MeasuresVerticalWithVerticalScale()
    {
        var doc = Profile();
        // 図面上 (0,0)-(30,40): 実際は 横 30m・縦 4m
        DimensionEntity Dim(double? angle) => new(doc.CurrentLayer, new Vec2(0, 0), new Vec2(30, 40), new Vec2(0, 0), angle, 1, 1) { VerticalFactor = doc.VerticalFactor };
        Assert.Equal(30, Dim(0).Measured, 9);                                  // 水平
        Assert.Equal(4, Dim(Math.PI / 2).Measured, 9);                         // 垂直
        Assert.Equal(Math.Sqrt(30 * 30 + 4 * 4), Dim(null).Measured, 9);       // 平行 = 実際の斜めの長さ
        var plain = new DimensionEntity(doc.CurrentLayer, new Vec2(0, 0), new Vec2(30, 40), new Vec2(0, 0), null, 1, 1);
        Assert.Equal(50, plain.Measured, 9);                                   // 倍率 1 なら今までどおり
        Assert.Equal(0.1, ((DimensionEntity)Dim(null).Clone()).VerticalFactor, 12);
    }

    [Fact]
    public void SetVerticalScaleCommand_UpdatesDimensions_AndUndo()
    {
        var doc = new CadDocument { ScaleDenominator = 1000 };
        var dim = new DimensionEntity(doc.CurrentLayer, new Vec2(0, 0), new Vec2(0, 50), new Vec2(0, 0), Math.PI / 2, 1, 1);
        doc.Entities.Add(dim);
        doc.History.Execute(new SetVerticalScaleCommand(200));
        Assert.Equal(200, doc.VerticalScaleDenominator);
        Assert.Equal(10, dim.Measured, 9); // 縦 50m × 200/1000
        doc.History.Undo();
        Assert.Null(doc.VerticalScaleDenominator);
        Assert.Equal(50, dim.Measured, 9);
    }

    [Fact]
    public void DistanceText_UsesRealLengths()
    {
        var keep = (MeasureSettings.Detail, MeasureSettings.Decimals, MeasureSettings.Unit);
        try
        {
            MeasureSettings.Detail = true;
            MeasureSettings.Decimals = 3;
            MeasureSettings.Unit = LengthUnit.M;
            // 横 40m・図面上の縦 20m (縦 1:100 / 横 1:1000 → 実際 2m): 勾配 5%
            Assert.Equal("L=40.050m  ΔH=2.000m  i=5%", MeasureSettings.DistanceText(new Vec2(0, 0), new Vec2(40, 20), 0, 0.1));
        }
        finally
        {
            (MeasureSettings.Detail, MeasureSettings.Decimals, MeasureSettings.Unit) = keep;
        }
    }

    [Fact]
    public void Sfc_PartWithDifferentXYScale_SetsVerticalScale()
    {
        // 縦断図の部分図: 横 1/1000 (0.001)、縦 1/100 (0.01) で配置
        const string text = """
            ISO-10303-21;
            DATA;
            /*SXF
            #10 = layer_feature(\'L1\','1')
            SXF*/
            /*SXF
            #20 = line_feature('1','1','1','1','0.0','0.0','40000.0','2000.0')
            SXF*/
            /*SXF
            #30 = sfig_org_feature(\'PROFILE\','1')
            SXF*/
            /*SXF
            #40 = sfig_locate_feature('0',\'PROFILE\','10.0','10.0','0.0','0.001','0.01')
            SXF*/
            ENDSEC;
            """;
        var path = Path.Combine(Path.GetTempPath(), $"civilcad_{Guid.NewGuid():N}.sfc");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        File.WriteAllText(path, text, Encoding.GetEncoding(932));
        try
        {
            var doc = new CadDocument();
            var r = SfcIO.Load(doc, path);
            Assert.Equal(1000, r.ScaleDenominator);
            Assert.Equal(100, r.VerticalScaleDenominator);
            Assert.Equal(100, doc.VerticalScaleDenominator);
            // 実際 横 40m・縦 2m の線: 縦の縮尺で直すと元の大きさ
            var line = Assert.Single(doc.Entities.OfType<LineEntity>());
            var real = doc.ToRealDelta(line.End - line.Start);
            Assert.Equal(40, real.X, 6);
            Assert.Equal(2, real.Y, 6);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ProjectAndDxf_KeepVerticalScale()
    {
        var doc = Profile();
        doc.Entities.Add(new DimensionEntity(doc.CurrentLayer, new Vec2(0, 0), new Vec2(0, 10), new Vec2(0, 0), Math.PI / 2, 1, 1) { VerticalFactor = 0.1 });
        var ccp = Path.Combine(Path.GetTempPath(), $"civilcad_{Guid.NewGuid():N}.ccp");
        var dxf = Path.Combine(Path.GetTempPath(), $"civilcad_{Guid.NewGuid():N}.dxf");
        try
        {
            ProjectFile.Save(ccp, [new ProjectSheet("縦断図", doc, AlignmentInput.Sample(), false)], 0);
            var loaded = ProjectFile.Load(ccp).Sheets[0].Doc;
            Assert.Equal(100, loaded.VerticalScaleDenominator);
            Assert.Equal(1, ((DimensionEntity)loaded.Entities[0]).Measured, 9);

            DxfIO.Save(doc, dxf);
            var fromDxf = new CadDocument();
            DxfIO.Load(fromDxf, dxf);
            Assert.Equal(100, fromDxf.VerticalScaleDenominator);
            Assert.Equal(1000, fromDxf.ScaleDenominator);
        }
        finally
        {
            File.Delete(ccp);
            File.Delete(dxf);
        }
    }
}
