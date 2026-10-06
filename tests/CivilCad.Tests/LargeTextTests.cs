using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

/// <summary>縮尺が大きい (1:1000 など) 図面で拡大したとき、文字が画面上とても大きくなっても落ちないこと。</summary>
public class LargeTextTests
{
    [Fact]
    public void HugeTextOnScreen_DoesNotThrow()
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                var doc = new CadDocument { ScaleDenominator = 1000 };
                var canvas = new CadCanvas { Document = doc };
                var win = new Window { Content = canvas, Width = 800, Height = 600, Left = -4000, Top = -4000, WindowStyle = WindowStyle.None, ShowActivated = false };
                win.Show();
                // 1:1000 で文字高 2.5mm → 2.5m。1cm 程度を測るために大きく拡大した状態 (1m = 50000px)
                doc.Entities.Add(new TextEntity(doc.CurrentLayer, Vec2.Zero, "A=1.000m²", doc.PaperMmToMeters(2.5)));
                doc.NotifyChanged();
                canvas.SetView(new Vec2(0.5, 0.5), 50000);
                canvas.RedrawAll();

                // 計測結果の文字をカーソルに付けて動かしているとき (プレビュー) も
                var tool = new DistanceTool();
                canvas.SetTool(tool);
                tool.OnPoint(new Vec2(0.5, 0.5));
                tool.OnPoint(new Vec2(0.51, 0.5));
                tool.OnMouseMove(new Vec2(0.505, 0.5));
                canvas.RedrawOverlay();
                win.UpdateLayout();
                win.Close();
            }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
