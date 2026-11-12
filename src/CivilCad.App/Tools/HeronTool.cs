using System.Globalization;
using System.Windows.Media;
using CivilCad.App.Views;
using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.Tools;

/// <summary>ヘロン面積の設定 (作図入力バーで変更。次の計測でも使う)。</summary>
public static class HeronSettings
{
    /// <summary>標高のある点どうしは斜距離 (3D) で辺長を出す。オフなら水平距離。</summary>
    public static bool Slope { get; set; } = true;

    public static int SideDecimals
    {
        get => _sideDecimals;
        set => _sideDecimals = Math.Clamp(value, 0, 4);
    }

    public static int AreaDecimals
    {
        get => _areaDecimals;
        set => _areaDecimals = Math.Clamp(value, 0, 6);
    }

    private static int _sideDecimals = 2;
    private static int _areaDecimals = 3;

    /// <summary>文字高 (用紙上の mm)。</summary>
    public static double TextHeightMm { get; set; } = 2.5;

    /// <summary>連続三角形: 最初の 3 点で 1 個目、以降は新しい 1 点と直前の 2 点で次の三角形。</summary>
    public static bool Strip { get; set; }

    /// <summary>計算書 (表) を図面に置く。</summary>
    public static bool PlaceTable { get; set; } = true;
}

/// <summary>
/// ヘロン面積 (HE): 三角形の頂点を順にクリックして、辺長・番号・計算書 (ヘロン面積計算書) を図面に記入する。
/// 座標の点 (標高あり) にスナップした頂点どうしは斜距離で辺長を出す。
/// </summary>
public sealed class HeronTool : CadTool
{
    public const string LayerTriangle = "ヘロン-三角形";
    public const string LayerSide = "ヘロン-辺長";
    public const string LayerNumber = "ヘロン-番号";
    public const string LayerTable = "ヘロン-計算書";

    private static readonly RgbColor PreviewColor = new(0x66, 0xCC, 0xFF);

    private readonly List<HeronVertex> _verts = [];
    private bool _placingTable;
    private Vec2 _cursor;

    public override string Name => "ヘロン面積";
    public override Vec2? BasePoint => !_placingTable && _verts.Count > 0 ? _verts[^1].Position : null;
    public override bool CanStepBack => _placingTable || _verts.Count > 0;

    public override string? ConfirmLabel =>
        _placingTable ? "計算書なしで記入 (Enter)" : TriangleCount > 0 ? "ヘロン計算 (Enter)" : null;

    protected override void OnActivated() => Restart();

    private void Restart()
    {
        _verts.Clear();
        _placingTable = false;
        SetPrompt("三角形の頂点を順に指定 (Enter で計算書)");
    }

    // ---- 三角形の組み立て ----

    private int TriangleCount => HeronSettings.Strip ? Math.Max(0, _verts.Count - 2) : _verts.Count / 3;

    private IEnumerable<(HeronVertex A, HeronVertex B, HeronVertex C)> TriangleVertices()
    {
        var count = TriangleCount;
        for (var i = 0; i < count; i++)
        {
            if (HeronSettings.Strip) yield return (_verts[i], _verts[i + 1], _verts[i + 2]);
            else yield return (_verts[i * 3], _verts[i * 3 + 1], _verts[i * 3 + 2]);
        }
    }

    /// <summary>まだ三角形になっていない、指定済みの頂点 (ゴムひもの線の元)。</summary>
    private IEnumerable<HeronVertex> Pending()
    {
        var n = _verts.Count;
        if (HeronSettings.Strip) return n >= 3 ? _verts.Skip(n - 2) : _verts;
        return _verts.Skip(n / 3 * 3);
    }

    private sealed record Tri(HeronVertex P0, HeronVertex P1, HeronVertex P2, HeronTriangle R);

    private static HeronTriangle Calc(HeronVertex p0, HeronVertex p1, HeronVertex p2) =>
        Heron.Compute(Heron.SideLength(p1, p2, HeronSettings.Slope), Heron.SideLength(p0, p2, HeronSettings.Slope),
            Heron.SideLength(p0, p1, HeronSettings.Slope), HeronSettings.SideDecimals, HeronSettings.AreaDecimals);

    private List<Tri> Triangles() => [.. TriangleVertices().Select(t => new Tri(t.A, t.B, t.C, Calc(t.A, t.B, t.C)))];

    // ---- 入力 ----

    private HeronVertex VertexAt(Vec2 p)
    {
        PointEntity? found = null;
        foreach (var pt in Doc.Entities.OfType<PointEntity>())
        {
            if (pt.Position.DistanceTo(p) > 1e-3) continue;
            if (found == null || (found.Z == null && pt.Z != null)) found = pt;
        }
        return found == null ? new HeronVertex(p, null) : new HeronVertex(found.Position, found.Z, found.Name);
    }

    public override void OnPoint(Vec2 p)
    {
        if (_placingTable)
        {
            Commit(p);
            return;
        }
        var v = VertexAt(p);

        // この点で三角形ができるなら、つぶれていないか確かめる
        var n = _verts.Count;
        var completes = HeronSettings.Strip ? n >= 2 : n % 3 == 2;
        if (completes)
        {
            var a = _verts[n - 2];
            var b = _verts[n - 1];
            var cross = (b.Position - a.Position).Cross(v.Position - a.Position);
            var dup = v.Position.DistanceTo(a.Position) < 1e-9 || v.Position.DistanceTo(b.Position) < 1e-9;
            if (dup || Math.Abs(cross) < 1e-9)
            {
                Canvas.ShowMessage("三角形がつぶれています (同じ点・一直線上)。別の点を指定してください。");
                return;
            }
        }
        else if (n > 0 && v.Position.DistanceTo(_verts[n - 1].Position) < 1e-9)
        {
            return;
        }

        _verts.Add(v);
        if (completes) ReportLast();
    }

    private void ReportLast()
    {
        var tris = Triangles();
        var t = tris[^1];
        var r = t.R;
        var total = tris.Sum(x => x.R.Area);
        var s = HeronSettings.SideDecimals;
        var f = "F" + s;
        var af = "F" + HeronSettings.AreaDecimals;
        var ic = CultureInfo.InvariantCulture;
        var msg = $"三角形 {tris.Count}: a={r.A.ToString(f, ic)} b={r.B.ToString(f, ic)} c={r.C.ToString(f, ic)}  s={r.S.ToString("F" + (s + 1), ic)}  " +
                  $"A={r.Area.ToString(af, ic)} m²   合計 {total.ToString(af, ic)} m² ({tris.Count} 個)";
        if (HeronSettings.Slope && (t.P0.Z == null || t.P1.Z == null || t.P2.Z == null)) msg += " (標高の無い点は水平距離)";
        Canvas.ShowMessage(msg);
    }

    public override void OnMouseMove(Vec2 p) => _cursor = p;

    public override void OnEnter()
    {
        if (_placingTable)
        {
            Commit(null);
            return;
        }
        if (TriangleCount > 0)
        {
            if (HeronSettings.PlaceTable)
            {
                _placingTable = true;
                SetPrompt("計算書を置く位置 (左上) を指定 (Enter で計算書なし)");
            }
            else
            {
                Commit(null);
            }
            return;
        }
        if (_verts.Count == 0) Finish();
        else Canvas.ShowMessage("三角形が 1 個もできていません。3 点指定してください。");
    }

    public override void StepBack()
    {
        if (_placingTable)
        {
            _placingTable = false;
            SetPrompt("三角形の頂点を順に指定 (Enter で計算書)");
        }
        else if (_verts.Count > 0)
        {
            _verts.RemoveAt(_verts.Count - 1);
        }
    }

    private void Commit(Vec2? tableTopLeft)
    {
        var tris = Triangles();
        var entities = Build(tris, tableTopLeft, Doc.GetOrCreateLayer);
        Execute(new AddEntitiesCommand(entities, $"ヘロン面積 ({tris.Count} 個)"));
        Canvas.ShowMessage($"ヘロン面積 {tris.Sum(t => t.R.Area).ToString("F" + HeronSettings.AreaDecimals, CultureInfo.InvariantCulture)} m² ({tris.Count} 個) を記入しました。");
        Restart();
    }

    // ---- 図形の組み立て ----

    private static string EdgeKey(Vec2 a, Vec2 b)
    {
        static string K(Vec2 p) => $"{Math.Round(p.X, 6).ToString(CultureInfo.InvariantCulture)},{Math.Round(p.Y, 6).ToString(CultureInfo.InvariantCulture)}";
        var ka = K(a);
        var kb = K(b);
        return string.CompareOrdinal(ka, kb) <= 0 ? ka + "|" + kb : kb + "|" + ka;
    }

    private List<Entity> Build(List<Tri> tris, Vec2? tableTopLeft, Func<string, RgbColor, Layer> layerOf)
    {
        var h = Doc.PaperMmToMeters(HeronSettings.TextHeightMm);
        var result = new List<Entity>();
        var lineLayer = layerOf(LayerTriangle, RgbColor.White);
        var sideLayer = layerOf(LayerSide, RgbColor.Yellow);
        var numLayer = layerOf(LayerNumber, RgbColor.Cyan);
        var f = "F" + HeronSettings.SideDecimals;
        var ic = CultureInfo.InvariantCulture;

        var seen = new HashSet<string>();
        for (var i = 0; i < tris.Count; i++)
        {
            var t = tris[i];
            HeronVertex[] v = [t.P0, t.P1, t.P2];
            var pa = v[0].Position;
            var pb = v[1].Position;
            var pc = v[2].Position;
            var la = pb.DistanceTo(pc);
            var lb = pa.DistanceTo(pc);
            var lc = pa.DistanceTo(pb);
            var incenter = (pa * la + pb * lb + pc * lc) / (la + lb + lc);

            // 辺 (頂点 j → k) と、その丸めた長さ。a = 1-2、b = 0-2、c = 0-1
            (int, int, double)[] edges = [(1, 2, t.R.A), (0, 2, t.R.B), (0, 1, t.R.C)];
            foreach (var (j, k, len) in edges)
            {
                var p = v[j].Position;
                var q = v[k].Position;
                if (!seen.Add(EdgeKey(p, q))) continue;
                result.Add(new LineEntity(lineLayer, p, q));
                var mid = (p + q) / 2;
                var normal = (q - p).Normalized().LeftNormal;
                if ((mid - incenter).Dot(normal) < 0) normal = -normal;
                result.Add(new TextEntity(sideLayer, mid + normal * (0.7 * h), len.ToString(f, ic), h, MeasureSettings.ReadableAngle(p, q))
                {
                    HAlign = TextHAlign.Center,
                    VAlign = TextVAlign.Middle,
                });
            }

            result.Add(new CircleEntity(numLayer, incenter, 0.9 * h));
            result.Add(new TextEntity(numLayer, incenter, (i + 1).ToString(ic), h)
            {
                HAlign = TextHAlign.Center,
                VAlign = TextVAlign.Middle,
            });
        }

        if (tableTopLeft is Vec2 tl) BuildTable(result, tris, tl, h, layerOf(LayerTable, RgbColor.Green));
        return result;
    }

    /// <summary>文字の幅の見積もり (半角 0.6h・全角 1.0h)。</summary>
    private static double TextWidth(string s, double h) => s.Sum(c => c < 0x100 ? 0.6 : 1.0) * h;

    private static void BuildTable(List<Entity> result, List<Tri> tris, Vec2 topLeft, double h, Layer layer)
    {
        var s = HeronSettings.SideDecimals;
        var f = "F" + s;
        var fs = "F" + (s + 1);
        var af = "F" + HeronSettings.AreaDecimals;
        var ic = CultureInfo.InvariantCulture;
        static bool Has3D(Tri t) => t.P0.Z != null && t.P1.Z != null && t.P2.Z != null;
        var detail = HeronSettings.Slope && tris.Any(Has3D)
            ? tris.All(Has3D) ? "斜距離" : "斜距離・水平距離"
            : "水平距離";

        string[] header = ["NO.", "a", "b", "c", "s", "s×(s−a)×(s−b)×(s−c)", "面積(m²)"];
        var rows = new List<string[]>();
        for (var i = 0; i < tris.Count; i++)
        {
            var r = tris[i].R;
            rows.Add([(i + 1).ToString(ic), r.A.ToString(f, ic), r.B.ToString(f, ic), r.C.ToString(f, ic), r.S.ToString(fs, ic),
                $"{r.S.ToString(fs, ic)} × {r.SA.ToString(fs, ic)} × {r.SB.ToString(fs, ic)} × {r.SC.ToString(fs, ic)}",
                r.Area.ToString(af, ic)]);
        }
        rows.Add(["合計", "", "", "", "", "", tris.Sum(t => t.R.Area).ToString(af, ic)]);

        var cols = header.Length;
        var widths = new double[cols];
        for (var c = 0; c < cols; c++)
        {
            widths[c] = TextWidth(header[c], h);
            foreach (var row in rows) widths[c] = Math.Max(widths[c], TextWidth(row[c], h));
            widths[c] += 1.6 * h;
        }
        var rowH = 2.0 * h;
        var gridTop = topLeft.Y - rowH; // 一番上の行は表題
        var totalRows = rows.Count + 1;
        var width = widths.Sum();

        result.Add(new TextEntity(layer, new Vec2(topLeft.X, topLeft.Y - rowH / 2), $"ヘロン面積計算書 ({detail})", h * 1.2)
        {
            HAlign = TextHAlign.Left,
            VAlign = TextVAlign.Middle,
        });

        // 罫線: 横線 (行の数 + 1 本)・縦線 (列の数 + 1 本)
        for (var r = 0; r <= totalRows; r++)
        {
            var y = gridTop - r * rowH;
            result.Add(new LineEntity(layer, new Vec2(topLeft.X, y), new Vec2(topLeft.X + width, y)));
        }
        var x = topLeft.X;
        for (var c = 0; c <= cols; c++)
        {
            result.Add(new LineEntity(layer, new Vec2(x, gridTop), new Vec2(x, gridTop - totalRows * rowH)));
            if (c < cols) x += widths[c];
        }

        // 文字 (各セルの中央)
        for (var r = 0; r < totalRows; r++)
        {
            var cells = r == 0 ? header : rows[r - 1];
            var cx = topLeft.X;
            for (var c = 0; c < cols; c++)
            {
                if (cells[c].Length > 0)
                {
                    result.Add(new TextEntity(layer, new Vec2(cx + widths[c] / 2, gridTop - (r + 0.5) * rowH), cells[c], h)
                    {
                        HAlign = TextHAlign.Center,
                        VAlign = TextVAlign.Middle,
                    });
                }
                cx += widths[c];
            }
        }
    }

    // ---- プレビュー ----

    public override void DrawPreview(DrawingContext dc)
    {
        var tris = Triangles();
        if (tris.Count > 0)
        {
            // プレビューでは図面にレイヤを足さない (無ければ仮のレイヤ)
            var temp = new Dictionary<string, Layer>();
            Layer TempLayer(string name, RgbColor color) =>
                Doc.FindLayer(name) ?? (temp.TryGetValue(name, out var l) ? l : temp[name] = new Layer(name, color));
            foreach (var e in Build(tris, _placingTable ? _cursor : null, TempLayer)) Canvas.DrawEntity(dc, e, PreviewColor);
        }
        if (_placingTable) return;
        var pending = Pending().ToList();
        for (var i = 1; i < pending.Count; i++) Canvas.DrawWorldLine(dc, CadCanvas.PreviewPen, pending[i - 1].Position, pending[i].Position);
        foreach (var p in pending) Canvas.DrawWorldLine(dc, CadCanvas.PreviewPen, p.Position, _cursor);
    }
}
