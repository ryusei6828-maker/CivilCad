using System.Runtime.ExceptionServices;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

/// <summary>文字の変更 (ED とダブルクリック)。その場の入力欄は MainWindow の役目なので、ここでは状態の持ち方と図面への反映を調べる (画面は出さない)。</summary>
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

    [Fact]
    public void EditTool_EditsClickedText_HidesOriginalWhileEditing_AndUndo()
    {
        OnSta(() =>
        {
            var canvas = new CadCanvas();
            var doc = canvas.Document;
            var t = new TextEntity(doc.CurrentLayer, new Vec2(0, 0), "No.1", 2) { Color = new RgbColor(255, 0, 0) };
            var line = new LineEntity(doc.CurrentLayer, new Vec2(-5, 0.5), new Vec2(5, 0.5)); // 文字に重なる線
            doc.Entities.AddRange([t, line]);
            canvas.SetTool(new TextEditTool());
            Assert.Null(canvas.TextEdit);
            canvas.Tool.OnPoint(new Vec2(0.5, 0.5)); // 線と重なっていても文字を直す
            var edit = Assert.IsType<InPlaceTextEdit>(canvas.TextEdit);
            Assert.Same(t, edit.Original); // 直している間は元の文字を隠す (キャンバスが描かない)
            Assert.Equal("No.1", edit.Text);
            Assert.False(doc.History.CanUndo);

            edit.Text = "No.2+5.000";
            Assert.True(canvas.EndTextEdit(true));
            Assert.Null(canvas.TextEdit); // 隠すのをやめる
            var changed = Assert.Single(doc.Entities.OfType<TextEntity>());
            Assert.Equal("No.2+5.000", changed.Text);
            Assert.Equal(new RgbColor(255, 0, 0), changed.Color); // 色・位置・大きさはそのまま
            Assert.Equal(2, changed.Height);
            Assert.Equal(0, doc.Entities.IndexOf(changed)); // 描画順も元の位置
            Assert.IsType<TextEditTool>(canvas.Tool); // 続けて別の文字も変えられる

            doc.History.Undo();
            Assert.Same(t, Assert.Single(doc.Entities.OfType<TextEntity>()));
            Assert.Equal("No.1", t.Text);
            Assert.False(doc.History.CanUndo);
        });
    }

    [Fact]
    public void Cancel_NoChange_Or_Empty_LeavesText()
    {
        OnSta(() =>
        {
            var canvas = new CadCanvas();
            var doc = canvas.Document;
            var t = new TextEntity(doc.CurrentLayer, new Vec2(0, 0), "測点", 2);
            doc.Entities.Add(t);

            // やめる: 内容を変えても図面は元のまま
            canvas.BeginTextEdit(t);
            canvas.TextEdit!.Text = "別の文字";
            Assert.False(canvas.EndTextEdit(false));
            Assert.Null(canvas.TextEdit);
            Assert.Equal("測点", t.Text);

            // 変えずに確定 / 空にして確定: 何も起きない
            foreach (var text in new[] { "測点", "", " \n \n" })
            {
                canvas.BeginTextEdit(t);
                canvas.TextEdit!.Text = text;
                Assert.False(canvas.EndTextEdit(true));
                Assert.Null(canvas.TextEdit);
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
            Assert.IsType<SelectTool>(canvas.Tool);
            canvas.Tool.OnDoubleClick(new Vec2(0.5, 0.5));
            Assert.Same(t, canvas.TextEdit?.Original);
            canvas.TextEdit!.Text = "EP";
            Assert.True(canvas.EndTextEdit(true));
            var changed = Assert.Single(doc.Entities.OfType<TextEntity>());
            Assert.Equal("EP", changed.Text);
            Assert.Contains(changed, canvas.Selection); // 選択は変えた文字に引き継ぐ
            Assert.DoesNotContain(t, canvas.Selection);

            // 文字以外のダブルクリックは普通のクリック (その場の入力欄は出ない)
            var line = new LineEntity(doc.CurrentLayer, new Vec2(20, 0), new Vec2(30, 0));
            doc.Entities.Add(line);
            canvas.Tool.OnDoubleClick(new Vec2(25, 0));
            Assert.Null(canvas.TextEdit);
        });
    }

    [Fact]
    public void StartingAnother_Or_SwitchingTool_CommitsTheFirst()
    {
        OnSta(() =>
        {
            var canvas = new CadCanvas();
            var doc = canvas.Document;
            var a = new TextEntity(doc.CurrentLayer, new Vec2(0, 0), "A", 2);
            var b = new TextEntity(doc.CurrentLayer, new Vec2(20, 0), "B", 2);
            doc.Entities.AddRange([a, b]);
            canvas.BeginTextEdit(a);
            canvas.TextEdit!.Text = "A2";
            canvas.BeginTextEdit(b); // 別の文字を直し始める → 先の文字は確定
            Assert.Same(b, canvas.TextEdit!.Original);
            Assert.Equal(["A2", "B"], doc.Entities.OfType<TextEntity>().Select(x => x.Text).ToArray());
            canvas.TextEdit.Text = "B2";
            canvas.SetTool(new LineTool()); // コマンドを替える → 確定
            Assert.Null(canvas.TextEdit);
            Assert.Equal(["A2", "B2"], doc.Entities.OfType<TextEntity>().Select(x => x.Text).ToArray());
            doc.History.Undo();
            doc.History.Undo();
            Assert.False(doc.History.CanUndo);
            Assert.Equal(["A", "B"], doc.Entities.OfType<TextEntity>().Select(x => x.Text).ToArray());
        });
    }

    [Fact]
    public void EditedTextRemovedFromDocument_EndsTheEdit()
    {
        OnSta(() =>
        {
            var canvas = new CadCanvas();
            var doc = new CadDocument();
            canvas.Document = doc; // 図面の変更を受け取るのは、図面を渡したあと
            var t = new TextEntity(doc.CurrentLayer, new Vec2(0, 0), "A", 2);
            doc.Entities.Add(t);
            canvas.BeginTextEdit(t);
            doc.History.Execute(new CivilCad.Core.Commands.RemoveEntitiesCommand([t], "削除"));
            Assert.Null(canvas.TextEdit); // 図面から消えたら、隠したままにしない
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

            // 入力欄の最初の状態: 揃えはそのまま、「元の幅に合わせる」はオフ
            var edit = new InPlaceTextEdit(t, doc);
            Assert.Equal(TextHAlign.Center, edit.HAlign);
            Assert.True(edit.HasFitWidth);
            Assert.True(edit.KeepFitEnabled);
            Assert.False(edit.KeepFit);
            Assert.Equal(t.Position, edit.Anchor);

            edit.Text = "ABCD";
            Assert.True(TextEditing.Apply(canvas, t, edit.Result));
            var c = Assert.Single(doc.Entities.OfType<TextEntity>());
            Assert.Null(c.FitWidth);
            Assert.Equal(4, c.ApproxWidth); // 元の幅 20 には伸ばさない
            Assert.Equal(new Vec2(0, 0), c.Position); // 揃えを変えなければ位置もそのまま

            // 幅指定を残すにしたときは残る。複数行にすると選べなくなり、残らない
            var t2 = new TextEntity(doc.CurrentLayer, new Vec2(0, 5), "古い", 2) { FitWidth = 20 };
            doc.Entities.Add(t2);
            var edit2 = new InPlaceTextEdit(t2, doc) { Text = "新しい", KeepFit = true };
            Assert.True(TextEditing.Apply(canvas, t2, edit2.Result));
            Assert.Equal(20, doc.Entities.OfType<TextEntity>().Last().FitWidth);
            edit2.Text = "新しい\n文字";
            Assert.False(edit2.KeepFitEnabled);
            Assert.False(edit2.Result.KeepFit);
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
            var edit = new InPlaceTextEdit(t, doc) { HAlign = TextHAlign.Right };
            edit.SetVAlign(TextVAlign.Top);
            // 入力欄の基準点は、元の枠の右上
            Assert.Equal(10, edit.Anchor.X, 9);
            Assert.Equal(2, edit.Anchor.Y, 9);
            Assert.True(TextEditing.Apply(canvas, t, edit.Result));
            var c = Assert.Single(doc.Entities.OfType<TextEntity>());
            Assert.Equal(10, c.Position.X, 9);
            Assert.Equal(2, c.Position.Y, 9);
            Assert.Equal((0, 0, 10, 2), (c.Bounds.Min.X, c.Bounds.Min.Y, c.Bounds.Max.X, c.Bounds.Max.Y));

            // 回転した文字でも枠の中で動く
            var r = new TextEntity(doc.CurrentLayer, new Vec2(100, 0), "ABCDEFGHIJ", 2, Math.PI / 2);
            doc.Entities.Add(r);
            var editR = new InPlaceTextEdit(r, doc) { HAlign = TextHAlign.Center };
            editR.SetVAlign(TextVAlign.Middle);
            Assert.True(TextEditing.Apply(canvas, r, editR.Result));
            var rc = doc.Entities.OfType<TextEntity>().Last();
            Assert.Equal(99, rc.Position.X, 9);
            Assert.Equal(5, rc.Position.Y, 9);
        });
    }

    [Fact]
    public void HeightInMm_ConvertsWithScale_AndKeepsOriginalWhenUntouched()
    {
        OnSta(() =>
        {
            var doc = new CadDocument();
            var t = new TextEntity(doc.CurrentLayer, Vec2.Zero, "AB", doc.PaperMmToMeters(3));
            var edit = new InPlaceTextEdit(t, doc);
            Assert.True(edit.HeightEditable);
            Assert.Equal(3, edit.HeightMm);
            Assert.True(edit.SetHeightMm(3));
            Assert.Equal(t.Height, edit.HeightM); // 触っていなければ元の高さそのまま
            Assert.True(edit.SetHeightMm(5));
            Assert.Equal(doc.PaperMmToMeters(5), edit.HeightM, 12);
            Assert.False(edit.SetHeightMm(0));
            Assert.False(edit.SetHeightMm(double.NaN));
            Assert.Equal(doc.PaperMmToMeters(5), edit.HeightM, 12);

            // 画面で一定の文字は高さを直せない
            var fixedText = new TextEntity(doc.CurrentLayer, Vec2.Zero, "px", 1) { ScreenHeightPx = 14 };
            var e2 = new InPlaceTextEdit(fixedText, doc);
            Assert.False(e2.HeightEditable);
            Assert.False(e2.SetHeightMm(9));
            Assert.Equal(1, e2.HeightM);
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
            canvas.BeginTextEdit(t);
            var edit = canvas.TextEdit!;
            edit.Text = "一行目\r\n二行目\r\n";
            Assert.True(edit.IsMultiLine);
            Assert.True(edit.SetHeightMm(3 / doc.ScaleDenominator * 1000)); // 文字高 3 m
            Assert.True(canvas.EndTextEdit(true));
            var c = Assert.Single(doc.Entities.OfType<TextEntity>());
            Assert.Equal("一行目\n二行目", c.Text);
            Assert.Equal(3, c.Height, 9);
            doc.History.Undo();
            Assert.Same(t, Assert.Single(doc.Entities.OfType<TextEntity>()));
            Assert.Equal("AB", t.Text);
            Assert.False(doc.History.CanUndo);
        });
    }

    [Fact]
    public void Apply_WhitespaceOnly_IsRejected_And_NoChange_ReturnsFalse()
    {
        OnSta(() =>
        {
            var canvas = new CadCanvas();
            var doc = canvas.Document;
            var t = new TextEntity(doc.CurrentLayer, new Vec2(0, 0), "AB", 2);
            doc.Entities.Add(t);
            Assert.False(TextEditing.Apply(canvas, t, new TextEditResult(" \n \n", 2, TextHAlign.Left, TextVAlign.Bottom, false)));
            Assert.False(TextEditing.Apply(canvas, t, new TextEditResult("AB", 2, TextHAlign.Left, TextVAlign.Baseline, false)));
            Assert.False(doc.History.CanUndo);

            // 図面から消えた文字には何もしない
            doc.Entities.Remove(t);
            Assert.False(TextEditing.Apply(canvas, t, new TextEditResult("CD", 2, TextHAlign.Left, TextVAlign.Bottom, false)));
        });
    }
}
