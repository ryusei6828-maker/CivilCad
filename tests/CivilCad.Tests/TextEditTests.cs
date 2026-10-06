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

    private static void WithAnswer(Func<string, string?> answer, Action action)
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
}
