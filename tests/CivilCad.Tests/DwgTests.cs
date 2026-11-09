using System.IO;
using CivilCad.App.IO;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

public class DwgTests
{
    [Fact]
    public void SaveAndLoad_KeepsShapesLayersAndJapaneseText()
    {
        var doc = new CadDocument { ScaleDenominator = 500, PlaneZone = 9 };
        var center = doc.GetOrCreateLayer("中心線", new RgbColor(255, 0, 0));
        var text = doc.GetOrCreateLayer("文字", new RgbColor(0, 255, 0));
        doc.Entities.Add(new LineEntity(center, new Vec2(-34950, 12030), new Vec2(-34800, 12100)) { LineWeight = 0.35 });
        doc.Entities.Add(new CircleEntity(center, new Vec2(-34900, 12080), 12.5));
        doc.Entities.Add(new ArcEntity(center, new Vec2(-34870, 12090), 20, 0.3, 2.5));
        doc.Entities.Add(new PolylineEntity(center, [new Vec2(-34950, 12030), new Vec2(-34910, 12035), new Vec2(-34890, 12060)], true));
        doc.Entities.Add(new TextEntity(text, new Vec2(-34940, 12130), "No.1+5.000 測点", 1.75, 0.2));

        var path = Path.Combine(Path.GetTempPath(), "civilcad-test-" + Guid.NewGuid().ToString("N") + ".dwg");
        try
        {
            DwgIO.Save(doc, path);
            var bytes = File.ReadAllBytes(path);
            Assert.Equal("AC1027", System.Text.Encoding.ASCII.GetString(bytes, 0, 6));

            var loaded = new CadDocument();
            Assert.Equal(0, DwgIO.Load(loaded, path));
            Assert.Equal(500, loaded.ScaleDenominator);
            Assert.Equal(doc.Entities.Count, loaded.Entities.Count);
            Assert.Contains(loaded.Layers, l => l.Name == "中心線");
            var t = Assert.Single(loaded.Entities.OfType<TextEntity>());
            Assert.Equal("No.1+5.000 測点", t.Text);
            Assert.True(t.Position.DistanceTo(new Vec2(-34940, 12130)) < 1e-6);
            var line = Assert.Single(loaded.Entities.OfType<LineEntity>());
            Assert.True(line.End.DistanceTo(new Vec2(-34800, 12100)) < 1e-6);
            Assert.Equal("中心線", line.Layer.Name);
            var arc = Assert.Single(loaded.Entities.OfType<ArcEntity>());
            Assert.Equal(20, arc.Radius, 6);
            Assert.Equal(0.3, arc.StartAngle, 6);
            var pl = Assert.Single(loaded.Entities.OfType<PolylineEntity>());
            Assert.True(pl.IsClosed);
            Assert.Equal(3, pl.Points.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_RejectsNonDwg()
    {
        var path = Path.Combine(Path.GetTempPath(), "civilcad-test-" + Guid.NewGuid().ToString("N") + ".dwg");
        File.WriteAllText(path, "not a dwg");
        try
        {
            Assert.Throws<InvalidDataException>(() => DwgIO.Load(new CadDocument(), path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(@"\U+6DF1\U+6D45\U+56F3", "深浅図")]
    [InlineData(@"\U+4EE4\U+548C8\U+5E74", "令和8年")] // 16 進 4 桁の後ろの数字は普通の文字
    [InlineData(@"\M+1955C", "表")] // Shift_JIS の 2 バイト (2 バイト目が \ の文字)
    [InlineData(@"\M+28140", @"\M+28140")] // Shift_JIS 以外のコードページはそのまま
    [InlineData(@"No.1 \P", @"No.1 \P")]
    [InlineData("測点", "測点")]
    public void DecodeText_RestoresUnicodeAndShiftJisEscapes(string raw, string expected) =>
        Assert.Equal(expected, DwgIO.DecodeText(raw));

    /// <summary>AutoCAD 2004 形式 (AC1018) の DWG は日本語を \U+XXXX で持つことがある。読み込むと元の文字・レイヤ名に戻る。</summary>
    [Fact]
    public void Load_Ac1018_DecodesUnicodeEscapesInTextAndLayers()
    {
        var cad = new ACadSharp.CadDocument(ACadSharp.ACadVersion.AC1018);
        var layer = new ACadSharp.Tables.Layer(@"\U+7D14\U+571F\U+91CF"); // 純土量
        cad.Layers.Add(layer);
        cad.Entities.Add(new ACadSharp.Entities.TextEntity { Value = @"\U+5CB8\U+58C1\U+6CD5\U+7DDA", Height = 2.5, Layer = layer }); // 岸壁法線
        var path = Path.Combine(Path.GetTempPath(), "civilcad-test-" + Guid.NewGuid().ToString("N") + ".dwg");
        try
        {
            using (var writer = new ACadSharp.IO.DwgWriter(path, cad)) writer.Write();
            Assert.Equal("AC1018", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(path), 0, 6));

            var loaded = new CadDocument();
            DwgIO.Load(loaded, path);
            var t = Assert.Single(loaded.Entities.OfType<TextEntity>());
            Assert.Equal("岸壁法線", t.Text);
            Assert.Equal("純土量", t.Layer.Name);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
