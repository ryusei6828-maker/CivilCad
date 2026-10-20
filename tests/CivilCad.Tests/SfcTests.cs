using System.IO;
using System.Text;
using CivilCad.App.Alignment;
using CivilCad.App.IO;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

public class SfcTests
{
    /// <summary>
    /// 部分図 PART (1/500 の実寸 mm で描かれた図形) を用紙の (20,20) に 90° 回転・倍率 0.002 で配置した SFC。
    /// 図面縮尺 1/500 → 用紙 1mm = 0.5m に換算されるので、部分図の中身は元の実寸 (mm/1000 = m) に戻る。
    /// </summary>
    private const string Sample = """
        ISO-10303-21;
        HEADER;
        FILE_DESCRIPTION(('SCADEC level2 feature_mode'),'2;1');
        ENDSEC;
        DATA;
        /*SXF
        #10 = layer_feature(\'L1\','1')
        SXF*/
        /*SXF
        #20 = layer_feature(\'HIDE\','0')
        SXF*/
        /*SXF
        #30 = user_defined_colour_feature('10','20','30')
        SXF*/
        /*SXF
        #40 = line_feature('1','2','1','8','0.000000','0.000000','10000.000000','0.000000')
        SXF*/
        /*SXF
        #50 = text_string_feature('1','17','1',\'\X2\5DE54E8B\X0\\','5000.000000','1000.000000','1750.000000','3500.000000','0.000000','0.000000','0.000000','5','1')
        SXF*/
        /*SXF
        #60 = arc_feature('1','1','1','1','0.0','0.0','1000.0','1','90.0',
        '0.0')
        SXF*/
        /*SXF
        #70 = sfig_org_feature(\'PART\','1')
        SXF*/
        /*SXF
        #80 = polyline_feature('0','0','0','0','4','(0,10,10,0)','(0,0,10,10)')
        SXF*/
        /*SXF
        #90 = composite_curve_org_feature('2','1','1','0')
        SXF*/
        /*SXF
        #100 = fill_area_style_colour_feature('1','3','1','0','()')
        SXF*/
        /*SXF
        #110 = drawing_sheet_feature(\'S\','3','1','420','297')
        SXF*/
        /*SXF
        #120 = sfig_locate_feature('0',\'PART\','20.000000','20.000000','90.0','0.002','0.002')
        SXF*/
        /*SXF
        #130 = line_feature('2','1','1','1','0','0','1','1')
        SXF*/
        ENDSEC;
        END-ISO-10303-21;
        """;

    private static CadDocument LoadSample(out SfcIO.LoadResult result)
    {
        var path = Path.Combine(Path.GetTempPath(), $"civilcad_{Guid.NewGuid():N}.sfc");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        File.WriteAllText(path, Sample, Encoding.GetEncoding(932));
        try
        {
            var doc = new CadDocument();
            result = SfcIO.Load(doc, path);
            return doc;
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Sfc_PartDrawing_IsPlacedAtRealSize()
    {
        var doc = LoadSample(out var r);
        Assert.Equal(500, r.ScaleDenominator);
        Assert.Equal(500, doc.ScaleDenominator);
        Assert.Equal(0, r.Skipped);

        // 線: 部分図 (0,0)-(10000,0) mm → 90° 回転して用紙 (20,20)-(20,40) mm → 図面 (10,10)-(10,20) m
        var line = doc.Entities.OfType<LineEntity>().Single(e => e.Layer.Name == "L1");
        AssertNear(new Vec2(10, 10), line.Start);
        AssertNear(new Vec2(10, 20), line.End);
        Assert.Equal(new RgbColor(255, 0, 0), line.Color);
        Assert.Equal(1.4, line.LineWeight);

        // 文字: \X2\ 表記の日本語、ユーザー定義色 17、90° 回転、高さ 1750mm@1/500 → 1.75m
        var text = doc.Entities.OfType<TextEntity>().Single();
        Assert.Equal("工事", text.Text);
        AssertNear(new Vec2(9, 15), text.Position);
        Assert.Equal(1.75, text.Height, 9);
        Assert.Equal(Math.PI / 2, text.Rotation, 9);
        Assert.Equal(3.5, text.FitWidth!.Value, 9);
        Assert.Equal((TextHAlign.Center, TextVAlign.Middle), (text.HAlign, text.VAlign));
        Assert.Equal(new RgbColor(10, 20, 30), text.Color);

        // 円弧: 時計回り 90°→0° = 反時計回り 0°→90°、配置で 90° 回って 90°→180°
        var arc = doc.Entities.OfType<ArcEntity>().Single();
        AssertNear(new Vec2(10, 10), arc.Center);
        Assert.Equal(1, arc.Radius, 9);
        Assert.Equal(Math.PI / 2, GeoMath.NormalizeAngle(arc.StartAngle), 9);
        Assert.Equal(Math.PI, GeoMath.NormalizeAngle(arc.EndAngle), 9);
    }

    [Fact]
    public void Sfc_Sxf3BlockEnd_DoesNotSwallowNextFeature()
    {
        // 属性付きブロックは "SXF3*/" で終わる。次の用紙定義と線が失われないこと
        const string text = """
            ISO-10303-21;
            DATA;
            /*SXF
            #10 = layer_feature(\'L1\','1')
            SXF*/
            /*SXF3
            #20 = drawing_attribute_feature(\' \',\' \',\' \',\' \',\' \',\' \',\' \','0','1','1',\' \',\' \')
            SXF3*/
            /*SXF
            #30 = drawing_sheet_feature(\'(02)02.縦断面図\','0','1','1189','841')
            SXF*/
            /*SXF
            #40 = line_feature('1','1','1','1','10.0','10.0','100.0','10.0')
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
            Assert.Single(doc.Entities.OfType<LineEntity>());
            // 部分図が無いので縮尺 1 (用紙 mm → m は 1/1000)。A0 横 = 1189 × 841 mm
            Assert.Equal(new Box2(Vec2.Zero, new Vec2(1.189, 0.841)), r.Sheet);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static CadDocument LoadText(string text, out SfcIO.LoadResult result)
    {
        var path = Path.Combine(Path.GetTempPath(), $"civilcad_{Guid.NewGuid():N}.sfc");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        File.WriteAllText(path, text, Encoding.GetEncoding(932));
        try
        {
            var doc = new CadDocument();
            result = SfcIO.Load(doc, path);
            return doc;
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Sfc_Sxf31Clothoid_IsRead_TurnsLeftForZero_AndGeodeticPartSetsScale()
    {
        // Ver.3.1 のクロソイドは "SXF3.1*/" で終わる。測地座標系の部分図 (種別 2) を 1/500 で配置
        const string text = """
            ISO-10303-21;
            DATA;
            /*SXF
            #10 = layer_feature(\'L1\','1')
            SXF*/
            /*SXF3.1
            #20 = clothoid_feature('1','2','1','1','0.0','0.0','100000.0','0','0.0','0.0','50000.0')
            SXF3.1*/
            /*SXF3.1
            #30 = clothoid_feature('1','2','1','1','0.0','0.0','100000.0','1','0.0','0.0','50000.0')
            SXF3.1*/
            /*SXF
            #40 = line_feature('1','1','1','1','0.0','0.0','-10000.0','0.0')
            SXF*/
            /*SXF
            #50 = sfig_org_feature(\'ROUTE\','2')
            SXF*/
            /*SXF
            #60 = sfig_locate_feature('0',\'ROUTE\','0.0','0.0','0.0','0.002','0.002')
            SXF*/
            ENDSEC;
            """;
        var doc = LoadText(text, out var r);
        Assert.Equal(500, r.ScaleDenominator);
        Assert.Single(doc.Entities.OfType<LineEntity>()); // クロソイドの後の図形も失われない
        var curves = doc.Entities.OfType<PolylineEntity>().ToList();
        Assert.Equal(2, curves.Count);
        // A = 100m、L = 50m の終点: x = L - L⁵/(40A⁴) ≈ 49.92m、y ≈ L³/(6A²) ≈ 2.08m。回転方向 0 = 左 (y > 0)、1 = 右
        AssertNear(Vec2.Zero, curves[0].Points[0]);
        Assert.Equal(49.922, curves[0].Points[^1].X, 2);
        Assert.Equal(2.08, curves[0].Points[^1].Y, 1);
        Assert.Equal(-2.08, curves[1].Points[^1].Y, 1);
    }

    [Fact]
    public void MainExtents_IgnoresFarStrayEntities()
    {
        var doc = new CadDocument();
        var l = doc.CurrentLayer;
        for (var i = 0; i < 50; i++) doc.Entities.Add(new LineEntity(l, new Vec2(i, 0), new Vec2(i, 10)));
        doc.Entities.Add(new LineEntity(l, new Vec2(90000, 50000), new Vec2(90001, 50000))); // 用紙外のゴミ

        var box = doc.MainExtents(out var excluded);
        Assert.Equal(1, excluded);
        Assert.Equal(49, box.Max.X, 9);
        Assert.Equal(90001, doc.VisibleExtents().Max.X, 9); // 図形自体は残っている

        // 離れた図形が無ければ全部
        doc.Entities.RemoveAt(doc.Entities.Count - 1);
        doc.MainExtents(out excluded);
        Assert.Equal(0, excluded);
    }

    [Fact]
    public void Sfc_FillAndHiddenLayer()
    {
        var doc = LoadSample(out _);

        // 塗りつぶし: 用紙 10mm 四方 → 5m 四方、緑、レイヤ L1
        var fill = doc.Entities.OfType<PolylineEntity>().Single(p => p.Filled);
        Assert.Equal("L1", fill.Layer.Name);
        Assert.Equal(new RgbColor(0, 255, 0), fill.Color);
        Assert.Equal(25, fill.Area, 9);

        // 非表示レイヤはそのまま非表示で取り込む
        Assert.False(doc.FindLayer("HIDE")!.IsVisible);
        Assert.Single(doc.Entities, e => e.Layer.Name == "HIDE");
    }

    [Fact]
    public void Scale_IsSavedInDxf_AndDrivesAnnotationSize()
    {
        var doc = new CadDocument { ScaleDenominator = 1000 };
        var input = AlignmentInput.Sample();
        doc.History.Execute(AlignmentDrawer.CreateDrawCommand(doc, input.Compute(), input));
        // 注記 3.5mm × 1/1000 → 3.5m
        Assert.All(doc.Entities.OfType<TextEntity>(), t => Assert.Equal(3.5, t.Height, 9));

        var path = Path.Combine(Path.GetTempPath(), $"civilcad_{Guid.NewGuid():N}.dxf");
        try
        {
            DxfIO.Save(doc, path);
            var loaded = new CadDocument();
            DxfIO.Load(loaded, path);
            Assert.Equal(1000, loaded.ScaleDenominator);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void AssertNear(Vec2 expected, Vec2 actual) =>
        Assert.True(expected.DistanceTo(actual) < 1e-6, $"expected {expected}, actual {actual}");
}
