using System.IO;
using System.Runtime.ExceptionServices;
using CivilCad.App.IO;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

public class LeaderTests
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
                LeaderSettings.Tip = LeaderTip.Arrow;
                LeaderSettings.TextHeightMm = 3.5;
                LeaderSettings.TipSizeMm = 3;
            }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    [Fact]
    public void Flag_ShelfUnderStackedText_ToTheRight() => RunSta(() =>
    {
        var doc = new CadDocument { ScaleDenominator = 200 }; // 3.5mm → 0.7m
        var canvas = new CadCanvas { Document = doc };
        var tool = new LeaderTool(flag: true);
        canvas.SetTool(tool);
        tool.OnPoint(new Vec2(0, 0));      // 先端
        tool.OnPoint(new Vec2(5, 5));      // 旗竿の始まり (先端の右上 → 旗竿は右へ)
        Assert.True(tool.WantsTextInput);
        Assert.True(tool.OnText("As=D13@200"));
        Assert.True(tool.OnText("L=12.5m | 2本")); // | で 2 行まとめて
        tool.OnEnter();                    // 空の Enter で確定

        var flag = Assert.Single(doc.Entities.OfType<LeaderEntity>());
        Assert.True(flag.IsFlag);
        Assert.Equal(["As=D13@200", "L=12.5m", "2本"], flag.Lines);
        Assert.Equal(0.7, flag.TextHeight, 9);
        Assert.Same(tool, canvas.Tool); // 続けて描ける

        var parts = flag.Explode().ToList();
        var line = parts.OfType<PolylineEntity>().Single();
        Assert.Equal(3, line.Points.Count);
        var shelfEnd = line.Points[2];
        Assert.Equal(5, shelfEnd.Y, 9);                 // 旗竿は水平
        Assert.True(shelfEnd.X > 5 + 10 * 0.35);         // 一番長い行 (10 文字 × 0.35m) より長い
        var texts = parts.OfType<TextEntity>().ToList();
        Assert.Equal(3, texts.Count);
        Assert.Equal("As=D13@200", texts.OrderByDescending(t => t.Position.Y).First().Text); // 1 行目が一番上
        Assert.All(texts, t => Assert.True(t.Position.Y > 5));                                // 旗竿の上
        Assert.All(texts, t => Assert.Equal(TextHAlign.Left, t.HAlign));
        Assert.True(parts.OfType<HatchEntity>().Single().Solid);                             // 矢印

        // 回転しても旗竿と文字は水平のまま
        flag.Rotate(Vec2.Zero, Math.PI / 2);
        var rotated = flag.Explode().OfType<PolylineEntity>().Single();
        Assert.Equal(rotated.Points[1].Y, rotated.Points[2].Y, 9);
        Assert.True(rotated.Points[2].X < rotated.Points[1].X); // 先端が右側になったので旗竿は左へ
        Assert.All(flag.Explode().OfType<TextEntity>(), t => Assert.Equal(0, t.Rotation));
    });

    [Fact]
    public void Leader_MultiplePoints_TextOptional_StepBack_Dot() => RunSta(() =>
    {
        LeaderSettings.Tip = LeaderTip.Dot;
        var doc = new CadDocument { ScaleDenominator = 100 };
        var canvas = new CadCanvas { Document = doc };
        var tool = new LeaderTool(flag: false);
        canvas.SetTool(tool);
        tool.OnPoint(new Vec2(10, 0));
        tool.OnPoint(new Vec2(8, 2));
        tool.OnPoint(new Vec2(9, 9));       // 間違えた点
        tool.StepBack();
        tool.OnPoint(new Vec2(4, 2));
        Assert.False(tool.OnText("abc"));   // 点の指定中の文字は座標などとして扱う
        tool.OnEnter();                     // 点の指定を終える
        Assert.True(tool.OnText("No.1"));
        tool.OnEnter();

        var ld = Assert.Single(doc.Entities.OfType<LeaderEntity>());
        Assert.False(ld.IsFlag);
        Assert.Equal([new Vec2(10, 0), new Vec2(8, 2), new Vec2(4, 2)], ld.Points);
        Assert.Equal(LeaderTip.Dot, ld.Tip);
        var text = ld.Explode().OfType<TextEntity>().Single();
        Assert.Equal(TextHAlign.Right, text.HAlign); // 線が左へ向かうので文字は左側 (右寄せ)
        Assert.True(text.Position.X < 4);

        // 文字なしの引き出し線
        tool.OnPoint(Vec2.Zero);
        tool.OnPoint(new Vec2(3, 3));
        tool.OnEnter();
        tool.OnEnter();
        Assert.Equal(2, doc.Entities.OfType<LeaderEntity>().Count());
        Assert.Empty(doc.Entities.OfType<LeaderEntity>().Last().Explode().OfType<TextEntity>());

        // 選択: 文字の上でも線の上でも選べる
        Assert.True(ld.DistanceTo(text.Position) < 0.5);
        Assert.Equal(0, ld.DistanceTo(new Vec2(6, 2)), 9);
    });

    [Fact]
    public void ScaleAndClone_AndSavedAsPlainShapes()
    {
        var doc = new CadDocument { ScaleDenominator = 500 };
        var flag = new LeaderEntity(doc.CurrentLayer, [new Vec2(0, 0), new Vec2(10, 10)], true, "上段\n下段", 1.75, 1.5);
        var clone = (LeaderEntity)flag.Clone();
        clone.Scale(Vec2.Zero, 2);
        Assert.Equal(3.5, clone.TextHeight, 9);
        Assert.Equal(new Vec2(20, 20), clone.Points[1]);
        Assert.Equal(1.75, flag.TextHeight); // 元はそのまま

        doc.Entities.Add(flag);
        foreach (var ext in new[] { ".dxf", ".sfc", ".jww" })
        {
            var path = Path.Combine(Path.GetTempPath(), $"civilcad_leader_{Guid.NewGuid():N}{ext}");
            try
            {
                DrawingFiles.Save(doc, path);
                var loaded = new CadDocument();
                DrawingFiles.Open(loaded, path);
                var texts = loaded.Entities.OfType<TextEntity>().Select(t => t.Text).ToList();
                Assert.Contains("上段", texts);
                Assert.Contains("下段", texts);
                Assert.True(loaded.Entities.Count >= 4, $"{ext}: {loaded.Entities.Count}");
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
