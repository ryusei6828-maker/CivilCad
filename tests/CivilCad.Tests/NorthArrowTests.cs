using CivilCad.Alignment;
using CivilCad.App;
using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

public class NorthArrowTests
{
    private const double Deg = Math.PI / 180;

    [Fact]
    public void TrueNorth_MatchesMeridianConvergence()
    {
        var zone = PlaneRectangular.Find(9)!; // 原点 北緯 36°, 東経 139°50'
        Assert.Equal(0, PlaneRectangular.TrueNorthRotation(zone, 0, 0), 9);
        // 原点の 50km 東: γ ≒ Δλ sinφ (Δλ = 50km / (111.3km × cos36°))
        var dLon = 50.0 / (111.32 * Math.Cos(36 * Deg));
        var expected = dLon * Math.Sin(36 * Deg) * Deg;
        var east = PlaneRectangular.TrueNorthRotation(zone, 0, 50000);
        Assert.True(east > 0, "東では真北は座標北より西 (反時計回り) へ傾く");
        Assert.Equal(expected, east, 4);
        Assert.Equal(-east, PlaneRectangular.TrueNorthRotation(zone, 0, -50000), 6);
    }

    [Fact]
    public void NorthArrow_AlwaysPointsNorth_AfterRotateMoveAndSettings()
    {
        MainWindow.RegisterTrueNorth();
        var doc = new CadDocument { ScaleDenominator = 500 };
        doc.History.Execute(new SetPlaneZoneCommand(9));
        var east = new Vec2(50000, -20000); // 図面の x = 東, y = 北
        var mark = new SymbolEntity(doc.CurrentLayer, SymbolLibrary.NorthArrow, east, 20, doc.PaperMmToMeters(20), rotation: 1.0);
        doc.History.Execute(new AddEntitiesCommand([mark]));
        var gamma = doc.NorthRotation(east);
        Assert.True(gamma > 0.003);
        Assert.Equal(gamma, mark.Rotation, 9); // 置いた向き (1.0) によらず北

        // 回転しても北を指したまま
        doc.History.Execute(new RotateTranslateCommand([mark], east, Math.PI / 2, Vec2.Zero));
        Assert.Equal(gamma, mark.Rotation, 9);
        // N は記号の中心から北側にある
        var n = (TextEntity)mark.Explode().Single(e => e is TextEntity);
        Assert.True(n.Position.Y > mark.Position.Y);

        // 系の西側へ移すと反対に傾く
        doc.History.Execute(new TranslateEntitiesCommand([mark], new Vec2(-100000, 0)));
        Assert.True(mark.Rotation < -0.003);

        // 座標北にする / 系を外す → 図面の上
        doc.UseTrueNorth = false;
        Assert.Equal(0, mark.Rotation);
        doc.UseTrueNorth = true;
        Assert.True(mark.Rotation < -0.003);
        doc.History.Execute(new SetPlaneZoneCommand(null));
        Assert.Equal(0, mark.Rotation);
    }

    [Fact]
    public void UserPreset_KeepsGivenRotation()
    {
        var doc = new CadDocument();
        var def = SymbolLibrary.FromEntities("x", [new LineEntity(doc.CurrentLayer, Vec2.Zero, new Vec2(1, 0))], Vec2.Zero, 1, out _);
        Assert.False(def.PointsNorth);
        SymbolLibrary.AddUser(def);
        try
        {
            var s = new SymbolEntity(doc.CurrentLayer, def.Kind, Vec2.Zero, 1, 1, rotation: 0.5);
            doc.History.Execute(new AddEntitiesCommand([s]));
            Assert.Equal(0.5, s.Rotation);
        }
        finally
        {
            SymbolLibrary.RemoveUser(def.Kind);
        }
    }
}
