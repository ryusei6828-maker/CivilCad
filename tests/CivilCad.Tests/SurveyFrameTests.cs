using System.IO;
using System.Runtime.ExceptionServices;
using CivilCad.App;
using CivilCad.App.IO;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

/// <summary>
/// 座標設定 (CS) を 2 点で行っても図面は回さず、測量座標の向きをシートに持つ。
/// 座標の入力・計測の方向角・方位記号・保存はこの向きで扱う。矩形 (REC) も。
/// </summary>
[Collection("SharedToolSettings")] // 共有の設定を書き換える
public class SurveyFrameTests
{
    private static void RunSta(Action<CadDocument, CadCanvas> action)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                var doc = new CadDocument { ScaleDenominator = 100 };
                action(doc, new CadCanvas { Document = doc });
            }
            catch (Exception ex) { error = ex; }
            finally { MeasureSettings.Detail = false; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    private static void Near(Vec2 expected, Vec2 actual) => Assert.True(expected.DistanceTo(actual) < 1e-6, $"期待 {expected}, 実際 {actual}");

    /// <summary>図面で東向きの線 (0,0)→(10,0) を、測量座標では北向き (X 100→110, Y 200) に合わせる。</summary>
    private static void SetCoordinates(CadCanvas canvas)
    {
        var tool = new CoordinateSetTool();
        canvas.SetTool(tool);
        tool.OnPoint(Vec2.Zero);
        Assert.True(tool.OnText("100,200"));
        tool.OnPoint(new Vec2(10, 0));
        Assert.True(tool.OnText("110,200"));
    }

    [Fact]
    public void CoordinateSet_TwoPoints_DoesNotRotateDrawing() => RunSta((doc, canvas) =>
    {
        var line = new LineEntity(doc.CurrentLayer, Vec2.Zero, new Vec2(10, 0));
        doc.Entities.Add(line);
        SetCoordinates(canvas);
        // 図面は回らず移動だけ (基準点1 が測量座標 X=100, Y=200 = CAD (200, 100) に来る)
        Near(new Vec2(200, 100), line.Start);
        Near(new Vec2(210, 100), line.End);
        Assert.Equal(Math.PI / 2, doc.SurveyRotation, 9);
        // 測量座標は 2 点とも指定どおり
        var (x1, y1) = doc.ToSurvey(line.Start);
        var (x2, y2) = doc.ToSurvey(line.End);
        Assert.Equal((100.0, 200.0), (Math.Round(x1, 6), Math.Round(y1, 6)));
        Assert.Equal((110.0, 200.0), (Math.Round(x2, 6), Math.Round(y2, 6)));
        // 元に戻すで向きも位置も戻る
        doc.History.Undo();
        Near(Vec2.Zero, line.Start);
        Assert.Equal(0, doc.SurveyRotation);
    });

    [Fact]
    public void CoordinateSet_ClickRegisteredPoints_FillsSurveyCoordinates_AndKeepsThem() => RunSta((doc, canvas) =>
    {
        // 図面の線 (0,0)→(10,0) を、登録した座標 K1 (X=100, Y=200)・K2 (X=110, Y=200) に合わせる
        var line = new LineEntity(doc.CurrentLayer, Vec2.Zero, new Vec2(10, 0));
        var k1 = new PointEntity(doc.CurrentLayer, doc.FromSurvey(100, 200)) { Name = "K1" };
        var k2 = new PointEntity(doc.CurrentLayer, doc.FromSurvey(110, 200)) { Name = "K2" };
        var unnamed = new PointEntity(doc.CurrentLayer, new Vec2(5, 5));
        var mark = new CircleEntity(doc.CurrentLayer, k1.Position, 0.5) { Tag = CoordinateMarks.TagPrefix + "K1" };
        doc.Entities.AddRange([line, k1, k2, unnamed, mark]);

        var tool = new CoordinateSetTool();
        canvas.SetTool(tool);
        Assert.False(tool.WaitingForSurvey);
        Assert.False(tool.UsePoint(k1)); // 基準点を指定する前は受け取らない
        tool.OnPoint(Vec2.Zero);
        Assert.True(tool.WaitingForSurvey);
        tool.OnPoint(new Vec2(50, 50));     // 登録した座標の無い所: 何も入らない
        tool.OnPoint(unnamed.Position);     // 点名の無い点も入らない
        Assert.True(tool.WaitingForSurvey);
        tool.OnPoint(k1.Position);          // 図面の登録した座標の点をクリック
        Assert.False(tool.WaitingForSurvey);
        tool.OnPoint(new Vec2(10, 0));
        tool.OnPoint(k1.Position);          // 基準点1 と同じ座標は入らない
        Assert.True(tool.WaitingForSurvey);
        Assert.True(tool.UsePoint(k2));     // 座標管理の一覧の行のクリックと同じ入口
        Assert.IsType<SelectTool>(canvas.Tool);

        // 線の両端の測量座標が K1・K2 と同じになる
        var (x1, y1) = doc.ToSurvey(line.Start);
        var (x2, y2) = doc.ToSurvey(line.End);
        Assert.Equal((100.0, 200.0), (Math.Round(x1, 6), Math.Round(y1, 6)));
        Assert.Equal((110.0, 200.0), (Math.Round(x2, 6), Math.Round(y2, 6)));
        Assert.Equal(Math.PI / 2, doc.SurveyRotation, 9);
        // 登録した座標の測量座標はそのまま (図面の方が座標に合う)。図面上では線の端に重なる
        var (kx1, ky1) = doc.ToSurvey(k1.Position);
        var (kx2, ky2) = doc.ToSurvey(k2.Position);
        Assert.Equal((100.0, 200.0), (Math.Round(kx1, 6), Math.Round(ky1, 6)));
        Assert.Equal((110.0, 200.0), (Math.Round(kx2, 6), Math.Round(ky2, 6)));
        Near(line.Start, k1.Position);
        Near(line.End, k2.Position);
        Near(k1.Position, mark.Center);       // 座標の描画のマークも点と一緒
        Near(new Vec2(205, 105), unnamed.Position); // 点名の無い点は図面と一緒に動く

        doc.History.Undo(); // 1 回で全部戻る
        Near(Vec2.Zero, line.Start);
        Near(new Vec2(200, 100), k1.Position);
        Near(new Vec2(200, 100), mark.Center);
        Assert.Equal(0, doc.SurveyRotation);
    });

    [Fact]
    public void CoordinateSet_TypedOnly_MovesRegisteredPointsWithDrawing() => RunSta((doc, canvas) =>
    {
        // 「X,Y」の入力だけなら今までどおり、登録した座標も図面と一緒に動く
        doc.Entities.Add(new LineEntity(doc.CurrentLayer, Vec2.Zero, new Vec2(10, 0)));
        var p = new PointEntity(doc.CurrentLayer, new Vec2(10, 0)) { Name = "P1" };
        doc.Entities.Add(p);
        SetCoordinates(canvas);
        Near(new Vec2(210, 100), p.Position);
        var (x, y) = doc.ToSurvey(p.Position);
        Assert.Equal((110.0, 200.0), (Math.Round(x, 6), Math.Round(y, 6)));
    });

    [Fact]
    public void CoordinateSet_ClickRowInCoordinatePanel_FillsSurveyCoordinates() => RunSta((doc, canvas) =>
    {
        var line = new LineEntity(doc.CurrentLayer, Vec2.Zero, new Vec2(10, 0));
        doc.Entities.AddRange([line,
            new PointEntity(doc.CurrentLayer, doc.FromSurvey(100, 200)) { Name = "K1" },
            new PointEntity(doc.CurrentLayer, doc.FromSurvey(110, 200)) { Name = "K2" }]);
        var panel = new CoordinatePanel();
        panel.Attach(canvas);
        var tool = new CoordinateSetTool();
        canvas.SetTool(tool);
        tool.OnPoint(Vec2.Zero);
        panel.SelectRow("K1");              // 一覧の行をクリック
        Assert.False(tool.WaitingForSurvey);
        Assert.Empty(canvas.Selection);     // 図面の選択は変えない
        tool.OnPoint(new Vec2(10, 0));
        panel.SelectRow("K2");
        Assert.IsType<SelectTool>(canvas.Tool);
        var (x2, y2) = doc.ToSurvey(line.End);
        Assert.Equal((110.0, 200.0), (Math.Round(x2, 6), Math.Round(y2, 6)));
    });

    [Fact]
    public void SurveyInput_Measure_PointImport_FollowFrame() => RunSta((doc, canvas) =>
    {
        doc.Entities.Add(new LineEntity(doc.CurrentLayer, Vec2.Zero, new Vec2(10, 0)));
        SetCoordinates(canvas);
        // 「s X,Y」の入力は測量座標 → 図面の点
        Assert.True(CoordinateInput.TryParse("s110,200", null, out var p, doc));
        Near(new Vec2(210, 100), p);
        // 計測: 図面で東向きの線は測量座標では北向き (方向角 0°、ΔX = 10)
        MeasureSettings.Detail = true;
        Assert.Contains("θ=0°00'00", MeasureSettings.DistanceText(new Vec2(200, 100), new Vec2(210, 100), doc.SurveyRotation));
        // 座標読込: 測量座標の点を図面の向きで置く
        var import = new PointImportOptions { ShowName = false, DrawLots = false };
        var file = new PointFile([new SurveyPoint("1", "P1", 110, 200, null)], [], 0);
        var circle = import.Build(doc, file).OfType<CircleEntity>().Single();
        Near(new Vec2(210, 100), circle.Center);
    });

    [Fact]
    public void NorthArrow_PointsToSurveyNorth() => RunSta((doc, canvas) =>
    {
        MainWindow.RegisterTrueNorth();
        doc.Entities.Add(new LineEntity(doc.CurrentLayer, Vec2.Zero, new Vec2(10, 0)));
        var arrow = new SymbolEntity(doc.CurrentLayer, SymbolLibrary.NorthArrow, new Vec2(5, 5), 20, 2);
        doc.Entities.Add(arrow);
        SetCoordinates(canvas);
        // 測量座標の北は図面では右 (東) を向く: 図面の上から -90°
        var auto = doc.Entities.OfType<SymbolEntity>().Single();
        Assert.Equal(-Math.PI / 2, auto.Rotation, 9);

        // 系を設定すると子午線収差も加わる (真北)
        doc.UseTrueNorth = true;
        doc.PlaneZone = 2;
        doc.NotifyChanged();
        var conv = CadDocument.TrueNorthProvider!(2, doc.ToSurveyCad(auto.Position))!.Value;
        Assert.Equal(-Math.PI / 2 + conv, auto.Rotation, 9);
    });

    [Fact]
    public void Dxf_KeepsSurveyFrame()
    {
        var doc = new CadDocument { ScaleDenominator = 100 };
        doc.Entities.Add(new LineEntity(doc.CurrentLayer, Vec2.Zero, new Vec2(10, 0)));
        doc.SetSurveyFrame(0.3, new Vec2(-60760, -95537));
        var path = Path.Combine(Path.GetTempPath(), "civilcad-test-" + Guid.NewGuid().ToString("N") + ".dxf");
        try
        {
            DxfIO.Save(doc, path);
            var back = new CadDocument();
            DxfIO.Load(back, path);
            Assert.Equal(0.3, back.SurveyRotation, 12);
            Assert.Equal(new Vec2(-60760, -95537), back.SurveyPivot);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Rectangle_TwoCorners_OrWidthHeight() => RunSta((doc, canvas) =>
    {
        var tool = new RectangleTool();
        canvas.SetTool(tool);
        tool.OnPoint(new Vec2(1, 2));
        tool.OnPoint(new Vec2(5, 4));
        // 「@幅,高さ」で 2 つ目
        tool.OnPoint(new Vec2(10, 10));
        Assert.True(CoordinateInput.TryParse("@10,5", tool.BasePoint, out var corner));
        tool.OnPoint(corner);
        tool.OnPoint(new Vec2(0, 0));
        tool.OnPoint(new Vec2(3, 0)); // 高さ 0 は描かない
        var rects = doc.Entities.OfType<PolylineEntity>().ToList();
        Assert.Equal(2, rects.Count);
        Assert.All(rects, r => Assert.True(r.IsClosed));
        Assert.Equal(new Box2(new Vec2(1, 2), new Vec2(5, 4)), rects[0].Bounds);
        Assert.Equal(8, rects[0].Area, 9);
        Assert.Equal(new Box2(new Vec2(10, 10), new Vec2(20, 15)), rects[1].Bounds);
        Assert.Equal(4, rects[0].Points.Count);
    });
}
