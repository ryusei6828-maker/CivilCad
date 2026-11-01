using System.IO;
using CivilCad.App.Alignment;
using CivilCad.App.IO;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

/// <summary>複数行の文字 (改行を含む Text) の形・出力・保存。</summary>
public class MultiLineTextTests
{
    private static TextEntity Make(string text, TextHAlign h = TextHAlign.Left, TextVAlign v = TextVAlign.Bottom, double rot = 0)
    {
        var doc = new CadDocument();
        return new TextEntity(doc.CurrentLayer, new Vec2(10, 20), text, 2, rot) { HAlign = h, VAlign = v };
    }

    [Fact]
    public void SingleLine_Geometry_IsUnchanged()
    {
        var t = Make("ABCD", TextHAlign.Center, TextVAlign.Middle);
        Assert.Equal(4, t.ApproxWidth);
        Assert.Equal(new Vec2(8, 19), t.BaselineStart(4));
        var b = t.Bounds;
        Assert.Equal((8, 19, 12, 21), (b.Min.X, b.Min.Y, b.Max.X, b.Max.Y));
        Assert.Same(t, Assert.Single(t.ExplodeForOutput()));
        Assert.Equal(0, t.DistanceTo(new Vec2(10, 20)));
        Assert.Equal(2, t.DistanceTo(new Vec2(10, 23)));
    }

    [Theory]
    [InlineData(TextHAlign.Left, TextVAlign.Bottom, 10, 14, 20)]
    [InlineData(TextHAlign.Center, TextVAlign.Bottom, 8, 12, 20)]
    [InlineData(TextHAlign.Right, TextVAlign.Bottom, 6, 10, 20)]
    [InlineData(TextHAlign.Left, TextVAlign.Baseline, 10, 14, 20)]
    [InlineData(TextHAlign.Left, TextVAlign.Middle, 10, 14, 20 - 16.0 / 6)]
    [InlineData(TextHAlign.Right, TextVAlign.Top, 6, 10, 20 - 16.0 / 3)]
    public void MultiLine_BlockBounds(TextHAlign h, TextVAlign v, double minX, double maxX, double minY)
    {
        var t = Make("ab\ncdef", h, v); // 行の幅 2 と 4、文字高 2 → ブロック幅 4、高さ 2 + 10/3
        Assert.True(t.IsMultiLine);
        Assert.Equal(4, t.ApproxWidth);
        Assert.Equal(2 + 10.0 / 3, t.BlockHeight, 9);
        var b = t.Bounds;
        Assert.Equal(minX, b.Min.X, 9);
        Assert.Equal(maxX, b.Max.X, 9);
        Assert.Equal(minY, b.Min.Y, 9);
        Assert.Equal(minY + t.BlockHeight, b.Max.Y, 9);
        Assert.Equal(0, t.DistanceTo(new Vec2((minX + maxX) / 2, minY + 4)));
    }

    [Theory]
    [InlineData(TextHAlign.Left)]
    [InlineData(TextHAlign.Center)]
    [InlineData(TextHAlign.Right)]
    public void Explode_PlacesEachLine_Rotation0(TextHAlign h)
    {
        var t = Make("ab\ncdef", h);
        var lines = t.ExplodeForOutput().Cast<TextEntity>().ToList();
        Assert.Equal(["ab", "cdef"], lines.Select(l => l.Text));
        Assert.All(lines, l =>
        {
            Assert.Equal(TextVAlign.Baseline, l.VAlign);
            Assert.Equal(h, l.HAlign);
            Assert.Null(l.FitWidth);
            Assert.False(l.IsMultiLine);
        });
        // 揃えの基準点 (左なら左端、中央なら中央、右なら右端) は行の Position
        Assert.Equal(10, lines[0].Position.X, 9); // どの揃えでも 1 行目の基準点は x=10
        Assert.Equal(20 + 10.0 / 3, lines[0].Position.Y, 9);
        Assert.Equal(10, lines[1].Position.X, 9);
        Assert.Equal(20, lines[1].Position.Y, 9);
        // 行の枠: 左揃えは両方 x=10 から、中央は 1 行目が 9..11、右は 1 行目が 8..10
        var b0 = lines[0].Bounds;
        var (lo, hi) = h switch { TextHAlign.Left => (10.0, 12.0), TextHAlign.Center => (9.0, 11.0), _ => (8.0, 10.0) };
        Assert.Equal((lo, hi), (b0.Min.X, b0.Max.X));
    }

    [Fact]
    public void Explode_Rotation90()
    {
        var t = Make("ab\ncdef", rot: Math.PI / 2);
        var lines = t.ExplodeForOutput().Cast<TextEntity>().ToList();
        // 上向きが -x 方向になる。1 行目は 2 × 5/3 だけ「上」(= -x)
        Assert.Equal(10 - 10.0 / 3, lines[0].Position.X, 9);
        Assert.Equal(20, lines[0].Position.Y, 9);
        Assert.Equal(10, lines[1].Position.X, 9);
        Assert.Equal(20, lines[1].Position.Y, 9);
        Assert.All(lines, l => Assert.Equal(Math.PI / 2, l.Rotation));
        var b = t.Bounds;
        Assert.Equal(10 - t.BlockHeight, b.Min.X, 9);
        Assert.Equal(24, b.Max.Y, 9);
    }

    [Fact]
    public void Explode_SkipsEmptyLines()
    {
        var t = Make("ab\n\ncd");
        Assert.Equal(2, t.ExplodeForOutput().Count());
    }

    [Fact]
    public void Clone_KeepsMultiLineText()
    {
        var t = Make("ab\ncd");
        Assert.Equal("ab\ncd", ((TextEntity)t.Clone()).Text);
    }

    [Fact]
    public void ProjectFile_RoundTrip_KeepsNewlines()
    {
        var doc = new CadDocument();
        doc.Entities.Add(new TextEntity(doc.CurrentLayer, new Vec2(1, 2), "一行目\n二行目\n3", 2));
        var path = Path.Combine(Path.GetTempPath(), $"civilcad_{Guid.NewGuid():N}.ccp");
        try
        {
            ProjectFile.Save(path, [new ProjectSheet("s", doc, AlignmentInput.Sample(), false)], 0);
            var loaded = ProjectFile.Load(path);
            Assert.Equal("一行目\n二行目\n3", loaded.Sheets[0].Doc.Entities.OfType<TextEntity>().Single().Text);
        }
        finally
        {
            var dir = ProjectFile.SheetFolder(path);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            File.Delete(path);
        }
    }

    [Fact]
    public void Dxf_Save_WritesOneTextPerLine()
    {
        var doc = new CadDocument();
        doc.Entities.Add(new TextEntity(doc.CurrentLayer, new Vec2(1, 2), "AAA\nBBB\nCCC", 2));
        var path = Path.Combine(Path.GetTempPath(), $"civilcad_{Guid.NewGuid():N}.dxf");
        try
        {
            DxfIO.Save(doc, path);
            var loaded = new CadDocument();
            DxfIO.Load(loaded, path);
            var texts = loaded.Entities.OfType<TextEntity>().ToList();
            Assert.Equal(3, texts.Count);
            Assert.Equal(["AAA", "BBB", "CCC"], texts.Select(t => t.Text).OrderBy(x => x));
            Assert.Equal(3, texts.Select(t => Math.Round(t.Position.Y, 6)).Distinct().Count());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
