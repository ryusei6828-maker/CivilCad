using System.Diagnostics;
using System.Runtime.ExceptionServices;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

/// <summary>座標管理の一覧: 図面の選択を一覧に映す (点が多くても固まらない)。</summary>
public class CoordinatePanelSelectionTests
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
    public void BulkSelectGrid_SelectsOnlyGivenItems() => RunSta(() =>
    {
        var items = Enumerable.Range(0, 6).Select(i => new CoordinateItem { Name = $"P{i}" }).ToList();
        var grid = new BulkSelectGrid { CanUserAddRows = false, ItemsSource = items };

        grid.SelectOnly([items[1], items[4]]);
        Assert.Equal(["P1", "P4"], grid.SelectedItems.OfType<CoordinateItem>().Select(i => i.Name).Order());
        grid.SelectOnly([items[2]]);
        Assert.Equal(["P2"], grid.SelectedItems.OfType<CoordinateItem>().Select(i => i.Name));
        grid.SelectOnly(items); // 全部
        Assert.Equal(6, grid.SelectedItems.OfType<CoordinateItem>().Count());
        grid.SelectOnly([]);
        Assert.Empty(grid.SelectedItems);
    });

    [Fact]
    public void ManyPoints_SelectionSyncDoesNotFreeze() => RunSta(() =>
    {
        const int n = 8000;
        var canvas = new CadCanvas();
        var doc = canvas.Document;
        for (var i = 0; i < n; i++) doc.Entities.Add(new PointEntity(doc.CurrentLayer, new Vec2(i % 100, i / 100)) { Name = $"P{i + 1}" });
        var panel = new CoordinatePanel();
        panel.Attach(canvas);
        Assert.Equal(n, panel.Items.Count);

        // 1 行ずつ選ぶと 8000 点で 10 秒ほどかかっていた (行の数の 2 乗)。まとめて選べば一瞬
        var sw = Stopwatch.StartNew();
        canvas.SetSelection(doc.Entities.OfType<PointEntity>());                          // 全部
        Assert.Equal(n, panel.SelectedCount);
        canvas.SetSelection(doc.Entities.OfType<PointEntity>().Where((_, i) => i % 2 == 0)); // 半分
        panel.Refresh();                                                                   // 一覧を作り直しても選択を戻す
        Assert.True(sw.ElapsedMilliseconds < 3000, $"選択の反映に {sw.ElapsedMilliseconds} ms かかりました");
        Assert.Equal(n / 2, panel.SelectedCount);
    });
}
