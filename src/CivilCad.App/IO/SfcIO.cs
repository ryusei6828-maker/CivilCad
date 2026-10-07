using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using CivilCad.Alignment;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.IO;

/// <summary>
/// SXF (SFC 形式, レベル2 フィーチャ) の読み込み。
/// SFC は用紙 (mm) 上に部分図・シンボル (sfig) を配置する構造なので、
/// 部分図の縮尺 (例: 1/500) を図面の縮尺とし、用紙 mm × 縮尺 / 1000 で実寸 (m) に換算する。
/// こうすると部分図の中身は元の実寸で取り込まれる。
/// </summary>
public static partial class SfcIO
{
    /// <param name="Sheet">用紙の範囲 (図面座標 m)。drawing_sheet_feature が無ければ null。</param>
    public sealed record LoadResult(int Entities, int Skipped, double ScaleDenominator, IReadOnlyDictionary<string, int> SkippedKinds, Box2? Sheet = null);

    /// <summary>SXF のフィーチャ (名前と引数。文字列は X2 表記を戻したもの)。P21 の読み書きでも使う。</summary>
    internal sealed record Feature(string Name, List<string> Args);

    /// <summary>SXF 既定義色 (コード 1〜16)。黒は暗い背景で見えるよう白で表示する。</summary>
    private static readonly RgbColor[] PredefinedColors =
    [
        RgbColor.White,              // 1 black
        new(255, 0, 0), new(0, 255, 0), new(0, 0, 255), new(255, 255, 0), new(255, 0, 255), new(0, 255, 255),
        RgbColor.White,              // 8 white
        new(192, 48, 192), new(192, 128, 64), new(255, 128, 0), new(128, 192, 128),
        new(0, 128, 255), new(128, 64, 255), new(192, 192, 192), new(128, 128, 128),
    ];

    /// <summary>SXF 既定義線幅 (コード 1〜9, mm)。</summary>
    private static readonly double[] PredefinedWidths = [0.13, 0.18, 0.25, 0.35, 0.5, 0.7, 1.0, 1.4, 2.0];

    public static LoadResult Load(CadDocument doc, string path)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var text = Encoding.GetEncoding(932).GetString(File.ReadAllBytes(path));
        var features = Parse(text);
        if (features.Count == 0) throw new InvalidDataException("SFC の図形が見つかりませんでした (SXF レベル2 の SFC 形式ではない可能性があります)。");
        return new Builder(doc).Build(features);
    }

    /// <summary>フィーチャの列から図面を作る (P21 の読み込みで、AP202 をフィーチャに直したもの)。</summary>
    internal static LoadResult Build(CadDocument doc, List<Feature> features) => new Builder(doc).Build(features);

    // ---- 構文解析 ----

    internal static List<Feature> Parse(string text)
    {
        var list = new List<Feature>();
        foreach (Match m in EntityRegex().Matches(text))
            list.Add(new Feature(m.Groups[2].Value, SplitArgs(m.Groups[3].Value.Replace("\r", "").Replace("\n", ""))));
        return list;
    }

    /// <summary>引数を分割する。文字列は \'…\'、数値は '…' で囲まれている。</summary>
    private static List<string> SplitArgs(string s)
    {
        var args = new List<string>();
        var i = 0;
        while (i < s.Length)
        {
            while (i < s.Length && (s[i] == ',' || char.IsWhiteSpace(s[i]))) i++;
            if (i >= s.Length) break;
            if (s[i] == '\\' && i + 1 < s.Length && s[i + 1] == '\'')
            {
                var end = s.IndexOf("\\'", i + 2, StringComparison.Ordinal);
                if (end < 0) end = s.Length;
                args.Add(DecodeString(s[(i + 2)..end]));
                i = end + 2;
            }
            else if (s[i] == '\'')
            {
                var end = s.IndexOf('\'', i + 1);
                if (end < 0) end = s.Length;
                args.Add(s[(i + 1)..end]);
                i = end + 1;
            }
            else
            {
                var end = s.IndexOf(',', i);
                if (end < 0) end = s.Length;
                args.Add(s[i..end].Trim());
                i = end;
            }
        }
        return args;
    }

    /// <summary>ISO 10303-21 の \X2\hhhh…\X0\ (UTF-16) を戻す。</summary>
    private static string DecodeString(string s) =>
        UnicodeEscapeRegex().Replace(s, m =>
        {
            var hex = m.Groups[1].Value;
            var sb = new StringBuilder();
            for (var k = 0; k + 4 <= hex.Length; k += 4) sb.Append((char)Convert.ToInt32(hex.Substring(k, 4), 16));
            return sb.ToString();
        });

    // ブロックの終わりは "SXF*/" のほか、属性付きの "SXF3*/" などもある
    [GeneratedRegex(@"#(\d+)\s*=\s*([a-z_0-9]+)\s*\((.*?)\)\s*;?\s*SXF\d*\*/", RegexOptions.Singleline)]
    private static partial Regex EntityRegex();

    [GeneratedRegex(@"\\X2\\([0-9A-Fa-f]+)\\X0\\")]
    private static partial Regex UnicodeEscapeRegex();

    // ---- 図形の組み立て ----

    /// <summary>2次元アフィン変換 p' = [A B; C D] p + T。</summary>
    private readonly record struct Affine(double A, double B, double C, double D, Vec2 T)
    {
        public static Affine Scale(double s) => new(s, 0, 0, s, Vec2.Zero);

        public static Affine Locate(Vec2 pos, double angleDeg, double sx, double sy)
        {
            var r = angleDeg * Math.PI / 180;
            var (cos, sin) = (Math.Cos(r), Math.Sin(r));
            return new(cos * sx, -sin * sy, sin * sx, cos * sy, pos);
        }

        public Vec2 Apply(Vec2 p) => new(A * p.X + B * p.Y + T.X, C * p.X + D * p.Y + T.Y);
        public Vec2 ApplyVector(Vec2 v) => new(A * v.X + B * v.Y, C * v.X + D * v.Y);
        public double Det => A * D - B * C;
        public double LengthScale => Math.Sqrt(Math.Abs(Det));
        public bool Mirrored => Det < 0;

        /// <summary>this ∘ inner (inner を先に適用)。</summary>
        public Affine Then(Affine inner) => new(
            A * inner.A + B * inner.C, A * inner.B + B * inner.D,
            C * inner.A + D * inner.C, C * inner.B + D * inner.D,
            Apply(inner.T));

        /// <summary>角度 (rad) の方向を変換した後の角度。</summary>
        public double MapAngle(double rad) => ApplyVector(Vec2.FromAngle(rad)).Angle;
    }

    private sealed class Builder(CadDocument doc)
    {
        private readonly List<Layer?> _layers = [null]; // SXF のレイヤコードは 1 始まり
        private readonly List<RgbColor> _userColors = [];
        private readonly List<double> _userWidths = [];
        private readonly Dictionary<string, (List<Feature> Members, string Flag)> _sfigs = [];
        private readonly List<(string Color, string Width, string Flag, List<Feature> Members)> _composites = [];
        private readonly Dictionary<string, int> _skipped = [];
        private readonly List<Entity> _output = [];
        private double _paperMmToMeter = 1;
        private int _skippedCount;

        public LoadResult Build(List<Feature> features)
        {
            doc.Clear();
            var top = new List<Feature>();
            var compositeMembers = new List<Feature>();

            foreach (var f in features)
            {
                var a = f.Args;
                switch (f.Name)
                {
                    case "layer_feature":
                        var layer = doc.GetOrCreateLayer(a[0], RgbColor.White);
                        layer.IsVisible = a.ElementAtOrDefault(1) != "0" || layer.Name == "0";
                        _layers.Add(layer);
                        break;
                    case "user_defined_colour_feature":
                        _userColors.Add(new RgbColor((byte)I(a[0]), (byte)I(a[1]), (byte)I(a[2])));
                        break;
                    case "width_feature":
                        _userWidths.Add(D(a[0]));
                        break;
                    case "sfig_org_feature":
                        _sfigs[a[0]] = (top, a.ElementAtOrDefault(1) ?? "");
                        top = [];
                        break;
                    case "composite_curve_org_feature":
                        // 直前の「レイヤ0・色0」の図形がこの複合曲線の構成要素
                        _composites.Add((a[0], a[2], a[3], compositeMembers));
                        if (a[3] == "1") top.Add(new Feature("#composite", [(_composites.Count).ToString()]));
                        compositeMembers = [];
                        break;
                    default:
                        if (IsGeometry(f.Name) && a.Count > 1 && a[0] == "0" && a[1] == "0") compositeMembers.Add(f);
                        else top.Add(f);
                        break;
                }
            }

            var scale = DetectScale(top);
            _paperMmToMeter = scale / 1000;
            doc.ScaleDenominator = scale;
            Emit(top, Affine.Scale(_paperMmToMeter), 0);

            doc.Entities.AddRange(_output);
            doc.NotifyChanged();
            return new LoadResult(_output.Count, _skippedCount, scale, _skipped, SheetBox(features));
        }

        /// <summary>用紙 (drawing_sheet_feature: 名前, 用紙種別, 向き, 幅, 高さ) の範囲を図面座標で。</summary>
        private Box2? SheetBox(List<Feature> features)
        {
            var sheet = features.FirstOrDefault(f => f.Name == "drawing_sheet_feature" && f.Args.Count >= 3);
            if (sheet == null) return null;
            var a = sheet.Args;
            double w = 0, h = 0;
            if (a.Count >= 5 && double.TryParse(a[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                double.TryParse(a[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) && x > 0 && y > 0)
            {
                (w, h) = (x, y);
            }
            else
            {
                (w, h) = a[1] switch { "0" => (1189, 841), "1" => (841, 594), "2" => (594, 420), "3" => (420, 297), "4" => (297, 210), _ => (0, 0) };
                if (a[2] == "0") (w, h) = (h, w); // 縦置き
            }
            return w > 0 && h > 0 ? new Box2(Vec2.Zero, new Vec2(w, h) * _paperMmToMeter) : null;
        }

        private static bool IsGeometry(string name) => name is "line_feature" or "polyline_feature" or "circle_feature"
            or "arc_feature" or "ellipse_feature" or "ellipse_arc_feature" or "spline_feature" or "clothoid_feature";

        /// <summary>
        /// 図面の縮尺 = 部分図 (sfig 種別 1) のうち、図形の数が最も多い縮尺。
        /// (縮尺の違う部分図が混ざっていても、主な図が実寸になるようにする)
        /// </summary>
        private double DetectScale(List<Feature> top)
        {
            var best = top.Where(f => f.Name == "sfig_locate_feature" && f.Args.Count >= 7 &&
                                      _sfigs.TryGetValue(f.Args[1], out var s) && s.Flag == "1")
                .Select(f => (Ratio: Math.Abs(D(f.Args[5])), Count: _sfigs[f.Args[1]].Members.Count))
                .Where(x => x.Ratio > 0)
                .GroupBy(x => Math.Round(1 / x.Ratio))
                .OrderByDescending(g => g.Sum(x => x.Count))
                .FirstOrDefault();
            if (best != null) return best.Key;
            // 部分図が無い (図枠・表題欄だけなど) ときは、用紙上の縮尺の文字 (表題欄の「1:100」「S=1/100」) から
            var fromText = top.Where(f => f.Name == "text_string_feature" && f.Args.Count > 3).Select(f => ScaleFromText(f.Args[3])).FirstOrDefault(s => s > 1);
            return fromText > 1 ? fromText : 1;
        }

        private static double ScaleFromText(string s)
        {
            var m = System.Text.RegularExpressions.Regex.Match(s, @"^\s*(?:[SsＳ]\s*[=＝]\s*)?[1１]\s*[:：/／]\s*([0-9]+(?:\.[0-9]+)?)\s*$");
            return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
        }

        private void Emit(List<Feature> features, Affine t, int depth)
        {
            if (depth > 16) return; // 循環参照よけ
            foreach (var f in features)
            {
                try
                {
                    EmitOne(f, t, depth);
                }
                catch (Exception ex) when (ex is FormatException or ArgumentException or IndexOutOfRangeException or InvalidOperationException)
                {
                    Skip(f.Name);
                }
            }
        }

        private void EmitOne(Feature f, Affine t, int depth)
        {
            var a = f.Args;
            switch (f.Name)
            {
                case "sfig_locate_feature":
                    if (_sfigs.TryGetValue(a[1], out var sfig))
                        Emit(sfig.Members, t.Then(Affine.Locate(P(a[2], a[3]), D(a[4]), D(a[5]), D(a[6]))), depth + 1);
                    else
                        Skip("sfig (未定義)");
                    break;

                case "line_feature":
                    Add(new LineEntity(L(a[0]), t.Apply(P(a[4], a[5])), t.Apply(P(a[6], a[7]))), a[1], a[3]);
                    break;

                case "polyline_feature":
                    Add(Polyline(L(a[0]), Points(a[5], a[6]), t), a[1], a[3]);
                    break;

                case "spline_feature":
                    // 通過点を結ぶ折れ線で近似
                    var sp = Polyline(L(a[0]), Points(a[6], a[7]), t);
                    sp.IsClosed = a[4] == "0";
                    Add(sp, a[1], a[3]);
                    break;

                case "circle_feature":
                    Add(new CircleEntity(L(a[0]), t.Apply(P(a[4], a[5])), D(a[6]) * t.LengthScale), a[1], a[3]);
                    break;

                case "arc_feature":
                    Add(Arc(L(a[0]), P(a[4], a[5]), D(a[6]), a[7] == "1", D(a[8]), D(a[9]), t), a[1], a[3]);
                    break;

                case "ellipse_feature":
                    Add(Ellipse(L(a[0]), P(a[4], a[5]), D(a[6]), D(a[7]), D(a[8]), 0, 360, false, t), a[1], a[3]);
                    break;

                case "ellipse_arc_feature":
                    Add(Ellipse(L(a[0]), P(a[4], a[5]), D(a[6]), D(a[7]), D(a[9]), D(a[10]), D(a[11]), a[8] == "1", t), a[1], a[3]);
                    break;

                case "clothoid_feature":
                    Add(ClothoidCurve(L(a[0]), P(a[4], a[5]), D(a[6]), a[7] == "1" ? 1 : -1, D(a[8]), D(a[9]), D(a[10]), t), a[1], a[3]);
                    break;

                case "point_marker_feature":
                    // 点マーカーは用紙上 1mm の × 印で表す
                    var c = t.Apply(P(a[2], a[3]));
                    var s = 0.5 * _paperMmToMeter;
                    Add(new LineEntity(L(a[0]), c + new Vec2(-s, -s), c + new Vec2(s, s)), a[1], null);
                    Add(new LineEntity(L(a[0]), c + new Vec2(-s, s), c + new Vec2(s, -s)), a[1], null);
                    break;

                case "text_string_feature":
                    Add(Text(L(a[0]), a, 3, t), a[1], null);
                    break;

                case "label_feature":
                    // 引出線 + 文字
                    Add(Polyline(L(a[0]), Points(a[5], a[6]), t), a[1], a[3]);
                    EmitEmbeddedText(f, t);
                    break;

                case "linear_dim_feature":
                    // 寸法線と寸法値だけ取り込む (補助線・矢印は省略)
                    Add(new LineEntity(L(a[0]), t.Apply(P(a[4], a[5])), t.Apply(P(a[6], a[7]))), a[1], a[3]);
                    EmitEmbeddedText(f, t);
                    break;

                case "curve_dim_feature" or "angular_dim_feature" or "radius_dim_feature" or "diameter_dim_feature" or "balloon_feature":
                    EmitEmbeddedText(f, t);
                    Skip(f.Name + " (線は省略)");
                    break;

                case "#composite":
                    EmitComposite(int.Parse(a[0]), null, null, t);
                    break;

                case "fill_area_style_colour_feature":
                    // 塗りつぶし (レイヤ, 色, 外形ID, 中抜き数, 中抜きID): 中抜きが無ければ塗りつぶしポリライン、あれば塗りのハッチング
                    if (!int.TryParse(a[2], out var outer)) break;
                    if (I(a.ElementAtOrDefault(3) ?? "0") > 0 && Loops(outer, a.ElementAtOrDefault(4), t) is { Count: > 0 } fillLoops)
                        Add(new HatchEntity(L(a[0]), fillLoops, [], solid: true), a[1], null);
                    else
                        EmitComposite(outer, L(a[0]), a[1], t, fill: true);
                    break;

                case "fill_area_style_hatching_feature":
                    EmitHatching(a, t);
                    break;

                case "externally_defined_hatch_feature":
                    // 既定義ハッチ (レイヤ, 名前, 外形ID, 中抜き数, 中抜きID): 模様は名前だけなので 45° の斜線 (用紙 2mm 間隔) で表す。
                    // "Area_control" は CAD が用紙の外に置く管理用のダミー (図面の内容ではない) なので読まない
                    if (a[1].Equals("Area_control", StringComparison.OrdinalIgnoreCase)) break;
                    if (int.TryParse(a[2], out var extOuter) && Loops(extOuter, a.ElementAtOrDefault(4), t) is { Count: > 0 } extLoops)
                    {
                        var origin = extLoops[0][0];
                        _output.Add(new HatchEntity(L(a[0]), extLoops, [new HatchPattern(Math.PI / 4, 2 * _paperMmToMeter, origin)])
                        {
                            PatternName = a[1],
                        });
                    }
                    break;

                case "fill_area_style_tiles_feature":
                    if (int.TryParse(a[2], out var tileOuter)) EmitComposite(tileOuter, L(a[0]), null, t);
                    Skip(f.Name + " (模様は省略)");
                    break;

                case "drawing_sheet_feature" or "drawing_attribute_feature" or "pre_defined_colour_feature" or "pre_defined_font_feature"
                    or "user_defined_font_feature" or "text_font_feature" or "externally_defined_symbol_feature":
                    break; // 定義情報のみ

                default:
                    Skip(f.Name);
                    break;
            }
        }

        /// <summary>
        /// ハッチング (レイヤ, パターン数 n, パターン × n, 外形ID, 中抜き数, 中抜きID)。
        /// パターンは (色, 線種, 線幅, 基準点x, 基準点y, 間隔, 角度)。
        /// </summary>
        private void EmitHatching(List<string> a, Affine t)
        {
            var n = I(a[1]);
            if (n < 1 || a.Count < 4 + n || !int.TryParse(a[2 + n], out var outer))
            {
                Skip("fill_area_style_hatching_feature");
                return;
            }
            var loops = Loops(outer, a.ElementAtOrDefault(4 + n), t);
            if (loops.Count == 0)
            {
                Skip("fill_area_style_hatching_feature (外形なし)");
                return;
            }
            var patterns = new List<HatchPattern>();
            string? color = null, width = null;
            for (var k = 0; k < n; k++)
            {
                var p = List(a[2 + k]);
                if (p.Length < 7) continue;
                color ??= ((int)p[0]).ToString(CultureInfo.InvariantCulture);
                width ??= ((int)p[2]).ToString(CultureInfo.InvariantCulture);
                patterns.Add(new HatchPattern(t.MapAngle(p[6] * Math.PI / 180), p[5] * t.LengthScale, t.Apply(new Vec2(p[3], p[4]))));
            }
            Add(new HatchEntity(L(a[0]), loops, patterns), color ?? "0", width);
        }

        /// <summary>外形と中抜き (ID の並び "(3,4)") の複合曲線を、閉じた点列の組にする。</summary>
        private List<List<Vec2>> Loops(int outer, string? innerIds, Affine t)
        {
            var ids = new List<int> { outer };
            if (innerIds != null)
                ids.AddRange(innerIds.Trim('(', ')', ' ').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(s => int.TryParse(s, out var v) ? v : 0).Where(v => v > 0));
            var loops = new List<List<Vec2>>();
            foreach (var id in ids)
            {
                var pts = CompositePoints(id, t);
                if (pts.Count >= 2 && pts[0].DistanceTo(pts[^1]) < 1e-9) pts.RemoveAt(pts.Count - 1);
                if (pts.Count >= 3) loops.Add(pts);
            }
            return loops;
        }

        private List<Vec2> CompositePoints(int index, Affine t)
        {
            var pts = new List<Vec2>();
            if (index < 1 || index > _composites.Count) return pts;
            foreach (var m in _composites[index - 1].Members)
            {
                var a = m.Args;
                IEnumerable<Vec2> seg = m.Name switch
                {
                    "line_feature" => [t.Apply(P(a[4], a[5])), t.Apply(P(a[6], a[7]))],
                    "polyline_feature" => Points(a[5], a[6]).Select(t.Apply),
                    "arc_feature" => ArcPoints(P(a[4], a[5]), D(a[6]), a[7] == "1", D(a[8]), D(a[9])).Select(t.Apply),
                    "circle_feature" => Enumerable.Range(0, 72).Select(k => t.Apply(P(a[4], a[5]) + Vec2.FromAngle(k * Math.PI / 36) * D(a[6]))),
                    _ => [],
                };
                foreach (var p in seg)
                    if (pts.Count == 0 || pts[^1].DistanceTo(p) > 1e-9) pts.Add(p);
            }
            return pts;
        }

        private void EmitComposite(int index, Layer? layer, string? colorCode, Affine t, bool fill = false)
        {
            if (index < 1 || index > _composites.Count) return;
            var comp = _composites[index - 1];
            var pts = new List<Vec2>();
            foreach (var m in comp.Members)
            {
                var a = m.Args;
                IEnumerable<Vec2> seg = m.Name switch
                {
                    "line_feature" => [t.Apply(P(a[4], a[5])), t.Apply(P(a[6], a[7]))],
                    "polyline_feature" => Points(a[5], a[6]).Select(t.Apply),
                    "arc_feature" => ArcPoints(P(a[4], a[5]), D(a[6]), a[7] == "1", D(a[8]), D(a[9])).Select(t.Apply),
                    _ => [],
                };
                foreach (var p in seg)
                    if (pts.Count == 0 || pts[^1].DistanceTo(p) > 1e-9) pts.Add(p);
            }
            if (pts.Count < 2) return;
            var pl = new PolylineEntity(layer ?? L("0"), pts, fill || pts[0].DistanceTo(pts[^1]) < 1e-9) { Filled = fill };
            Add(pl, colorCode ?? comp.Color, fill ? null : comp.Width);
        }

        /// <summary>引数の中の「文字列 + 文字配置 9 項目」を探して文字にする (引出線・寸法用)。</summary>
        private void EmitEmbeddedText(Feature f, Affine t)
        {
            var a = f.Args;
            for (var i = 4; i + 9 < a.Count; i++)
            {
                if (double.TryParse(a[i], NumberStyles.Float, CultureInfo.InvariantCulture, out _)) continue;
                if (a[i].StartsWith('(')) continue;
                if (!double.TryParse(a[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out _)) continue;
                Add(Text(L(a[0]), a, i, t), a[1], null);
                return;
            }
        }

        /// <summary>文字列, 位置x, 位置y, 高さ, 幅, 間隔, 角度, 斜体角, 配置, 方向 (a[i] から)。</summary>
        private TextEntity Text(Layer layer, List<string> a, int i, Affine t)
        {
            var s = t.LengthScale;
            var bpnt = a.Count > i + 8 ? I(a[i + 8]) : 1;
            var e = new TextEntity(layer, t.Apply(P(a[i + 1], a[i + 2])), a[i], D(a[i + 3]) * s,
                t.MapAngle(D(a[i + 6]) * Math.PI / 180))
            {
                HAlign = ((bpnt - 1) % 3) switch { 1 => TextHAlign.Center, 2 => TextHAlign.Right, _ => TextHAlign.Left },
                VAlign = ((bpnt - 1) / 3) switch { 1 => TextVAlign.Middle, 2 => TextVAlign.Top, _ => TextVAlign.Bottom },
            };
            var width = D(a[i + 4]) * s;
            if (width > 0) e.FitWidth = width; // SXF の幅は文字列全体の幅
            return e;
        }

        private PolylineEntity Polyline(Layer layer, IEnumerable<Vec2> pts, Affine t) => new(layer, pts.Select(t.Apply));

        private Entity Arc(Layer layer, Vec2 center, double r, bool clockwise, double startDeg, double endDeg, Affine t)
        {
            // 反時計回りに直してから変換 (鏡像なら向きが逆になる)
            var (s, e) = clockwise ? (endDeg, startDeg) : (startDeg, endDeg);
            var sa = t.MapAngle(s * Math.PI / 180);
            var ea = t.MapAngle(e * Math.PI / 180);
            if (t.Mirrored) (sa, ea) = (ea, sa);
            return new ArcEntity(layer, t.Apply(center), r * t.LengthScale, sa, ea);
        }

        private static IEnumerable<Vec2> ArcPoints(Vec2 c, double r, bool clockwise, double startDeg, double endDeg)
        {
            var s = startDeg * Math.PI / 180;
            var sweep = GeoMath.SweepAngle(s, endDeg * Math.PI / 180);
            if (clockwise) sweep = -GeoMath.SweepAngle(endDeg * Math.PI / 180, s);
            var n = Math.Max(4, (int)(Math.Abs(sweep) / (Math.PI / 36)));
            for (var k = 0; k <= n; k++) yield return c + Vec2.FromAngle(s + sweep * k / n) * r;
        }

        private Entity Ellipse(Layer layer, Vec2 c, double rx, double ry, double rotDeg, double startDeg, double endDeg, bool clockwise, Affine t)
        {
            var rot = rotDeg * Math.PI / 180;
            var full = Math.Abs(endDeg - startDeg) >= 360 || (startDeg == 0 && endDeg == 360);
            var s = startDeg * Math.PI / 180;
            var sweep = full ? GeoMath.TwoPi : GeoMath.SweepAngle(s, endDeg * Math.PI / 180);
            if (clockwise && !full) sweep = -GeoMath.SweepAngle(endDeg * Math.PI / 180, s);
            const int n = 72;
            var pts = Enumerable.Range(0, full ? n : n + 1)
                .Select(k => s + sweep * k / n)
                .Select(u => c + new Vec2(rx * Math.Cos(u), ry * Math.Sin(u)).Rotate(rot))
                .Select(t.Apply);
            return new PolylineEntity(layer, pts, full);
        }

        private static PolylineEntity ClothoidCurve(Layer layer, Vec2 origin, double a, int turn, double angleDeg, double s0, double s1, Affine t)
        {
            var dir = angleDeg * Math.PI / 180;
            var n = Math.Max(8, (int)Math.Ceiling(Math.Abs(s1 - s0) / Math.Max(a / 50, 1e-6)));
            n = Math.Min(n, 400);
            var pts = Enumerable.Range(0, n + 1).Select(k =>
            {
                var s = s0 + (s1 - s0) * k / n;
                var p = Clothoid.LocalPoint(a, Math.Abs(s));
                if (s < 0) p = new Vec2(-p.X, p.Y); // 原点の反対側は点対称
                return t.Apply(origin + new Vec2(p.X, turn * p.Y).Rotate(dir));
            });
            return new PolylineEntity(layer, pts);
        }

        private void Add(Entity e, string colorCode, string? widthCode)
        {
            e.Color = Color(colorCode);
            if (widthCode != null) e.LineWeight = Width(widthCode);
            _output.Add(e);
        }

        private RgbColor? Color(string code)
        {
            var c = I(code);
            if (c >= 1 && c <= 16) return PredefinedColors[c - 1];
            if (c > 16 && c - 17 < _userColors.Count) return _userColors[c - 17];
            return null;
        }

        private double? Width(string code)
        {
            var w = I(code);
            if (w >= 1 && w <= 9) return PredefinedWidths[w - 1];
            if (w >= 10 && w - 10 < _userWidths.Count) return _userWidths[w - 10];
            return null;
        }

        private Layer L(string code)
        {
            var i = I(code);
            return i >= 1 && i < _layers.Count && _layers[i] is { } l ? l : doc.GetOrCreateLayer("0", RgbColor.White);
        }

        private void Skip(string kind)
        {
            _skippedCount++;
            _skipped[kind] = _skipped.GetValueOrDefault(kind) + 1;
        }

        private static IEnumerable<Vec2> Points(string xs, string ys)
        {
            var x = List(xs);
            var y = List(ys);
            for (var k = 0; k < Math.Min(x.Length, y.Length); k++) yield return new Vec2(x[k], y[k]);
        }

        private static double[] List(string s) =>
            s.Trim('(', ')', ' ').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(D).ToArray();

        private static Vec2 P(string x, string y) => new(D(x), D(y));
        private static double D(string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        private static int I(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : (int)D(s);
    }
}
