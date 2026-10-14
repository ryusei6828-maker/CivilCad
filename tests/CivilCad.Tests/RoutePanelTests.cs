using System.Runtime.ExceptionServices;
using CivilCad.Alignment;
using CivilCad.App.Alignment;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

public class RoutePanelTests
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

    // Excel で 属性・点名・X・Y・R・A1 の表をコピーしたときのクリップボード (タブ区切り、見出し付き)
    private const string ExcelTable =
        "属性\t点名\tX\tY\tR\tA1\r\n" +
        "BP\tBP\t0\t0\t\t\r\n" +
        "IP\tIP1\t200\t150\t300\t\r\n" +
        "クロソイド\tIP2\t250\t450\t250\t120\r\n" +
        "EP\tEP\t450\t600\t\t\r\n";

    [Fact]
    public void Paste_FromExcel_SkipsHeader_NormalizesKinds()
    {
        var rows = new List<RouteRow>();
        Assert.Equal(4, RouteTable.Paste(rows, 0, 0, ExcelTable));
        Assert.Equal(["始点", "IP", "クロソイド", "終点"], rows.Select(r => r.Kind));
        Assert.Equal("250", rows[2].R);
        Assert.Equal("120", rows[2].A1);

        // 点名・X・Y・R だけの表を「点名」の列から貼る (属性は位置で決まる、点名が空なら付ける)
        var rows2 = new List<RouteRow>();
        RouteTable.Paste(rows2, 0, 1, "A\t10\t20\t\nB\t30\t40\t100\n\t50\t90\t\n");
        Assert.Equal(["始点", "IP", "終点"], rows2.Select(r => r.Kind));
        Assert.Equal("EP", rows2[2].Name);
        Assert.Equal("100", rows2[1].R);

        // 途中のセルから貼ると、その位置から上書き・行を足す
        RouteTable.Paste(rows2, 2, 2, "60\t95\n70\t99");
        Assert.Equal("60", rows2[2].X);
        Assert.Equal(4, rows2.Count);
        Assert.Equal("終点", rows2[3].Kind);
        Assert.Equal("IP", rows2[2].Kind);
    }

    [Fact]
    public void ToInput_ValidatesRows_WithRowNumbers()
    {
        var rows = new List<RouteRow>();
        RouteTable.Paste(rows, 0, 0, ExcelTable);
        var input = RouteTable.ToInput(rows, new AlignmentInput());
        Assert.Equal(4, input.Rows.Count);
        Assert.Equal(120, input.Rows[2].A2); // A2 が空なら A1 と同じ
        Assert.Equal(0, input.Rows[1].A1);   // IP にはクロソイドを付けない

        rows[2].A1 = "";
        var ex = Assert.Throws<AlignmentException>(() => RouteTable.ToInput(rows, new AlignmentInput()));
        Assert.Contains("3 行目", ex.Message);
        Assert.Contains("A1", ex.Message);
        rows[2].A1 = "120";
        rows[1].X = "abc";
        Assert.Contains("2 行目", Assert.Throws<AlignmentException>(() => RouteTable.ToInput(rows, new AlignmentInput())).Message);
    }

    [Fact]
    public void PassingPoints_PitchSubAndMainPoints()
    {
        var input = new AlignmentInput
        {
            Rows = [new() { Name = "BP", X = 0, Y = 0 }, new() { Name = "EP", X = 0, Y = 65 }],
            Pitch = 20,
            SubPitch = 10,
        };
        var al = input.Compute();
        var pts = input.PassingPoints(al);
        Assert.Equal(["BP", "No.0+10.00", "No.1", "No.1+10.00", "No.2", "No.2+10.00", "No.3", "EP"], pts.Select(p => p.Name));
        Assert.Equal(AlignmentInput.KindCenter, pts[2].Kind);
        Assert.Equal(20, pts[2].Point.X, 9);           // 東へ 20m

        input.Pitch = 50;
        input.SubPitch = 0;
        input.IncludeMainPoints = false;
        Assert.Equal(["No.0", "No.1"], input.PassingPoints(al).Select(p => p.Name)); // 50m ごとの No.杭
    }

    [Fact]
    public void Panel_PickFromDrawing_ComputeChooseAndDraw() => RunSta(() =>
    {
        var doc = new CadDocument { ScaleDenominator = 500 };
        var canvas = new CadCanvas { Document = doc };
        var panel = new RoutePanel();
        panel.Attach(canvas);
        (AlignmentInput Input, CivilCad.Alignment.HorizontalAlignment Al)? drawn = null;
        panel.Drawn += (i, a) => drawn = (i, a);

        // 図面から追加: 3 点クリックして Enter → 始点・IP・終点
        panel.PickAppend();
        var tool = Assert.IsType<RoutePickTool>(canvas.Tool);
        tool.OnPoint(new Vec2(0, 0));      // 図面の x = 東, y = 北
        tool.OnPoint(new Vec2(100, 0));
        tool.OnPoint(new Vec2(100, 100));
        tool.OnEnter();
        Assert.Equal(["始点", "IP", "終点"], panel.Rows.Where(r => !r.IsEmpty).Select(r => r.Kind));
        Assert.Equal("100.000", panel.Rows[1].Y);  // 東 = 測量の Y
        Assert.Equal("0.000", panel.Rows[1].X);

        // IP に R を入れて計算 → 通過点
        panel.Rows[1].R = "50";
        Assert.True(panel.Compute());
        Assert.NotNull(panel.Result);
        Assert.Contains(panel.Passing, p => p.点名 == "No.1" && p.種類 == AlignmentInput.KindCenter);
        Assert.Contains(panel.Passing, p => p.種類 == AlignmentInput.KindMain && p.点名 == "BC1");

        // 選んだ行を図面で指定: IP の座標を置き直す
        panel.Grid.CurrentCell = new System.Windows.Controls.DataGridCellInfo(panel.Rows[1], panel.Grid.Columns[2]);
        panel.PickSelected();
        canvas.Tool.OnPoint(new Vec2(110, -5));
        Assert.Equal("110.000", panel.Rows[1].Y);
        Assert.Equal("-5.000", panel.Rows[1].X);
        Assert.Null(panel.Result);                // 表が変わったので計算し直す

        // 作図: 中心線・No.杭と、通過点が座標管理に登録される
        Assert.True(panel.Draw());
        Assert.NotNull(drawn);
        Assert.Contains(doc.Entities.OfType<PointEntity>(), p => p.Name == "路線1 No.1" && p.Tags == AlignmentInput.KindCenter);
        Assert.Contains(doc.Entities.OfType<ArcEntity>(), a => Math.Abs(a.Radius - 50) < 1e-6);
        var count = doc.Entities.Count;
        Assert.True(panel.Draw());                // 同じ路線名で作図し直すと置き換え
        Assert.Equal(count, doc.Entities.Count);
    });
}
