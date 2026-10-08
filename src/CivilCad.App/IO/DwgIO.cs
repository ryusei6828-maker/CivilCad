using System.IO;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using CSMath;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;
using AcadDocument = ACadSharp.CadDocument;
using AcadEntity = ACadSharp.Entities.Entity;
using AcadLayer = ACadSharp.Tables.Layer;
using CadDocument = CivilCad.Core.Model.CadDocument;
using Entity = CivilCad.Core.Model.Entity;
using HatchPattern = CivilCad.Core.Model.HatchPattern;
using Layer = CivilCad.Core.Model.Layer;
using TextEntity = CivilCad.Core.Model.TextEntity;

namespace CivilCad.App.IO;

/// <summary>
/// DWG の読み書き (ACadSharp)。対応は DXF (<see cref="DxfIO"/>) と同じ:
/// 線・ポリライン・円・円弧・文字・ハッチングとレイヤ、縮尺 ($DIMSCALE)、平面直角座標系 ($USERI1)。
/// ブロック参照は分解して取り込む。保存は DXF を書いてから DWG に変換する。
/// </summary>
public static class DwgIO
{
    /// <summary>保存する DWG の版。AutoCAD 2013 形式 (文字列が Unicode なので日本語がコードページに左右されない)。</summary>
    public const ACadVersion SaveVersion = ACadVersion.AC1027;

    private const int MaxBlockDepth = 16;

    /// <summary>DWG を読み込んで doc を置き換える。戻り値は読み込めなかった図形の数。</summary>
    public static int Load(CadDocument doc, string path) => Load(doc, path, out _, out _);

    /// <param name="fileScale">ファイルの縮尺 ($DIMSCALE)。無ければ null。</param>
    /// <param name="unit">ファイルの単位 ($INSUNITS)。m / cm / mm 以外は null。</param>
    public static int Load(CadDocument doc, string path, out double? fileScale, out ImportUnit? unit)
    {
        AcadDocument dwg;
        try
        {
            dwg = DwgReader.Read(path);
        }
        catch (Exception ex) when (ex is not IOException and not UnauthorizedAccessException)
        {
            throw new InvalidDataException("DWG ファイルを読み込めませんでした (対応は AutoCAD R14 以降の形式): " + ex.Message, ex);
        }
        fileScale = dwg.Header.DimensionScaleFactor > 1 ? dwg.Header.DimensionScaleFactor : null;
        unit = dwg.Header.InsUnits switch
        {
            ACadSharp.Types.Units.UnitsType.Meters => ImportUnit.Meter,
            ACadSharp.Types.Units.UnitsType.Centimeters => ImportUnit.Centimeter,
            ACadSharp.Types.Units.UnitsType.Millimeters => ImportUnit.Millimeter,
            _ => null,
        };
        return new Importer(doc).Run(dwg);
    }

    public static void Save(CadDocument doc, string path)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "civilcad-" + Guid.NewGuid().ToString("N") + ".dxf");
        try
        {
            DxfIO.Save(doc, tmp);
            var cad = DxfReader.Read(tmp);
            cad.Header.Version = SaveVersion;
            using var writer = new DwgWriter(path, cad);
            writer.Write();
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    private sealed class Importer(CadDocument doc)
    {
        private readonly Dictionary<string, Layer> _layers = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<Entity> _out = [];
        private int _skipped;

        public int Run(AcadDocument dwg)
        {
            doc.Clear();
            if (dwg.Header.DimensionScaleFactor > 1) doc.ScaleDenominator = dwg.Header.DimensionScaleFactor; // 1 以下は未設定とみなす
            if (dwg.Header.UserShort1 is >= 1 and <= 19) doc.PlaneZone = dwg.Header.UserShort1;
            // 測量座標の向き (CivilCad の DXF と同じ $USERR1〜3)
            if (dwg.Header.UserDouble1 is var rot && rot != 0 && double.IsFinite(rot) && double.IsFinite(dwg.Header.UserDouble2) && double.IsFinite(dwg.Header.UserDouble3))
                doc.SetSurveyFrame(rot, new Vec2(dwg.Header.UserDouble2, dwg.Header.UserDouble3));
            foreach (var al in dwg.Layers)
            {
                var color = ToRgb(al.Color) ?? RgbColor.White;
                var l = doc.GetOrCreateLayer(al.Name, color);
                l.Color = color;
                l.IsVisible = (al.IsOn && !al.Flags.HasFlag(ACadSharp.Tables.LayerFlags.Frozen)) || l.Name == "0";
                l.IsLocked = al.Flags.HasFlag(ACadSharp.Tables.LayerFlags.Locked) && l.Name != "0";
                _layers[al.Name] = l;
            }
            foreach (var e in dwg.Entities) SafeConvert(e, null, 0);
            doc.Entities.AddRange(_out);
            doc.NotifyChanged();
            return _skipped;
        }

        private Layer LayerOf(AcadEntity e, AcadLayer? inherited)
        {
            // ブロックの中の「0」レイヤの図形は、ブロック参照のレイヤに乗る
            var al = e.Layer is { } l && !(l.Name == "0" && inherited != null) ? l : inherited;
            var name = al?.Name ?? "0";
            return _layers.TryGetValue(name, out var layer) ? layer : _layers[name] = doc.GetOrCreateLayer(name, RgbColor.White);
        }

        /// <summary>壊れた図形が 1 つあっても、ほかの図形は読み込む。</summary>
        private void SafeConvert(AcadEntity e, AcadLayer? inherited, int depth)
        {
            try
            {
                Convert(e, inherited, depth);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NullReferenceException or IndexOutOfRangeException or NotSupportedException)
            {
                _skipped++;
            }
        }

        private void Convert(AcadEntity e, AcadLayer? inherited, int depth)
        {
            var layer = LayerOf(e, inherited);
            var made = new List<Entity>();
            switch (e)
            {
                case Line l:
                    made.Add(new LineEntity(layer, V(l.StartPoint), V(l.EndPoint)));
                    break;
                case LwPolyline pl:
                    made.Add(new PolylineEntity(layer, ExpandBulges(pl.Vertices.Select(v => (V(v.Location), v.Bulge)).ToList(), pl.IsClosed), pl.IsClosed));
                    break;
                case Polyline2D pl:
                    made.Add(new PolylineEntity(layer, ExpandBulges(pl.Vertices.Select(v => (V(v.Location), v.Bulge)).ToList(), pl.IsClosed), pl.IsClosed));
                    break;
                case ACadSharp.Entities.Point pt:
                    made.Add(new PointEntity(layer, V(pt.Location)));
                    break;
                case Arc a: // Arc は Circle を継承しているので先に判定する
                    made.Add(new ArcEntity(layer, V(a.Center), a.Radius, a.StartAngle, a.EndAngle));
                    break;
                case Circle c:
                    made.Add(new CircleEntity(layer, V(c.Center), c.Radius));
                    break;
                case Ellipse el:
                    var pts = el.PolygonalVertexes(72).Select(V).ToList();
                    var full = Math.Abs(el.EndParameter - el.StartParameter - GeoMath.TwoPi) < 1e-9;
                    made.Add(new PolylineEntity(layer, pts, full));
                    break;
                case Spline sp:
                    // 制御点が無いもの (通過点だけのスプライン) は通過点を結ぶ
                    var spPts = sp.ControlPoints.Count > 0 ? sp.PolygonalVertexes(64).Select(V) : sp.FitPoints.Select(V);
                    made.Add(new PolylineEntity(layer, spPts));
                    break;
                case MText mt:
                    made.AddRange(FromMText(mt, layer));
                    break;
                case ACadSharp.Entities.TextEntity t: // 属性 (AttributeEntity) も TEXT として扱う
                    made.Add(FromText(t, layer));
                    break;
                case Hatch h:
                    if (FromHatch(h, layer) is { } hatch) made.Add(hatch);
                    break;
                case Insert ins when depth < MaxBlockDepth:
                    // ブロック参照は分解して中身を取り込む (入れ子も)
                    var insLayer = e.Layer;
                    foreach (var child in SafeExplode(ins)) SafeConvert(child, insLayer, depth + 1);
                    foreach (var attr in ins.Attributes) SafeConvert(attr, insLayer, depth + 1);
                    return;
                default:
                    _skipped++;
                    return;
            }
            foreach (var m in made)
            {
                m.Color = e.Color.IsByLayer || e.Color.IsByBlock ? null : ToRgb(e.Color);
                m.LineWeight = (short)e.LineWeight > 0 ? (short)e.LineWeight / 100.0 : null;
                m.LineType = DxfIO.LineTypeFromName(e.LineType?.Name is "ByLayer" or "BYLAYER" or null ? e.Layer?.LineType?.Name : e.LineType.Name);
                _out.Add(m);
            }
        }

        private IEnumerable<AcadEntity> SafeExplode(Insert ins)
        {
            try
            {
                return [.. ins.Explode()];
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NullReferenceException or NotSupportedException)
            {
                _skipped++;
                return [];
            }
        }

        private static TextEntity FromText(ACadSharp.Entities.TextEntity o, Layer layer)
        {
            var (h, v) = (o.HorizontalAlignment, o.VerticalAlignment);
            // 左下揃え以外は「配置基準点」(DXF の 11 番) が位置
            var leftBaseline = h == TextHorizontalAlignment.Left && v == TextVerticalAlignmentType.Baseline;
            var pos = leftBaseline ? o.InsertPoint : o.AlignmentPoint;
            var t = new TextEntity(layer, V(pos), o.Value, o.Height, o.Rotation)
            {
                WidthFactor = o.WidthFactor > 0 ? o.WidthFactor : 1,
            };
            if (h is TextHorizontalAlignment.Aligned or TextHorizontalAlignment.Fit)
            {
                // 両端揃え・フィット: 始点から終点の範囲に収める
                t.Position = V(o.InsertPoint);
                t.FitWidth = V(o.InsertPoint).DistanceTo(V(o.AlignmentPoint));
                return t;
            }
            t.HAlign = h switch
            {
                TextHorizontalAlignment.Center or TextHorizontalAlignment.Middle => TextHAlign.Center,
                TextHorizontalAlignment.Right => TextHAlign.Right,
                _ => TextHAlign.Left,
            };
            t.VAlign = h == TextHorizontalAlignment.Middle ? TextVAlign.Middle : v switch
            {
                TextVerticalAlignmentType.Top => TextVAlign.Top,
                TextVerticalAlignmentType.Middle => TextVAlign.Middle,
                TextVerticalAlignmentType.Bottom => TextVAlign.Bottom,
                _ => TextVAlign.Baseline,
            };
            return t;
        }

        /// <summary>MTEXT は行ごとの文字に分解する (DXF と同じ扱い)。</summary>
        private static IEnumerable<TextEntity> FromMText(MText o, Layer layer)
        {
            var lines = o.GetPlainTextLines();
            var h = o.Height;
            var spacing = h * 5.0 / 3.0 * (o.LineSpacing > 0 ? o.LineSpacing : 1);
            var total = h + (lines.Length - 1) * spacing;
            var name = o.AttachmentPoint.ToString();
            var halign = name.EndsWith("Center") ? TextHAlign.Center : name.EndsWith("Right") ? TextHAlign.Right : TextHAlign.Left;
            var top = name.StartsWith("Top") ? 0 : name.StartsWith("Middle") ? total / 2 : total;
            var rot = Math.Atan2(o.AlignmentPoint.Y, o.AlignmentPoint.X); // 文字の向き (X 軸方向ベクトル)
            var up = Vec2.FromAngle(rot).LeftNormal;
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Length == 0) continue;
                yield return new TextEntity(layer, V(o.InsertPoint) + up * (top - i * spacing), lines[i], h, rot)
                {
                    HAlign = halign,
                    VAlign = TextVAlign.Top,
                };
            }
        }

        /// <summary>ハッチング: 境界の辺をつないだ閉じた点列と、パターンの線の角度・間隔 (塗りつぶしは模様なし)。</summary>
        private static HatchEntity? FromHatch(Hatch o, Layer layer)
        {
            var loops = new List<List<Vec2>>();
            foreach (var path in o.Paths)
            {
                var loop = new List<Vec2>();
                foreach (var edge in path.Edges)
                {
                    var seg = EdgePoints(edge);
                    if (seg.Count == 0) continue;
                    if (loop.Count > 0 && loop[^1].DistanceTo(seg[^1]) < loop[^1].DistanceTo(seg[0])) seg.Reverse();
                    foreach (var p in seg)
                        if (loop.Count == 0 || loop[^1].DistanceTo(p) > 1e-9) loop.Add(p);
                }
                if (loop.Count >= 2 && loop[0].DistanceTo(loop[^1]) < 1e-9) loop.RemoveAt(loop.Count - 1);
                if (loop.Count >= 3) loops.Add(loop);
            }
            if (loops.Count == 0) return null;
            var lines = o.Pattern?.Lines ?? [];
            var patterns = o.IsSolid
                ? []
                : lines.Select(d => new HatchPattern(d.Angle, Math.Abs(d.Offset.Y * Math.Cos(d.Angle) - d.Offset.X * Math.Sin(d.Angle)) is var s && s > 0 ? s : V(d.Offset).Length, V(d.BasePoint)))
                    .Where(p => p.Spacing > 0).ToList();
            return new HatchEntity(layer, loops, patterns, o.IsSolid || patterns.Count == 0) { PatternName = o.Pattern?.Name ?? "" };
        }

        private static List<Vec2> EdgePoints(Hatch.BoundaryPath.Edge edge) => edge switch
        {
            Hatch.BoundaryPath.Line l => [V(l.Start), V(l.End)],
            Hatch.BoundaryPath.Polyline p => PolylineEdge(p),
            Hatch.BoundaryPath.Arc a => [.. a.PolygonalVertexes(72).Select(V)],
            Hatch.BoundaryPath.Ellipse el => [.. el.PolygonalVertexes(72).Select(V)],
            Hatch.BoundaryPath.Spline sp => [.. sp.ControlPoints.Select(V)], // 制御点の折れ線で近似
            _ => [],
        };

        private static List<Vec2> PolylineEdge(Hatch.BoundaryPath.Polyline p)
        {
            // Vertices の Z はふくらみ (bulge)
            var pts = ExpandBulges(p.Vertices.Select(v => (new Vec2(v.X, v.Y), v.Z)).ToList(), p.IsClosed).ToList();
            if (p.IsClosed && pts.Count > 0) pts.Add(pts[0]);
            return pts;
        }

        /// <summary>ふくらみ (bulge) 付きの頂点を円弧の折れ線に展開する。</summary>
        private static IEnumerable<Vec2> ExpandBulges(List<(Vec2 P, double Bulge)> v, bool closed)
        {
            for (var i = 0; i < v.Count; i++)
            {
                var a = v[i].P;
                yield return a;
                if (i == v.Count - 1 && !closed) break;
                var bulge = v[i].Bulge;
                if (Math.Abs(bulge) < 1e-9) continue;
                var b = v[(i + 1) % v.Count].P;
                var theta = 4 * Math.Atan(bulge); // 中心角 (正 = 反時計回り)
                var chord = b - a;
                if (chord.Length < 1e-12) continue;
                var r = chord.Length / (2 * Math.Sin(Math.Abs(theta) / 2));
                var center = (a + b) / 2 + chord.Normalized().LeftNormal * (Math.Sign(bulge) * r * Math.Cos(theta / 2));
                var n = Math.Max(4, (int)(Math.Abs(theta) / (Math.PI / 36)));
                for (var k = 1; k < n; k++) yield return center + (a - center).Rotate(theta * k / n);
            }
        }

        private static Vec2 V(XYZ p) => new(p.X, p.Y);
        private static Vec2 V(XY p) => new(p.X, p.Y);

        /// <summary>色番号 7 (白/黒) は画面の既定色 (白) として扱う (DXF と同じ)。</summary>
        private static RgbColor? ToRgb(Color c)
        {
            if (c.IsByLayer || c.IsByBlock) return null;
            if (!c.IsTrueColor && c.Index == 7) return RgbColor.White;
            return new RgbColor(c.R, c.G, c.B);
        }
    }
}
