using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using CivilCad.App.IO;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

public class LineTypeTests
{
    private static void RunSta(Action action)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    private static CadDocument Sample()
    {
        var doc = new CadDocument { ScaleDenominator = 500 };
        var l = doc.CurrentLayer;
        doc.Entities.AddRange(
        [
            new LineEntity(l, new Vec2(0, 0), new Vec2(100, 0)) { LineType = LineType.Dashed },
            new PolylineEntity(l, [new Vec2(0, 10), new Vec2(100, 10), new Vec2(100, 30)], false) { LineType = LineType.Dotted },
            new CircleEntity(l, new Vec2(50, 60), 20) { LineType = LineType.DashDot },
            new ArcEntity(l, new Vec2(50, 60), 30, 0, Math.PI) { LineType = LineType.DashDotDot },
            new LineEntity(l, new Vec2(0, 100), new Vec2(100, 100)), // 実線
        ]);
        return doc;
    }

    [Fact]
    public void Names_Patterns_AndNameMapping()
    {
        Assert.Equal(["実線", "破線", "点線", "一点鎖線", "二点鎖線"], LineTypes.All.Select(LineTypes.Name));
        Assert.Empty(LineTypes.PatternMm(LineType.Continuous));
        Assert.Equal(4, LineTypes.PeriodMm(LineType.Dashed));
        Assert.True(LineTypes.TryParse("一点鎖線", out var t) && t == LineType.DashDot);

        Assert.Equal(LineType.Dashed, DxfIO.LineTypeFromName("HIDDEN"));
        Assert.Equal(LineType.Dashed, DxfIO.LineTypeFromName("DASHED2"));
        Assert.Equal(LineType.DashDot, DxfIO.LineTypeFromName("CENTER"));
        Assert.Equal(LineType.DashDotDot, DxfIO.LineTypeFromName("PHANTOM"));
        Assert.Equal(LineType.Dotted, DxfIO.LineTypeFromName("DOT"));
        Assert.Equal(LineType.Continuous, DxfIO.LineTypeFromName("ByLayer"));
        Assert.Equal(LineType.DashDot, SfcIO.LineTypeFromSxfName("long dashed dotted"));
        Assert.Equal(LineType.DashDotDot, SfcIO.LineTypeFromSxfName("long dashed double-dotted"));
        Assert.Equal(LineType.Dashed, SfcIO.LineTypeFromSxfName("dashed spaced"));
        Assert.Equal(LineType.Dotted, SfcIO.LineTypeFromSxfName("dotted"));
    }

    [Fact]
    public void LineTool_UsesCurrentLineType_AndCloneKeepsIt() => RunSta(() =>
    {
        var doc = new CadDocument { CurrentLineType = LineType.DashDot };
        var canvas = new CadCanvas { Document = doc };
        var tool = new LineTool();
        canvas.SetTool(tool);
        tool.OnPoint(new Vec2(0, 0));
        tool.OnPoint(new Vec2(10, 0));
        var line = Assert.Single(doc.Entities.OfType<LineEntity>());
        Assert.Equal(LineType.DashDot, line.LineType);
        Assert.Equal(LineType.DashDot, line.Clone().LineType);

        // いろいろな拡大率で描ける (細かすぎるときは実線で描く)
        foreach (var e in Sample().Entities) doc.Entities.Add(e);
        canvas.Measure(new Size(600, 400));
        canvas.Arrange(new Rect(0, 0, 600, 400));
        foreach (var k in new[] { 0.01, 1, 50, 5000 })
        {
            canvas.SetView(new Vec2(50, 50), k);
            canvas.RedrawAll();
        }
    });

    [Theory]
    [InlineData(".dxf")]
    [InlineData(".dwg")]
    [InlineData(".sfc")]
    [InlineData(".jww")]
    public void SavedAndLoaded(string ext)
    {
        var doc = Sample();
        var path = Path.Combine(Path.GetTempPath(), $"civilcad_ltype_{Guid.NewGuid():N}{ext}");
        try
        {
            DrawingFiles.Save(doc, path);
            var loaded = new CadDocument();
            DrawingFiles.Open(loaded, path);
            LineType Of<T>(Func<T, bool>? where = null) where T : Entity =>
                loaded.Entities.OfType<T>().First(e => where == null || where(e)).LineType;
            Assert.Equal(LineType.Dashed, Of<LineEntity>(l => Math.Abs(l.Start.Y - l.End.Y) < 1e-6 && loaded.Entities.OfType<LineEntity>().Min(x => x.Start.Y) == l.Start.Y));
            Assert.Equal(LineType.DashDot, Of<CircleEntity>());
            Assert.Equal(LineType.DashDotDot, Of<ArcEntity>());
            // ポリラインは形式によって線分に分かれることがある
            var dotted = loaded.Entities.Where(e => e is PolylineEntity || e is LineEntity).Count(e => e.LineType == LineType.Dotted);
            Assert.True(dotted >= 1, $"{ext}: 点線が無い");
            Assert.Contains(loaded.Entities.OfType<LineEntity>(), l => l.LineType == LineType.Continuous);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Dxf_PatternIsPaperMmAtDrawingScale()
    {
        var doc = Sample(); // 1:500 → 用紙 3mm = 1.5m
        var path = Path.Combine(Path.GetTempPath(), $"civilcad_ltype_{Guid.NewGuid():N}.dxf");
        try
        {
            DxfIO.Save(doc, path);
            var dxf = netDxf.DxfDocument.Load(path);
            var dashed = dxf.Linetypes["DASHED"];
            Assert.Equal(1.5, dashed.Segments[0].Length, 9);
            Assert.Equal(-0.5, dashed.Segments[1].Length, 9);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
