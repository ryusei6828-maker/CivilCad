namespace CivilCad.Alignment;

/// <summary>平面直角座標系の系 (平成14年国土交通省告示第9号)。原点の緯度・経度は度。</summary>
public sealed record PlaneZone(int Number, double OriginLatitude, double OriginLongitude, string Area)
{
    private static readonly string[] Roman =
        ["I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI", "XII", "XIII", "XIV", "XV", "XVI", "XVII", "XVIII", "XIX"];

    public string RomanNumber => Roman[Number - 1];
    public string Name => $"{RomanNumber}系";

    public override string ToString() =>
        $"{Name} (原点 北緯{Dm(OriginLatitude)} 東経{Dm(OriginLongitude)})  {Area}";

    private static string Dm(double deg)
    {
        var d = (int)deg;
        var m = (int)Math.Round((deg - d) * 60);
        return $"{d}°{m:00}'";
    }
}

/// <summary>
/// 平面直角座標 ⇔ 緯度経度 (GRS80, 縮尺係数 0.9999)。
/// 国土地理院「平面直角座標への換算」(河瀬 2011 のガウス・クリューゲル展開式) による。
/// 座標は測量の向き: X = 北向き, Y = 東向き (m)。
/// </summary>
public static class PlaneRectangular
{
    private const double A = 6378137.0;           // GRS80 長半径
    private const double F = 298.257222101;       // 逆扁平率
    private const double M0 = 0.9999;             // 原点の縮尺係数

    public static IReadOnlyList<PlaneZone> Zones { get; } =
    [
        new(1, 33, 129.5, "長崎県、鹿児島県のうち北方北緯32度南方北緯27度西方東経128度18分東方東経130度を境界線とする区域内の島 (奄美群島は東経130度13分まで)"),
        new(2, 33, 131.0, "福岡県、佐賀県、熊本県、大分県、宮崎県、鹿児島県 (I系の区域を除く)"),
        new(3, 36, 132 + 10 / 60.0, "山口県、島根県、広島県"),
        new(4, 33, 133.5, "香川県、愛媛県、徳島県、高知県"),
        new(5, 36, 134 + 20 / 60.0, "兵庫県、鳥取県、岡山県"),
        new(6, 36, 136.0, "京都府、大阪府、福井県、滋賀県、三重県、奈良県、和歌山県"),
        new(7, 36, 137 + 10 / 60.0, "石川県、富山県、岐阜県、愛知県"),
        new(8, 36, 138.5, "新潟県、長野県、山梨県、静岡県"),
        new(9, 36, 139 + 50 / 60.0, "東京都 (XIV・XVIII・XIX系の区域を除く)、福島県、栃木県、茨城県、埼玉県、千葉県、群馬県、神奈川県"),
        new(10, 40, 140 + 50 / 60.0, "青森県、秋田県、山形県、岩手県、宮城県"),
        new(11, 44, 140.25, "北海道 (小樽市、函館市、伊達市、北斗市、後志・胆振・渡島・檜山の一部など)"),
        new(12, 44, 142.25, "北海道 (札幌市、旭川市など XI・XIII系以外)"),
        new(13, 44, 144.25, "北海道 (北見市、帯広市、釧路市、網走市、根室市など)"),
        new(14, 26, 142.0, "東京都のうち北緯28度から南であり東経140度30分から東であり東経143度から西 (小笠原諸島)"),
        new(15, 26, 127.5, "沖縄県のうち東経126度から東であり東経130度から西"),
        new(16, 26, 124.0, "沖縄県のうち東経126度から西"),
        new(17, 26, 131.0, "沖縄県のうち東経130度から東"),
        new(18, 20, 136.0, "東京都のうち北緯28度から南であり東経140度30分から西 (沖ノ鳥島)"),
        new(19, 26, 154.0, "東京都のうち北緯28度から南であり東経143度から東 (南鳥島)"),
    ];

    public static PlaneZone? Find(int number) => number is >= 1 and <= 19 ? Zones[number - 1] : null;

    private static readonly double N = 1 / (2 * F - 1);
    private static readonly double[] Alpha =
    [
        0,
        N / 2 - 2.0 / 3 * N * N + 5.0 / 16 * Pow(3) + 41.0 / 180 * Pow(4) - 127.0 / 288 * Pow(5),
        13.0 / 48 * Pow(2) - 3.0 / 5 * Pow(3) + 557.0 / 1440 * Pow(4) + 281.0 / 630 * Pow(5),
        61.0 / 240 * Pow(3) - 103.0 / 140 * Pow(4) + 15061.0 / 26880 * Pow(5),
        49561.0 / 161280 * Pow(4) - 179.0 / 168 * Pow(5),
        34729.0 / 80640 * Pow(5),
    ];
    private static readonly double[] Beta =
    [
        0,
        N / 2 - 2.0 / 3 * N * N + 37.0 / 96 * Pow(3) - 1.0 / 360 * Pow(4) - 81.0 / 512 * Pow(5),
        1.0 / 48 * Pow(2) + 1.0 / 15 * Pow(3) - 437.0 / 1440 * Pow(4) + 46.0 / 105 * Pow(5),
        17.0 / 480 * Pow(3) - 37.0 / 840 * Pow(4) - 209.0 / 4480 * Pow(5),
        4397.0 / 161280 * Pow(4) - 11.0 / 504 * Pow(5),
        4583.0 / 161280 * Pow(5),
    ];
    private static readonly double[] Delta =
    [
        0,
        2 * N - 2.0 / 3 * Pow(2) - 2 * Pow(3) + 116.0 / 45 * Pow(4) + 26.0 / 45 * Pow(5) - 2854.0 / 675 * Pow(6),
        7.0 / 3 * Pow(2) - 8.0 / 5 * Pow(3) - 227.0 / 45 * Pow(4) + 2704.0 / 315 * Pow(5) + 2323.0 / 945 * Pow(6),
        56.0 / 15 * Pow(3) - 136.0 / 35 * Pow(4) - 1262.0 / 105 * Pow(5) + 73814.0 / 2835 * Pow(6),
        4279.0 / 630 * Pow(4) - 332.0 / 35 * Pow(5) - 399572.0 / 14175 * Pow(6),
        4174.0 / 315 * Pow(5) - 144838.0 / 6237 * Pow(6),
        601676.0 / 22275 * Pow(6),
    ];
    private static readonly double[] ACoef =
    [
        1 + Pow(2) / 4 + Pow(4) / 64,
        -1.5 * (N - Pow(3) / 8 - Pow(5) / 64),
        15.0 / 16 * (Pow(2) - Pow(4) / 4),
        -35.0 / 48 * (Pow(3) - 5.0 / 16 * Pow(5)),
        315.0 / 512 * Pow(4),
        -693.0 / 1280 * Pow(5),
    ];
    private static readonly double ABar = M0 * A / (1 + N) * ACoef[0];

    private static double Pow(int k) => Math.Pow(N, k);
    private static double Rad(double deg) => deg * Math.PI / 180;

    /// <summary>原点緯度から赤道までの子午線弧長 × m0 (S̄φ0)。</summary>
    private static double MeridianArc(double lat0Rad)
    {
        var s = ACoef[0] * lat0Rad;
        for (var j = 1; j <= 5; j++) s += ACoef[j] * Math.Sin(2 * j * lat0Rad);
        return M0 * A / (1 + N) * s;
    }

    /// <summary>緯度経度 (度) → 平面直角座標 X (北), Y (東) [m]。</summary>
    public static (double X, double Y) FromLatLon(PlaneZone zone, double latDeg, double lonDeg)
    {
        var phi = Rad(latDeg);
        var dl = Rad(lonDeg - zone.OriginLongitude);
        var k = 2 * Math.Sqrt(N) / (1 + N);
        var t = Math.Sinh(Math.Atanh(Math.Sin(phi)) - k * Math.Atanh(k * Math.Sin(phi)));
        var tBar = Math.Sqrt(1 + t * t);
        var xi = Math.Atan2(t, Math.Cos(dl));
        var eta = Math.Atanh(Math.Sin(dl) / tBar);

        double x = xi, y = eta;
        for (var j = 1; j <= 5; j++)
        {
            x += Alpha[j] * Math.Sin(2 * j * xi) * Math.Cosh(2 * j * eta);
            y += Alpha[j] * Math.Cos(2 * j * xi) * Math.Sinh(2 * j * eta);
        }
        return (ABar * x - MeridianArc(Rad(zone.OriginLatitude)), ABar * y);
    }

    /// <summary>平面直角座標 X (北), Y (東) [m] → 緯度経度 (度)。</summary>
    public static (double Latitude, double Longitude) ToLatLon(PlaneZone zone, double x, double y)
    {
        var xi = (x + MeridianArc(Rad(zone.OriginLatitude))) / ABar;
        var eta = y / ABar;

        double xi2 = xi, eta2 = eta;
        for (var j = 1; j <= 5; j++)
        {
            xi2 -= Beta[j] * Math.Sin(2 * j * xi) * Math.Cosh(2 * j * eta);
            eta2 -= Beta[j] * Math.Cos(2 * j * xi) * Math.Sinh(2 * j * eta);
        }
        var chi = Math.Asin(Math.Sin(xi2) / Math.Cosh(eta2));
        var phi = chi;
        for (var j = 1; j <= 6; j++) phi += Delta[j] * Math.Sin(2 * j * chi);
        var lambda = Rad(zone.OriginLongitude) + Math.Atan2(Math.Sinh(eta2), Math.Cos(xi2));
        return (phi * 180 / Math.PI, lambda * 180 / Math.PI);
    }

    /// <summary>度 → "35°39'29.16""" 形式。</summary>
    public static string ToDms(double deg, int secDecimals = 2)
    {
        var sign = deg < 0 ? "-" : "";
        var totalSec = Math.Round(Math.Abs(deg) * 3600, secDecimals);
        var d = (int)(totalSec / 3600);
        var m = (int)((totalSec - d * 3600) / 60);
        var s = totalSec - d * 3600 - m * 60;
        return $"{sign}{d}°{m:00}'{s.ToString("00." + new string('0', secDecimals))}\"";
    }
}
