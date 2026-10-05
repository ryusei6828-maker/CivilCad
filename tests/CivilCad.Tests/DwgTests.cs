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
}
