using System.IO;
using System.Runtime.ExceptionServices;
using System.Text;
using CivilCad.App.Alignment;
using CivilCad.App.IO;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

/// <summary>座標管理: 登録された座標 (点名のある点) の一覧・編集・書き出し。幅杭の座標。</summary>
public class CoordinateRegistryTests
{
    private static void RunSta(Action<CadDocument, CadCanvas> action)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                var doc = new CadDocument { ScaleDenominator = 500 };
                action(doc, new CadCanvas { Document = doc });
            }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    private static string Temp(string ext) => Path.Combine(Path.GetTempPath(), "civilcad-test-" + Guid.NewGuid().ToString("N") + ext);

    [Fact]
    public void WidthStakes_AreRegisteredWithCoordinates()
    {
        var doc = new CadDocument { ScaleDenominator = 500 };
        // 北へまっすぐ 100m、測点間隔 20m、左 5.5m・右 3.0m
        var input = new AlignmentInput
        {
            Name = "路線A",
            Pitch = 20,
            LeftWidths = "3.0, 5.5",
            RightWidths = "3.0",
            Rows = [new() { Name = "BP", X = 0, Y = 0 }, new() { Name = "EP", X = 100, Y = 0 }],
        };
        doc.History.Execute(AlignmentDrawer.CreateDrawCommand(doc, input.Compute(), input));
        var rows = CoordinateRegistry.Rows(doc);
        Assert.Equal(12, rows.Count); // No.0〜No.5 の左右
        var no1Left = Assert.Single(rows, r => r.Name == "路線A No.1 左5.5");
        Assert.Equal((20.0, -5.5), (Math.Round(no1Left.X, 6), Math.Round(no1Left.Y, 6))); // X = 北, Y = 東 (左は西)
        var no5Right = Assert.Single(rows, r => r.Name == "路線A No.5 右3");
        Assert.Equal((100.0, 3.0), (Math.Round(no5Right.X, 6), Math.Round(no5Right.Y, 6)));
    }

    [Fact]
    public void Export_CsvAndSima_RoundTrip()
    {
        var doc = new CadDocument();
        doc.Entities.Add(new PointEntity(doc.CurrentLayer, doc.FromSurvey(-95537.123, -60760.5)) { Name = "BM1", Z = 12.345 });
        doc.Entities.Add(new PointEntity(doc.CurrentLayer, doc.FromSurvey(-95500, -60700)) { Name = "幅杭,左" });
        doc.Entities.Add(new PointEntity(doc.CurrentLayer, new Vec2(1, 1))); // 点名なしは一覧に出さない
        var rows = CoordinateRegistry.Rows(doc);
        Assert.Equal(2, rows.Count);

        var csv = Temp(".csv");
        var sim = Temp(".sim");
        try
        {
            CoordinateRegistry.ExportCsv(rows, csv);
            var text = File.ReadAllText(csv, Encoding.UTF8);
            Assert.StartsWith("点名,X,Y,Z", text.TrimStart('﻿'));
            Assert.Contains("BM1,-95537.123,-60760.500,12.345", text);
            Assert.Contains("\"幅杭,左\",-95500.000,-60700.000,", text);

            CoordinateRegistry.ExportSima(rows, sim);
            var back = PointImport.Load(sim);
            Assert.Equal(["BM1", "幅杭，左"], back.Points.Select(p => p.Name));
            Assert.Equal(-95537.123, back.Points[0].X, 6);
            Assert.Equal(-60760.5, back.Points[0].Y, 6);
            Assert.Equal(12.345, back.Points[0].Z);
            Assert.Null(back.Points[1].Z);
        }
        finally
        {
            File.Delete(csv);
            File.Delete(sim);
        }
    }

    [Fact]
    public void PointImport_And_PointTool_RegisterNamedPoints() => RunSta((doc, canvas) =>
    {
        var file = new PointFile([new SurveyPoint("1", "T1", 100, 200, 5.5)], [], 0);
        doc.Entities.AddRange(new PointImportOptions().Build(doc, file));
        var imported = Assert.Single(CoordinateRegistry.Rows(doc));
        Assert.Equal(("T1", 100.0, 200.0, 5.5), (imported.Name, imported.X, imported.Y, imported.Z!.Value));

        doc.Entities.Add(new PointEntity(doc.CurrentLayer, Vec2.Zero) { Name = "P1" });
        var tool = new PointTool();
        canvas.SetTool(tool);
        tool.OnPoint(new Vec2(10, 10));
        tool.OnPoint(new Vec2(20, 10));
        Assert.Equal(["T1", "P1", "P2", "P3"], CoordinateRegistry.Rows(doc).Select(r => r.Name)); // 使っていない番号
    });

    [Theory]
    [InlineData(".dxf")]
    [InlineData(".dwg")]
    public void Save_KeepsNameAndZ(string ext)
    {
        var doc = new CadDocument();
        doc.Entities.Add(new PointEntity(doc.CurrentLayer, new Vec2(10, 20)) { Name = "BM1", Z = 3.25 });
        doc.Entities.Add(new PointEntity(doc.CurrentLayer, new Vec2(30, 40)) { Name = "No.1 左" });
        var path = Temp(ext);
        try
        {
            DrawingFiles.Save(doc, path);
            var back = new CadDocument();
            DrawingFiles.Open(back, path);
            var pts = back.Entities.OfType<PointEntity>().OrderBy(p => p.Position.X).ToList();
            Assert.Equal(("BM1", 3.25), (pts[0].Name, pts[0].Z!.Value));
            Assert.Equal("No.1 左", pts[1].Name);
            Assert.Null(pts[1].Z);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Panel_ListsEditsAndDeletes() => RunSta((doc, canvas) =>
    {
        doc.SetSurveyFrame(Math.PI / 2, Vec2.Zero); // 座標設定の向きも含めて表示・入力する
        var bm = new PointEntity(doc.CurrentLayer, doc.FromSurvey(100, 200)) { Name = "BM1", Z = 1 };
        doc.Entities.AddRange([bm, new PointEntity(doc.CurrentLayer, doc.FromSurvey(110, 200)) { Name = "BM2" }]);
        var panel = new CoordinatePanel();
        panel.Attach(canvas);
        Assert.Equal(2, panel.Items.Count);
        var item = panel.Items[0];
        Assert.Equal(("BM1", "100.000", "200.000", "1.000"), (item.Name, item.X, item.Y, item.Z));

        // 一覧で直すと図面の点が動く (元に戻せる)
        item.Name = "BM1-改";
        item.X = "150";
        item.Z = "";
        Assert.True(panel.Apply(item));
        var moved = doc.Entities.OfType<PointEntity>().First(p => p.Name == "BM1-改");
        Assert.Equal((150.0, 200.0), (Math.Round(doc.ToSurvey(moved.Position).X, 6), Math.Round(doc.ToSurvey(moved.Position).Y, 6)));
        Assert.Null(moved.Z);
        doc.History.Undo();
        Assert.Contains(bm, doc.Entities);

        // 数でない値は直さない
        panel.Refresh();
        var bad = panel.Items[0];
        bad.Y = "abc";
        Assert.False(panel.Apply(bad));
        Assert.Contains(bm, doc.Entities);
    });
}

/// <summary>登録する座標のタグ: 付け方・一覧の絞り込みと編集・CSV / DXF / DWG での保存。</summary>
public class CoordinateTagTests
{
    private static void RunSta(Action<CadDocument, CadCanvas> action)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                var doc = new CadDocument { ScaleDenominator = 500 };
                action(doc, new CadCanvas { Document = doc });
            }
            catch (Exception ex) { error = ex; }
            finally { CoordinateRegistry.DefaultTag = ""; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    private static string Temp(string ext) => Path.Combine(Path.GetTempPath(), "civilcad-test-" + Guid.NewGuid().ToString("N") + ext);

    [Fact]
    public void Sources_SetTags() => RunSta((doc, canvas) =>
    {
        // 幅杭は「幅杭」
        var input = new AlignmentInput
        {
            Name = "路線A",
            Pitch = 20,
            RightWidths = "3",
            DrawShoulders = false,
            Rows = [new() { Name = "BP", X = 0, Y = 0 }, new() { Name = "EP", X = 40, Y = 0 }],
        };
        doc.History.Execute(AlignmentDrawer.CreateDrawCommand(doc, input.Compute(), input));
        Assert.All(CoordinateRegistry.Rows(doc), r => Assert.Equal("幅杭", r.Tags));

        // 点 (PO)・座標計測 (ID) は「登録するタグ」
        CoordinateRegistry.DefaultTag = "境界";
        var po = new PointTool();
        canvas.SetTool(po);
        po.OnPoint(new Vec2(100, 100));
        var id = new CoordinateMeasureTool();
        canvas.SetTool(id);
        id.OnPoint(new Vec2(200, 100));
        Assert.Equal(2, CoordinateRegistry.Rows(doc).Count(r => r.Tags == "境界"));

        // 座標読込: CSV の座標の後ろの文字の列はタグ
        var file = PointImport.ParseCsv(["点名,X,Y,Z,タグ", "BM1,10,20,1.5,BM", "T1,30,40,,境界 仮"]);
        doc.Entities.AddRange(new PointImportOptions().Build(doc, file));
        Assert.Equal("BM", CoordinateRegistry.Rows(doc).Single(r => r.Name == "BM1").Tags);
        var t1 = CoordinateRegistry.Rows(doc).Single(r => r.Name == "T1");
        Assert.Equal(["境界", "仮"], t1.Point.TagList);
        Assert.Equal(["BM", "仮", "境界", "幅杭"], CoordinateRegistry.AllTags(doc).Order(StringComparer.Ordinal));
    });

    [Fact]
    public void Panel_FiltersByTag_AndEditsTags() => RunSta((doc, canvas) =>
    {
        doc.Entities.AddRange(
        [
            new PointEntity(doc.CurrentLayer, new Vec2(0, 0)) { Name = "K1", Tags = "幅杭" },
            new PointEntity(doc.CurrentLayer, new Vec2(1, 0)) { Name = "K2", Tags = "幅杭, 境界" },
            new PointEntity(doc.CurrentLayer, new Vec2(2, 0)) { Name = "B1" },
        ]);
        var panel = new CoordinatePanel();
        panel.Attach(canvas);
        Assert.Equal(3, panel.Items.Count);
        panel.FilterByTag("幅杭");
        Assert.Equal(["K1", "K2"], panel.Items.Select(i => i.Name));
        panel.FilterByTag("境界");
        Assert.Equal(["K2"], panel.Items.Select(i => i.Name));
        panel.FilterByTag(null);

        // 一覧でタグを直す
        var b1 = panel.Items.Single(i => i.Name == "B1");
        b1.Tags = "BM";
        Assert.True(panel.Apply(b1));
        Assert.Equal("BM", doc.Entities.OfType<PointEntity>().Single(p => p.Name == "B1").Tags);
        doc.History.Undo();
        Assert.Equal("", doc.Entities.OfType<PointEntity>().Single(p => p.Name == "B1").Tags);
    });

    [Fact]
    public void Sort_NaturalNameOrder_XYZ_OnlyMovesPoints_Undo()
    {
        var doc = new CadDocument();
        var line = new LineEntity(doc.CurrentLayer, Vec2.Zero, new Vec2(1, 1));
        PointEntity P(string name, double x, double y, double? z = null) => new(doc.CurrentLayer, doc.FromSurvey(x, y)) { Name = name, Z = z };
        var p10 = P("P10", 3, 1, 5);
        var p2 = P("P2", 1, 3);
        var p1 = P("p1", 2, 2, 7);
        doc.Entities.AddRange([p10, line, p2, p1]);

        doc.History.Execute(CoordinateRegistry.SortCommand(doc, CoordinateSort.NameAsc)!);
        Assert.Equal(["p1", "P2", "P10"], CoordinateRegistry.Rows(doc).Select(r => r.Name)); // P2 は P10 より前
        Assert.Same(line, doc.Entities[1]);                                                    // ほかの図形の順番は変えない
        doc.History.Execute(CoordinateRegistry.SortCommand(doc, CoordinateSort.XDesc)!);
        Assert.Equal(["P10", "p1", "P2"], CoordinateRegistry.Rows(doc).Select(r => r.Name));
        Assert.Null(CoordinateRegistry.SortCommand(doc, CoordinateSort.ZAsc));               // 変わらなければ null (標高の無い P2 は最後)
        doc.History.Execute(CoordinateRegistry.SortCommand(doc, CoordinateSort.ZDesc)!);
        Assert.Equal(["p1", "P10", "P2"], CoordinateRegistry.Rows(doc).Select(r => r.Name)); // 標高の無い点は大きい順でも最後
        doc.History.Undo();
        doc.History.Undo();
        Assert.Equal(["p1", "P2", "P10"], CoordinateRegistry.Rows(doc).Select(r => r.Name));
        doc.History.Undo();
        Assert.Equal(["P10", "P2", "p1"], CoordinateRegistry.Rows(doc).Select(r => r.Name));
    }

    [Fact]
    public void Panel_ManualEntry_RenameKeepsOrder_CurrentCellStays() => RunSta((doc, canvas) =>
    {
        doc.Entities.AddRange([
            new PointEntity(doc.CurrentLayer, doc.FromSurvey(10, 20)) { Name = "A" },
            new PointEntity(doc.CurrentLayer, doc.FromSurvey(30, 40)) { Name = "B" },
        ]);
        var panel = new CoordinatePanel();
        panel.Attach(canvas);

        // 点名を Z… に変えても並びは変わらない
        var a = panel.Items[0];
        a.Name = "Z1";
        Assert.True(panel.Apply(a));
        Assert.Equal(["Z1", "B"], panel.Items.Select(i => i.Name));

        // 一覧で 2 行目のセルにいるとき、作り直しても同じセルのまま (一番上に戻らない)
        var grid = (System.Windows.Controls.DataGrid)panel.Children.OfType<System.Windows.Controls.DataGrid>().Single();
        grid.CurrentCell = new System.Windows.Controls.DataGridCellInfo(panel.Items[1], grid.Columns[1]);
        panel.Refresh();
        Assert.Same(panel.Items[1], grid.CurrentCell.Item);
        Assert.Same(grid.Columns[1], grid.CurrentCell.Column);

        // 手入力: 新しい行に X・Y (点名が空なら P1)、Z、タグ
        Assert.True(panel.AddNew(new CoordinateItem { X = "50", Y = "60", Z = "1.5" }));
        var added = doc.Entities.OfType<PointEntity>().Last();
        Assert.Equal("P1", added.Name);
        Assert.Equal(1.5, added.Z);
        var (x, y) = doc.ToSurvey(added.Position);
        Assert.Equal((50.0, 60.0), (Math.Round(x, 6), Math.Round(y, 6)));
        Assert.Equal(["Z1", "B", "P1"], panel.Items.Select(i => i.Name));
        Assert.False(panel.AddNew(new CoordinateItem { Name = "Q", X = "abc", Y = "1" })); // 数でなければ登録しない
        Assert.False(panel.AddNew(new CoordinateItem()));                                   // 空の行は無視
        Assert.Equal(3, panel.Items.Count);

        // 並べ替え (点名 A→Z) と元に戻す
        panel.Sort(CoordinateSort.NameAsc);
        panel.Refresh();
        Assert.Equal(["B", "P1", "Z1"], panel.Items.Select(i => i.Name));
        doc.History.Undo();
        panel.Refresh();
        Assert.Equal(["Z1", "B", "P1"], panel.Items.Select(i => i.Name));
    });

    [Fact]
    public void Csv_RoundTripsTags()
    {
        var doc = new CadDocument();
        doc.Entities.Add(new PointEntity(doc.CurrentLayer, new Vec2(10, 20)) { Name = "K1", Tags = "幅杭, 境界" });
        doc.Entities.Add(new PointEntity(doc.CurrentLayer, new Vec2(30, 40)) { Name = "K2", Z = 2 });
        var path = Temp(".csv");
        try
        {
            CoordinateRegistry.ExportCsv(CoordinateRegistry.Rows(doc), path);
            Assert.Contains("K1,20.000,10.000,,幅杭 境界", File.ReadAllText(path));
            var back = PointImport.Load(path);
            Assert.Equal("幅杭 境界", back.Points[0].Tag);
            Assert.Equal("", back.Points[1].Tag);
            Assert.Equal(2, back.Points[1].Z);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(".dxf")]
    [InlineData(".dwg")]
    public void Save_KeepsTags(string ext)
    {
        var doc = new CadDocument();
        doc.Entities.Add(new PointEntity(doc.CurrentLayer, new Vec2(10, 20)) { Name = "K1", Tags = "幅杭 境界" });
        var path = Temp(ext);
        try
        {
            DrawingFiles.Save(doc, path);
            var back = new CadDocument();
            DrawingFiles.Open(back, path);
            var p = back.Entities.OfType<PointEntity>().Single();
            Assert.Equal(("K1", "幅杭 境界"), (p.Name, p.Tags));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

/// <summary>SFC / P21: 点名・標高を点の横の文字として書き、読み込むと点に戻す。</summary>
public class PointLabelSfcTests
{
    [Theory]
    [InlineData(".sfc", 500)]
    [InlineData(".p21", 250)]
    public void NameAndElevation_RoundTripAsText(string ext, double scale)
    {
        var doc = new CadDocument { ScaleDenominator = scale };
        var red = doc.GetOrCreateLayer("基準点", new RgbColor(255, 0, 0));
        doc.Entities.Add(new LineEntity(doc.CurrentLayer, new Vec2(0, 0), new Vec2(50, 0)));
        doc.Entities.Add(new PointEntity(red, new Vec2(10, 5)) { Name = "BM1", Z = 12.345 });
        doc.Entities.Add(new PointEntity(red, new Vec2(13, 5)) { Name = "No.1 左3" });   // 標高なし (3m 隣)
        doc.Entities.Add(new PointEntity(red, new Vec2(30, 5)));                          // 点名なし
        doc.Entities.Add(new TextEntity(doc.CurrentLayer, new Vec2(40, 10), "ふつうの文字", 1));
        var path = Path.Combine(Path.GetTempPath(), "civilcad-test-" + Guid.NewGuid().ToString("N") + ext);
        try
        {
            DrawingFiles.Save(doc, path);
            if (ext == ".sfc")
            {
                // ほかの CAD でも見えるよう、点名と標高が文字として書いてある
                var text = File.ReadAllText(path, System.Text.Encoding.Latin1);
                Assert.Contains("BM1", text);
                Assert.Contains("H=12.345", text);
            }
            var back = new CadDocument();
            DrawingFiles.Open(back, path);
            var origin = back.Entities.OfType<LineEntity>().Single().Start; // SFC / P21 は用紙の左下が原点
            var pts = back.Entities.OfType<PointEntity>().OrderBy(p => p.Position.X).ToList();
            Assert.Equal(3, pts.Count);
            Assert.Equal(("BM1", 12.345), (pts[0].Name, pts[0].Z!.Value));
            Assert.True((pts[0].Position - origin).DistanceTo(new Vec2(10, 5)) < 1e-6);
            Assert.Equal("No.1 左3", pts[1].Name);
            Assert.Null(pts[1].Z);
            Assert.Equal("", pts[2].Name);
            // 点名・標高の文字は点に戻したので残らない。ほかの文字はそのまま
            Assert.Equal(["ふつうの文字"], back.Entities.OfType<TextEntity>().Select(t => t.Text));
            Assert.DoesNotContain(back.Layers, l => l.Name is PointLabels.NameLayer or PointLabels.ElevationLayer);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Fold_LeavesTextsThatAreNotBesideAPoint()
    {
        var doc = new CadDocument { ScaleDenominator = 500 };
        var layer = doc.GetOrCreateLayer(PointLabels.NameLayer, RgbColor.White);
        doc.Entities.Add(new PointEntity(doc.CurrentLayer, new Vec2(0, 0)));
        doc.Entities.Add(new TextEntity(layer, new Vec2(20, 20), "はなれた文字", 1));
        Assert.Equal(0, PointLabels.Fold(doc));
        Assert.Single(doc.Entities.OfType<TextEntity>());
        Assert.Equal("", doc.Entities.OfType<PointEntity>().Single().Name);
    }
}
