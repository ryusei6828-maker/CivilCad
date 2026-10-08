using System.Runtime.ExceptionServices;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

public class TextBoxSnapTests
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
    public void TextBoxPoints_SnapOnlyInRotateCommand() => RunSta(() =>
    {
        var doc = new CadDocument();
        // 半角 4 文字・高さ 1m → 枠は (0,0)-(2,1)
        var text = new TextEntity(doc.CurrentLayer, Vec2.Zero, "ABCD", 1);
        doc.Entities.Add(text);
        var canvas = new CadCanvas { Document = doc };
        canvas.SetView(new Vec2(1, 0.5), 100); // 1m = 100px → 許容 0.12m

        // ふだん (選択) は文字の枠にスナップしない
        Assert.Null(canvas.FindSnap(new Vec2(2.05, 1.03)));

        canvas.SetSelection([text]);
        canvas.SetTool(new RotateTool());
        Assert.Equal(new SnapPoint(new Vec2(2, 1), SnapKind.Endpoint), canvas.FindSnap(new Vec2(2.05, 1.03)));   // 右上の角
        Assert.Equal(new SnapPoint(new Vec2(1, 0.5), SnapKind.Center), canvas.FindSnap(new Vec2(1.04, 0.52))); // 中心
        Assert.Equal(new SnapPoint(new Vec2(1, 0), SnapKind.Midpoint), canvas.FindSnap(new Vec2(0.98, -0.05))); // 下の辺の中点
        Assert.Equal(new Vec2(0, 0), canvas.FindSnap(new Vec2(0.03, 0.02))!.Value.Point);                     // 左下 (= 文字の位置)

        // 回転した文字の枠も回る
        text.Rotation = Math.PI / 2;
        doc.NotifyChanged();
        var sp = canvas.FindSnap(new Vec2(-1.02, 2.03))!.Value;
        Assert.Equal(-1, sp.Point.X, 9);
        Assert.Equal(2, sp.Point.Y, 9);

        // 回転コマンドを終えると、また枠にはスナップしない
        canvas.SetTool(new SelectTool());
        Assert.Null(canvas.FindSnap(new Vec2(-1.02, 2.03)));
    });
}
