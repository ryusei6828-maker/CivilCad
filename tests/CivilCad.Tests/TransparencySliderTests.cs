using System.Runtime.ExceptionServices;
using System.Windows.Controls.Primitives;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

/// <summary>透明度のスライダー (塗りつぶし・ハッチング・画像)。</summary>
public class TransparencySliderTests
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

    [Fact]
    public void FilledPolyline_HasTransparency_Cloned_AndSetGetForAllKinds()
    {
        var doc = new CadDocument();
        var p = new PolylineEntity(doc.CurrentLayer, [new(0, 0), new(1, 0), new(1, 1)], isClosed: true) { Filled = true, FillTransparencyPercent = 40 };
        Assert.Equal(0.6, p.FillOpacity, 9);
        Assert.Equal(40, ((PolylineEntity)p.Clone()).FillTransparencyPercent, 9);
        p.FillTransparencyPercent = 150; // 範囲外は 100 まで
        Assert.Equal(100, p.FillTransparencyPercent, 9);

        var hatch = new HatchEntity(doc.CurrentLayer, [[new Vec2(0, 0), new Vec2(1, 0), new Vec2(1, 1)]], [], solid: true);
        var image = new ImageEntity(doc.CurrentLayer, "a.png", 10, 10, Vec2.Zero, new Vec2(1, 0), new Vec2(0, 1));
        foreach (var e in new Entity[] { p, hatch, image })
        {
            PropertiesPanel.SetTransparency(e, 25);
            Assert.Equal(25, PropertiesPanel.GetTransparency(e), 6);
        }
    }

    [Fact]
    public void Slider_DragPreviewsThenCommitsOnce_ClickAndBoxCommit() => RunSta(() =>
    {
        var previews = new List<double>();
        var commits = new List<double>();
        var s = new TransparencySlider(10, previews.Add, commits.Add);
        Assert.Equal(10, s.Slider.Value);
        Assert.Equal("10", s.Box.Text);

        // ドラッグ中は preview だけ、離したら commit 1 回
        s.Slider.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        s.Slider.Value = 30;
        s.Slider.Value = 60;
        Assert.Equal([30, 60], previews);
        Assert.Empty(commits);
        s.Slider.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
        Assert.Equal([60], commits);
        Assert.Equal("60", s.Box.Text);

        // クリック・キー操作はすぐ確定。同じ値なら何もしない
        s.Slider.Value = 75;
        Assert.Equal([60, 75], commits);
        s.Slider.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        s.Slider.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
        Assert.Equal([60, 75], commits);

        // 選んだ図形で値が違えば欄は空
        Assert.Equal("", new TransparencySlider(null, _ => { }, _ => { }).Box.Text);
    });
}
