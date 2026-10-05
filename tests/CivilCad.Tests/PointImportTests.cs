using System.IO;
using System.Text;
using CivilCad.App.IO;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

public class PointImportTests
{
    [Fact]
    public void Sima_PointsAndLots()
    {
        var f = PointImport.ParseSima(
        [
            "G00,01,テスト工事,",
            "Z00, /* 座標データ */,",
            "A00,",
            "A01,   20,BP                  , -95437.317, -60763.757,,",
            "A01,1,IN1,-45316.086,85770.834,10.084,",
            "A01,2,IN2,-45393.256,85867.528,6.064,",
            "A01,3,IN3,-45393.317,85816.385,27.555,",
            "A01,4,壊れた行,abc,1,",
            "A99,",
            "D00,1,区画1,",
            "B01,1,IN1,",
            "B01,2,IN2,",
            "B01,3,IN3,",
            "D99,",
        ]);
        Assert.Equal(4, f.Points.Count);
        Assert.Equal(1, f.SkippedLines);
        Assert.Equal(new SurveyPoint("20", "BP", -95437.317, -60763.757, null), f.Points[0]);
        Assert.Equal(10.084, f.Points[1].Z);
        var lot = Assert.Single(f.Lots);
        Assert.Equal("区画1", lot.Name);
        Assert.Equal(["1", "2", "3"], lot.PointNumbers);
    }

    [Theory]
    [InlineData("BP,-95437.317,-60763.757", "", "BP", -95437.317, -60763.757, null)]
    [InlineData("1,BP,-95437.317,-60763.757,12.5", "1", "BP", -95437.317, -60763.757, 12.5)]
    [InlineData("\"KT.1\"\t100.5\t200.25\t3", "", "KT.1", 100.5, 200.25, 3.0)]
    [InlineData("No.1 100 200", "", "No.1", 100.0, 200.0, null)]
    [InlineData("P1,100,200,,備考", "", "P1", 100.0, 200.0, null)]
    [InlineData("7,100,200", "7", "", 100.0, 200.0, null)]            // 点番号,X,Y
    [InlineData("100.5,200.5,3.5", "1", "", 100.5, 200.5, 3.5)]       // X,Y,Z (番号は行番号)
    [InlineData("8,100,200,4", "8", "", 100.0, 200.0, 4.0)]          // 点番号,X,Y,Z
    [InlineData("253,1068,NO.21Y190,-162126.118,-43021.965,0.090", "1068", "NO.21Y190", -162126.118, -43021.965, 0.09)] // No,点番,点名,X,Y,Z
    [InlineData("1,801,801,-162118.5,-42883.2,1.5", "801", "", -162118.5, -42883.2, 1.5)] // 点名が数字
    public void Csv_Formats(string line, string number, string name, double x, double y, double? z)
    {
        var p = Assert.Single(PointImport.ParseCsv([line]).Points);
        Assert.Equal(new SurveyPoint(number, name, x, y, z), p);
    }

    [Fact]
    public void Csv_SkipsHeader()
    {
        var f = PointImport.ParseCsv(["点名,X,Y,Z", "A,1,2,3", "", "B,4,5,6"]);
        Assert.Equal(["A", "B"], f.Points.Select(p => p.Name));
        Assert.Equal(1, f.SkippedLines);
    }

    [Fact]
    public void Load_DetectsShiftJisAndUtf8()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var dir = Path.Combine(Path.GetTempPath(), $"civilcad_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var sim = Path.Combine(dir, "a.sim");
            File.WriteAllText(sim, "A01,1,基準点,100,200,5,\r\n", Encoding.GetEncoding(932));
            Assert.Equal("基準点", PointImport.Load(sim).Points[0].Name);
            var csv = Path.Combine(dir, "b.csv");
            File.WriteAllText(csv, "杭１,100,200\n", new UTF8Encoding(true));
            Assert.Equal("杭１", PointImport.Load(csv).Points[0].Name);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Build_PlacesMarkerAndLabelsAtSurveyCoordinates()
    {
        var doc = new CadDocument { ScaleDenominator = 500 };
        var file = new PointFile([new SurveyPoint("1", "BP", 1000, 2000, 12.345), new SurveyPoint("2", "", 1010, 2000, null), new SurveyPoint("3", "C", 1010, 2010, null)],
            [("区画", ["1", "2", "3"])], 0);
        var options = new PointImportOptions { ShowElevation = true };
        var entities = options.Build(doc, file);

        var circles = entities.OfType<CircleEntity>().ToList();
        Assert.Equal(3, circles.Count);
        Assert.Equal(new Vec2(2000, 1000), circles[0].Center);                // x = 東 = Y, y = 北 = X
        Assert.Equal(doc.PaperMmToMeters(1.5) / 2, circles[0].Radius, 9);
        var texts = entities.OfType<TextEntity>().Select(t => t.Text).ToList();
        Assert.Equal(["BP", "12.345", "2", "C"], texts);                     // 点名が無ければ点番号
        Assert.Single(entities.OfType<PolylineEntity>(), p => p.IsClosed && p.Points.Count == 3);
        Assert.All(entities, e => Assert.Equal("座標点", e.Layer.Name));
    }
}
