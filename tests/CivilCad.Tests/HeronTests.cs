using System.Runtime.ExceptionServices;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

[Collection("SharedToolSettings")] // 共有の設定を書き換える
public class HeronTests
{
    [Theory]
    [InlineData(10.58, 12.35, 7.42, 15.175, 39.085)]
    public void Compute_ReferenceExample(double a, double b, double c, double s, double area)
    {
        var t = Heron.Compute(a, b, c, 2, 3);
        Assert.Equal(s, t.S, 9);
        Assert.Equal(area, t.Area, 9);
        Assert.Equal(4.595, t.SA, 9);
        Assert.Equal(2.825, t.SB, 9);
        Assert.Equal(7.755, t.SC, 9);
    }

    [Fact]
    public void Compute_OtherReferences()
    {
        // sa=2.265 sb=4.475 sc=7.875 → s=14.615 なら辺 12.35, 10.14, 6.74 (s-a など)
        var t1 = Heron.Compute(14.615 - 2.265, 14.615 - 4.475, 14.615 - 7.875, 2, 3);
        Assert.Equal(14.615, t1.S, 9);
        Assert.Equal(34.155, t1.Area, 9);

        var t2 = Heron.Compute(10.470 - 0.520, 10.470 - 9.370, 10.470 - 0.580, 2, 3);
        Assert.Equal(10.470, t2.S, 9);
        Assert.Equal(5.439, t2.Area, 9);
    }

    [Fact]
    public void Compute_RoundsSidesFirst()
    {
        var t = Heron.Compute(10.576, 12.354, 7.4249, 2, 3);
        Assert.Equal(10.58, t.A, 9);
        Assert.Equal(12.35, t.B, 9);
        Assert.Equal(7.42, t.C, 9);
        Assert.Equal(39.085, t.Area, 9);
    }

    [Fact]
    public void SideLength_SlopeOrHorizontal()
    {
        var a = new HeronVertex(new Vec2(0, 0), 0);
        var b = new HeronVertex(new Vec2(3, 0), 4);
        var c = new HeronVertex(new Vec2(3, 0), null);
        Assert.Equal(5, Heron.SideLength(a, b, true), 9);
        Assert.Equal(3, Heron.SideLength(a, b, false), 9);
        Assert.Equal(3, Heron.SideLength(a, c, true), 9);
    }

    private static void RunSta(Action<CadDocument, CadCanvas> action)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                var doc = new CadDocument { ScaleDenominator = 1 };
                action(doc, new CadCanvas { Document = doc });
            }
            catch (Exception ex) { error = ex; }
            finally
            {
                HeronSettings.Slope = true;
                HeronSettings.SideDecimals = 2;
                HeronSettings.AreaDecimals = 3;
                HeronSettings.TextHeightMm = 2.5;
                HeronSettings.Strip = false;
                HeronSettings.PlaceTable = true;
            }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    private static void AddPoint(CadDocument doc, string name, double x, double y, double? z) =>
        doc.Entities.Add(new PointEntity(doc.CurrentLayer, new Vec2(x, y)) { Name = name, Z = z });

    [Fact]
    public void Strip_TwoTriangles_PlacesSheetAsOneUndoStep() => RunSta((doc, canvas) =>
    {
        AddPoint(doc, "1", 0, 0, 0);
        AddPoint(doc, "2", 10, 0, 0);
        AddPoint(doc, "3", 10, 10, 0);
        AddPoint(doc, "4", 0, 10, 0);
        var before = doc.Entities.Count;
        HeronSettings.Strip = true;
        var tool = new HeronTool();
        canvas.SetTool(tool);
        tool.OnPoint(new Vec2(0, 0));
        tool.OnPoint(new Vec2(10, 0));
        tool.OnPoint(new Vec2(10, 10));
        tool.OnPoint(new Vec2(0, 10));
        Assert.Equal(before, doc.Entities.Count); // まだ置いていない
        tool.OnEnter();
        Assert.Equal(before, doc.Entities.Count);
        tool.OnPoint(new Vec2(20, 20));           // 計算書の位置

        var added = doc.Entities.Skip(before).ToList();
        Assert.Equal("ヘロン面積 (2 個)", doc.History.Peek?.Name);
        var triLines = added.OfType<LineEntity>().Where(e => e.Layer.Name == HeronTool.LayerTriangle).ToList();
        Assert.Equal(5, triLines.Count);           // 四角形の 4 辺 + 対角線 1 本
        var sideTexts = added.OfType<TextEntity>().Where(e => e.Layer.Name == HeronTool.LayerSide).ToList();
        Assert.Equal(5, sideTexts.Count);
        Assert.Contains(sideTexts, t => t.Text == "14.14");
        Assert.Equal(2, added.OfType<CircleEntity>().Count());
        Assert.Equal(2, added.OfType<TextEntity>().Count(e => e.Layer.Name == HeronTool.LayerNumber));
        var table = added.OfType<TextEntity>().Where(e => e.Layer.Name == HeronTool.LayerTable).ToList();
        Assert.Contains(table, t => t.Text == "合計");
        var total = 2 * Heron.Compute(10, Math.Sqrt(200), 10, 2, 3).Area;
        Assert.Contains(table, t => t.Text == total.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
        Assert.Contains(table, t => t.Text.StartsWith("ヘロン面積計算書"));

        doc.History.Undo();
        Assert.Equal(before, doc.Entities.Count);
        Assert.DoesNotContain(doc.Entities, e => e.Layer.Name.StartsWith("ヘロン-"));
    });

    [Fact]
    public void Slope_UsesZ_AndRejectsDegenerate() => RunSta((doc, canvas) =>
    {
        AddPoint(doc, "1", 0, 0, 0);
        AddPoint(doc, "2", 3, 0, 4);
        AddPoint(doc, "3", 0, 4, 0);
        HeronSettings.PlaceTable = false;
        var tool = new HeronTool();
        canvas.SetTool(tool);
        tool.OnPoint(new Vec2(0, 0));
        tool.OnPoint(new Vec2(3, 0));
        tool.OnPoint(new Vec2(6, 0));              // 一直線上: 無視される
        tool.OnPoint(new Vec2(0, 4));
        tool.OnEnter();                             // 計算書なしで記入
        var sides = doc.Entities.OfType<TextEntity>().Where(e => e.Layer.Name == HeronTool.LayerSide).Select(e => e.Text).ToList();
        Assert.Contains("5.00", sides);             // 点 1-2 は斜距離 (3, 0, 4 → 5)
        Assert.Contains("4.00", sides);             // 点 1-3 は標高があるが同標高: 水平 4
        Assert.Contains("6.40", sides);             // 点 2-3: sqrt(9+16+16)=6.40
        Assert.Equal(3, doc.Entities.OfType<LineEntity>().Count());
    });
}
