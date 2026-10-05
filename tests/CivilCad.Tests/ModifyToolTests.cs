using System.Runtime.ExceptionServices;
using System.Windows.Media;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

/// <summary>修正ツール (トリム・延長・オフセット・回転・連結) の操作の流れ。画面は出さずにキャンバスだけで動かす。</summary>
public class ModifyToolTests
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

    private static void Preview(CadCanvas canvas)
    {
        using var dc = new DrawingVisual().RenderOpen();
        canvas.Tool.DrawPreview(dc);
    }

    [Fact]
    public void TrimJoinExtendOffsetRotate()
    {
        OnSta(() =>
        {
            var canvas = new CadCanvas();
            var doc = canvas.Document;
            var layer = doc.CurrentLayer;
            var h = new LineEntity(layer, new Vec2(0, 0), new Vec2(100, 0));
            var v1 = new LineEntity(layer, new Vec2(30, -20), new Vec2(30, 20));
            var v2 = new LineEntity(layer, new Vec2(70, -20), new Vec2(70, 20));
            doc.Entities.AddRange([h, v1, v2]);

            // トリム: 境界を選ばず Enter → 全図形が境界。横線の真ん中を切る
            canvas.SetTool(new TrimTool());
            canvas.Tool.OnEnter();
            canvas.Tool.OnMouseMove(new Vec2(50, 0.5));
            Preview(canvas);
            canvas.Tool.OnPoint(new Vec2(50, 0.5));
            Assert.DoesNotContain(h, doc.Entities);
            Assert.Equal(4, doc.Entities.Count);
            canvas.Tool.OnEnter();
            Assert.IsType<SelectTool>(canvas.Tool);

            // 連結: 切り分けた 2 本を選択 → 元の 1 本に戻る。元に戻すとトリム直後に戻る
            var pieces = doc.Entities.OfType<LineEntity>().Where(l => l.Start.Y == 0 && l.End.Y == 0).ToList();
            Assert.Equal(2, pieces.Count);
            canvas.SetSelection(pieces);
            Assert.StartsWith("連結: 2 個", JoinCommand.Run(canvas));
            Assert.Equal(100.0, Assert.Single(doc.Entities.OfType<LineEntity>(), l => l.Start.Y == 0 && l.End.Y == 0).Length, 9);
            doc.History.Undo();
            Assert.Equal(2, doc.Entities.OfType<LineEntity>().Count(l => l.Start.Y == 0 && l.End.Y == 0));
            doc.History.Undo(); // トリムも戻す。描画順も元どおり
            Assert.Equal(0, doc.Entities.IndexOf(h));

            // 延長: 先に境界 (v2) を選んでおく → 短い線の端をクリック
            var shortLine = new LineEntity(layer, new Vec2(0, 10), new Vec2(50, 10));
            doc.Entities.Add(shortLine);
            canvas.SetSelection([v2]);
            canvas.SetTool(new ExtendTool());
            canvas.Tool.OnMouseMove(new Vec2(49, 10));
            Preview(canvas);
            canvas.Tool.OnPoint(new Vec2(49, 10));
            Assert.Equal(new Vec2(70, 10), doc.Entities.OfType<LineEntity>().Single(l => l.Start == new Vec2(0, 10)).End);
            canvas.Tool.OnCancel();

            // オフセット: 距離 5 → 横線 → 上側
            canvas.SetTool(new OffsetTool());
            Assert.True(canvas.Tool.OnText("5"));
            canvas.Tool.OnPoint(new Vec2(90, 0));
            canvas.Tool.OnMouseMove(new Vec2(90, 3));
            Preview(canvas);
            canvas.Tool.OnPoint(new Vec2(90, 3));
            Assert.Contains(doc.Entities.OfType<LineEntity>(), l => l.Start == new Vec2(0, 5) && l.End == new Vec2(100, 5));
            canvas.Tool.OnCancel();

            // 回転: 横線を原点まわりに 90°。C で元を残すと複写になる
            canvas.SetSelection([h]);
            canvas.SetTool(new RotateTool());
            canvas.Tool.OnPoint(new Vec2(0, 0));
            canvas.Tool.OnMouseMove(new Vec2(10, 10));
            Preview(canvas);
            Assert.True(canvas.Tool.OnText("90"));
            Assert.True(h.End.DistanceTo(new Vec2(0, 100)) < 1e-9);
            Assert.IsType<SelectTool>(canvas.Tool);
            var count = doc.Entities.Count;
            canvas.SetSelection([h]);
            canvas.SetTool(new RotateTool());
            canvas.Tool.OnPoint(new Vec2(0, 0));
            Assert.True(canvas.Tool.OnText("C"));
            Assert.True(canvas.Tool.OnText("-90"));
            Assert.Equal(count + 1, doc.Entities.Count);
            Assert.True(h.End.DistanceTo(new Vec2(0, 100)) < 1e-9);
        });
    }

    [Fact]
    public void Join_NeedsTwoSelected()
    {
        OnSta(() =>
        {
            var canvas = new CadCanvas();
            Assert.StartsWith("連結: つなぎたい図形を 2 つ以上", JoinCommand.Run(canvas));
        });
    }

    [Fact]
    public void PolylineVertices_AreVertexSnaps()
    {
        var layer = new Layer("0", RgbColor.White);
        var open = new PolylineEntity(layer, [new Vec2(0, 0), new Vec2(10, 0), new Vec2(10, 10)]);
        Assert.Equal([SnapKind.Endpoint, SnapKind.Vertex, SnapKind.Endpoint],
            open.SnapPoints.Where(s => s.Kind != SnapKind.Midpoint).Select(s => s.Kind));
        var closed = new PolylineEntity(layer, [new Vec2(0, 0), new Vec2(10, 0), new Vec2(10, 10)], isClosed: true);
        Assert.All(closed.SnapPoints.Where(s => s.Kind != SnapKind.Midpoint), s => Assert.Equal(SnapKind.Vertex, s.Kind));

        // 頂点だけをオフにすると、途中の点には吸着しない
        var withoutVertex = SnapFinder.DefaultKinds.Where(k => k != SnapKind.Vertex).ToHashSet();
        Assert.Equal(SnapKind.Vertex, SnapFinder.Find([open], new Vec2(10.2, 0.1), 1, null, SnapFinder.DefaultKinds)?.Kind);
        Assert.NotEqual(SnapKind.Vertex, SnapFinder.Find([open], new Vec2(10.2, 0.1), 1, null, withoutVertex)?.Kind);
        Assert.Equal("頂点", SnapFinder.Label(SnapKind.Vertex));
    }
}
