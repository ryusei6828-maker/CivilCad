using System.Runtime.ExceptionServices;
using CivilCad.App.IO;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

/// <summary>座標計測 (ID): クリックした点の測量座標を表示し、座標管理に登録する。</summary>
public class CoordinateMeasureTests
{
    private static void RunSta(Action<CadDocument, CadCanvas, List<string>> action)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                var doc = new CadDocument { ScaleDenominator = 500 };
                var canvas = new CadCanvas { Document = doc };
                var messages = new List<string>();
                canvas.Message += (_, m) => messages.Add(m);
                action(doc, canvas, messages);
            }
            catch (Exception ex) { error = ex; }
            finally { CoordinateMeasureTool.Register = true; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    [Fact]
    public void Click_ShowsSurveyCoordinates_AndRegisters() => RunSta((doc, canvas, messages) =>
    {
        doc.Entities.Add(new PointEntity(doc.CurrentLayer, new Vec2(0, 0)) { Name = "P1" });
        var tool = new CoordinateMeasureTool();
        canvas.SetTool(tool);
        tool.OnPoint(new Vec2(-60760.5, -95537.25)); // 図面 x = 東, y = 北
        Assert.Contains("X(北) = -95537.250  Y(東) = -60760.500", messages[^1]);
        var row = Assert.Single(CoordinateRegistry.Rows(doc), r => r.Name == "P2"); // 使っていない番号
        Assert.Equal((-95537.25, -60760.5), (row.X, row.Y));

        // 点名を入力すると直前の点の名前を変える
        Assert.True(tool.OnText("BM-A"));
        Assert.Contains(CoordinateRegistry.Rows(doc), r => r.Name == "BM-A");
        Assert.DoesNotContain(CoordinateRegistry.Rows(doc), r => r.Name == "P2");
        // 座標の入力は点の指定 (名前の変更にしない)
        Assert.False(tool.OnText("s100,200"));

        // 続けて計測、一つ戻すで直前の登録を取り消す
        tool.OnPoint(new Vec2(10, 20));
        Assert.Equal(3, CoordinateRegistry.Rows(doc).Count);
        tool.StepBack();
        Assert.Equal(2, CoordinateRegistry.Rows(doc).Count);
        Assert.Same(tool, canvas.Tool);
    });

    [Fact]
    public void RegisterOff_OnlyShows() => RunSta((doc, canvas, messages) =>
    {
        CoordinateMeasureTool.Register = false;
        var tool = new CoordinateMeasureTool();
        canvas.SetTool(tool);
        tool.OnPoint(new Vec2(5, 7));
        Assert.Contains("X(北) = 7.000  Y(東) = 5.000", messages[^1]);
        Assert.Empty(CoordinateRegistry.Rows(doc));
        Assert.False(tool.OnText("名前")); // 登録していないので名前は付けない
    });

    [Fact]
    public void FollowsSurveyFrame() => RunSta((doc, canvas, messages) =>
    {
        doc.SetSurveyFrame(Math.PI / 2, Vec2.Zero); // 図面の東 = 測量の北
        var tool = new CoordinateMeasureTool();
        canvas.SetTool(tool);
        tool.OnPoint(new Vec2(10, 0));
        var row = Assert.Single(CoordinateRegistry.Rows(doc));
        Assert.Equal(10, row.X, 9);
        Assert.Equal(0, row.Y, 9);
    });
}
