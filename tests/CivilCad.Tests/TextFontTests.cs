using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using CivilCad.App.IO;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

public class TextFontTests
{
    private const string Mincho = "ＭＳ 明朝";

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
            finally { TextTool.DefaultFontName = null; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    [Fact]
    public void TextTool_UsesChosenFont_CloneKeepsIt_CanvasDraws() => RunSta(() =>
    {
        TextTool.DefaultFontName = Mincho;
        var doc = new CadDocument();
        var canvas = new CadCanvas { Document = doc };
        var tool = new TextTool();
        canvas.SetTool(tool);
        tool.OnPoint(Vec2.Zero);
        Assert.True(tool.OnText("明朝の文字"));
        var t = Assert.Single(doc.Entities.OfType<TextEntity>());
        Assert.Equal(Mincho, t.FontName);
        Assert.Equal(Mincho, ((TextEntity)t.Clone()).FontName);

        // 既定・この PC に無いフォントでも描ける
        doc.Entities.Add(new TextEntity(doc.CurrentLayer, new Vec2(0, 5), "既定", 1));
        doc.Entities.Add(new TextEntity(doc.CurrentLayer, new Vec2(0, 10), "無いフォント", 1) { FontName = "存在しないフォント" });
        canvas.Measure(new Size(400, 300));
        canvas.Arrange(new Rect(0, 0, 400, 300));
        canvas.ZoomExtents();
        canvas.RedrawAll();

        Assert.Equal(TextEntity.DefaultFontName, FontCombo.Names[0]);
        Assert.Contains(Mincho, FontCombo.Names);
    });

    [Theory]
    [InlineData(".dxf")]
    [InlineData(".dwg")]
    [InlineData(".sfc")]
    [InlineData(".jww")]
    public void FontSavedAndLoaded(string ext)
    {
        var doc = new CadDocument { ScaleDenominator = 100 };
        doc.Entities.Add(new TextEntity(doc.CurrentLayer, new Vec2(0, 0), "明朝", 0.5) { FontName = Mincho });
        doc.Entities.Add(new TextEntity(doc.CurrentLayer, new Vec2(0, 5), "既定", 0.5));
        var path = Path.Combine(Path.GetTempPath(), $"civilcad_font_{Guid.NewGuid():N}{ext}");
        try
        {
            DrawingFiles.Save(doc, path);
            var loaded = new CadDocument();
            DrawingFiles.Open(loaded, path);
            var texts = loaded.Entities.OfType<TextEntity>().ToList();
            Assert.Equal(Mincho, texts.Single(t => t.Text == "明朝").FontName);
            Assert.Equal(TextEntity.DefaultFontName, texts.Single(t => t.Text == "既定").EffectiveFontName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PdfExport_WithFonts()
    {
        var doc = new CadDocument { ScaleDenominator = 100 };
        doc.Entities.Add(new TextEntity(doc.CurrentLayer, new Vec2(0, 0), "明朝", 0.5) { FontName = Mincho });
        doc.Entities.Add(new TextEntity(doc.CurrentLayer, new Vec2(0, 5), "無い", 0.5) { FontName = "存在しないフォント" });
        var path = Path.Combine(Path.GetTempPath(), $"civilcad_font_{Guid.NewGuid():N}.pdf");
        try
        {
            PdfExport.Export(doc, path, new PdfOptions());
            Assert.True(new FileInfo(path).Length > 0);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
