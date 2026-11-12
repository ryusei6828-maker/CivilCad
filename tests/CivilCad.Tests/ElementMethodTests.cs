using System.IO;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using CivilCad.Alignment;
using CivilCad.App.Alignment;
using CivilCad.App.IO;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

/// <summary>要素法 (直線・円曲線・クロソイドの要素を順につなぐ線形) の計算・LandXML の読み書き・入力の保存。</summary>
public class ElementMethodTests
{
    /// <summary>要素法・IP なし・円曲線 5 つが直接つながる路線の CoordGeom (実例の並び)。</summary>
    private const string CdRampXml = """
        <LandXML xmlns="http://www.landxml.org/schema/LandXML-1.2" version="1.2">
          <Alignments name="test">
            <Alignment name="CDランプ" length="649.696657055766" staStart="-332.161000000000">
              <CoordGeom>
                <Curve rot="cw" radius="10000.000" length="72.797525484693">
                  <Start name="DCBC1-0">-127142.3730000000 -71049.8640000000</Start>
                  <Center>-135209.9667139767 -76958.6652038148</Center>
                  <End name="DCBC2-0">-127185.6010000000 -70991.2910000000</End>
                </Curve>
                <Curve rot="ccw" radius="5000.000" length="87.755171204340">
                  <Start name="DCBC2-0">-127185.6010000000 -70991.2910000000</Start>
                  <Center>-123173.4110750746 -68007.6134024156</Center>
                  <End name="DCEBC2-1">-127237.3470000000 -70920.4170000000</End>
                </Curve>
                <Curve rot="ccw" radius="300.000" length="62.348231277542">
                  <Start name="DCEBC2-1">-127237.3470000000 -70920.4170000000</Start>
                  <Center>-126993.5084622745 -70745.6521078738</Center>
                  <End name="DCBC3-0">-127268.1600000000 -70866.3440000000</End>
                </Curve>
                <Curve rot="cw" radius="800.000" length="164.975400096723">
                  <Start name="DCBC3-0">-127268.1600000000 -70866.3440000000</Start>
                  <Center>-128000.5626660186 -71188.1923102470</Center>
                  <End name="DCBC4-0">-127349.5800000000 -70723.1960000000</End>
                </Curve>
                <Curve rot="ccw" radius="3000.000" length="261.820328992467">
                  <Start name="DCBC4-0">-127349.5800000000 -70723.1960000000</Start>
                  <Center>-124908.4044200966 -68979.4466521626</Center>
                  <End name="DCEP">-127492.2790000000 -70503.7800000000</End>
                </Curve>
              </CoordGeom>
              <Feature name="Horizontal">
                <Property label="method" value="要素法" />
              </Feature>
            </Alignment>
          </Alignments>
        </LandXML>
        """;

    private static readonly (string Name, double X, double Y)[] CdPoints =
    [
        ("DCBC1-0", -127142.373, -71049.864), ("DCBC2-0", -127185.601, -70991.291), ("DCEBC2-1", -127237.347, -70920.417),
        ("DCBC3-0", -127268.160, -70866.344), ("DCBC4-0", -127349.580, -70723.196), ("DCEP", -127492.279, -70503.780),
    ];

    // ---- SpiralElement ----

    [Fact]
    public void Spiral_ZeroStartCurvature_MatchesClothoidElement()
    {
        var anchor = new Vec2(10, 20);
        var dir = 0.7;
        foreach (var turn in new[] { 1, -1 })
        {
            var a = 120.0;
            var len = 150.0;
            var cl = new ClothoidElement(a, len, turn, true, anchor, dir);
            var sp = new SpiralElement(anchor, dir, len, 0, turn * len / (a * a));
            foreach (var s in new[] { 0.0, 10, 55.5, 100, 150 })
            {
                var p = cl.Evaluate(s);
                var q = sp.Evaluate(s);
                Assert.True(p.Point.DistanceTo(q.Point) < 1e-6, $"turn={turn} s={s}: {p.Point} vs {q.Point}");
                Assert.Equal(p.Direction, q.Direction, 9);
            }
            Assert.Equal(a, sp.A, 6);
        }
    }

    [Fact]
    public void Spiral_Egg_MatchesHighResolutionReference()
    {
        // 卵形: 半径 300 → 800 (曲率が小さくなる)、左カーブ
        var k1 = 1 / 300.0;
        var k2 = 1 / 800.0;
        var len = 300.0;
        var start = new Vec2(5, -3);
        var dir = 1.1;
        var sp = new SpiralElement(start, dir, len, k1, k2);
        Assert.Equal(300, sp.RadiusStart, 9);
        Assert.Equal(800, sp.RadiusEnd, 9);
        Assert.Equal(1, sp.Turn);

        // 参照解: 中点則 (200 万分割) の誤差は 1e-10 m 以下
        const int n = 2_000_000;
        double x = 0, y = 0;
        var h = len / n;
        for (var i = 0; i < n; i++)
        {
            var t = (i + 0.5) * h;
            var th = dir + k1 * t + (k2 - k1) * t * t / (2 * len);
            x += Math.Cos(th) * h;
            y += Math.Sin(th) * h;
        }
        var (p, d) = sp.Evaluate(len);
        Assert.True(p.DistanceTo(start + new Vec2(x, y)) < 1e-7, $"{p} vs {start + new Vec2(x, y)}");
        Assert.Equal(dir + k1 * len + (k2 - k1) * len / 2, d, 12);
    }

    // ---- FromElements ----

    [Fact]
    public void FromElements_LineArcLine_Geometry()
    {
        // 東へ 100m → 左へ半径 50m の 1/4 円 → 北へ 100m
        var quarter = Math.PI / 2 * 50;
        var al = HorizontalAlignment.FromElements(Vec2.Zero, 0,
            [ElementDef.Line(100), ElementDef.Arc(quarter, 50, 1), ElementDef.Line(100)], startStation: 20);
        Assert.True(al.IsElementMethod);
        Assert.Empty(al.Curves);
        Assert.Equal(3, al.Elements.Count);
        Assert.Equal(200 + quarter, al.Length, 9);
        var end = al.Evaluate(al.EndStation);
        Assert.True(end.Point.DistanceTo(new Vec2(150, 150)) < 1e-9, end.Point.ToString());
        Assert.Equal(Math.PI / 2, end.Direction, 9);

        Assert.Equal(["BP", "BC1", "EC1", "EP"], al.MainPoints.Select(m => m.Name));
        double[] stations = [20, 120, 120 + quarter, 220 + quarter];
        for (var i = 0; i < stations.Length; i++) Assert.Equal(stations[i], al.MainPoints[i].Station, 9);
        Assert.True(al.MainPoints[1].Point.DistanceTo(new Vec2(100, 0)) < 1e-9);
        Assert.True(al.PointAt(120 + quarter / 2).DistanceTo(new Vec2(100 + 50 * Math.Sin(Math.PI / 4), 50 - 50 * Math.Cos(Math.PI / 4))) < 1e-9);
    }

    [Fact]
    public void FromElements_ClothoidNamesAndEndNames()
    {
        var al = HorizontalAlignment.FromElements(Vec2.Zero, 0,
        [
            ElementDef.Line(50),
            ElementDef.Spiral(60, double.PositiveInfinity, 200, -1),
            ElementDef.Arc(80, 200, -1, "中央"),
            ElementDef.Spiral(60, 200, double.PositiveInfinity, -1),
            ElementDef.Line(40),
        ], 0, "起点");
        Assert.Equal("KA1-1", al.MainPoints[1].Name);
        Assert.Equal("KE1-1", al.MainPoints[2].Name);
        Assert.Equal("中央", al.MainPoints[3].Name);
        Assert.Equal("KA1-2", al.MainPoints[4].Name);
        Assert.Equal("EP", al.MainPoints[^1].Name);
        // 右カーブ: 終点の方向は南寄りに曲がる
        Assert.True(al.Evaluate(al.EndStation).Direction < 0);
    }

    [Fact]
    public void FromElements_ReverseCurves_ReturnToStartDirection()
    {
        var al = HorizontalAlignment.FromElements(Vec2.Zero, 0.3,
            [ElementDef.Arc(50, 100, 1), ElementDef.Arc(50, 100, -1)]);
        Assert.Equal(100, al.Length, 9);
        Assert.Equal(0.3, al.Evaluate(al.EndStation).Direction, 9);
        Assert.Equal(["BP", "CP1-1", "EP"], al.MainPoints.Select(m => m.Name));
        double[] stations = [0, 50, 100];
        for (var i = 0; i < stations.Length; i++) Assert.Equal(stations[i], al.MainPoints[i].Station, 9);
        // 点対称: 終点は中間点について始点と反対側
        var mid = al.MainPoints[1].Point;
        Assert.True(al.MainPoints[2].Point.DistanceTo(mid * 2) < 1e-9);
    }

    [Fact]
    public void FromElements_RejectsBadElements()
    {
        Assert.Throws<AlignmentException>(() => HorizontalAlignment.FromElements(Vec2.Zero, 0, []));
        Assert.Throws<AlignmentException>(() => HorizontalAlignment.FromElements(Vec2.Zero, 0, [ElementDef.Arc(10, 0, 1)]));
        Assert.Throws<AlignmentException>(() => HorizontalAlignment.FromElements(Vec2.Zero, 0, [ElementDef.Arc(10, 50, 0)]));
        Assert.Throws<AlignmentException>(() => HorizontalAlignment.FromElements(Vec2.Zero, 0,
            [ElementDef.Spiral(10, double.PositiveInfinity, double.PositiveInfinity, 1)]));
    }

    // ---- LandXmlReader ----

    [Fact]
    public void Reader_ElementMethodAlignment_BuildsFromElements()
    {
        var read = Assert.Single(LandXmlReader.Parse(XDocument.Parse(CdRampXml)));
        Assert.True(read.IsElementMethod);
        Assert.Empty(read.Ips);
        Assert.Equal(5, read.Elements!.Count);
        Assert.All(read.Elements, e => Assert.Equal(ElementKind.Arc, e.Kind));
        Assert.Equal([-1, 1, 1, -1, 1], read.Elements.Select(e => e.Turn));
        Assert.Equal("DCBC1-0", read.StartName);

        var h = read.ToHorizontal();
        Assert.True(h.IsElementMethod);
        Assert.Equal(-332.161, h.StartStation, 9);
        Assert.Equal(read.Elements.Sum(e => e.Length), h.Length, 9);
        Assert.Equal(649.696657055766, h.Length, 6);

        // 要素をつないだ点がファイルの主要点 (mm 丸め) と数 mm 以内で合う
        Assert.Equal(CdPoints.Select(p => p.Name), h.MainPoints.Select(m => m.Name));
        for (var i = 0; i < CdPoints.Length; i++)
        {
            var expected = Vec2.FromSurvey(CdPoints[i].X, CdPoints[i].Y);
            var err = h.MainPoints[i].Point.DistanceTo(expected);
            Assert.True(err < 0.005, $"{CdPoints[i].Name}: 誤差 {err * 1000:F2} mm");
        }
    }

    [Fact]
    public void Reader_IpMethodAlignment_StillUsesIps()
    {
        var ips = new List<IntersectionPoint>
        {
            new("BP", Vec2.FromSurvey(0, 0)),
            new("IP.1", Vec2.FromSurvey(200, 150), 300, 150, 150),
            new("EP", Vec2.FromSurvey(450, 600)),
        };
        var h = HorizontalAlignment.FromIps(ips);
        var xml = LandXmlWriter.Build("本線", h, ips, 20);
        var read = Assert.Single(LandXmlReader.Parse(xml));
        Assert.False(read.IsElementMethod);
        Assert.True(read.HasIps);
        Assert.False(read.ToHorizontal().IsElementMethod);
        Assert.Equal(h.Length, read.ToHorizontal().Length, 3);
    }

    [Fact]
    public void Reader_EggClothoid_ReadableByElements()
    {
        var al = HorizontalAlignment.FromElements(Vec2.FromSurvey(1000, 2000), 0.5,
        [
            ElementDef.Line(30),
            ElementDef.Spiral(80, double.PositiveInfinity, 300, 1),
            ElementDef.Spiral(120, 300, 800, 1),   // 卵形クロソイド
            ElementDef.Arc(70, 800, 1),
            ElementDef.Spiral(60, 800, double.PositiveInfinity, 1),
            ElementDef.Line(30),
        ]);
        var doc = LandXmlWriter.Build("卵形", al, null, 20);

        // 方法 (Feature) を消しても、IP 法で表せない線形は要素から読める
        foreach (var f in doc.Descendants().Where(e => e.Name.LocalName == "Feature" && (string?)e.Attribute("name") == "Horizontal").ToList()) f.Remove();
        var read = Assert.Single(LandXmlReader.Parse(doc));
        Assert.True(read.IsElementMethod);   // IP が作れないので要素法
        Assert.False(read.FlaggedElementMethod);
        Assert.False(read.HasIps);
        var back = read.ToHorizontal();
        Assert.True(back.IsElementMethod);
        Assert.Equal(al.Length, back.Length, 4);
        Assert.True(back.Evaluate(back.EndStation).Point.DistanceTo(al.Evaluate(al.EndStation).Point) < 1e-3);
        Assert.True(back.PointAt(back.StartStation + 150).DistanceTo(al.PointAt(al.StartStation + 150)) < 1e-3);

        // 卵形のクロソイド要素の半径 (300 → 800) が読める
        var egg = read.Elements!.Single(e => e.Kind == ElementKind.Clothoid && !double.IsInfinity(e.RadiusStart) && !double.IsInfinity(e.RadiusEnd));
        Assert.Equal((300.0, 800.0), (egg.RadiusStart, egg.RadiusEnd));
    }

    [Fact]
    public void SectionReader_ElementMethodAlignment_GetsLengthWithoutAttribute()
    {
        var doc = XDocument.Parse(CdRampXml);
        doc.Descendants().First(e => e.Name.LocalName == "Alignment").Attribute("length")!.Remove();
        var sections = LandXmlSectionReader.Parse(doc);
        var sec = Assert.Single(sections);
        Assert.Equal(649.696657055766, sec.EndStation - sec.StartStation, 4);
    }

    // ---- LandXmlWriter ----

    [Fact]
    public void Writer_ElementAlignment_RoundTrips()
    {
        var al = HorizontalAlignment.FromElements(Vec2.FromSurvey(-127142.373, -71049.864), 1.2,
            [ElementDef.Arc(72.8, 10000, -1, "A"), ElementDef.Arc(87.8, 5000, 1, "B"), ElementDef.Spiral(60, 300, double.PositiveInfinity, 1, "C"), ElementDef.Line(25, "終")],
            -332.161, "始");
        var xml = LandXmlWriter.Build("CDランプ", al, null, 20);
        var ns = (XNamespace)"http://www.landxml.org/schema/LandXML-1.2";
        Assert.Empty(xml.Descendants(ns + "AlignPIs"));
        var feature = xml.Descendants(ns + "Feature").Single(f => (string?)f.Attribute("name") == "Horizontal");
        Assert.Equal("要素法", (string?)feature.Element(ns + "Property")!.Attribute("value"));

        var read = Assert.Single(LandXmlReader.Parse(xml));
        Assert.True(read.IsElementMethod);
        var back = read.ToHorizontal();
        Assert.Equal(al.Length, back.Length, 3);
        Assert.Equal(-332.161, back.StartStation, 3);
        Assert.Equal(al.MainPoints.Select(m => m.Name), back.MainPoints.Select(m => m.Name));
        Assert.True(back.Evaluate(back.EndStation).Point.DistanceTo(al.Evaluate(al.EndStation).Point) < 1e-3);
        Assert.Equal(al.Evaluate(al.EndStation).Direction, back.Evaluate(back.EndStation).Direction, 6);
    }

    // ---- AlignmentInput ----

    private static AlignmentInput ElementInput() => new()
    {
        Name = "CDランプ",
        StartStation = -332.161,
        IsElementMethod = true,
        StartName = "DCBC1-0",
        StartX = -127142.373,
        StartY = -71049.864,
        StartAzimuthDeg = 125.4321,
        ElementRows =
        [
            new() { Kind = ElementRow.KindArc, Length = 72.797525, R1 = 10000, Turn = ElementRow.TurnRight, EndName = "DCBC2-0" },
            new() { Kind = ElementRow.KindClothoid, Length = 60, R1 = 5000, R2 = 800, Turn = ElementRow.TurnLeft },
            new() { Kind = ElementRow.KindLine, Length = 30 },
        ],
    };

    [Fact]
    public void Input_Compute_ElementMethod_UsesSurveyAzimuth()
    {
        // 方向角 90° (真東)・座標は測量座標 → 直線は Y (東) 方向に延びる
        var input = new AlignmentInput
        {
            IsElementMethod = true, StartX = 100, StartY = 200, StartAzimuthDeg = 90,
            ElementRows = [new() { Kind = ElementRow.KindLine, Length = 50 }],
        };
        var al = input.Compute();
        var (x, y) = al.Evaluate(al.EndStation).Point.ToSurvey();
        Assert.Equal(100, x, 9);
        Assert.Equal(250, y, 9);
        // 方向角 0° (真北): X が増える
        input.StartAzimuthDeg = 0;
        var (x2, y2) = input.Compute().Evaluate(50).Point.ToSurvey();
        Assert.Equal((150, 200), (Math.Round(x2, 9), Math.Round(y2, 9)));
    }

    [Fact]
    public void Angles_SurveyAzimuthAndParse()
    {
        foreach (var deg in new[] { 0.0, 33.3, 90, 179.9, 270, 359.5 })
        {
            var az = deg * Math.PI / 180;
            Assert.Equal(az, Angles.ToSurveyAzimuth(Angles.FromSurveyAzimuth(az)), 12);
        }
        Assert.Equal(Math.PI / 2, Angles.FromSurveyAzimuth(0), 12); // 北 = 数学角 90°
        Assert.True(Angles.TryParseDegrees("123.5", out var r1));
        Assert.Equal(123.5, r1 * 180 / Math.PI, 9);
        Assert.True(Angles.TryParseDegrees("123°30'00.0\"", out var r2));
        Assert.Equal(123.5, r2 * 180 / Math.PI, 9);
        Assert.True(Angles.TryParseDegrees("12-30-36", out var r3));
        Assert.Equal(12 + 30 / 60.0 + 36 / 3600.0, r3 * 180 / Math.PI, 9);
        Assert.False(Angles.TryParseDegrees("abc", out _));
        var text = Angles.ToDms(1.2345678, 3);
        Assert.True(Angles.TryParseDegrees(text, out var back));
        Assert.Equal(1.2345678, back, 7);
    }

    [Fact]
    public void Input_RowValidation_ReportsRowNumber()
    {
        var input = ElementInput();
        input.ElementRows[1].Turn = "";
        var ex = Assert.Throws<AlignmentException>(() => input.Compute());
        Assert.Contains("2 行目", ex.Message);
        input.ElementRows[1].Turn = ElementRow.TurnLeft;
        input.ElementRows[0].R1 = 0;
        Assert.Contains("1 行目", Assert.Throws<AlignmentException>(() => input.Compute()).Message);
    }

    [Fact]
    public void Input_ElementRow_ComputesA()
    {
        var row = new ElementRow { Kind = ElementRow.KindClothoid, Length = 100, R1 = 0, R2 = 400, Turn = ElementRow.TurnLeft };
        Assert.Equal(200, row.A, 9); // A² = 100 × 400
        row.R1 = 800;                // 卵形: A² = L / (1/400 − 1/800)
        Assert.Equal(Math.Sqrt(100 / (1 / 400.0 - 1 / 800.0)), row.A, 9);
        Assert.Equal(0, new ElementRow { Kind = ElementRow.KindLine, Length = 10 }.A);
    }

    [Fact]
    public void Input_FromLandXml_ElementMethod_AndClone()
    {
        var read = Assert.Single(LandXmlReader.Parse(XDocument.Parse(CdRampXml)));
        var input = AlignmentInput.FromLandXml(read, 3.5);
        Assert.True(input.IsElementMethod);
        Assert.Empty(input.Rows);
        Assert.Equal(5, input.ElementRows.Count);
        Assert.Equal("DCBC1-0", input.StartName);
        Assert.Equal(-332.161, input.StartStation, 9);

        var al = input.Compute();
        Assert.Equal(649.696657055766, al.Length, 6);
        var (x, y) = al.Evaluate(al.EndStation).Point.ToSurvey();
        Assert.True(Math.Abs(x - -127492.279) < 0.005 && Math.Abs(y - -70503.780) < 0.005, $"{x:F3} {y:F3}");

        var clone = input.Clone();
        Assert.True(clone.IsElementMethod);
        Assert.Equal(input.StartAzimuthDeg, clone.StartAzimuthDeg);
        Assert.NotSame(input.ElementRows[0], clone.ElementRows[0]);
        Assert.Equal(al.Length, clone.Compute().Length, 9);
    }

    [Fact]
    public void Input_IpToElements_KeepsGeometryAndNames()
    {
        var input = AlignmentInput.Sample();
        var ip = input.Compute();
        var el = input.Clone();
        el.SetElementsFrom(ip);
        Assert.True(el.IsElementMethod);
        var al = el.Compute();
        Assert.True(al.IsElementMethod);
        Assert.Equal(ip.Length, al.Length, 6);
        Assert.True(al.Evaluate(al.EndStation).Point.DistanceTo(ip.Evaluate(ip.EndStation).Point) < 1e-6);
        Assert.Equal(ip.MainPoints.Select(m => m.Name), al.MainPoints.Select(m => m.Name));
        Assert.True(al.PointAt(al.StartStation + ip.Length * 0.4).DistanceTo(ip.PointAt(ip.StartStation + ip.Length * 0.4)) < 1e-6);
    }

    [Fact]
    public void Input_ProjectFileRoundTrip_ElementMethod_AndOldFileLoadsAsIp()
    {
        var path = Path.Combine(Path.GetTempPath(), $"civilcad_el_{Guid.NewGuid():N}.ccp");
        try
        {
            var input = ElementInput();
            var ipInput = AlignmentInput.Sample();
            ProjectFile.Save(path, [new ProjectSheet("要素", new CadDocument(), input, true), new ProjectSheet("IP", new CadDocument(), ipInput, true)], 0);
            var loaded = ProjectFile.Load(path);
            var a = loaded.Sheets[0].Alignment;
            Assert.True(a.IsElementMethod);
            Assert.Equal((input.StartName, input.StartX, input.StartY, input.StartAzimuthDeg), (a.StartName, a.StartX, a.StartY, a.StartAzimuthDeg));
            Assert.Equal(input.ElementRows.Count, a.ElementRows.Count);
            Assert.Equal(input.ElementRows.Select(r => (r.Kind, r.Length, r.R1, r.R2, r.Turn, r.EndName)),
                a.ElementRows.Select(r => (r.Kind, r.Length, r.R1, r.R2, r.Turn, r.EndName)));
            Assert.Equal(input.Compute().Length, a.Compute().Length, 9);
            Assert.False(loaded.Sheets[1].Alignment.IsElementMethod);

            // 新しい項目が無い (昔の) ファイルは IP 法として読む
            var json = JsonNode.Parse(File.ReadAllText(path))!;
            var removed = 0;
            void Strip(JsonNode? node)
            {
                switch (node)
                {
                    case JsonObject o:
                        foreach (var key in new[] { "IsElementMethod", "StartName", "StartX", "StartY", "StartAzimuthDeg", "ElementRows" })
                            if (o.Remove(key)) removed++;
                        foreach (var kv in o.ToList()) Strip(kv.Value);
                        break;
                    case JsonArray arr:
                        foreach (var n in arr) Strip(n);
                        break;
                }
            }
            Strip(json);
            Assert.True(removed >= 6, "保存されたファイルに要素法の項目が入っているはず");
            File.WriteAllText(path, json.ToJsonString());
            var old = ProjectFile.Load(path);
            Assert.All(old.Sheets, s => Assert.False(s.Alignment.IsElementMethod));
            Assert.Equal(ipInput.Rows.Count, old.Sheets[1].Alignment.Rows.Count);
            Assert.Equal(ipInput.Compute().Length, old.Sheets[1].Alignment.Compute().Length, 9);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Drawer_ElementMethod_HasNoIpEntities()
    {
        var input = ElementInput();
        input.Pitch = 20;
        var al = input.Compute();
        var doc = new CadDocument();
        doc.History.Execute(AlignmentDrawer.CreateDrawCommand(doc, al, input));
        Assert.DoesNotContain(doc.Entities, e => e.Layer.Name == AlignmentDrawer.LayerIp);
        Assert.Contains(doc.Entities, e => e.Layer.Name == AlignmentDrawer.LayerCenter);
        Assert.Contains(doc.Entities.OfType<TextEntity>(), t => t.Text.StartsWith("DCBC2-0"));
        // 通過点 (主要点・No.杭) も IP 法と同じように出る
        Assert.Contains(input.PassingPoints(al), p => p.Kind == AlignmentInput.KindMain && p.Name == "DCBC2-0");
        Assert.Contains(input.PassingPoints(al), p => p.Kind == AlignmentInput.KindCenter);
    }

    // ---- 方法の判定 (IP 法が再現できれば IP 法、できなければ要素法) ----

    [Fact]
    public void Decision_FlaggedFile_WhoseIpsReproduceIt_UsesIpMethod()
    {
        var ips = new List<IntersectionPoint>
        {
            new("BP", Vec2.FromSurvey(0, 0)),
            new("IP.1", Vec2.FromSurvey(200, 150), 300, 150, 150),
            new("EP", Vec2.FromSurvey(450, 600)),
        };
        var h = HorizontalAlignment.FromIps(ips);
        var doc = LandXmlWriter.Build("本線", h, ips, 20);
        // 方法の印だけ「要素法」にする
        doc.Descendants().First(e => e.Name.LocalName == "Alignment").Add(new XElement(doc.Root!.Name.Namespace + "Feature", new XAttribute("name", "Horizontal"),
            new XElement(doc.Root.Name.Namespace + "Property", new XAttribute("label", "method"), new XAttribute("value", "要素法"))));
        var read = Assert.Single(LandXmlReader.Parse(doc));
        Assert.True(read.FlaggedElementMethod);
        Assert.False(read.IsElementMethod);
        Assert.False(read.UseElementMethod);
        Assert.True(read.HasIps);
        Assert.False(read.ToHorizontal().IsElementMethod);
        var input = AlignmentInput.FromLandXml(read, 3.5);
        Assert.False(input.IsElementMethod);
        Assert.Equal(3, input.Rows.Count);
    }

    [Fact]
    public void Decision_FlaggedFile_WhoseIpsFail_UsesElementMethod()
    {
        var read = Assert.Single(LandXmlReader.Parse(XDocument.Parse(CdRampXml)));
        Assert.True(read.FlaggedElementMethod);
        Assert.True(read.IsElementMethod);
        Assert.False(read.HasIps);
        Assert.True(AlignmentInput.FromLandXml(read, 3.5).IsElementMethod);

        // 要素法の印が無くても、IP 法が点を再現できなければ要素法 (円曲線どうしが直接つながる線形)
        var doc = XDocument.Parse(CdRampXml);
        foreach (var f in doc.Descendants().Where(e => e.Name.LocalName == "Feature").ToList()) f.Remove();
        var plain = Assert.Single(LandXmlReader.Parse(doc));
        Assert.False(plain.FlaggedElementMethod);
        Assert.True(plain.IsElementMethod);
    }

    // ---- 路線計算ウィンドウ (表示はしない) ----

    [Fact]
    public void AlignmentWindow_ElementMethod_CreatesAndComputes()
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                var win = new AlignmentWindow(ElementInput());
                win.Measure(new System.Windows.Size(1000, 720)); // 表示せずに、画面の定義 (XAML) が読めて部品が揃うことを確かめる
                Assert.NotNull(win.FindName("ElementGrid"));
                Assert.NotNull(win.FindName("MethodBox"));
                Assert.Equal(System.Windows.Visibility.Visible, ((System.Windows.FrameworkElement)win.FindName("ElementBox")).Visibility);
                Assert.Equal(System.Windows.Visibility.Collapsed, ((System.Windows.FrameworkElement)win.FindName("IpBox")).Visibility);

                var ip = new AlignmentWindow(AlignmentInput.Sample());
                Assert.Equal(System.Windows.Visibility.Visible, ((System.Windows.FrameworkElement)ip.FindName("IpBox")).Visibility);
            }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}
