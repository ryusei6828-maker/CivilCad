using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using CivilCad.Core.Model;
using Feature = CivilCad.App.IO.SfcIO.Feature;

namespace CivilCad.App.IO;

/// <summary>
/// SXF (P21 形式) の読み書き。P21 は STEP AP202 (ISO 10303-21) のインスタンスで、SFC のフィーチャ 1 つが
/// 決まった形のインスタンスの組になる (SCADEC のライブラリの書き方)。そこで
/// 保存は SFC と同じフィーチャを作ってから AP202 に直し、読み込みは AP202 をフィーチャに直して SFC と同じ組み立てを使う。
/// .p2z (P21 を ZIP にしたもの) も読める。
/// </summary>
public static class P21IO
{
    private static readonly string[] PredefinedColorNames =
    [
        "black", "red", "green", "blue", "yellow", "magenta", "cyan", "white",
        "deeppink", "brown", "orange", "lightgreen", "lightblue", "lavender", "lightgray", "darkgray",
    ];

    /// <summary>既定義色 9〜16 の RGB (SXF の仕様の値)。P21 では COLOUR_RGB('$$SXF_名前',…) で書く。</summary>
    private static readonly (int R, int G, int B)[] ExtraColorRgb =
    [
        (192, 0, 128), (192, 128, 64), (255, 128, 0), (128, 192, 128), (0, 128, 255), (128, 64, 255), (192, 192, 192), (128, 128, 128),
    ];

    private static readonly double[] PredefinedWidths = [0.13, 0.18, 0.25, 0.35, 0.5, 0.7, 1.0, 1.4, 2.0];

    private static readonly string[] SfigPrefixes = ["$$SXF_FM_", "$$SXF_FG_", "$$SXF_G_", "$$SXF_P_"];

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ---- 保存 ----

    public static void Save(CadDocument doc, string path)
    {
        var sfc = SfcWriter.BuildText(doc, Path.GetFileNameWithoutExtension(path), Path.GetFileName(path));
        var features = SfcIO.Parse(sfc);
        var body = new Writer().Build(features);
        var head = "ISO-10303-21;\nHEADER;\nFILE_DESCRIPTION(('SCADEC level2 AP202_mode'),\n\t\t'2;1');\n" +
                   $"FILE_NAME({StepWriter.S(Path.GetFileName(path))},\n\t\t   '{DateTime.Now:yyyy-M-d\\THH:mm:ss}',\n\t\t   (''),\n\t\t   (''),\n" +
                   "\t\t   'SCADEC_API_Ver3.30$$3.1',\n\t\t   'CivilCad',\n\t\t'');\nFILE_SCHEMA(('ASSOCIATIVE_DRAUGHTING'));\nENDSEC;\nDATA;\n";
        File.WriteAllText(path, head + body + "ENDSEC;\nEND-ISO-10303-21;\n", new UTF8Encoding(false)); // 本文は ASCII のみ
    }

    /// <summary>SFC のフィーチャを AP202 のインスタンスに直す (gen.py/SCADEC と同じ形)。</summary>
    private sealed class Writer
    {
        private readonly StepWriter _p = new();
        private readonly string _mm, _context, _source, _nullStyle;
        private readonly Dictionary<int, string> _colours = [];
        private readonly Dictionary<int, string> _widths = [];
        private string? _font;
        private string? _textFont;
        private int _userColors, _userWidths;
        private readonly List<(string Name, bool Visible)> _layers = [];
        private readonly Dictionary<int, List<string>> _layerItems = [];
        private readonly List<string> _subfigureItems = []; // 部分図の配置 (レイヤ「$$SXF_dummy_layer_for_subfigure」)
        private List<string> _items = [];
        private readonly List<string> _segments = [];
        private readonly List<string> _composites = [];
        private readonly Dictionary<string, (string Map, string Full)> _sfigs = [];
        private (string Name, double W, double H) _sheet = ("", 420, 297);

        public Writer()
        {
            _mm = _p.Complex("LENGTH_UNIT()", "NAMED_UNIT(*)", "SI_UNIT(.MILLI.,.METRE.)");
            var rad = _p.Complex("NAMED_UNIT(*)", "PLANE_ANGLE_UNIT()", "SI_UNIT($,.RADIAN.)");
            _context = _p.Complex("GEOMETRIC_REPRESENTATION_CONTEXT(2)", $"GLOBAL_UNIT_ASSIGNED_CONTEXT(({rad},{_mm}))", "REPRESENTATION_CONTEXT('ID1','2D')");
            _source = _p.E("EXTERNAL_SOURCE(IDENTIFIER('scadec'))");
            _nullStyle = _p.E("PRESENTATION_STYLE_ASSIGNMENT((NULL_STYLE(.NULL.)))");
        }

        public string Build(List<Feature> features)
        {
            foreach (var f in features) Write(f);
            Finish();
            return _p.Body;
        }

        private void Write(Feature f)
        {
            var a = f.Args;
            switch (f.Name)
            {
                case "pre_defined_colour_feature":
                {
                    var code = Array.IndexOf(PredefinedColorNames, a[0]) + 1;
                    if (code <= 0) break;
                    if (code <= 8) _colours[code] = _p.E($"DRAUGHTING_PRE_DEFINED_COLOUR('{a[0]}')");
                    else
                    {
                        var (r, g, b) = ExtraColorRgb[code - 9];
                        _colours[code] = _p.E($"COLOUR_RGB('$$SXF_{a[0]}',{StepWriter.F(r / 255.0)},{StepWriter.F(g / 255.0)},{StepWriter.F(b / 255.0)})");
                    }
                    break;
                }
                case "user_defined_colour_feature":
                    _colours[17 + _userColors++] = _p.E($"COLOUR_RGB(' ',{StepWriter.F(D(a[0]) / 255)},{StepWriter.F(D(a[1]) / 255)},{StepWriter.F(D(a[2]) / 255)})");
                    break;
                case "pre_defined_font_feature":
                    _font ??= _p.E($"DRAUGHTING_PRE_DEFINED_CURVE_FONT('{a[0]}')");
                    break;
                case "width_feature":
                    _widths[10 + _userWidths++] = WidthMeasure(D(a[0]));
                    break;
                case "text_font_feature":
                    _textFont ??= _p.E($"EXTERNALLY_DEFINED_TEXT_FONT(IDENTIFIER({StepWriter.S(a[0])}),{_source})");
                    break;
                case "layer_feature":
                    _layers.Add((a[0], a.ElementAtOrDefault(1) != "0"));
                    break;
                case "line_feature":
                    Curve(a, LineCurve(D(a[4]), D(a[5]), D(a[6]), D(a[7])));
                    break;
                case "polyline_feature":
                {
                    var pts = Points(a[5], a[6]);
                    Curve(a, _p.E($"POLYLINE(' ',({string.Join(",", pts.Select(p => Point(p.X, p.Y)))}))"));
                    break;
                }
                case "circle_feature":
                    Curve(a, _p.E($"CIRCLE(' ',{Placement(D(a[4]), D(a[5]))},{StepWriter.F(D(a[6]))})"));
                    break;
                case "arc_feature":
                {
                    var basis = _p.E($"CIRCLE(' ',{Placement(D(a[4]), D(a[5]))},{StepWriter.F(D(a[6]))})");
                    var ccw = a[7] != "1";
                    Curve(a, _p.E($"TRIMMED_CURVE(' ',{basis},(PARAMETER_VALUE({StepWriter.A(D(a[8]) * Math.PI / 180)})),(PARAMETER_VALUE({StepWriter.A(D(a[9]) * Math.PI / 180)})),{(ccw ? ".T." : ".F.")},.PARAMETER.)"));
                    break;
                }
                case "composite_curve_org_feature":
                {
                    var cc = _p.E($"COMPOSITE_CURVE(' ',({string.Join(",", _segments)}),.T.)");
                    _segments.Clear();
                    _composites.Add(cc);
                    break;
                }
                case "fill_area_style_colour_feature":
                {
                    var st = _p.E($"FILL_AREA_STYLE_COLOUR(' ',{Colour(I(a[1]))})");
                    Fill(I(a[0]), I(a[2]), Ints(a[4]), [st]);
                    break;
                }
                case "fill_area_style_hatching_feature":
                {
                    var n = I(a[1]);
                    var styles = new List<string>();
                    for (var i = 0; i < n; i++)
                    {
                        var v = Ints(a[2 + i]);
                        var cs = _p.E($"CURVE_STYLE(' ',{Font()},{Width((int)v[2])},{Colour((int)v[0])})");
                        var t = v[6] * Math.PI / 180;
                        var d = _p.E($"DIRECTION(' ',({StepWriter.A(-Math.Sin(t))},{StepWriter.A(Math.Cos(t))}))");
                        var vec = _p.E($"VECTOR(' ',{d},{StepWriter.F(v[5])})");
                        var rf = _p.E($"ONE_DIRECTION_REPEAT_FACTOR(' ',{vec})");
                        var pt = Point(v[3], v[4]);
                        styles.Add(_p.E($"FILL_AREA_STYLE_HATCHING(' ',{cs},{rf},{pt},{pt},{StepWriter.A(t)})"));
                    }
                    Fill(I(a[0]), I(a[2 + n]), Ints(a[4 + n]), styles);
                    break;
                }
                case "point_marker_feature":
                    Marker(I(a[0]), I(a[1]), D(a[2]), D(a[3]), I(a[4]), D(a[5]), D(a[6]));
                    break;
                case "text_string_feature":
                    Text(I(a[0]), I(a[1]), a[3], D(a[4]), D(a[5]), D(a[6]), D(a[7]), D(a[8]), D(a[9]), D(a[10]), I(a[11]), I(a[12]));
                    break;
                case "sfig_org_feature":
                {
                    var flag = Math.Clamp(I(a[1]), 1, 4);
                    var full = SfigPrefixes[flag - 1] + a[0];
                    var origin = Placement(0, 0);
                    var rep = _p.E($"DRAUGHTING_SUBFIGURE_REPRESENTATION({StepWriter.S(full)},({string.Join(",", _items.Append(origin))}),{_context})");
                    _sfigs[a[0]] = (_p.E($"SYMBOL_REPRESENTATION_MAP({origin},{rep})"), full);
                    _items = [];
                    break;
                }
                case "sfig_locate_feature":
                {
                    if (!_sfigs.TryGetValue(a[1], out var sfig)) break;
                    var target = _p.E($"SYMBOL_TARGET(' ',{Placement(D(a[2]), D(a[3]), D(a[4]))},{StepWriter.A(D(a[5]))},{StepWriter.A(D(a[6]))})");
                    var mi = _p.Complex("ANNOTATION_SYMBOL()", "GEOMETRIC_REPRESENTATION_ITEM()", $"MAPPED_ITEM({sfig.Map},{target})", "REPRESENTATION_ITEM(' ')");
                    var occ = _p.Complex(Sorted("ANNOTATION_OCCURRENCE()", "ANNOTATION_SUBFIGURE_OCCURRENCE()", "ANNOTATION_SYMBOL_OCCURRENCE()",
                        "DRAUGHTING_ANNOTATION_OCCURRENCE()", "GEOMETRIC_REPRESENTATION_ITEM()", $"REPRESENTATION_ITEM({StepWriter.S(sfig.Full)})",
                        $"STYLED_ITEM(({_nullStyle}),{mi})"));
                    _subfigureItems.Add(occ);
                    _items.Add(occ);
                    break;
                }
                case "drawing_sheet_feature":
                    _sheet = (a[0], D(a[3]), D(a[4]));
                    break;
            }
        }

        private void Finish()
        {
            var box = _p.E($"PLANAR_BOX(' ',{StepWriter.F(_sheet.W)},{StepWriter.F(_sheet.H)},{Placement(0, 0)})");
            var definition = _p.E("DRAWING_DEFINITION(' ',' ')");
            var rev = _p.E($"DRAUGHTING_DRAWING_REVISION('01',{definition},' ')");
            _p.E($"DRAUGHTING_TITLE(({rev}),'JAPANESE',{StepWriter.S(_sheet.Name)})");
            var sh = _p.E($"DRAWING_SHEET_REVISION({StepWriter.S(_sheet.Name)},({string.Join(",", _items.Append(box))}),{_context},'01')");
            _p.E($"DRAWING_SHEET_REVISION_USAGE({sh},{rev},'01')");
            _p.E($"PRESENTATION_SIZE({sh},{box})");
            var hidden = new List<string>();
            void Layer(string name, List<string> items, bool visible)
            {
                if (items.Count == 0) return;
                var la = _p.E($"PRESENTATION_LAYER_ASSIGNMENT({StepWriter.S(name)},' ',({string.Join(",", items)}))");
                _p.E($"PRESENTATION_LAYER_USAGE({la},{sh})");
                if (!visible) hidden.Add(la);
            }
            Layer("$$SXF_dummy_layer_for_subfigure", _subfigureItems, true);
            for (var i = 0; i < _layers.Count; i++) Layer(_layers[i].Name, _layerItems.GetValueOrDefault(i + 1, []), _layers[i].Visible);
            if (hidden.Count > 0) _p.E($"INVISIBILITY(({string.Join(",", hidden)}))");
        }

        // 図形: レイヤ 0・色 0 は複合曲線の構成要素 (直後の composite_curve_org_feature で束ねる)
        private void Curve(List<string> a, string curve)
        {
            if (a[0] == "0" && a[1] == "0")
            {
                _segments.Add(_p.E($"COMPOSITE_CURVE_SEGMENT(.CONTINUOUS.,.T.,{curve})"));
                return;
            }
            var style = CurveStyle(I(a[1]), I(a[3]));
            Occurrence(I(a[0]), "ANNOTATION_CURVE_OCCURRENCE()", style, curve);
        }

        private void Fill(int layer, int outer, List<double> holes, List<string> styles)
        {
            var loops = new[] { outer }.Concat(holes.Select(h => (int)h)).Where(i => i >= 1 && i <= _composites.Count).Select(i => _composites[i - 1]).ToList();
            if (loops.Count == 0) return;
            var area = _p.E($"ANNOTATION_FILL_AREA(' ',({string.Join(",", loops)}))");
            var fs = _p.E($"FILL_AREA_STYLE(' ',({string.Join(",", styles)}))");
            var style = _p.E($"PRESENTATION_STYLE_ASSIGNMENT(({fs}))");
            Occurrence(layer, $"ANNOTATION_FILL_AREA_OCCURRENCE({Point(0, 0)})", style, area);
        }

        private void Text(int layer, int color, string s, double x, double y, double h, double w, double spc, double angle, double slant, int b, int direct)
        {
            b = Math.Clamp(b, 1, 9);
            int row = (b - 1) / 3, col = (b - 1) % 3;
            // SCADEC は文字の点を動かす: 下の行は高さの半分上、中の行は半分下、上の行は高さ分下
            var k = new[] { 0.5, -0.5, -1.0 }[row] * h;
            var t = angle * Math.PI / 180;
            var (px, py) = (x - Math.Sin(t) * k, y + Math.Cos(t) * k);
            var ext = _p.E($"PLANAR_EXTENT(' ',{StepWriter.F(w)},{StepWriter.F(h)})");
            var dir = _p.E($"DIRECTION(' ',({StepWriter.A(Math.Cos(t))},{StepWriter.A(Math.Sin(t))}))");
            var pl = _p.E($"AXIS2_PLACEMENT_2D(' ',{Point(px, py)},{dir})");
            string[] cols = ["left", "centre", "right"];
            string[] rows = ["baseline", "middleline", "topline"];
            _textFont ??= _p.E($"EXTERNALLY_DEFINED_TEXT_FONT(IDENTIFIER({StepWriter.S("ＭＳ ゴシック")}),{_source})");
            var lit = _p.E($"TEXT_LITERAL_WITH_EXTENT('$$SXF_{rows[row]} {cols[col]}',{StepWriter.S(s)},{pl},'baseline {cols[col]}',{(direct == 2 ? ".DOWN." : ".RIGHT.")},{_textFont},{ext})");
            var fd = _p.E($"TEXT_STYLE_FOR_DEFINED_FONT({Colour(color)})");
            var n = Math.Max(s.Length, 1);
            var ts = _p.Complex($"TEXT_STYLE(' ',{fd})",
                $"TEXT_STYLE_WITH_BOX_CHARACTERISTICS((BOX_HEIGHT({StepWriter.F(h)}),BOX_WIDTH({StepWriter.F(w / n)}),BOX_SLANT_ANGLE({StepWriter.A(slant * Math.PI / 180)}),BOX_ROTATE_ANGLE({StepWriter.A(0)})))",
                $"TEXT_STYLE_WITH_SPACING(LENGTH_MEASURE({StepWriter.F(spc)}))");
            var style = _p.E($"PRESENTATION_STYLE_ASSIGNMENT(({ts}))");
            Occurrence(layer, "ANNOTATION_TEXT_OCCURRENCE()", style, lit);
        }

        /// <summary>点マーカー (既定義の点マーカー記号を SYMBOL_TARGET の位置・角度・倍率で置く)。</summary>
        private void Marker(int layer, int color, double x, double y, int code, double angleDeg, double scale)
        {
            string[] markers = ["asterisk", "circle", "dot", "plus", "square", "triangle", "x"];
            var symbol = _p.E($"PRE_DEFINED_POINT_MARKER_SYMBOL('{markers[Math.Clamp(code, 1, 7) - 1]}')");
            var t = angleDeg * Math.PI / 180;
            var dir = _p.E($"DIRECTION(' ',({StepWriter.A(Math.Cos(t))},{StepWriter.A(Math.Sin(t))}))");
            var pl = _p.E($"AXIS2_PLACEMENT_2D(' ',{Point(x, y)},{dir})");
            var k = StepWriter.A(scale > 0 ? scale : 1);
            var target = _p.E($"SYMBOL_TARGET(' ',{pl},{k},{k})");
            var defined = _p.E($"DEFINED_SYMBOL(' ',{symbol},{target})");
            var sc = _p.E($"SYMBOL_COLOUR({Colour(color)})");
            var ss = _p.E($"SYMBOL_STYLE(' ',{sc})");
            var style = _p.E($"PRESENTATION_STYLE_ASSIGNMENT(({ss}))");
            Occurrence(layer, "ANNOTATION_SYMBOL_OCCURRENCE()", style, defined);
        }

        private void Occurrence(int layer, string kind, string style, string item)
        {
            var occ = _p.Complex(Sorted(kind, "ANNOTATION_OCCURRENCE()", "DRAUGHTING_ANNOTATION_OCCURRENCE()", "GEOMETRIC_REPRESENTATION_ITEM()",
                "REPRESENTATION_ITEM(' ')", $"STYLED_ITEM(({style}),{item})"));
            if (!_layerItems.TryGetValue(layer, out var list)) _layerItems[layer] = list = [];
            list.Add(occ);
            _items.Add(occ);
        }

        private string CurveStyle(int color, int width)
        {
            var cs = _p.E($"CURVE_STYLE(' ',{Font()},{Width(width)},{Colour(color)})");
            return _p.E($"PRESENTATION_STYLE_ASSIGNMENT(({cs}))");
        }

        private string Font() => _font ??= _p.E("DRAUGHTING_PRE_DEFINED_CURVE_FONT('continuous')");

        private string Colour(int code)
        {
            if (_colours.TryGetValue(code, out var c)) return c;
            if (code is < 1 or > 16) code = 1;
            if (_colours.TryGetValue(code, out c)) return c;
            Write(new Feature("pre_defined_colour_feature", [PredefinedColorNames[code - 1]]));
            return _colours[code];
        }

        private string Width(int code)
        {
            if (_widths.TryGetValue(code, out var w)) return w;
            var v = code is >= 1 and <= 9 ? PredefinedWidths[code - 1] : PredefinedWidths[0];
            return _widths[code] = WidthMeasure(v);
        }

        private string WidthMeasure(double mm) => _p.E($"LENGTH_MEASURE_WITH_UNIT(POSITIVE_LENGTH_MEASURE({StepWriter.F(mm)}),{_mm})");

        private string Point(double x, double y) => _p.E($"CARTESIAN_POINT(' ',({StepWriter.F(x)},{StepWriter.F(y)}))");

        private string Placement(double x, double y, double? angleDeg = null)
        {
            var p = Point(x, y);
            if (angleDeg is not double deg || deg == 0) return _p.E($"AXIS2_PLACEMENT_2D(' ',{p},$)");
            var t = deg * Math.PI / 180;
            var d = _p.E($"DIRECTION(' ',({StepWriter.A(Math.Cos(t))},{StepWriter.A(Math.Sin(t))}))");
            return _p.E($"AXIS2_PLACEMENT_2D(' ',{p},{d})");
        }

        private string LineCurve(double x0, double y0, double x1, double y1)
        {
            var (p, q) = (Point(x0, y0), Point(x1, y1));
            var d = _p.E($"DIRECTION(' ',({StepWriter.F(x1 - x0)},{StepWriter.F(y1 - y0)}))");
            var v = _p.E($"VECTOR(' ',{d},1.000000)");
            var ln = _p.E($"LINE(' ',{p},{v})");
            return _p.E($"TRIMMED_CURVE(' ',{ln},({p}),({q}),.T.,.CARTESIAN.)");
        }

        private static string[] Sorted(params string[] records) => [.. records.OrderBy(r => r, StringComparer.Ordinal)];
    }

    // ---- 読み込み ----

    public static SfcIO.LoadResult Load(CadDocument doc, string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length > 2 && bytes[0] == 'P' && bytes[1] == 'K') bytes = Unzip(bytes); // .p2z
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var text = Encoding.GetEncoding(932).GetString(bytes); // 本文は ASCII (日本語は \X2\)。念のため Shift_JIS で
        var step = StepFile.Parse(text);
        var features = new Reader(step).Run();
        if (!features.Any(f => f.Name is not ("layer_feature" or "user_defined_colour_feature" or "width_feature")))
            throw new InvalidDataException("P21 の図形が見つかりませんでした (SXF の AP202 形式ではない可能性があります)。");
        return SfcIO.Build(doc, features);
    }

    private static byte[] Unzip(byte[] zip)
    {
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        var entry = archive.Entries.FirstOrDefault(e => e.Name.EndsWith(".p21", StringComparison.OrdinalIgnoreCase)) ?? archive.Entries.FirstOrDefault()
            ?? throw new InvalidDataException("P2Z の中に P21 ファイルがありません。");
        using var s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>AP202 のインスタンスを SFC のフィーチャに直す。</summary>
    private sealed class Reader(StepFile f)
    {
        private readonly List<Feature> _tables = [];
        private readonly List<Feature> _defs = [];           // 部分図の定義 (中身 + sfig_org_feature)。入れ子は内側が先
        private readonly Dictionary<int, int> _layerOf = []; // 図形 → レイヤ番号
        private readonly Dictionary<int, string> _sfigNames = [];
        private readonly Dictionary<string, int> _userColors = [];
        private readonly Dictionary<string, int> _userWidths = [];
        private readonly HashSet<int> _defining = [];
        private int _composites;

        public List<Feature> Run()
        {
            // レイヤ (部分図の配置用のダミーは除く)。INVISIBILITY のものは非表示
            var invisible = new HashSet<int>();
            foreach (var inst in Instances("INVISIBILITY"))
                foreach (var r in List(inst.Records[0][0])) if (r is StepRef rr) invisible.Add(rr.Id);
            var layers = new List<Feature>();
            foreach (var la in Instances("PRESENTATION_LAYER_ASSIGNMENT"))
            {
                var name = Str(la.Records[0][0]);
                if (name.StartsWith("$$SXF_dummy", StringComparison.Ordinal)) continue;
                layers.Add(new Feature("layer_feature", [name, invisible.Contains(la.Id) ? "0" : "1"]));
                foreach (var item in List(la.Records[0][2])) if (item is StepRef r) _layerOf[r.Id] = layers.Count;
            }

            var body = new List<Feature>();
            var sheet = Instances("DRAWING_SHEET_REVISION").FirstOrDefault();
            Feature? sheetFeature = null;
            if (sheet != null)
            {
                var rec = sheet.Records[0];
                foreach (var item in List(rec[1]))
                {
                    if (f[item] is { Name: "PLANAR_BOX" } box)
                    {
                        var (w, h) = (Num(box.Records[0][1]), Num(box.Records[0][2]));
                        sheetFeature = new Feature("drawing_sheet_feature", [Str(rec[0]), PaperType(w, h), "1", N(w), N(h)]);
                        continue;
                    }
                    Item(item, body);
                }
            }
            var result = new List<Feature>();
            result.AddRange(_tables);
            result.AddRange(layers);
            result.AddRange(_defs);
            if (sheetFeature != null) result.Add(sheetFeature);
            result.AddRange(body);
            return result;
        }

        private static string PaperType(double w, double h)
        {
            var (a, b) = (Math.Max(w, h), Math.Min(w, h));
            (double, double)[] sizes = [(1189, 841), (841, 594), (594, 420), (420, 297), (297, 210)];
            var i = Array.FindIndex(sizes, s => Math.Abs(s.Item1 - a) < 1 && Math.Abs(s.Item2 - b) < 1);
            return i >= 0 ? i.ToString(Inv) : "9";
        }

        private IEnumerable<StepInstance> Instances(string name) => f.Order.Select(id => f.Instances[id]).Where(i => i.Has(name));

        /// <summary>図面・部分図の要素 1 つ (注記の出現・寸法などのまとまり)。</summary>
        /// <param name="inheritedLayer">寸法などのまとまりのレイヤ (中の要素はレイヤに入っていない)。</param>
        private void Item(StepValue v, List<Feature> output, int? inheritedLayer = null)
        {
            if (f[v] is not { } inst) return;
            var layerCode = _layerOf.TryGetValue(inst.Id, out var lc) ? lc : inheritedLayer ?? 1;
            var layer = layerCode.ToString(Inv);
            var styled = inst.Get("STYLED_ITEM");
            if (styled == null) return;
            // STYLED_ITEM の引数: 単純なインスタンスは (名前, 様式, 対象)、複合では (様式, 対象)
            var (styles, target) = styled.Params.Count >= 3 ? (styled[1], styled[2]) : (styled[0], styled[1]);
            var item = f[target];
            if (item == null) return;

            if (item.Get("DRAUGHTING_CALLOUT") is { } callout)
            {
                Callout(item, callout, output, layerCode);
                return;
            }
            if (inst.Has("ANNOTATION_SUBFIGURE_OCCURRENCE") || item.Has("MAPPED_ITEM"))
            {
                Subfigure(item, output);
                return;
            }
            var style = FirstStyle(styles);
            if (inst.Has("ANNOTATION_TEXT_OCCURRENCE")) Text(item, style, layer, output);
            else if (inst.Has("ANNOTATION_FILL_AREA_OCCURRENCE")) Fill(item, style, layer, output);
            else if (inst.Has("ANNOTATION_SYMBOL_OCCURRENCE")) Symbol(item, style, layer, output);
            else if (inst.Has("ANNOTATION_CURVE_OCCURRENCE"))
            {
                var (color, width) = CurveStyle(style);
                CurveFeatures(item, layer, color, width, output);
            }
        }

        /// <summary>
        /// 寸法・引出線・バルーン。SFC の読み込みと同じく、長さ寸法は寸法線と値、引出線は線と文字、
        /// それ以外 (角度・半径・直径寸法、バルーン) は文字だけを取り込む (補助線・矢印などは省く)。
        /// </summary>
        private void Callout(StepInstance item, StepRecord callout, List<Feature> output, int layer)
        {
            var members = List(callout[0]).Select(m => f[m]).OfType<StepInstance>().ToList();
            var linear = item.Has("LINEAR_DIMENSION");
            var balloon = members.Any(m => m.Get("STYLED_ITEM") is { } s && f[s.Params.Count >= 3 ? s[2] : s[1]] is { } g && g.Has("CIRCLE"));
            var leader = item.Has("LEADER_DIRECTED_CALLOUT") && !balloon;
            foreach (var m in members)
            {
                var keep = m.Has("ANNOTATION_TEXT_OCCURRENCE")
                           || linear && m.Has("DIMENSION_CURVE")
                           || leader && m.Has("LEADER_CURVE");
                if (keep) Item(new StepRef(m.Id), output, layer);
            }
        }

        private StepInstance? FirstStyle(StepValue styles)
        {
            // PRESENTATION_STYLE_ASSIGNMENT((様式, …))
            foreach (var s in List(styles))
                if (f[s] is { } psa && psa.Get("PRESENTATION_STYLE_ASSIGNMENT") is { } r)
                    foreach (var st in List(r[0]))
                        if (f[st] is { } x) return x;
            return null;
        }

        private (string Color, string Width) CurveStyle(StepInstance? style)
        {
            if (style?.Get("CURVE_STYLE") is not { } cs) return ("1", "1");
            return (ColourCode(cs[3]), WidthCode(cs[2]));
        }

        private string ColourCode(StepValue v)
        {
            if (f[v] is not { } c) return "1";
            if (c.Get("DRAUGHTING_PRE_DEFINED_COLOUR") is { } pre)
            {
                var i = Array.IndexOf(PredefinedColorNames, Str(pre[0]).ToLowerInvariant());
                return (i >= 0 ? i + 1 : 1).ToString(Inv);
            }
            if (c.Get("COLOUR_RGB") is { } rgb)
            {
                var name = Str(rgb[0]);
                if (name.StartsWith("$$SXF_", StringComparison.Ordinal))
                {
                    var i = Array.IndexOf(PredefinedColorNames, name[6..].ToLowerInvariant());
                    if (i >= 0) return (i + 1).ToString(Inv);
                }
                var r = (int)Math.Round(Math.Clamp(Num(rgb[1]), 0, 1) * 255);
                var g = (int)Math.Round(Math.Clamp(Num(rgb[2]), 0, 1) * 255);
                var b = (int)Math.Round(Math.Clamp(Num(rgb[3]), 0, 1) * 255);
                var key = $"{r},{g},{b}";
                if (!_userColors.TryGetValue(key, out var code))
                {
                    _userColors[key] = code = 17 + _userColors.Count;
                    _tables.Add(new Feature("user_defined_colour_feature", [r.ToString(Inv), g.ToString(Inv), b.ToString(Inv)]));
                }
                return code.ToString(Inv);
            }
            return "1";
        }

        private string WidthCode(StepValue v)
        {
            var w = f[v] is { } m && m.Get("LENGTH_MEASURE_WITH_UNIT") is { } r ? Num(r[0]) : double.NaN;
            if (!double.IsFinite(w) || w <= 0) return "1";
            var i = Array.FindIndex(PredefinedWidths, p => Math.Abs(p - w) < 0.005);
            if (i >= 0) return (i + 1).ToString(Inv);
            var key = N(w);
            if (!_userWidths.TryGetValue(key, out var code))
            {
                if (_userWidths.Count >= 7) return (Array.IndexOf(PredefinedWidths, PredefinedWidths.MinBy(p => Math.Abs(p - w))) + 1).ToString(Inv);
                _userWidths[key] = code = 10 + _userWidths.Count;
                _tables.Add(new Feature("width_feature", [key]));
            }
            return code.ToString(Inv);
        }

        /// <summary>曲線を SFC の図形フィーチャに (レイヤ 0・色 0 にすると複合曲線の構成要素になる)。</summary>
        private void CurveFeatures(StepInstance curve, string layer, string color, string width, List<Feature> output)
        {
            List<string> Head() => [layer, color, "1", width];
            if (curve.Get("TRIMMED_CURVE") is { } tc)
            {
                var basis = f[tc[1]];
                if (basis?.Get("LINE") != null)
                {
                    if (TrimPoint(tc[2]) is { } p && TrimPoint(tc[3]) is { } q)
                        output.Add(new Feature("line_feature", [.. Head(), N(p.X), N(p.Y), N(q.X), N(q.Y)]));
                    return;
                }
                if (basis?.Get("CIRCLE") is { } circle && TrimParam(tc[2]) is double a0 && TrimParam(tc[3]) is double a1)
                {
                    var (c, rot) = Placement(circle[1]);
                    var ccw = tc[4] is not StepEnum { Value: "F" };
                    output.Add(new Feature("arc_feature", [.. Head(), N(c.X), N(c.Y), N(Num(circle[2])), ccw ? "0" : "1", Deg(a0 + rot), Deg(a1 + rot)]));
                    return;
                }
                if (basis?.Get("ELLIPSE") is { } el && TrimParam(tc[2]) is double e0 && TrimParam(tc[3]) is double e1)
                {
                    var (c, rot) = Placement(el[1]);
                    var ccw = tc[4] is not StepEnum { Value: "F" };
                    output.Add(new Feature("ellipse_arc_feature", [.. Head(), N(c.X), N(c.Y), N(Num(el[2])), N(Num(el[3])), ccw ? "0" : "1", Deg(rot), Deg(e0), Deg(e1)]));
                    return;
                }
                if (basis?.Get("CLOTHOID") is { } cl && TrimParam(tc[2]) is double s0 && TrimParam(tc[3]) is double s1)
                {
                    var (c, rot) = Placement(cl[1]);
                    output.Add(new Feature("clothoid_feature", [.. Head(), N(c.X), N(c.Y), N(Num(cl[2])), Str(cl[0]) == "1" ? "1" : "0", Deg(rot), N(s0), N(s1)]));
                    return;
                }
                return;
            }
            if (curve.Get("POLYLINE") is { } pl)
            {
                var pts = List(pl[1]).Select(PointOf).OfType<(double X, double Y)>().ToList();
                if (pts.Count >= 2) output.Add(PolylineFeature(Head(), pts));
                return;
            }
            if (curve.Get("CIRCLE") is { } ci)
            {
                var (c, _) = Placement(ci[1]);
                output.Add(new Feature("circle_feature", [.. Head(), N(c.X), N(c.Y), N(Num(ci[2]))]));
                return;
            }
            if (curve.Get("ELLIPSE") is { } e)
            {
                var (c, rot) = Placement(e[1]);
                output.Add(new Feature("ellipse_feature", [.. Head(), N(c.X), N(c.Y), N(Num(e[2])), N(Num(e[3])), Deg(rot)]));
                return;
            }
            if ((curve.Get("BEZIER_CURVE") ?? curve.Get("B_SPLINE_CURVE") ?? curve.Get("B_SPLINE_CURVE_WITH_KNOTS") ?? curve.Get("RATIONAL_B_SPLINE_CURVE")) is not null)
            {
                // スプラインは制御点を結んだ折れ線で近似する
                var ptsList = curve.Records.SelectMany(r => r.Params).OfType<StepList>().FirstOrDefault(l => l.Items.Count > 1 && l.Items.All(x => x is StepRef));
                var pts = (ptsList?.Items ?? []).Select(PointOf).OfType<(double X, double Y)>().ToList();
                if (pts.Count >= 2) output.Add(PolylineFeature(Head(), pts));
                return;
            }
            if (curve.Get("COMPOSITE_CURVE") is { } cc)
            {
                // 見えている複合曲線は、構成要素をそれぞれ同じ様式の図形にする
                foreach (var seg in List(cc[1]))
                    if (f[seg]?.Get("COMPOSITE_CURVE_SEGMENT") is { } s && f[s[2]] is { } part) CurveFeatures(part, layer, color, width, output);
            }
        }

        private static Feature PolylineFeature(List<string> head, List<(double X, double Y)> pts) =>
            new("polyline_feature", [.. head, pts.Count.ToString(Inv), "(" + string.Join(",", pts.Select(p => N(p.X))) + ")", "(" + string.Join(",", pts.Select(p => N(p.Y))) + ")"]);

        private (double X, double Y)? TrimPoint(StepValue v)
        {
            foreach (var x in List(v)) if (PointOf(x) is { } p) return p;
            return null;
        }

        private static double? TrimParam(StepValue v)
        {
            foreach (var x in v is StepList l ? l.Items : [v])
            {
                var inner = x is StepTyped { Name: "PARAMETER_VALUE" } t ? t.Value : x;
                if (inner is StepList { Items: [StepNumber n] }) return n.Value;
                if (inner is StepNumber num) return num.Value;
            }
            return null;
        }

        private void Text(StepInstance lit, StepInstance? style, string layer, List<Feature> output)
        {
            var r = lit.Get("TEXT_LITERAL_WITH_EXTENT") ?? lit.Get("TEXT_LITERAL");
            if (r == null) return;
            var name = Str(r[0]);
            var s = Str(r[1]);
            var ((px, py), angle) = Placement(r[2]);
            var down = r[4] is StepEnum { Value: "DOWN" };
            double w = 0, h = 0;
            if (r.Name == "TEXT_LITERAL_WITH_EXTENT" && f[r[6]]?.Get("PLANAR_EXTENT") is { } ext) (w, h) = (Num(ext[1]), Num(ext[2]));
            var color = "1";
            double slant = 0, spacing = 0;
            if (style != null)
            {
                if (style.Get("TEXT_STYLE") is { } ts && f[ts[1]]?.Get("TEXT_STYLE_FOR_DEFINED_FONT") is { } fd) color = ColourCode(fd[0]);
                if (style.Get("TEXT_STYLE_WITH_BOX_CHARACTERISTICS") is { } box)
                    foreach (var c in List(box[0]))
                        switch (c)
                        {
                            case StepTyped { Name: "BOX_HEIGHT", Value: StepNumber bh }: h = bh.Value; break;
                            case StepTyped { Name: "BOX_SLANT_ANGLE", Value: StepNumber sl }: slant = sl.Value * 180 / Math.PI; break;
                        }
                if (style.Get("TEXT_STYLE_WITH_SPACING") is { } sp && sp[0] is StepTyped { Value: StepNumber spc }) spacing = spc.Value;
            }
            // 配置基点: 名前 '$$SXF_<行> <列>' (行: baseline / middleline / topline, 列: left / centre / right)
            var row = name.Contains("topline") ? 2 : name.Contains("middleline") ? 1 : 0;
            var col = name.Contains("centre") || name.Contains("center") ? 1 : name.Contains("right") ? 2 : 0;
            // SCADEC が動かした点を戻す
            var k = new[] { 0.5, -0.5, -1.0 }[row] * h;
            var x = px + Math.Sin(angle) * k;
            var y = py - Math.Cos(angle) * k;
            output.Add(new Feature("text_string_feature",
                [layer, color, "1", s, N(x), N(y), N(h), N(w), N(spacing), Deg(angle), N(slant), (row * 3 + col + 1).ToString(Inv), down ? "2" : "1"]));
        }

        private void Fill(StepInstance area, StepInstance? style, string layer, List<Feature> output)
        {
            if (area.Get("ANNOTATION_FILL_AREA") is not { } fa) return;
            // 輪郭の複合曲線を構成要素 (レイヤ 0・色 0) と composite_curve_org_feature にする
            var ids = new List<int>();
            foreach (var b in List(fa[1]))
            {
                if (f[b] is not { } boundary) continue;
                var parts = new List<Feature>();
                if (boundary.Get("COMPOSITE_CURVE") is { } cc)
                {
                    foreach (var seg in List(cc[1]))
                        if (f[seg]?.Get("COMPOSITE_CURVE_SEGMENT") is { } s && f[s[2]] is { } part) CurveFeatures(part, "0", "0", "0", parts);
                }
                else
                {
                    CurveFeatures(boundary, "0", "0", "0", parts);
                }
                if (parts.Count == 0) continue;
                output.AddRange(parts);
                output.Add(new Feature("composite_curve_org_feature", ["1", "1", "1", "0"]));
                ids.Add(++_composites);
            }
            if (ids.Count == 0 || style?.Get("FILL_AREA_STYLE") is not { } fs) return;
            var holes = "(" + string.Join(",", ids.Skip(1)) + ")";
            var patterns = new List<string>();
            foreach (var st in List(fs[1]))
            {
                if (f[st] is not { } s) continue;
                if (s.Get("FILL_AREA_STYLE_COLOUR") is { } col)
                {
                    output.Add(new Feature("fill_area_style_colour_feature", [layer, ColourCode(col[1]), ids[0].ToString(Inv), (ids.Count - 1).ToString(Inv), holes]));
                    return;
                }
                if (s.Get("FILL_AREA_STYLE_HATCHING") is { } h)
                {
                    var (color, width) = CurveStyle(f[h[1]]);
                    var spacing = 1.0;
                    if (f[h[2]]?.Get("ONE_DIRECTION_REPEAT_FACTOR") is { } rf && f[rf[1]]?.Get("VECTOR") is { } vec) spacing = Num(vec[2]);
                    var origin = PointOf(h[3]) ?? (0, 0);
                    patterns.Add($"({color},1,{width},{N(origin.X)},{N(origin.Y)},{N(spacing)},{Deg(Num(h[5]))})");
                }
            }
            if (patterns.Count > 0)
                output.Add(new Feature("fill_area_style_hatching_feature",
                    [layer, patterns.Count.ToString(Inv), .. patterns, ids[0].ToString(Inv), (ids.Count - 1).ToString(Inv), holes]));
        }

        private void Symbol(StepInstance item, StepInstance? style, string layer, List<Feature> output)
        {
            // 点マーカー (端末記号は寸法の一部なので省く)
            if (item.Get("DEFINED_SYMBOL") is not { } ds || f[ds[1]]?.Get("PRE_DEFINED_POINT_MARKER_SYMBOL") is not { } marker) return;
            if (f[ds[2]]?.Get("SYMBOL_TARGET") is not { } target) return;
            var ((x, y), angle) = Placement(target[1]);
            string[] markers = ["asterisk", "circle", "dot", "plus", "square", "triangle", "x"];
            var code = Array.IndexOf(markers, Str(marker[0])) + 1;
            var color = style?.Get("SYMBOL_STYLE") is { } ss && f[ss[1]]?.Get("SYMBOL_COLOUR") is { } sc ? ColourCode(sc[0]) : "1";
            output.Add(new Feature("point_marker_feature", [layer, color, N(x), N(y), Math.Max(code, 1).ToString(Inv), Deg(angle), N(Num(target[2]))]));
        }

        private void Subfigure(StepInstance mapped, List<Feature> output)
        {
            if (mapped.Get("MAPPED_ITEM") is not { } mi) return;
            // 単純なインスタンスは (名前, 写像, 配置先)、複合インスタンスの部分は自分の属性 (写像, 配置先) だけ
            var (mapRef, targetRef) = mi.Params.Count >= 3 ? (mi[1], mi[2]) : (mi[0], mi[1]);
            if (f[mapRef] is not { } map || (map.Get("SYMBOL_REPRESENTATION_MAP") ?? map.Get("REPRESENTATION_MAP")) is not { } mapRec) return;
            var name = Define(mapRec[1]);
            if (name == null || f[targetRef]?.Get("SYMBOL_TARGET") is not { } target) return;
            var ((x, y), angle) = Placement(target[1]);
            output.Add(new Feature("sfig_locate_feature", ["0", name, N(x), N(y), Deg(angle), N(Num(target[2])), N(Num(target[3]))]));
        }

        /// <summary>部分図の定義を (まだなら) フィーチャにして、その名前を返す。</summary>
        private string? Define(StepValue repRef)
        {
            if (f[repRef] is not { } rep || rep.Records[0] is not { } r) return null;
            if (_sfigNames.TryGetValue(rep.Id, out var known)) return known;
            if (!_defining.Add(rep.Id)) return null; // 循環
            var full = Str(r[0]);
            var flag = 1;
            var name = full;
            for (var i = 0; i < SfigPrefixes.Length; i++)
                if (full.StartsWith(SfigPrefixes[i], StringComparison.Ordinal))
                {
                    flag = i + 1;
                    name = full[SfigPrefixes[i].Length..];
                }
            if (_sfigNames.ContainsValue(name)) name += "_" + rep.Id.ToString(Inv);
            var members = new List<Feature>();
            foreach (var item in List(r[1])) Item(item, members);
            _defs.AddRange(members);
            _defs.Add(new Feature("sfig_org_feature", [name, flag.ToString(Inv)]));
            _sfigNames[rep.Id] = name;
            return name;
        }

        // ---- 値 ----

        private (( double X, double Y) P, double Angle) Placement(StepValue v)
        {
            if (f[v]?.Get("AXIS2_PLACEMENT_2D") is not { } pl) return ((0, 0), 0);
            var p = PointOf(pl[1]) ?? (0, 0);
            var angle = f[pl[2]]?.Get("DIRECTION") is { } d && List(d[1]) is [StepNumber dx, StepNumber dy, ..] ? Math.Atan2(dy.Value, dx.Value) : 0;
            return (p, angle);
        }

        private (double X, double Y)? PointOf(StepValue v) =>
            f[v]?.Get("CARTESIAN_POINT") is { } cp && List(cp[1]) is [StepNumber x, StepNumber y, ..] ? (x.Value, y.Value) : null;

        private static List<StepValue> List(StepValue v) => v is StepList l ? l.Items : [];
        private static string Str(StepValue v) => v is StepString s ? s.Value : "";

        private static double Num(StepValue v) => v switch
        {
            StepNumber n => n.Value,
            StepTyped t => Num(t.Value),
            StepList { Items: [var x] } => Num(x),
            _ => 0,
        };

        private static string N(double v) => v.ToString("R", Inv);
        // 度は丸めて書く (360.0000000000002° のような値が 0° に戻って全円が消えないように)
        private static string Deg(double rad) => Math.Round(rad * 180 / Math.PI, 9).ToString("R", Inv);
    }

    // ---- フィーチャの値 ----

    private static double D(string s) => double.Parse(s, NumberStyles.Float, Inv);
    private static int I(string s) => (int)Math.Round(D(s));

    private static List<double> Ints(string list) =>
        [.. list.Trim('(', ')', ' ').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(D)];

    private static List<(double X, double Y)> Points(string xs, string ys)
    {
        var x = Ints(xs);
        var y = Ints(ys);
        return [.. x.Zip(y)];
    }
}
