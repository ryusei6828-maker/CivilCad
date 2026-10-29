using System.IO;
using System.Runtime.ExceptionServices;
using CivilCad.App.Alignment;
using CivilCad.App.IO;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

/// <summary>グループ化 (G): まとめて選び、まとめて移動・複写する。</summary>
public class GroupTests
{
    private static void RunSta(Action<CadDocument, CadCanvas> action)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                var doc = new CadDocument();
                action(doc, new CadCanvas { Document = doc });
            }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    private static (LineEntity A, CircleEntity B, LineEntity Other) Sample(CadDocument doc)
    {
        var a = new LineEntity(doc.CurrentLayer, new Vec2(0, 0), new Vec2(10, 0));
        var b = new CircleEntity(doc.CurrentLayer, new Vec2(5, 5), 2);
        var other = new LineEntity(doc.CurrentLayer, new Vec2(0, 20), new Vec2(10, 20));
        doc.Entities.AddRange([a, b, other]);
        doc.History.Execute(new SetGroupCommand([a, b], Guid.NewGuid(), "グループ化"));
        return (a, b, other);
    }

    [Fact]
    public void ClickOneMember_SelectsWholeGroup_UndoUngroups() => RunSta((doc, canvas) =>
    {
        var (a, b, other) = Sample(doc);

        canvas.SetSelection([canvas.HitTest(new Vec2(5, 0))!]);
        Assert.Equal([a, b], canvas.Selection.OrderBy(doc.Entities.IndexOf));

        // Shift+クリックで外すとグループごと外れる
        canvas.AddToSelection([other]);
        canvas.AddToSelection([b], remove: true);
        Assert.Equal([other], canvas.Selection);

        // 元に戻すとグループが無くなり、1 つずつ選べる
        doc.History.Undo();
        canvas.SetSelection([a]);
        Assert.Equal([a], canvas.Selection);
    });

    [Fact]
    public void BoxTouchingOneMember_SelectsWholeGroup() => RunSta((doc, canvas) =>
    {
        var (a, b, _) = Sample(doc);
        canvas.SetSelection(BoxDrag.Hits(doc, new Vec2(-1, -1), new Vec2(11, 1))); // 線 a だけが入る範囲
        Assert.Equal([a, b], canvas.Selection.OrderBy(doc.Entities.IndexOf));
    });

    [Fact]
    public void Copy_MakesNewGroup_MoveMovesAll() => RunSta((doc, canvas) =>
    {
        var (a, b, other) = Sample(doc);
        var original = a.GroupId;

        canvas.SetSelection([b]);
        var copy = new MoveCopyTool(copy: true);
        canvas.SetTool(copy);
        copy.OnPoint(new Vec2(0, 0));
        copy.OnPoint(new Vec2(100, 0));

        var copies = doc.Entities.Skip(3).ToList();
        Assert.Equal(2, copies.Count);
        Assert.Equal(new Vec2(100, 0), ((LineEntity)copies[0]).Start);
        Assert.NotNull(copies[0].GroupId);
        Assert.Equal(copies[0].GroupId, copies[1].GroupId);
        Assert.NotEqual(original, copies[0].GroupId); // 元のグループとは別
        Assert.Equal(original, b.GroupId);

        // 複写したものをクリックしても、元のグループは選ばれない
        canvas.SetSelection([copies[1]]);
        Assert.Equal(copies, canvas.Selection.OrderBy(doc.Entities.IndexOf));

        // 移動はグループの全部が動く
        canvas.SetSelection([a]);
        var move = new MoveCopyTool(copy: false);
        canvas.SetTool(move);
        move.OnPoint(new Vec2(0, 0));
        move.OnPoint(new Vec2(0, 50));
        Assert.Equal(new Vec2(0, 50), a.Start);
        Assert.Equal(new Vec2(5, 55), b.Center);
        Assert.Equal(new Vec2(0, 20), other.Start);
    });

    [Fact]
    public void DoubleClick_EntersGroupEdit_OnlyMembersEditable_EscExits() => RunSta((doc, canvas) =>
    {
        var (a, b, other) = Sample(doc);
        var group = a.GroupId!.Value;
        var select = new SelectTool();
        canvas.SetTool(select);
        var changes = 0;
        canvas.GroupEditChanged += (_, _) => changes++;

        select.OnDoubleClick(new Vec2(5, 0)); // グループの線をダブルクリック
        Assert.True(canvas.IsGroupEditing);
        Assert.Equal(1, changes);
        Assert.Empty(canvas.Selection);

        // グループの中は 1 つずつ選べ、グループの外は選べない
        canvas.SetSelection([canvas.HitTest(new Vec2(5, 0))!]);
        Assert.Equal([a], canvas.Selection);
        Assert.Null(canvas.HitTest(new Vec2(5, 20)));
        Assert.False(doc.IsEditable(other));
        Assert.Equal([a, b], BoxDrag.Hits(doc, new Vec2(-50, -50), new Vec2(50, 50)));

        // 編集中に描いた図形はグループに入る
        var added = new LineEntity(doc.CurrentLayer, new Vec2(0, 2), new Vec2(10, 2));
        doc.History.Execute(new AddEntitiesCommand([added]));
        Assert.Equal(group, added.GroupId);

        // Esc で終了。終えるとグループごと選ぶに戻る
        select.OnCancel();
        Assert.False(canvas.IsGroupEditing);
        Assert.Equal(2, changes);
        canvas.SetSelection([a]);
        Assert.Equal([a, b, added], canvas.Selection.OrderBy(doc.Entities.IndexOf));
        Assert.True(doc.IsEditable(other));
    });

    [Fact]
    public void SwitchingDocument_EndsGroupEdit() => RunSta((doc, canvas) =>
    {
        var (a, _, _) = Sample(doc);
        canvas.EnterGroupEdit(a.GroupId!.Value);
        canvas.Document = new CadDocument();
        Assert.Null(doc.EditingGroupId);
        Assert.False(canvas.IsGroupEditing);
    });

    [Fact]
    public void Group_SurvivesProjectAndDxf()
    {
        var doc = new CadDocument();
        Sample(doc);
        var dir = Path.Combine(Path.GetTempPath(), $"civilcad_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            ProjectFile.Save(Path.Combine(dir, "p.ccp"), [new ProjectSheet("s", doc, AlignmentInput.Sample(), false)], 0);
            AssertGroups(ProjectFile.Load(Path.Combine(dir, "p.ccp")).Sheets[0].Doc);

            var dxf = Path.Combine(dir, "a.dxf");
            DxfIO.Save(doc, dxf);
            var loaded = new CadDocument();
            DxfIO.Load(loaded, dxf);
            AssertGroups(loaded);
        }
        finally { Directory.Delete(dir, true); }

        static void AssertGroups(CadDocument d)
        {
            var line = d.Entities.OfType<LineEntity>().Single(l => l.Start == new Vec2(0, 0));
            var circle = d.Entities.OfType<CircleEntity>().Single();
            var other = d.Entities.OfType<LineEntity>().Single(l => l.Start == new Vec2(0, 20));
            Assert.NotNull(line.GroupId);
            Assert.Equal(line.GroupId, circle.GroupId);
            Assert.Null(other.GroupId);
        }
    }
}
