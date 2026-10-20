using System.IO;
using System.Xml.Linq;
using CivilCad.Alignment;
using CivilCad.App.Alignment;
using CivilCad.Core.Geometry;

namespace CivilCad.Tests;

public class LandXmlWriterTests
{
    [Fact]
    public void Write_ThenRead_RestoresIpTable_StationAndPitch()
    {
        var input = AlignmentInput.Sample();
        input.Name = "本線";
        input.StartStation = 120;
        input.Pitch = 20;
        var al = input.Compute();
        var ips = input.Rows.Select(r => new IntersectionPoint(r.Name, Vec2.FromSurvey(r.X, r.Y), r.R, r.A1, r.A2)).ToList();
        var xml = LandXmlWriter.Build(input.Name, al, ips, input.Pitch, planeZone: 2);

        // 要素は直線・円曲線・クロソイド。始点・終点に主要点の名前
        var ns = (XNamespace)"http://www.landxml.org/schema/LandXML-1.2";
        var geom = xml.Descendants(ns + "CoordGeom").Single().Elements().ToList();
        Assert.Contains(geom, e => e.Name.LocalName == "Spiral");
        Assert.Contains(geom, e => e.Name.LocalName == "Curve");
        Assert.Equal("BP", (string?)geom[0].Element(ns + "Start")!.Attribute("name"));
        Assert.Equal("2(X,Y)", (string?)xml.Descendants(ns + "CoordinateSystem").Single().Attribute("horizontalCoordinateSystemName"));

        // 読み戻すと IP 表 (名前・座標・R・A)、開始追加距離、測点間隔が元に戻る
        var back = Assert.Single(LandXmlReader.Parse(xml));
        Assert.Equal("本線", back.Name);
        Assert.Equal(120, back.StartStation, 6);
        Assert.Equal(20, back.Pitch);
        Assert.Equal(al.Length, back.FileLength, 3);
        Assert.Equal(ips.Count, back.Ips.Count);
        for (var i = 0; i < ips.Count; i++)
        {
            Assert.Equal(ips[i].Name, back.Ips[i].Name);
            Assert.True(ips[i].Point.DistanceTo(back.Ips[i].Point) < 1e-3, $"{ips[i].Name} の座標");
            Assert.Equal(ips[i].Radius, back.Ips[i].Radius, 3);
            Assert.Equal(ips[i].A1, back.Ips[i].A1, 3);
            Assert.Equal(ips[i].A2, back.Ips[i].A2, 3);
        }
    }

    [Fact]
    public void Export_File_ReadsBackAsSameAlignment()
    {
        var input = AlignmentInput.Sample();
        var path = Path.Combine(Path.GetTempPath(), $"civilcad_{Guid.NewGuid():N}.xml");
        try
        {
            LandXmlExport.Write(path, input, input.Compute(), planeZone: null);
            var back = AlignmentInput.FromLandXml(Assert.Single(LandXmlReader.Read(path)), input.TextHeightMm);
            var a = input.Compute();
            var b = back.Compute();
            Assert.True(Math.Abs(a.Length - b.Length) < 1e-3); // 座標は 0.1mm 単位で書くので、読み戻しの差は 1mm 未満
            foreach (var st in new[] { 0.0, 100, 250, a.Length })
                Assert.True(a.PointAt(st).DistanceTo(b.PointAt(st)) < 1e-3, $"追加距離 {st}");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
