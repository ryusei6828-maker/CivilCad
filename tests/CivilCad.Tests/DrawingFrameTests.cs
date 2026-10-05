using System.IO;
using CivilCad.App.Drawing;
using CivilCad.App.Settings;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

public class DrawingFrameTests
{
    private static readonly Vec2 Origin = new(1000, 2000);

    /// <summary>
    /// 「2.平面図.sfc」(A1, 1/500) の表題欄を基準にした比較。
    /// SFC では外枠左下を原点に、1/500 の座標 (mm) で 工事名ラベル = (355500, 27500) など。
    /// </summary>
    [Fact]
    public void A1_1to500_MatchesReferenceSfc()
    {
        var doc = new CadDocument();
        var o = new DrawingFrameOptions { ProjectName = "鹿児島３号針原地区１０工区外改良工事", Client = "九州地方整備局  八代河川国道事務所" };
        var list = DrawingFrame.Build(doc, o, Origin);
        Vec2 Rel(Vec2 p) => p - Origin;

        // 外枠: 801 × 554 mm → 400.5 × 277 m、太線
        var outer = list.OfType<PolylineEntity>().Single(e => e.Layer.Name == DrawingFrame.LayerFrame);
        Assert.True(outer.IsClosed);
        Assert.Equal(new Box2(Vec2.Zero, new Vec2(400.5, 277)), new Box2(Rel(outer.Bounds.Min), Rel(outer.Bounds.Max)));
        Assert.Equal(1.4, outer.LineWeight);

        // 表題欄の外形: (350.5, 0) - (400.5, 30)
        var title = list.OfType<PolylineEntity>().Single(e => e.Layer.Name == DrawingFrame.LayerTitleFrame);
        AssertNear(new Vec2(350.5, 0), Rel(title.Bounds.Min));
        AssertNear(new Vec2(400.5, 30), Rel(title.Bounds.Max));

        // 罫線: 横 5 本 (y = 5,10,15,20,25)、ラベル区切り x = 360.5、縮尺段の区切り x = 375.5 / 385.5
        var lines = list.OfType<LineEntity>().Select(l => (A: Rel(l.Start), B: Rel(l.End))).ToList();
        foreach (var y in new[] { 5.0, 10, 15, 20, 25 })
            Assert.Contains(lines, l => Near(l.A, new Vec2(350.5, y)) && Near(l.B, new Vec2(400.5, y)));
        Assert.Contains(lines, l => Near(l.A, new Vec2(360.5, 0)) && Near(l.B, new Vec2(360.5, 30)));
        Assert.Contains(lines, l => Near(l.A, new Vec2(375.5, 10)) && Near(l.B, new Vec2(375.5, 15)));
        Assert.Contains(lines, l => Near(l.A, new Vec2(385.5, 10)) && Near(l.B, new Vec2(385.5, 15)));

        // 文字: 高さ 3.5mm → 1.75m、中央揃え
        var texts = list.OfType<TextEntity>().ToDictionary(t => t.Text);
        AssertNear(new Vec2(355.5, 27.5), Rel(texts["工 事 名"].Position));
        AssertNear(new Vec2(355.5, 22.5), Rel(texts["図 面 名"].Position));
        AssertNear(new Vec2(355.5, 17.5), Rel(texts["作成年月日"].Position));
        AssertNear(new Vec2(355.5, 7.5), Rel(texts["会 社 名"].Position));
        AssertNear(new Vec2(355.5, 2.5), Rel(texts["事業者名"].Position));
        AssertNear(new Vec2(380.5, 12.5), Rel(texts["図面番号"].Position));
        AssertNear(new Vec2(380.5, 27.5), Rel(texts[o.ProjectName].Position));
        AssertNear(new Vec2(380.5, 2.5), Rel(texts[o.Client].Position));
        Assert.Equal("1:500", texts.Keys.Single(k => k.StartsWith("1:")));
        Assert.All(texts.Values, t => Assert.Equal(1.75, t.Height, 9));
        Assert.All(texts.Values, t => Assert.Equal((TextHAlign.Center, TextVAlign.Middle), (t.HAlign, t.VAlign)));
        Assert.All(list, e => Assert.Equal(DrawingFrame.Tag, e.Tag));
    }

    [Fact]
    public void LongText_IsSqueezedIntoCell()
    {
        var doc = new CadDocument();
        var o = new DrawingFrameOptions { Paper = "A3", ScaleDenominator = 100, ProjectName = new string('工', 40) };
        var t = DrawingFrame.Build(doc, o, Vec2.Zero).OfType<TextEntity>().Single(e => e.Text == o.ProjectName);
        // 値の欄 (80mm - 余裕 4mm) に収まる
        Assert.True(t.ApproxWidth / o.MetersPerPaperMm <= 76 + 1e-9);
        Assert.True(t.WidthFactor < 1);
    }

    [Fact]
    public void WithoutTitleBlock_OnlyOuterFrame()
    {
        var list = DrawingFrame.Build(new CadDocument(), new DrawingFrameOptions { IncludeTitleBlock = false }, Vec2.Zero);
        Assert.Single(list);
    }

    [Fact]
    public void FrameDefaults_AreSavedInSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"civilcad_settings_{Guid.NewGuid():N}.json");
        try
        {
            var s = AppSettings.Load(path);
            s.FrameDefaults = new DrawingFrameOptions { Paper = "A3", ScaleDenominator = 250, Company = "株式会社  大進" };
            s.Save(path);
            var loaded = AppSettings.Load(path).FrameDefaults;
            Assert.Equal(("A3", 250.0, "株式会社  大進"), (loaded.Paper, loaded.ScaleDenominator, loaded.Company));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static bool Near(Vec2 a, Vec2 b) => a.DistanceTo(b) < 1e-6;
    private static void AssertNear(Vec2 expected, Vec2 actual) =>
        Assert.True(Near(expected, actual), $"expected {expected}, actual {actual}");
}
