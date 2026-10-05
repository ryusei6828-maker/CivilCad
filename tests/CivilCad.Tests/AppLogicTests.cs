using System.IO;
using CivilCad.App.Alignment;
using CivilCad.App.IO;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

public class AppLogicTests
{
    [Fact]
    public void AlignmentDrawer_ReplacesPreviousDrawing_AndUndoes()
    {
        var doc = new CadDocument();
        var input = AlignmentInput.Sample();
        var al = input.Compute();

        doc.History.Execute(AlignmentDrawer.CreateDrawCommand(doc, al, input));
        var count = doc.Entities.Count;
        Assert.True(count > 50);
        Assert.All(doc.Entities, e => Assert.Equal(input.Tag, e.Tag));
        Assert.NotNull(doc.FindLayer(AlignmentDrawer.LayerCenter));

        // 再作図しても重複しない
        doc.History.Execute(AlignmentDrawer.CreateDrawCommand(doc, al, input));
        Assert.Equal(count, doc.Entities.Count);

        doc.History.Undo();
        doc.History.Undo();
        Assert.Empty(doc.Entities);
    }

    [Fact]
    public void Dxf_AlignedText_KeepsPosition()
    {
        // 他 CAD が書く形: 10 = 左下 (ダミー), 11 = 配置基準点 (中央) を再現する
        var dxf = new netDxf.DxfDocument();
        dxf.Entities.Add(new netDxf.Entities.Text("中央", new netDxf.Vector2(100, 50), 2) { Alignment = netDxf.Entities.TextAlignment.MiddleCenter, WidthFactor = 0.8 });
        dxf.Entities.Add(new netDxf.Entities.Text("右上", new netDxf.Vector2(200, 50), 3) { Alignment = netDxf.Entities.TextAlignment.TopRight });
        dxf.Entities.Add(new netDxf.Entities.Text("FIT", new netDxf.Vector2(0, 0), 1) { Alignment = netDxf.Entities.TextAlignment.Fit, Width = 12 });
        dxf.Entities.Add(new netDxf.Entities.MText("1行目\\P2行目", new netDxf.Vector2(10, 90), 2)
        {
            AttachmentPoint = netDxf.Entities.MTextAttachmentPoint.MiddleCenter,
        });

        var path = Path.Combine(Path.GetTempPath(), $"civilcad_{Guid.NewGuid():N}.dxf");
        try
        {
            dxf.Save(path);
            // 1つ目の TEXT の 10/20 (第1点) を別の値にする
            var lines = File.ReadAllLines(path);
            for (int i = 0, seen = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim() == "TEXT" && lines[i - 1].Trim() == "0" && seen++ == 0)
                {
                    for (var j = i + 1; lines[j].Trim() != "0"; j += 2)
                        if (lines[j].Trim() is "10" or "20") lines[j + 1] = "0.0";
                }
            }
            File.WriteAllLines(path, lines);

            var doc = new CadDocument();
            DxfIO.Load(doc, path);
            var texts = doc.Entities.OfType<TextEntity>().ToList();

            var center = texts.Single(t => t.Text == "中央");
            Assert.Equal(new Vec2(100, 50), center.Position);
            Assert.Equal((TextHAlign.Center, TextVAlign.Middle), (center.HAlign, center.VAlign));
            Assert.Equal(0.8, center.WidthFactor, 9);
            // 文字列の中心が基準点に来る: 幅 = 2字 × 2m × 0.8 = 3.2m → 左下は (98.4, 49)
            var start = center.BaselineStart(center.ApproxWidth);
            Assert.Equal(98.4, start.X, 9);
            Assert.Equal(49.0, start.Y, 9);

            var topRight = texts.Single(t => t.Text == "右上");
            Assert.Equal((TextHAlign.Right, TextVAlign.Top), (topRight.HAlign, topRight.VAlign));
            Assert.Equal(new Vec2(194, 47), topRight.BaselineStart(topRight.ApproxWidth));

            Assert.Equal(12, texts.Single(t => t.Text == "FIT").FitWidth);

            // MTEXT は行ごとに分かれ、中央揃えの基準点に対して上下に振り分けられる
            var l1 = texts.Single(t => t.Text == "1行目");
            var l2 = texts.Single(t => t.Text == "2行目");
            var spacing = 2 * 5.0 / 3.0;
            var total = 2 + spacing;
            Assert.Equal(90 + total / 2, l1.Position.Y, 9);
            Assert.Equal(90 + total / 2 - spacing, l2.Position.Y, 9);
            Assert.Equal(TextHAlign.Center, l1.HAlign);

            // 保存して読み直しても揃え方が保たれる
            var path2 = path + "2.dxf";
            DxfIO.Save(doc, path2);
            var doc2 = new CadDocument();
            DxfIO.Load(doc2, path2);
            File.Delete(path2);
            var again = doc2.Entities.OfType<TextEntity>().Single(t => t.Text == "中央");
            Assert.Equal(center.Position, again.Position);
            Assert.Equal((center.HAlign, center.VAlign, center.WidthFactor), (again.HAlign, again.VAlign, again.WidthFactor));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Dxf_RoundTrip_KeepsEntitiesAndLayers()
    {
        var doc = new CadDocument();
        var layer = doc.GetOrCreateLayer("構造物", RgbColor.Red);
        layer.IsLocked = true;
        doc.Entities.AddRange(
        [
            new LineEntity(doc.CurrentLayer, new Vec2(-35000.123, 12000.5), new Vec2(-34900, 12100)),
            new PolylineEntity(layer, [new(0, 0), new(10, 0), new(10, 5)], true),
            new CircleEntity(layer, new Vec2(5, 5), 2.5),
            new ArcEntity(layer, Vec2.Zero, 10, 0, Math.PI / 2),
            new TextEntity(layer, new Vec2(1, 2), "No.1 測点", 2, Math.PI / 6) { Color = RgbColor.Cyan },
        ]);

        var path = Path.Combine(Path.GetTempPath(), $"civilcad_{Guid.NewGuid():N}.dxf");
        try
        {
            DxfIO.Save(doc, path);
            var loaded = new CadDocument();
            var skipped = DxfIO.Load(loaded, path);

            Assert.Equal(0, skipped);
            Assert.Equal(5, loaded.Entities.Count);
            var l2 = loaded.FindLayer("構造物")!;
            Assert.Equal(RgbColor.Red, l2.Color);
            Assert.True(l2.IsLocked);

            var line = loaded.Entities.OfType<LineEntity>().Single();
            Assert.Equal(-35000.123, line.Start.X, 6);
            var arc = loaded.Entities.OfType<ArcEntity>().Single();
            Assert.Equal(Math.PI / 2, arc.EndAngle, 9);
            var text = loaded.Entities.OfType<TextEntity>().Single();
            Assert.Equal("No.1 測点", text.Text);
            Assert.Equal(Math.PI / 6, text.Rotation, 9);
            Assert.Equal(RgbColor.Cyan, text.Color);
            Assert.True(loaded.Entities.OfType<PolylineEntity>().Single().IsClosed);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
