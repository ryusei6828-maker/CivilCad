using System.IO;
using CivilCad.App.IO;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

public class P21Tests
{
    private static string TestFile(string name) => Path.Combine(AppContext.BaseDirectory, "testdata", "sxf", name);

    private static string Temp(string ext) => Path.Combine(Path.GetTempPath(), "civilcad-test-" + Guid.NewGuid().ToString("N") + ext);

    /// <summary>図形を比べられる形 (種類・レイヤ・色・外接矩形・文字) にして並べる。</summary>
    private static List<string> Signature(CadDocument doc) =>
    [
        .. doc.Entities.Select(e =>
        {
            var b = e.Bounds;
            var extra = e switch
            {
                TextEntity t => $" '{t.Text}' h={t.Height:F4} rot={t.Rotation:F4} {t.HAlign}/{t.VAlign}",
                HatchEntity h => $" solid={h.Solid} loops={h.Loops.Count} patterns={h.Patterns.Count}",
                _ => "",
            };
            return $"{e.GetType().Name} {e.Layer.Name} {e.Color} w={e.LineWeight} [{b.Min} {b.Max}]{extra}";
        }).OrderBy(s => s, StringComparer.Ordinal),
    ];

    [Fact]
    public void P21_ReadsSameDrawingAsSfc()
    {
        // 同じ図面の SFC と P21 (SCADEC の書き方): 読み込んだ図形はほぼ一致する
        var fromSfc = new CadDocument();
        var rs = SfcIO.Load(fromSfc, TestFile("shapes.sfc"));
        var fromP21 = new CadDocument();
        var rp = P21IO.Load(fromP21, TestFile("shapes.p21"));
        Assert.Equal(rs.ScaleDenominator, rp.ScaleDenominator);
        Assert.Equal(rs.Sheet, rp.Sheet);
        Assert.True(fromSfc.Entities.Count > 100);
        Assert.Equal(fromSfc.Layers.Select(l => (l.Name, l.IsVisible)), fromP21.Layers.Select(l => (l.Name, l.IsVisible)));

        // 違いは次のものだけ (P21 は図形が様式を直接参照するので、こちらの方が元の図面どおり):
        // - この SFC は複合曲線の構成要素を自分のレイヤ・色で書いている。CivilCad の SFC の読み込み (SCADEC の
        //   書いたファイルに合わせ、構成要素はレイヤ 0・色 0) では構成要素にならず、塗りつぶしの外形が線として残る
        // - この SFC はユーザー定義の線幅を 11 番から数えている (CivilCad の SFC は 10 番から)
        // - 寸法値・バルーンの文字の一部は、SFC の寸法フィーチャの並びの違いで SFC からは読めない
        var sfc = Signature(fromSfc);
        var p21 = Signature(fromP21);
        var onlySfc = sfc.Except(p21).ToList();
        var onlyP21 = p21.Except(sfc).ToList();
        Assert.Equal(4, onlySfc.Count);
        Assert.Equal(3, onlySfc.Count(s => s.StartsWith("PolylineEntity 図形 #FFFFFF")));
        Assert.Single(onlySfc, s => s.StartsWith("LineEntity 図形 #00A0A0 w=0.18"));
        Assert.Equal(7, onlyP21.Count);
        Assert.Single(onlyP21, s => s.StartsWith("LineEntity 図形 #00A0A0 w=0.3")); // ユーザー定義の線幅 0.3mm
        Assert.Single(onlyP21, s => s.StartsWith("PolylineEntity 図形 #00A0A0 w=")); // 塗りつぶし (ユーザー定義色)
        Assert.Single(onlyP21, s => s.StartsWith("PolylineEntity 図形 #C08040 w=")); // 塗りつぶし (既定義色 brown)
        Assert.Single(onlyP21, s => s.StartsWith("HatchEntity 図形 #00FF00") && s.EndsWith("loops=2 patterns=2")); // 穴のあるハッチング
        Assert.Equal(3, onlyP21.Count(s => s.StartsWith("TextEntity 寸法")));
        Assert.Contains(fromP21.Entities.OfType<TextEntity>(), t => t.Text == "60" && t.Layer.Name == "寸法");
    }

    [Fact]
    public void P2Z_IsZippedP21()
    {
        var a = new CadDocument();
        P21IO.Load(a, TestFile("shapes.p21"));
        var b = new CadDocument();
        P21IO.Load(b, TestFile("shapes.p2z"));
        Assert.Equal(Signature(a), Signature(b));
    }

    [Fact]
    public void SaveP21_ReadsBackLikeSfc()
    {
        var doc = new CadDocument { ScaleDenominator = 500, PlaneZone = 9 };
        var center = doc.GetOrCreateLayer("中心線", new RgbColor(255, 0, 0));
        var text = doc.GetOrCreateLayer("文字", RgbColor.White);
        var hidden = doc.GetOrCreateLayer("非表示", RgbColor.White);
        hidden.IsVisible = false;
        doc.Entities.Add(new LineEntity(center, new Vec2(-34950, 12030), new Vec2(-34800, 12100)) { LineWeight = 0.35 });
        doc.Entities.Add(new CircleEntity(center, new Vec2(-34900, 12080), 12.5) { Color = new RgbColor(10, 20, 200) });
        doc.Entities.Add(new ArcEntity(center, new Vec2(-34870, 12090), 20, 0.3, 2.5));
        doc.Entities.Add(new PolylineEntity(center, [new Vec2(-34950, 12030), new Vec2(-34910, 12035), new Vec2(-34890, 12060)], true));
        doc.Entities.Add(new TextEntity(text, new Vec2(-34940, 12130), "No.1+5.000 測点 ①", 1.75, 0.2) { HAlign = TextHAlign.Center, VAlign = TextVAlign.Middle });
        doc.Entities.Add(new LineEntity(hidden, new Vec2(-34950, 12000), new Vec2(-34900, 12000)));
        doc.Entities.Add(new HatchEntity(center, [[new Vec2(-34950, 12150), new Vec2(-34930, 12150), new Vec2(-34930, 12160)]], [], solid: true));
        doc.Entities.Add(new HatchEntity(center, [[new Vec2(-34900, 12150), new Vec2(-34880, 12150), new Vec2(-34880, 12170), new Vec2(-34900, 12170)]],
            [new HatchPattern(Math.PI / 4, 2, new Vec2(-34900, 12150))]));

        var sfc = Temp(".sfc");
        var p21 = Temp(".p21");
        try
        {
            SfcWriter.Save(doc, sfc);
            P21IO.Save(doc, p21);
            var text21 = File.ReadAllText(p21);
            Assert.StartsWith("ISO-10303-21;", text21);
            Assert.Contains("FILE_DESCRIPTION(('SCADEC level2 AP202_mode')", text21);
            Assert.Contains("DRAWING_SHEET_REVISION(", text21);
            Assert.DoesNotContain("/*SXF", text21);
            Assert.True(text21.All(c => c < 0x80), "P21 の本文は ASCII のみ (日本語は \\X2\\)");

            var fromSfc = new CadDocument();
            SfcIO.Load(fromSfc, sfc);
            var fromP21 = new CadDocument();
            P21IO.Load(fromP21, p21);
            Assert.Equal(Signature(fromSfc), Signature(fromP21));

            // 元の図面とも一致する (位置・文字・非表示のレイヤ)。SFC と同じく用紙の左下が原点になるので、用紙の位置だけずれる
            var origin = SfcWriter.Layout(doc).Origin;
            var t = Assert.Single(fromP21.Entities.OfType<TextEntity>());
            Assert.Equal("No.1+5.000 測点 ①", t.Text);
            Assert.True(t.Position.DistanceTo(new Vec2(-34940, 12130) - origin) < 1e-4);
            Assert.Equal(1.75, t.Height, 4);
            var line = fromP21.Entities.OfType<LineEntity>().First(l => l.Layer.Name == "中心線");
            Assert.True(line.End.DistanceTo(new Vec2(-34800, 12100) - origin) < 1e-4);
            Assert.Equal(0.35, line.LineWeight);
            Assert.False(fromP21.Layers.Single(l => l.Name == "非表示").IsVisible);
            Assert.Equal(new RgbColor(10, 20, 200), Assert.Single(fromP21.Entities.OfType<CircleEntity>()).Color);
            var arc = Assert.Single(fromP21.Entities.OfType<ArcEntity>());
            Assert.Equal(0.3, arc.StartAngle, 6);
            Assert.Equal(2.5, arc.EndAngle, 6);
            // 模様のハッチングはハッチング、穴の無い塗りつぶしは塗りつぶしのポリラインになる (SFC と同じ)
            Assert.Single(fromP21.Entities.OfType<HatchEntity>(), h => !h.Solid && h.Patterns.Count == 1);
            Assert.Single(fromP21.Entities.OfType<PolylineEntity>(), p => p.Filled);
        }
        finally
        {
            File.Delete(sfc);
            File.Delete(p21);
        }
    }

    [Fact]
    public void Load_RejectsOtherFiles()
    {
        var path = Temp(".p21");
        File.WriteAllText(path, "not a step file");
        try
        {
            Assert.Throws<InvalidDataException>(() => P21IO.Load(new CadDocument(), path));
        }
        finally { File.Delete(path); }
    }
}
