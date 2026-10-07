using System.IO;
using CivilCad.App.IO;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

public class JwwTests
{
    private static string TestFile(string name) => Path.Combine(AppContext.BaseDirectory, "testdata", "jww", name);

    private static string Temp(string ext) => Path.Combine(Path.GetTempPath(), "civilcad-test-" + Guid.NewGuid().ToString("N") + ext);

    private static int Count<T>(CadDocument doc) => doc.Entities.OfType<T>().Count();

    [Fact]
    public void Load_Version700_AllFigureKinds()
    {
        var doc = new CadDocument();
        var r = JwwIO.Load(doc, TestFile("shapes.jww"));
        Assert.Equal(700, r.Version);
        Assert.Equal(50, doc.ScaleDenominator);
        // 線 32 + 寸法 (寸法線・補助線 2) 3 + ブロック MARK ×4 の線 8
        Assert.Equal(43, Count<LineEntity>(doc));
        // 円 1 + ブロックの円 4。実点 3 は点 (仮点は読まない)
        Assert.Equal(5, Count<CircleEntity>(doc));
        Assert.Equal(3, Count<PointEntity>(doc));
        Assert.Equal(1, Count<ArcEntity>(doc));
        Assert.Equal(2, Count<PolylineEntity>(doc)); // 楕円と楕円弧
        Assert.Equal(14, Count<TextEntity>(doc));
        Assert.Equal(5, Count<HatchEntity>(doc)); // ソリッド 2 + 円のソリッド 3
        Assert.Contains("縦書き文字 (横書きで表示)", r.SkippedKinds.Keys);

        // 図寸 mm × 1/50 → 実寸 m: 用紙の枠 (-200,-140)-(200,-140) は (-10,-7)-(10,-7)
        Assert.Contains(doc.Entities.OfType<LineEntity>(), l => l.Start.DistanceTo(new Vec2(-10, -7)) < 1e-9 && l.End.DistanceTo(new Vec2(10, -7)) < 1e-9);
        Assert.True(r.Sheet is { } sh && sh.Min.DistanceTo(new Vec2(-10.5, -7.425)) < 1e-9 && sh.Max.DistanceTo(new Vec2(10.5, 7.425)) < 1e-9);

        // 文字: 大きさ (図寸 5mm → 0.25m)、角度
        var size = Assert.Single(doc.Entities.OfType<TextEntity>(), t => t.Text == "文字のサイズ 5mm");
        Assert.Equal(0.25, size.Height, 9);
        var turned = Assert.Single(doc.Entities.OfType<TextEntity>(), t => t.Text == "カイテン 30°");
        Assert.Equal(30 * Math.PI / 180, turned.Rotation, 9);

        // レイヤ: 非表示のレイヤグループ 5 とレイヤ 0-3
        Assert.False(doc.Entities.OfType<TextEntity>().Single(t => t.Text == "HIDDEN").Layer.IsVisible);
        Assert.Contains(doc.Layers, l => l.Name.StartsWith("0-3") && !l.IsVisible);
        Assert.Contains(doc.Layers, l => l.Name.StartsWith("0-0") && l.IsVisible);

        // 線幅は 1/100 mm 単位 (線 0.5mm と 1mm)
        Assert.Contains(doc.Entities.OfType<LineEntity>(), l => l.LineWeight == 1.0);

        // 任意色のソリッド
        Assert.Contains(doc.Entities.OfType<HatchEntity>(), h => h.Color == new RgbColor(255, 160, 0));

        // 回転・拡大したブロック: MARK の円 (半径 8) は 1.5 倍 → 図寸 12mm × 縮尺 50 = 0.6m
        Assert.Contains(doc.Entities.OfType<CircleEntity>(), c => Math.Abs(c.Radius - 0.6) < 1e-9);
    }

    [Fact]
    public void Load_Version300_FourByteBlockTime()
    {
        var doc = new CadDocument();
        var r = JwwIO.Load(doc, TestFile("old.jww"));
        Assert.Equal(300, r.Version);
        Assert.Equal(0, r.Skipped);
        Assert.Equal(5 + 4, Count<LineEntity>(doc)); // 枠 4 + 線 1 + ブロック BOX の 4 本
        Assert.Equal(1, Count<CircleEntity>(doc));
        Assert.Equal("キュウ バージョン", Assert.Single(doc.Entities.OfType<TextEntity>()).Text);
    }

    [Fact]
    public void Load_RejectsOtherFiles()
    {
        var path = Temp(".jww");
        File.WriteAllText(path, "not a jww file");
        try
        {
            Assert.Throws<InvalidDataException>(() => JwwIO.Load(new CadDocument(), path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SaveAndLoad_RoundTrip()
    {
        var doc = new CadDocument { ScaleDenominator = 500 };
        var center = doc.GetOrCreateLayer("中心線", new RgbColor(255, 0, 0));
        var text = doc.GetOrCreateLayer("2-5 文字", RgbColor.White);
        var hidden = doc.GetOrCreateLayer("非表示", RgbColor.White);
        hidden.IsVisible = false;
        doc.Entities.Add(new LineEntity(center, new Vec2(-34950, 12030), new Vec2(-34800, 12100)) { LineWeight = 0.35, Color = new RgbColor(255, 40, 40) });
        doc.Entities.Add(new CircleEntity(center, new Vec2(-34900, 12080), 12.5) { Color = new RgbColor(10, 20, 200) });
        doc.Entities.Add(new ArcEntity(center, new Vec2(-34870, 12090), 20, 0.3, 2.5));
        doc.Entities.Add(new PolylineEntity(center, [new Vec2(-34950, 12030), new Vec2(-34910, 12035), new Vec2(-34890, 12060)], true));
        doc.Entities.Add(new TextEntity(text, new Vec2(-34940, 12130), "No.1+5.000 測点", 1.75, 0.2));
        doc.Entities.Add(new LineEntity(hidden, new Vec2(-34950, 12000), new Vec2(-34900, 12000)));
        doc.Entities.Add(new HatchEntity(center, [[new Vec2(-34950, 12150), new Vec2(-34930, 12150), new Vec2(-34930, 12160), new Vec2(-34940, 12155), new Vec2(-34950, 12160)]], [], solid: true));
        doc.Entities.Add(new PointEntity(center, new Vec2(-34920, 12140)));
        doc.Entities.Add(new CircleEntity(center, new Vec2(-34925, 12140), 0.0625) { Tag = JwwIO.PointTag }); // 以前の版の点の印も実点で書く

        var path = Temp(".jww");
        try
        {
            JwwIO.Save(doc, path);
            var loaded = new CadDocument();
            var r = JwwIO.Load(loaded, path);
            Assert.Equal(JwwIO.SaveVersion, r.Version);
            Assert.Equal(0, r.Skipped);
            Assert.Equal(500, loaded.ScaleDenominator);

            // 座標は図寸 (用紙の中心が原点) を経ても元の実寸に戻る
            var line = loaded.Entities.OfType<LineEntity>().First(l => l.Start.DistanceTo(new Vec2(-34950, 12030)) < 1e-6);
            Assert.True(line.End.DistanceTo(new Vec2(-34800, 12100)) < 1e-6);
            Assert.Equal(0.35, line.LineWeight);
            Assert.Equal(new RgbColor(255, 40, 40), line.Color); // 線色 8 (赤)
            var circle = Assert.Single(loaded.Entities.OfType<CircleEntity>(), c => c.Tag == null);
            Assert.Equal(12.5, circle.Radius, 6);
            Assert.Equal(new RgbColor(10, 20, 200), circle.Color); // SXF 対応拡張線色
            var arc = Assert.Single(loaded.Entities.OfType<ArcEntity>());
            Assert.Equal(0.3, arc.StartAngle, 9);
            Assert.Equal(2.5, arc.EndAngle, 9);
            Assert.Equal(2, loaded.Entities.OfType<PointEntity>().Count());
            Assert.Contains(loaded.Entities.OfType<PointEntity>(), p => p.Position.DistanceTo(new Vec2(-34920, 12140)) < 1e-6);
            Assert.DoesNotContain(loaded.Entities.OfType<CircleEntity>(), c => c.Tag == JwwIO.PointTag);

            // ポリラインは線分 (閉じているので 3 本)
            Assert.Equal(1 + 3 + 1, loaded.Entities.OfType<LineEntity>().Count());

            var t = Assert.Single(loaded.Entities.OfType<TextEntity>());
            Assert.Equal("No.1+5.000 測点", t.Text);
            Assert.Equal(1.75, t.Height, 6);
            Assert.Equal(0.2, t.Rotation, 9);
            Assert.True(t.Position.DistanceTo(new Vec2(-34940, 12130)) < 1e-6);
            // 「g-l 名前」のレイヤはそのグループ・レイヤに、ほかは空いているところに入る
            Assert.Equal("2-5 文字", t.Layer.Name);
            Assert.Contains(loaded.Layers, l => l.Name == "0-1 中心線");
            Assert.Contains(loaded.Layers, l => l.Name == "0-2 非表示" && !l.IsVisible);

            // 塗りつぶしは三角形のソリッドに分かれる (面積は同じ)
            var solids = loaded.Entities.OfType<HatchEntity>().ToList();
            Assert.Equal(3, solids.Count);
            Assert.Equal(20 * 10 - 20 * 5 / 2.0, solids.Sum(h => h.Area), 6); // 長方形から上辺の三角の切り欠きを引いた面積
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Triangulate_ConcavePolygon()
    {
        // L 字 (凹) の 6 角形 → 4 つの三角形、面積 3
        var tris = JwwIO.Triangulate([new(0, 0), new(2, 0), new(2, 1), new(1, 1), new(1, 2), new(0, 2)]);
        Assert.Equal(4, tris.Count);
        Assert.Equal(3.0, tris.Sum(t => Math.Abs((t.Item2 - t.Item1).Cross(t.Item3 - t.Item1)) / 2), 9);
    }
}
