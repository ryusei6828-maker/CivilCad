using System.IO;
using System.Xml.Linq;
using CivilCad.Alignment;
using CivilCad.App.Sections;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

public class SectionDrawingTests
{
    /// <summary>
    /// CivilCadPoint (点群) の書き出しと同じ形の LandXML: 平面線形 + 現況地盤の縦断 (ProfSurf) + 計画縦断 (ProfAlign) + 横断 (CrossSectSurf)、
    /// それに CivilCad3D の書き出しと同じ計画横断 (DesignCrossSectSurf) を 1 測点に足したもの。
    /// </summary>
    private const string Xml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <LandXML xmlns="http://www.landxml.org/schema/LandXML-1.2" version="1.2">
          <Alignments name="本線">
            <Alignment name="本線" length="100.0000" staStart="0.0000">
              <CoordGeom>
                <Line staStart="0" length="100"><Start>-127500 -71000</Start><End>-127500 -70900</End></Line>
              </CoordGeom>
              <Profile name="本線">
                <ProfAlign name="計画縦断"><PVI>0 21</PVI><PVI>100 23</PVI></ProfAlign>
                <ProfSurf name="現況地盤(点群)"><PntList2D>0 20.0 1 20.1 2 20.2 3 20.3 50 25.0 51 25.1 52 25.2 98 22.0 99 22.1 100 22.2</PntList2D></ProfSurf>
              </Profile>
              <CrossSects name="本線">
                <CrossSect sta="0.0000" name="No.0 (BP)">
                  <CrossSectSurf name="現況地盤(点群)"><PntList2D>-10 21 -5 20.5 0 20 5 19.5 10 19</PntList2D></CrossSectSurf>
                </CrossSect>
                <CrossSect sta="20.0000" name="No.1">
                  <CrossSectSurf name="現況地盤(点群)"><PntList2D>-10 22 0 21 10 20</PntList2D></CrossSectSurf>
                  <DesignCrossSectSurf name="標準横断" closedArea="false">
                    <CrossSectPnt code="法尻">-8 20.5</CrossSectPnt>
                    <CrossSectPnt code="路肩">-4 21.4</CrossSectPnt>
                    <CrossSectPnt code="CL">0 21.4</CrossSectPnt>
                    <CrossSectPnt code="路肩">4 21.4</CrossSectPnt>
                  </DesignCrossSectSurf>
                </CrossSect>
                <CrossSect sta="40.0000">
                  <CrossSectSurf name="現況地盤(点群)"><PntList2D>-10 23 10 22</PntList2D></CrossSectSurf>
                </CrossSect>
              </CrossSects>
              <Feature name="Interval"><Property label="main" value="20.0000"/></Feature>
            </Alignment>
          </Alignments>
        </LandXML>
        """;

    private static RouteSections Read() => Assert.Single(LandXmlSectionReader.Parse(XDocument.Parse(Xml)));

    [Fact]
    public void Reader_ReadsProfilesAndSections()
    {
        var r = Read();
        Assert.Equal("本線", r.Name);
        Assert.Equal((0, 100, 20), (r.StartStation, r.EndStation, r.Pitch));
        var g = Assert.Single(r.GroundProfiles);
        Assert.Equal("現況地盤(点群)", g.Name);
        Assert.Equal(10, g.Points.Count);
        Assert.Equal(22, r.Design!.ElevationAt(50), 9);
        Assert.Equal("計画縦断", r.DesignName);

        Assert.Equal([0.0, 20, 40], r.Sections.Select(s => s.Station).ToArray());
        Assert.Equal("No.0 (BP)", r.Sections[0].Name);
        Assert.Null(r.Sections[2].Name);
        var no1 = r.Sections[1];
        Assert.Equal(2, no1.Lines.Count);
        var design = Assert.Single(no1.Lines, l => l.Design);
        Assert.Equal("標準横断", design.Name);
        Assert.Equal(["法尻", "路肩", "CL", "路肩"], design.Points.Select(p => p.Code ?? "").ToArray());
        Assert.Equal(new SectionPoint(-8, 20.5, "法尻"), design.Points[0]);
    }

    [Fact]
    public void Reader_LengthFromGeometryWhenMissing()
    {
        var r = Assert.Single(LandXmlSectionReader.Parse(XDocument.Parse(Xml.Replace("length=\"100.0000\" ", ""))));
        Assert.Equal(100, r.EndStation, 6);
    }

    [Fact]
    public void Reader_AlignmentWithoutSectionsIsEmpty()
    {
        var xml = XDocument.Parse("""
            <LandXML xmlns="http://www.landxml.org/schema/LandXML-1.2"><Alignments><Alignment name="A" length="10" staStart="0">
              <CoordGeom><Line><Start>0 0</Start><End>0 10</End></Line></CoordGeom></Alignment></Alignments></LandXML>
            """);
        Assert.True(Assert.Single(LandXmlSectionReader.Parse(xml)).IsEmpty);
    }

    [Fact]
    public void Profile_DrawsGroundDesignAndTable()
    {
        var r = Read();
        var doc = new CadDocument();
        var scales = new SectionScales(1000, 100);
        SectionDrawing.DrawProfile(doc, r, scales);
        Assert.Equal(1000, doc.ScaleDenominator);
        foreach (var name in new[] { "現況地盤", "計画縦断", "枠", "文字", "補助線" }) Assert.NotNull(doc.FindLayer(name));

        // 地盤線は点のあき (4〜49m) で 3 本に分かれる
        var ground = doc.Entities.OfType<PolylineEntity>().Where(p => p.Layer.Name == "現況地盤").ToList();
        Assert.Equal(3, ground.Count);
        // 横 = 追加距離、縦 = (標高 − DL) × 10。DL = floor(最低 20 − 2) = 18
        Assert.Equal(new Vec2(0, (20 - 18) * 10), ground[0].Points[0]);
        Assert.Equal(new Vec2(51, (25.1 - 18) * 10), ground[1].Points[1]);

        var texts = doc.Entities.OfType<TextEntity>().Select(t => t.Text).ToList();
        Assert.Contains("No.0 (BP)", texts);
        Assert.Contains("No.1", texts);
        Assert.Contains("No.2", texts); // 名前の無い測点は No. の表記
        Assert.Contains("DL=18.000", texts);
        Assert.Contains("計画高", texts);
        // 文字は用紙の 2.5mm = 1:1000 で 2.5m
        Assert.Contains(doc.Entities.OfType<TextEntity>(), t => t.Text == "DL=18.000" && Math.Abs(t.Height - 2.5) < 1e-9);
        // No.1 (追加距離 20) の切盛: 地盤は拾っていない (あき) ので "-"、No.0 は 地盤 20 / 計画 21 → 盛土 1
        Assert.Contains("F 1.000", texts);
    }

    [Fact]
    public void Sections_DrawGroundDesignAndLabels()
    {
        var r = Read();
        var doc = new CadDocument();
        var n = SectionDrawing.DrawSections(doc, r, new SectionScales(Section: 100, Columns: 2));
        Assert.Equal(3, n);
        Assert.Equal(100, doc.ScaleDenominator);
        Assert.Equal(3, doc.Entities.OfType<PolylineEntity>().Count(p => p.Layer.Name == "現況地盤"));
        var design = Assert.Single(doc.Entities.OfType<PolylineEntity>(), p => p.Layer.Name == "計画");
        Assert.Equal(4, design.Points.Count);
        // 中心線は一点鎖線
        Assert.All(doc.Entities.OfType<LineEntity>().Where(l => l.Layer.Name == "中心線"), l => Assert.Equal(LineType.DashDot, l.LineType));
        var texts = doc.Entities.OfType<TextEntity>().Select(t => t.Text).ToList();
        Assert.Contains(texts, t => t.StartsWith("No.0 (BP)   GH=20.000"));
        Assert.Contains(texts, t => t.StartsWith("No.2"));
        Assert.Contains("FH=21.400", texts); // No.1 の計画高 (21 + 2% × 20)
        // 距離・地盤高の表は描かない。DL は 5m 刻みに切り下げる (最低 19 → 15、22 → 20)
        Assert.DoesNotContain("距離", texts);
        Assert.DoesNotContain("地盤高", texts);
        Assert.Equal(["DL=15.000", "DL=15.000", "DL=20.000"], texts.Where(t => t.StartsWith("DL=")).ToArray());
        // 2 列に並ぶ: 3 つ目の横断は 2 列目 (x が大きい)
        var cl = doc.Entities.OfType<LineEntity>().Where(l => l.Layer.Name == "中心線").OrderBy(l => l.Start.X).ToList();
        Assert.True(cl[2].Start.X > cl[0].Start.X + 20);
    }

    [Theory]
    [InlineData(5, 19.0, 15)]
    [InlineData(5, 21.0, 20)]   // ちょうど 1m 下が刻みの倍数
    [InlineData(5, 20.5, 15)]
    [InlineData(1, 19.4, 18)]
    [InlineData(10, 32.0, 30)]
    public void SectionDatum_RoundsDownToStep(double step, double min, double want) =>
        Assert.Equal(want, new SectionScales(DatumStep: step).SectionDatum(min));

    [Fact]
    public void Reader_FileRoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"civilcad_sections_{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(path, Xml);
            var r = Assert.Single(LandXmlSectionReader.Read(path));
            Assert.Equal(path, r.Source);
        }
        finally { File.Delete(path); }
    }
}
