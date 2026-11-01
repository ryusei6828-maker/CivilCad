using System.Runtime.ExceptionServices;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

/// <summary>文字の変更 (ED とダブルクリック)。入力の窓は差し替えて、画面は出さない。</summary>
[Collection("TextEditing")] // TextEditing.Ask を差し替えるので、ほかのテストと並べて走らせない
public class TextEditTests
{
    private static void OnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    /// <summary>文字だけ答える (大きさ・揃えはそのまま、幅指定も残す)。</summary>
    private static void WithAnswer(Func<string, string?> answer, Action action) =>
        WithResult(t => answer(t.Text) is string s ? new TextEditResult(s, t.Height, t.HAlign, t.VAlign, true) : null, action);

    private static void WithResult(Func<TextEntity, TextEditResult?> answer, Action action)
    {
        var original = TextEditing.Ask;
        TextEditing.Ask = (_, current) => answer(current);
        try { action(); }
        finally { TextEditing.Ask = original; }
    }

    [Fact]
    public void EditTool_ChangesClickedText_AndUndo()
    {
        OnSta(() =>
        {
            var canvas = new CadCanvas();
            var doc = canvas.Document;
            var t = new TextEntity(doc.CurrentLayer, new Vec2(0, 0), "No.1", 2) { Color = new RgbColor(255, 0, 0) };
            var line = new LineEntity(doc.CurrentLayer, new Vec2(-5, 0.5), new Vec2(5, 0.5)); // 文字に重なる線
            doc.Entities.AddRange([t, line]);
            string? asked = null;
            WithAnswer(current => { asked = current; return "No.2+5.000"; }, () =>
            {
                canvas.SetTool(new TextEditTool());
                canvas.Tool.OnPoint(new Vec2(0.5, 0.5)); // 線と重なっていても文字を変える
            });
            Assert.Equal("No.1", asked);
            var changed = Assert.Single(doc.Entities.OfType<TextEntity>());
            Assert.Equal("No.2+5.000", changed.Text);
            Assert.Equal(new RgbColor(255, 0, 0), changed.Color); // 色・位置・大きさはそのまま
            Assert.Equal(2, changed.Height);
            Assert.Equal(0, doc.Entities.IndexOf(changed)); // 描画順も元の位置
            Assert.IsType<TextEditTool>(canvas.Tool); // 続けて別の文字も変えられる

            doc.History.Undo();
            Assert.Same(t, Assert.Single(doc.Entities.OfType<TextEntity>()));
            Assert.Equal("No.1", t.Text);
        });
    }

    [Fact]
    public void Cancel_Or_Empty_LeavesText()
    {
        OnSta(() =>
        {
            var canvas = new CadCanvas();
            var doc = canvas.Document;
            var t = new TextEntity(doc.CurrentLayer, new Vec2(0, 0), "測点", 2);
            doc.Entities.Add(t);
            foreach (var answer in new string?[] { null, "", "測点" })
            {
                WithAnswer(_ => answer, () => Assert.False(TextEditing.Edit(canvas, t)));
                Assert.Same(t, Assert.Single(doc.Entities));
                Assert.Equal("測点", t.Text);
            }
            Assert.False(doc.History.CanUndo);
        });
    }

    [Fact]
    public void DoubleClickText_InSelect_EditsAndKeepsSelection()
    {
        OnSta(() =>
        {
            var canvas = new CadCanvas();
            var doc = canvas.Document;
            var t = new TextEntity(doc.CurrentLayer, new Vec2(0, 0), "BP", 2);
            doc.Entities.Add(t);
            canvas.SetSelection([t]);
            WithAnswer(_ => "EP", () =>
            {
                Assert.IsType<SelectTool>(canvas.Tool);
                canvas.Tool.OnDoubleClick(new Vec2(0.5, 0.5));
            });
            var changed = Assert.Single(doc.Entities.OfType<TextEntity>());
            Assert.Equal("EP", changed.Text);
            Assert.Contains(changed, canvas.Selection); // 選択は変えた文字に引き継ぐ
            Assert.DoesNotContain(t, canvas.Selection);

            // 文字以外のダブルクリックは普通のクリック (何も起きない)
            var line = new LineEntity(doc.CurrentLayer, new Vec2(20, 0), new Vec2(30, 0));
            doc.Entities.Add(line);
            WithAnswer(_ => throw new InvalidOperationException("文字以外で尋ねてはいけない"), () => canvas.Tool.OnDoubleClick(new Vec2(25, 0)));
        });
    }

    [Fact]
    public void FitWidthCenterText_NewText_IsNotStretched_WhenKeepFitOff()
    {
        OnSta(() =>
        {
            var canvas = new CadCanvas();
            var doc = canvas.Document;
            var t = new TextEntity(doc.CurrentLayer, new Vec2(0, 0), "古い文字", 2) { HAlign = TextHAlign.Center, FitWidth = 20 };
            doc.Entities.Add(t);
            WithResult(x => new TextEditResult("ABCD", x.Height, x.HAlign, x.VAlign, false), () => Assert.True(TextEditing.Edit(canvas, t)));
            var c = Assert.Single(doc.Entities.OfType<TextEntity>());
            Assert.Null(c.FitWidth);
            Assert.Equal(4, c.ApproxWidth); // 元の幅 20 には伸ばさない
            Assert.Equal(new Vec2(0, 0), c.Position); // 揃えを変えなければ位置もそのまま

            // 幅指定を残すにしたときは残る
            var t2 = new TextEntity(doc.CurrentLayer, new Vec2(0, 5), "古い", 2) { FitWidth = 20 };
            doc.Entities.Add(t2);
            WithResult(x => new TextEditResult("新しい", x.Height, x.HAlign, x.VAlign, true), () => Assert.True(TextEditing.Edit(canvas, t2)));
            Assert.Equal(20, doc.Entities.OfType<TextEntity>().Last().FitWidth);
        });
    }

    [Fact]
    public void ChangingAlignment_KeepsTextInTheOldBox()
    {
        OnSta(() =>
        {
            var canvas = new CadCanvas();
            var doc = canvas.Document;
            var t = new TextEntity(doc.CurrentLayer, new Vec2(0, 0), "ABCDEFGHIJ", 2); // 幅 10、左下
            doc.Entities.Add(t);
            WithResult(x => new TextEditResult(x.Text, x.Height, TextHAlign.Right, TextVAlign.Top, false), () => Assert.True(TextEditing.Edit(canvas, t)));
            var c = Assert.Single(doc.Entities.OfType<TextEntity>());
            Assert.Equal(10, c.Position.X, 9); // 元の枠の右上
            Assert.Equal(2, c.Position.Y, 9);
            Assert.Equal((0, 0, 10, 2), (c.Bounds.Min.X, c.Bounds.Min.Y, c.Bounds.Max.X, c.Bounds.Max.Y));

            // 回転した文字でも枠の中で動く
            var r = new TextEntity(doc.CurrentLayer, new Vec2(100, 0), "ABCDEFGHIJ", 2, Math.PI / 2);
            doc.Entities.Add(r);
            WithResult(x => new TextEditResult(x.Text, x.Height, TextHAlign.Center, TextVAlign.Middle, false), () => Assert.True(TextEditing.Edit(canvas, r)));
            var rc = doc.Entities.OfType<TextEntity>().Last();
            Assert.Equal(99, rc.Position.X, 9);
            Assert.Equal(5, rc.Position.Y, 9);
        });
    }

    [Fact]
    public void MultiLineEdit_AndHeightChange_UndoInOneStep()
    {
        OnSta(() =>
        {
            var canvas = new CadCanvas();
            var doc = canvas.Document;
            var t = new TextEntity(doc.CurrentLayer, new Vec2(0, 0), "AB", 2);
            doc.Entities.Add(t);
            WithResult(x => new TextEditResult("一行目\r\n二行目\r\n", 3, TextHAlign.Left, TextVAlign.Bottom, false), () => Assert.True(TextEditing.Edit(canvas, t)));
            var c = Assert.Single(doc.Entities.OfType<TextEntity>());
            Assert.Equal("一行目\n二行目", c.Text);
            Assert.Equal(3, c.Height);
            doc.History.Undo();
            Assert.Same(t, Assert.Single(doc.Entities.OfType<TextEntity>()));
            Assert.Equal("AB", t.Text);
            Assert.False(doc.History.CanUndo);
        });
    }

    [Fact]
    public void WhitespaceOnly_IsRejected_And_NoChange_ReturnsFalse()
    {
        OnSta(() =>
        {
            var canvas = new CadCanvas();
            var doc = canvas.Document;
            var t = new TextEntity(doc.CurrentLayer, new Vec2(0, 0), "AB", 2);
            doc.Entities.Add(t);
            WithAnswer(_ => " \n \n", () => Assert.False(TextEditing.Edit(canvas, t)));
            WithResult(x => new TextEditResult(x.Text, x.Height, x.HAlign, TextVAlign.Baseline, false), () => Assert.False(TextEditing.Edit(canvas, t)));
            Assert.False(doc.History.CanUndo);
        });
    }
}
