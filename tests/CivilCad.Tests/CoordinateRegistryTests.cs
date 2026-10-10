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
