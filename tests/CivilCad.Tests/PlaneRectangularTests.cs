using System.IO;
using CivilCad.Alignment;
using CivilCad.App.IO;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

public class PlaneRectangularTests
{
    private static PlaneZone Zone(int n) => PlaneRectangular.Find(n)!;

    [Fact]
    public void MatchesGsiExample_TsukubaZone9()
    {
        // 国土地理院 測量計算サイト (bl2xy) の例: 国土地理院 (つくば市)、IX系
        var (x, y) = PlaneRectangular.FromLatLon(Zone(9), 36.103774791666666, 140.08785504166664);
        Assert.Equal(11543.6883, x, 3);
        Assert.Equal(22916.2436, y, 3);

        var (lat, lon) = PlaneRectangular.ToLatLon(Zone(9), 11543.6883, 22916.2436);
        Assert.Equal(36.103774791666666, lat, 8);
        Assert.Equal(140.08785504166664, lon, 8);
    }

    [Fact]
    public void Origin_IsZero_ForAllZones()
    {
        foreach (var z in PlaneRectangular.Zones)
        {
            var (x, y) = PlaneRectangular.FromLatLon(z, z.OriginLatitude, z.OriginLongitude);
            Assert.Equal(0, x, 6);
            Assert.Equal(0, y, 6);
        }
    }

    [Theory]
    [InlineData(2, 31.6, 130.55)]    // 鹿児島市付近
    [InlineData(9, 35.681, 139.767)] // 東京駅付近
    [InlineData(12, 43.07, 141.35)]  // 札幌付近
    [InlineData(15, 26.21, 127.68)]  // 那覇付近
    public void RoundTrip_IsExact(int zone, double lat, double lon)
    {
        var (x, y) = PlaneRectangular.FromLatLon(Zone(zone), lat, lon);
        var (lat2, lon2) = PlaneRectangular.ToLatLon(Zone(zone), x, y);
        Assert.Equal(lat, lat2, 10);
        Assert.Equal(lon, lon2, 10);
    }

    [Fact]
    public void CentralMeridian_DistanceIsMeridianArcTimesScaleFactor()
    {
        // 原点の経線上で 36°→37°: 子午線弧長 約 110,968 m × 0.9999
        var (x, y) = PlaneRectangular.FromLatLon(Zone(9), 37, Zone(9).OriginLongitude);
        Assert.Equal(0, y, 6);
        Assert.InRange(x, 110957.2 - 2, 110957.2 + 2);
    }

    [Fact]
    public void Zone_IsSavedInDxf_AndClearedOnNew()
    {
        var doc = new CadDocument { PlaneZone = 2 };
        var path = Path.Combine(Path.GetTempPath(), $"civilcad_{Guid.NewGuid():N}.dxf");
        try
        {
            DxfIO.Save(doc, path);
            var loaded = new CadDocument();
            DxfIO.Load(loaded, path);
            Assert.Equal(2, loaded.PlaneZone);

            // 未設定の図面は未設定のまま
            DxfIO.Save(new CadDocument(), path);
            var loaded2 = new CadDocument { PlaneZone = 5 };
            DxfIO.Load(loaded2, path);
            Assert.Null(loaded2.PlaneZone);
        }
        finally
        {
            File.Delete(path);
        }
        doc.Clear();
        Assert.Null(doc.PlaneZone);
        Assert.Equal("II系", Zone(2).Name);
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.PlaneZone = 20);
    }
}
