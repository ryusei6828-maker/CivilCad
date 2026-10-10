using System.Globalization;
using System.IO;
using System.Text;
using CivilCad.App.Drawing;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.IO;

/// <summary>
/// SXF (SFC 形式, レベル2 フィーチャ) の書き出し。
/// 表題欄・図枠 (レイヤ D-TTL*) は用紙 (mm) に直接、それ以外は図面縮尺の部分図 (sfig 種別 1) として
/// 実寸 mm (図面 m × 1000) で書き、用紙に 1/縮尺 の倍率で配置する。部分図の中は元の座標がそのまま残る。
/// </summary>
public static class SfcWriter
{
    private const double Margin = 20; // 図枠が無いときの用紙余白 (mm)

    private static readonly string[] PredefinedColorNames =
    [
        "black", "red", "green", "blue", "yellow", "magenta", "cyan", "white",
        "deeppink", "brown", "orange", "lightgreen", "lightblue", "lavender", "lightgray", "darkgray",
    ];

    private static readonly RgbColor[] PredefinedColorValues =
    [
        new(0, 0, 0), new(255, 0, 0), new(0, 255, 0), new(0, 0, 255), new(255, 255, 0), new(255, 0, 255), new(0, 255, 255), new(255, 255, 255),
        new(192, 48, 192), new(192, 128, 64), new(255, 128, 0), new(128, 192, 128), new(0, 128, 255), new(128, 64, 255), new(192, 192, 192), new(128, 128, 128),
    ];

    private static readonly double[] PredefinedWidths = [0.13, 0.18, 0.25, 0.35, 0.5, 0.7, 1.0, 1.4, 2.0];

    public static void Save(CadDocument doc, string path)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var sjis = Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        var w = new Writer(doc, Path.GetFileNameWithoutExtension(path), sjis);
        File.WriteAllBytes(path, sjis.GetBytes(w.Build(Path.GetFileName(path))));
    }

    /// <summary>SFC の本文 (P21 の書き出しで、このフィーチャを AP202 に直す)。</summary>
    internal static string BuildText(CadDocument doc, string sheetName, string fileName)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var sjis = Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        return new Writer(doc, sheetName, sjis).Build(fileName);
    }

    /// <summary>用紙の選び方と、図面 (m) → 用紙 (mm) の原点。</summary>
    public static (PaperSize? Paper, double Width, double Height, Vec2 Origin) Layout(CadDocument doc)
    {
        var k = doc.ScaleDenominator / 1000; // 用紙 1mm = k [m]

        // 図枠 (外枠) があれば、それが収まる A 判と余白から用紙の位置を決める
        var frame = doc.Entities.OfType<PolylineEntity>()
            .Where(p => p.Layer.Name == DrawingFrame.LayerFrame && p.Points.Count >= 4 &&
                        (p.IsClosed || p.Points[0].DistanceTo(p.Points[^1]) < 1e-6)) // 始点に戻る折線も枠とみなす
            .MaxBy(p => p.Bounds.Width * p.Bounds.Height);
        if (frame != null)
        {
            var b = frame.Bounds;
            var fw = b.Width / k;
            var fh = b.Height / k;
            var paper = PaperSize.All
                .Where(p => p.Width >= fw - 0.5 && p.Height >= fh - 0.5)
                .OrderBy(p => Math.Abs((p.Width - fw) - (p.Height - fh)))
                .ThenBy(p => p.Width)
                .FirstOrDefault();
            if (paper != null)
            {
                var margin = new Vec2((paper.Width - fw) / 2, (paper.Height - fh) / 2);
                return (paper, paper.Width, paper.Height, b.Min - margin * k);
            }
        }

        // 図枠が無ければ、図形全体が余白付きで収まる最小の A 判の中央に置く
        var ext = doc.Entities.Where(e => e is not ImageEntity).Aggregate(Box2.Empty, (acc, e) => acc.Union(e.Bounds));
        if (ext.IsEmpty) ext = new Box2(Vec2.Zero, Vec2.Zero);
        var needW = ext.Width / k + 2 * Margin;
        var needH = ext.Height / k + 2 * Margin;
        var fit = PaperSize.All.Where(p => p.Width >= needW && p.Height >= needH).OrderBy(p => p.Width).FirstOrDefault();
        var (pw, ph) = fit != null ? (fit.Width, fit.Height) : (Math.Ceiling(needW), Math.Ceiling(needH));
        return (fit, pw, ph, ext.Center - new Vec2(pw, ph) / 2 * k);
    }

    private sealed class Writer(CadDocument doc, string sheetName, Encoding sjis)
    {
        private readonly StringBuilder _sb = new();
        private readonly Dictionary<Layer, int> _layerCodes = [];
        private readonly List<RgbColor> _userColors = [];
        private readonly List<double> _userWidths = [];
        private int _nextId = 10;
        private int _compositeCount;

        /// <summary>書き出す図形。プリセット (記号) は線・塗り・円・文字に展開する (他の CAD でも表示できるように)。</summary>
        // 画像は SFC (SXF レベル 2) には書けないので除く
        // 点名・標高のある点は、点の横に点名・標高の文字も書く (SFC の点には名前・標高の欄が無いため。読み込むと点に戻す)
        private List<Entity> Entities => _entities ??= Expand();

        private List<Entity> Expand()
        {
            var labelLayers = PointLabels.CreateLayers();
            return [.. doc.Entities.Where(e => e is not ImageEntity).SelectMany(e => e.ExplodeForOutput())
                .SelectMany(e => e is PointEntity p ? PointLabels.Expand(doc, p, labelLayers) : [e])];
        }
        private List<Entity>? _entities;

        public string Build(string fileName)
        {
            var (paper, pw, ph, origin) = Layout(doc);
            var k = doc.ScaleDenominator / 1000;
            var partName = $"図面:{doc.ScaleText.Replace(':', '/')}";

            // 色・線幅の表を先に作る (定義は使用より前に書く)
            foreach (var e in Entities)
            {
                ColorCode(e.EffectiveColor, register: true);
                WidthCode(e.LineWeight, register: true);
            }

            _sb.Append("ISO-10303-21;\nHEADER;\n");
            _sb.Append("FILE_DESCRIPTION(('SCADEC level2 feature_mode'),\n        '2;1');\n");
            _sb.Append($"FILE_NAME('{HeaderString(fileName)}',\n        '{DateTime.Now:yyyy-M-d\\THH:mm:ss}',\n        (''),\n        (''),\n        'SCADEC_API_Ver3.30$$3.1',\n        'CivilCad',\n        '');\n");
            _sb.Append("FILE_SCHEMA(('ASSOCIATIVE_DRAUGHTING'));\nENDSEC;\nDATA;\n\n");

            foreach (var name in PredefinedColorNames) Feature($"pre_defined_colour_feature({Str(name)})");
            foreach (var c in _userColors) Feature($"user_defined_colour_feature({Q(c.R)},{Q(c.G)},{Q(c.B)})");
            // 線種の表: 書いた順に線種コード 1〜5 (実線・破線・点線・一点鎖線・二点鎖線)
            foreach (var t in LineTypes.All) Feature($"pre_defined_font_feature({Str(SxfLineTypeName(t))})");
            foreach (var width in _userWidths) Feature($"width_feature({Q(width)})");
            // 文字フォントの表: 書いた順にフォントコード 1〜 (1 = 既定の ＭＳ ゴシック)
            foreach (var f in TextFonts) Feature($"text_font_feature({Str(f)})");

            // レイヤの番号 (1 始まり) は先に決めておき、レイヤの定義 (layer_feature) は最後に書く
            var layers = UsableLayers();
            for (var i = 0; i < layers.Count; i++) _layerCodes[layers[i]] = i + 1;

            // 並びは SCADEC (他の CAD が出力する SFC) と同じにする:
            //   定義 (色・線種・線幅・文字フォント) → 部分図の中身 + sfig_org → 用紙上の図形 + sfig_locate
            //   → drawing_sheet_feature (用紙。ここまでが用紙の中身) → layer_feature (レイヤ表)
            // 用紙の後に図形を書いたり、レイヤ表を先頭に書いたりすると、SXF ブラウザ等で開けない・レイヤ名がずれる

            // 部分図: 実寸 mm (図面 m × 1000)
            var sheetLevel = Entities.Where(IsSheetLevel).ToList();
            var partLevel = Entities.Where(e => !IsSheetLevel(e)).ToList();
            if (partLevel.Count > 0)
            {
                foreach (var e in partLevel) WriteEntity(e, p => p * 1000, 1000);
                Feature($"sfig_org_feature({Str(partName)},{Q(1)})");
            }

            // 用紙 (mm)
            foreach (var e in sheetLevel) WriteEntity(e, p => (p - origin) / k, 1 / k);
            if (partLevel.Count > 0)
            {
                var pos = -origin / k; // 部分図の原点 (図面座標 0,0) の用紙上の位置
                var ratio = 1 / doc.ScaleDenominator;
                Feature($"sfig_locate_feature({Q(1)},{Str(partName)},{Q(pos.X)},{Q(pos.Y)},{Q(0.0)},{Q(ratio, 14)},{Q(ratio, 14)})");
            }

            Feature($"drawing_sheet_feature({Str(sheetName)},{Q(PaperTypeCode(paper))},{Q(1)},{Q(pw)},{Q(ph)})");

            foreach (var l in layers)
                Feature($"layer_feature({Str(LayerName(l))},{Q(l.IsVisible ? 1 : 0)})");

            _sb.Append("ENDSEC;\nEND-ISO-10303-21;\n");
            return _sb.ToString();
        }

        /// <summary>
        /// 書き出すレイヤ。図形のあるレイヤは必ず入れる (レイヤ一覧に無いものも)。図形の無い "0" は入れない
        /// (CivilCad の既定レイヤで、他 CAD の SFC には無い)。SXF のレイヤは 256 個までなので、超える分は図形の無いものから省く。
        /// </summary>
        private List<Layer> UsableLayers()
        {
            var used = Entities.Select(e => e.Layer).ToHashSet();
            var list = doc.Layers.Where(l => used.Contains(l) || l.Name != "0").ToList();
            list.AddRange(used.Where(l => !list.Contains(l)));
            if (list.Count > MaxLayers) list = [.. list.Where(used.Contains).Concat(list.Where(l => !used.Contains(l))).Take(MaxLayers)];
            if (list.Count == 0) list.Add(doc.Layers.FirstOrDefault() ?? new Layer("0", RgbColor.White));
            _uniqueNames = UniqueNames(list);
            return list;
        }

        private const int MaxLayers = 256;
        private Dictionary<Layer, string> _uniqueNames = [];

        private string LayerName(Layer l) => _uniqueNames.TryGetValue(l, out var n) ? n : l.Name;

        /// <summary>
        /// レイヤ名を SXF で使える形にする: 前後の空白・改行を除き、空なら「レイヤn」、長すぎれば切り詰め (Shift_JIS 256 バイトまで)、
        /// 重複していれば「_2」などを付けて区別する (同じ名前があると他のソフトでレイヤがまとまったり読めなかったりする)。
        /// </summary>
        private Dictionary<Layer, string> UniqueNames(List<Layer> layers)
        {
            var result = new Dictionary<Layer, string>();
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < layers.Count; i++)
            {
                var name = new string(layers[i].Name.Where(c => !char.IsControl(c)).ToArray()).Trim();
                if (name.Length == 0) name = $"レイヤ{i + 1}";
                while (SjisLength(name) > 250) name = name[..^1];
                var unique = name;
                for (var n = 2; !taken.Add(unique); n++) unique = $"{name}_{n}";
                result[layers[i]] = unique;
            }
            return result;
        }

        private int SjisLength(string s)
        {
            try
            {
                return sjis.GetByteCount(s);
            }
            catch (EncoderFallbackException)
            {
                return s.Length * 8; // \X2\ 表記になる文字があるときは長めに見積もる
            }
        }

        private List<string>? _textFonts;

        /// <summary>使っている文字フォント (1 番目は既定のフォント)。</summary>
        private List<string> TextFonts => _textFonts ??=
            [TextEntity.DefaultFontName, .. Entities.OfType<TextEntity>().Select(t => t.EffectiveFontName).Where(f => f != TextEntity.DefaultFontName).Distinct()];

        private int FontCode(TextEntity t) => Math.Max(0, TextFonts.IndexOf(t.EffectiveFontName)) + 1;

        /// <summary>SXF の既定義線種名。</summary>
        public static string SxfLineTypeName(LineType t) => t switch
        {
            LineType.Dashed => "dashed",
            LineType.Dotted => "dotted",
            LineType.DashDot => "long dashed dotted",
            LineType.DashDotDot => "long dashed double-dotted",
            _ => "continuous",
        };

        /// <summary>線種コード (表に書いた順、1 から)。</summary>
        private static int LineTypeCode(LineType t) => LineTypes.All.ToList().IndexOf(t) + 1;

        private static bool IsSheetLevel(Entity e) => e.Layer.Name.StartsWith("D-TTL", StringComparison.OrdinalIgnoreCase);

        private void WriteEntity(Entity e, Func<Vec2, Vec2> map, double lengthScale)
        {
            var layer = _layerCodes.TryGetValue(e.Layer, out var lc) ? lc : 1;
            var color = ColorCode(e.EffectiveColor);
            var width = WidthCode(e.LineWeight);
            var head = $"{Q(layer)},{Q(color)},{Q(LineTypeCode(e.LineType))},{Q(width)}";

            switch (e)
            {
                case LineEntity l:
                    var (a, b) = (map(l.Start), map(l.End));
                    Feature($"line_feature({head},{Q(a.X)},{Q(a.Y)},{Q(b.X)},{Q(b.Y)})");
                    break;

                case PolylineEntity pl when pl.Points.Count >= 2:
                    var pts = pl.Points.Select(map).ToList();
                    if ((pl.IsClosed || pl.Filled) && pts[0].DistanceTo(pts[^1]) > 1e-9) pts.Add(pts[0]);
                    if (pl.Filled)
                    {
                        // 塗りつぶし = 外形 (複合曲線) + 塗り
                        Feature($"polyline_feature({Q(0)},{Q(0)},{Q(0)},{Q(0)},{Q(pts.Count)},{List(pts, p => p.X)},{List(pts, p => p.Y)})");
                        Feature($"composite_curve_org_feature({Q(color)},{Q(1)},{Q(width)},{Q(0)})");
                        _compositeCount++;
                        Feature($"fill_area_style_colour_feature({Q(layer)},{Q(color)},{Q(_compositeCount)},{Q(0)},'()')");
                    }
                    else
                    {
                        Feature($"polyline_feature({head},{Q(pts.Count)},{List(pts, p => p.X)},{List(pts, p => p.Y)})");
                    }
                    break;

                case HatchEntity h when h.Loops.Count > 0:
                    // 境界ごとに複合曲線を書き、外形 (1 つ目) と中抜き (2 つ目以降) として参照する
                    var ids = new List<int>();
                    foreach (var loop in h.Loops)
                    {
                        var lp = loop.Select(map).ToList();
                        lp.Add(lp[0]);
                        Feature($"polyline_feature({Q(0)},{Q(0)},{Q(0)},{Q(0)},{Q(lp.Count)},{List(lp, p => p.X)},{List(lp, p => p.Y)})");
                        Feature($"composite_curve_org_feature({Q(color)},{Q(1)},{Q(width)},{Q(0)})");
                        ids.Add(++_compositeCount);
                    }
                    var inner = $"'({string.Join(",", ids.Skip(1))})'";
                    if (h.Solid || h.Patterns.Count == 0)
                    {
                        Feature($"fill_area_style_colour_feature({Q(layer)},{Q(color)},{Q(ids[0])},{Q(ids.Count - 1)},{inner})");
                    }
                    else
                    {
                        // パターン: (色, 線種, 線幅, 基準点x, 基準点y, 間隔, 角度)
                        var patterns = h.Patterns.Select(p =>
                        {
                            var o = map(p.Origin);
                            var deg = GeoMath.NormalizeAngle(p.Angle) * 180 / Math.PI;
                            return "'(" + string.Join(",", color, 1, width, N(o.X), N(o.Y), N(p.Spacing * lengthScale), N(deg)) + ")'";
                        });
                        Feature($"fill_area_style_hatching_feature({Q(layer)},{Q(h.Patterns.Count)},{string.Join(",", patterns)},{Q(ids[0])},{Q(ids.Count - 1)},{inner})");
                    }
                    break;

                case PointEntity pt:
                    // 点マーカー (既定義の点マーカーのコード: 3 = 点。読み込んだ別のマーカーはそのコードのまま)
                    var pp = map(pt.Position);
                    Feature($"point_marker_feature({Q(layer)},{Q(color)},{Q(pp.X)},{Q(pp.Y)},{Q(SfcIO.MarkerCode(pt))},{Q(0.0)},{Q(1.0)})");
                    break;

                case CircleEntity c:
                    var cc = map(c.Center);
                    Feature($"circle_feature({head},{Q(cc.X)},{Q(cc.Y)},{Q(c.Radius * lengthScale)})");
                    break;

                case ArcEntity arc:
                    var ac = map(arc.Center);
                    // 角度は桁を多めに出す (ごく短い円弧で始点=終点に丸まると全円になってしまうため)
                    Feature($"arc_feature({head},{Q(ac.X)},{Q(ac.Y)},{Q(arc.Radius * lengthScale)},{Q(0)}," +
                            $"{Q(GeoMath.NormalizeAngle(arc.StartAngle) * 180 / Math.PI, 14)},{Q(GeoMath.NormalizeAngle(arc.EndAngle) * 180 / Math.PI, 14)})");
                    break;

                case TextEntity t:
                    var tp = map(t.Position);
                    var row = t.VAlign switch { TextVAlign.Middle => 1, TextVAlign.Top => 2, _ => 0 };
                    var col = t.HAlign switch { TextHAlign.Center => 1, TextHAlign.Right => 2, _ => 0 };
                    Feature($"text_string_feature({Q(layer)},{Q(color)},{Q(FontCode(t))},{Str(t.Text)},{Q(tp.X)},{Q(tp.Y)}," +
                            $"{Q(t.Height * lengthScale)},{Q(t.ApproxWidth * lengthScale)},{Q(0.0)}," +
                            $"{Q(GeoMath.NormalizeAngle(t.Rotation) * 180 / Math.PI, 14)},{Q(0.0, 14)},{Q(row * 3 + col + 1)},{Q(1)})");
                    break;
            }
        }

        // ---- 色・線幅 ----

        private int ColorCode(RgbColor c, bool register = false)
        {
            // 画面で白 (= 黒背景用の既定色) は用紙では黒にする
            if (c == RgbColor.White) return 1;
            var i = Array.IndexOf(PredefinedColorValues, c);
            if (i >= 0) return i + 1;
            var u = _userColors.IndexOf(c);
            if (u < 0 && register) { _userColors.Add(c); u = _userColors.Count - 1; }
            return u >= 0 ? 17 + u : 1;
        }

        private int WidthCode(double? mm, bool register = false)
        {
            if (mm is not double w) return 1;
            var i = Array.FindIndex(PredefinedWidths, p => Math.Abs(p - w) < 0.005);
            if (i >= 0) return i + 1;
            var u = _userWidths.FindIndex(p => Math.Abs(p - w) < 0.005);
            if (u < 0 && register && _userWidths.Count < 7) { _userWidths.Add(w); u = _userWidths.Count - 1; }
            if (u >= 0) return 10 + u;
            // ユーザー定義は 7 種まで。溢れたら最も近い既定義に丸める
            return Array.IndexOf(PredefinedWidths, PredefinedWidths.MinBy(p => Math.Abs(p - w))) + 1;
        }

        private static int PaperTypeCode(PaperSize? p) => p?.Name switch
        {
            "A0" => 0, "A1" => 1, "A2" => 2, "A3" => 3, "A4" => 4, _ => 9,
        };

        // ---- 書式 ----

        private void Feature(string body)
        {
            _sb.Append("/*SXF\n#").Append(_nextId).Append(" = ").Append(body).Append("\nSXF*/\n\n");
            _nextId += 10;
        }

        private static string Q(double v, int decimals = 6) => "'" + v.ToString("F" + decimals, CultureInfo.InvariantCulture) + "'";
        private static string Q(int v) => "'" + v.ToString(CultureInfo.InvariantCulture) + "'";
        private static string N(double v) => v.ToString("F6", CultureInfo.InvariantCulture);

        private static string List(IReadOnlyList<Vec2> pts, Func<Vec2, double> f) =>
            "'(" + string.Join(",", pts.Select(p => f(p).ToString("F6", CultureInfo.InvariantCulture))) + ")'";

        /// <summary>文字列 \'…\'。Shift_JIS で表せない文字は \X2\hhhh\X0\ にする。</summary>
        private string Str(string s)
        {
            var sb = new StringBuilder("\\'");
            foreach (var ch in s.Replace('\'', '’').Replace('\\', '＼'))
            {
                if (CanEncode(ch)) sb.Append(ch);
                else sb.Append("\\X2\\").Append(((int)ch).ToString("X4")).Append("\\X0\\");
            }
            return sb.Append("\\'").ToString();
        }

        private bool CanEncode(char ch)
        {
            if (ch < 0x80) return true;
            try
            {
                sjis.GetBytes([ch]);
                return true;
            }
            catch (EncoderFallbackException)
            {
                return false;
            }
        }

        /// <summary>ヘッダは ASCII のみにする (日本語は \X2\ 表記)。</summary>
        private static string HeaderString(string s)
        {
            var sb = new StringBuilder();
            var i = 0;
            while (i < s.Length)
            {
                if (s[i] < 0x80 && s[i] != '\'')
                {
                    sb.Append(s[i++]);
                    continue;
                }
                sb.Append("\\X2\\");
                while (i < s.Length && (s[i] >= 0x80 || s[i] == '\'')) sb.Append(((int)s[i++]).ToString("X4"));
                sb.Append("\\X0\\");
            }
            return sb.ToString();
        }
    }
}
