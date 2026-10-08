using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using CivilCad.App.IO;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

public class TextEntryDimensionTests
{
    private static void RunSta(Action action)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
            finally
            {
                DimSettings.Mode = DimMode.Aligned;
                DimSettings.Unit = DimUnit.M;
                DimSettings.Decimals = 3;
            }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    [Fact]
    public void TextTool_TypedInPropertiesPanel() => RunSta(() =>
    {
        var doc = new CadDocument();
        var canvas = new CadCanvas { Document = doc };
        var panel = new PropertiesPanel();
        var win = new Window { Content = panel, Width = 300, Height = 400, ShowActivated = false, WindowStyle = WindowStyle.None };
        win.Show();
        try
        {
            panel.Attach(canvas);
            var tool = new TextTool();
            canvas.SetTool(tool);
            panel.Refresh();
            Assert.Null(panel.EntryBox);                  // 位置を決める前は入力欄なし

            tool.OnPoint(new Vec2(5, 5));
            panel.Refresh();
            var box = Assert.IsType<System.Windows.Controls.TextBox>(panel.EntryBox);
            box.Text = "右で入力";
            Assert.Equal("右で入力", tool.DraftText);       // 入力中の文字は見本に出る
            canvas.RedrawOverlay();
            panel.SubmitEntry();

            var t = Assert.Single(doc.Entities.OfType<TextEntity>());
            Assert.Equal("右で入力", t.Text);
            Assert.Equal(new Vec2(5, 5), t.Position);
            Assert.False(tool.IsWaitingForText);           // 続けて次の位置を指定できる
            Assert.Same(tool, canvas.Tool);
            panel.Refresh();
            Assert.Null(panel.EntryBox);

            // やめると位置の指定に戻る
            tool.OnPoint(new Vec2(1, 1));
            tool.DraftText = "途中";
            tool.CancelText();
            Assert.False(tool.IsWaitingForText);
            Assert.Single(doc.Entities.OfType<TextEntity>());
        }
        finally
        {
            win.Close();
        }
    });

    [Fact]
    public void FlagText_MultipleLines_FromEntry() => RunSta(() =>
    {
        var doc = new CadDocument();
        var canvas = new CadCanvas { Document = doc };
        var tool = new LeaderTool(flag: true);
        canvas.SetTool(tool);
        tool.OnPoint(Vec2.Zero);
        tool.OnPoint(new Vec2(3, 3));
        Assert.True(tool.IsWaitingForText);
        Assert.True(tool.AllowsMultipleLines);
        tool.DraftText = "1行目\r\n2行目";
        tool.SubmitText(tool.DraftText);
        var flag = Assert.Single(doc.Entities.OfType<LeaderEntity>());
        Assert.Equal(["1行目", "2行目"], flag.Lines);
        Assert.False(tool.IsWaitingForText);
    });

    [Fact]
    public void Dimension_Aligned_Horizontal_Continuous() => RunSta(() =>
    {
        var doc = new CadDocument { ScaleDenominator = 100 };
        var canvas = new CadCanvas { Document = doc };
        var tool = new DimensionTool();
        canvas.SetTool(tool);

        // 平行: (0,0)-(3,4) = 5m
        tool.OnPoint(new Vec2(0, 0));
        tool.OnPoint(new Vec2(3, 4));
        tool.OnPoint(new Vec2(-4, 3)); // 左上側に寸法線
        var d1 = Assert.Single(doc.Entities.OfType<DimensionEntity>());
        Assert.Equal(5, d1.Measured, 9);
        Assert.Equal("5.000", d1.DisplayText);
        var (a, b) = d1.DimLine;
        Assert.Equal(5, (b - a).Length, 9);
        Assert.Equal(5, (a - Vec2.Zero).Length, 9);    // 寸法線は 2 点を結ぶ線から 5m 離れた所
        var parts = d1.Explode().ToList();
        Assert.Equal(2, parts.OfType<HatchEntity>().Count());   // 矢印 2 つ
        Assert.Equal("5.000", parts.OfType<TextEntity>().Single().Text);
        Assert.Equal(0.25, parts.OfType<TextEntity>().Single().TextHeightOrHeight(), 9); // 2.5mm × 1:100

        // 直列: 続けてクリックすると、前の 2 点目からの寸法
        tool.OnPoint(new Vec2(6, 8));
        Assert.Equal(2, doc.Entities.OfType<DimensionEntity>().Count());
        Assert.Equal(5, doc.Entities.OfType<DimensionEntity>().Last().Measured, 9);
        tool.OnEnter(); // 新しい寸法へ

        // 水平・mm: (10,0)-(12.5,7) の水平距離 2500mm
        DimSettings.Mode = DimMode.Horizontal;
        DimSettings.Unit = DimUnit.Mm;
        DimSettings.Decimals = 0;
        tool.OnPoint(new Vec2(10, 0));
        tool.OnPoint(new Vec2(12.5, 7));
        tool.OnPoint(new Vec2(11, -3));
        var d3 = doc.Entities.OfType<DimensionEntity>().Last();
        Assert.Equal("2500", d3.DisplayText);
        var (a3, b3) = d3.DimLine;
        Assert.Equal(-3, a3.Y, 9);
        Assert.Equal(-3, b3.Y, 9);

        // 寸法値の書き換え・回転・拡大縮小
        d3.TextOverride = "L=<>";
        Assert.Equal("L=2500", d3.DisplayText);
        var clone = (DimensionEntity)d3.Clone();
        clone.Rotate(Vec2.Zero, Math.PI / 2);
        Assert.Equal(2.5, clone.Measured, 9);          // 回しても同じ長さ (向きも一緒に回る)
        clone.Scale(Vec2.Zero, 2);
        Assert.Equal(5, clone.Measured, 9);
        Assert.Equal("L=2500", d3.DisplayText);        // 元はそのまま
    });

    [Fact]
    public void Dimension_SavedAsLinesAndText()
    {
        var doc = new CadDocument { ScaleDenominator = 100 };
        doc.Entities.Add(new DimensionEntity(doc.CurrentLayer, new Vec2(0, 0), new Vec2(10, 0), new Vec2(5, 2), null, 0.25, 0.25));
        foreach (var ext in new[] { ".dxf", ".sfc", ".jww" })
        {
            var path = Path.Combine(Path.GetTempPath(), $"civilcad_dim_{Guid.NewGuid():N}{ext}");
            try
            {
                DrawingFiles.Save(doc, path);
                var loaded = new CadDocument();
                DrawingFiles.Open(loaded, path);
                Assert.Contains(loaded.Entities.OfType<TextEntity>(), t => t.Text == "10.000");
                Assert.True(loaded.Entities.Count >= 4, $"{ext}: {loaded.Entities.Count}");
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}

internal static class TextHeightExt
{
    public static double TextHeightOrHeight(this TextEntity t) => t.Height;
}
