using CivilCad.Core.Geometry;
using CivilCad.Core.Model;
using netDxf;
using netDxf.Entities;
using DxfLayer = netDxf.Tables.Layer;
using Layer = CivilCad.Core.Model.Layer;
using DxfHatchPattern = netDxf.Entities.HatchPattern;
using HatchPattern = CivilCad.Core.Model.HatchPattern;

namespace CivilCad.App.IO;

/// <summary>DXF の読み書き (netDxf)。線・ポリライン・円・円弧・文字とレイヤに対応。</summary>
public static class DxfIO
{
    private const double Deg = 180 / Math.PI;
    private const string PlaneZoneVariable = "$USERI1";

    /// <summary>CivilCad 独自の情報 (PDF のページ番号など) を図形の XData に書くときのアプリ名。</summary>
    private const string AppId = "CIVILCAD";

    public static void Save(CadDocument doc, string path)
    {
        var dxf = new DxfDocument(netDxf.Header.DxfVersion.AutoCad2018);
        var layers = new Dictionary<Layer, DxfLayer>();
        foreach (var l in doc.Layers)
        {
            var dl = dxf.Layers.Contains(l.Name) ? dxf.Layers[l.Name] : dxf.Layers.Add(new DxfLayer(l.Name));
            dl.Color = ToAci(l.Color);
            dl.IsVisible = l.IsVisible;
            dl.IsLocked = l.IsLocked;
            layers[l] = dl;
        }

        var images = new Dictionary<string, netDxf.Objects.ImageDefinition>(StringComparer.OrdinalIgnoreCase);
        // プリセット (記号) は線・塗り・円・文字に展開して書く (他の CAD でも表示できるように)
        foreach (var e in doc.Entities.SelectMany(x => x is SymbolEntity s ? s.Explode() : [x]))
        {
            EntityObject? de = e switch
            {
                LineEntity l => new Line(V(l.Start), V(l.End)),
                PolylineEntity pl => new Polyline2D(pl.Points.Select(p => new Polyline2DVertex(V(p))), pl.IsClosed),
                CircleEntity c => new Circle(V(c.Center), c.Radius),
                ArcEntity a => new Arc(V(a.Center), a.Radius, GeoMath.NormalizeAngle(a.StartAngle) * Deg, GeoMath.NormalizeAngle(a.EndAngle) * Deg),
                TextEntity t => ToDxfText(t),
                HatchEntity h when h.Loops.Count > 0 => ToDxfHatch(h),
                ImageEntity img => ToDxfImage(img, images),
                _ => null,
            };
            if (de == null) continue;
            de.Layer = layers[e.Layer];
            de.Color = e.Color is RgbColor c2 ? ToAci(c2) : AciColor.ByLayer;
            de.Lineweight = e.LineWeight is double w ? ToDxfWeight(w) : Lineweight.ByLayer;
            // 透明度 (DXF は 0〜90%)
            if (e is HatchEntity { TransparencyPercent: > 0 } th) de.Transparency = new Transparency((short)Math.Clamp(Math.Round(th.TransparencyPercent), 0, 90));
            if (e is ImageEntity { TransparencyPercent: > 0 } ti) de.Transparency = new Transparency((short)Math.Clamp(Math.Round(ti.TransparencyPercent), 0, 90));
            dxf.Entities.Add(de);
        }
        // 図面の縮尺は AutoCAD 等と同じく DIMSCALE (寸法スタイルの全体尺度, ヘッダの $DIMSCALE) に入れる
        dxf.DimensionStyles[dxf.DrawingVariables.DimStyle].DimScaleOverall = doc.ScaleDenominator;
        // 平面直角座標系の系番号は AutoCAD のユーザー変数 USERI1 に入れる (0 = 未設定)
        dxf.DrawingVariables.AddCustomVariable(new netDxf.Header.HeaderVariable(PlaneZoneVariable, 70, (short)(doc.PlaneZone ?? 0)));
        dxf.Save(path);
    }

    /// <summary>DXF を読み込んで doc を置き換える。戻り値は読み込めなかった図形の数。</summary>
    public static int Load(CadDocument doc, string path)
    {
        var dxf = DxfDocument.Load(path) ?? throw new System.IO.InvalidDataException("DXF ファイルを読み込めませんでした (未対応の形式の可能性があります)。");

        doc.Clear();
        if (dxf.DimensionStyles.Contains(dxf.DrawingVariables.DimStyle) &&
            dxf.DimensionStyles[dxf.DrawingVariables.DimStyle].DimScaleOverall is > 1 and var scale)
            doc.ScaleDenominator = scale; // 1 以下 (他 CAD の未設定値) は無視
        if (dxf.DrawingVariables.TryGetCustomVariable(PlaneZoneVariable, out var zoneVar) &&
            Convert.ToInt32(zoneVar.Value, System.Globalization.CultureInfo.InvariantCulture) is >= 1 and <= 19 and var zone)
            doc.PlaneZone = zone;
        var layers = new Dictionary<string, Layer>(StringComparer.OrdinalIgnoreCase);
        foreach (var dl in dxf.Layers)
        {
            var l = doc.GetOrCreateLayer(dl.Name, ToRgb(dl.Color) ?? RgbColor.White);
            l.Color = ToRgb(dl.Color) ?? RgbColor.White;
            l.IsVisible = dl.IsVisible || l.Name == "0";
            l.IsLocked = dl.IsLocked && l.Name != "0";
            layers[dl.Name] = l;
        }

        Layer LayerOf(EntityObject o) =>
            layers.TryGetValue(o.Layer.Name, out var l) ? l : layers[o.Layer.Name] = doc.GetOrCreateLayer(o.Layer.Name, RgbColor.White);

        var list = new List<Entity>();
        var converted = 0;
        void Add(EntityObject o, params Entity[] entities)
        {
            foreach (var e in entities)
            {
                e.Color = o.Color.IsByLayer || o.Color.IsByBlock ? null : ToRgb(o.Color);
                e.LineWeight = (short)o.Lineweight > 0 ? (short)o.Lineweight / 100.0 : null;
                list.Add(e);
            }
            converted++;
        }

        foreach (var o in dxf.Entities.Lines)
            Add(o, new LineEntity(LayerOf(o), V(o.StartPoint), V(o.EndPoint)));
        foreach (var o in dxf.Entities.Polylines2D)
            Add(o, new PolylineEntity(LayerOf(o), ExpandBulges(o), o.IsClosed));
        foreach (var o in dxf.Entities.Circles)
            Add(o, new CircleEntity(LayerOf(o), V(o.Center), o.Radius));
        foreach (var o in dxf.Entities.Arcs)
            Add(o, new ArcEntity(LayerOf(o), V(o.Center), o.Radius, o.StartAngle / Deg, o.EndAngle / Deg));
        foreach (var o in dxf.Entities.Texts)
            Add(o, FromDxfText(o, LayerOf(o)));
        foreach (var o in dxf.Entities.MTexts)
            Add(o, [.. FromDxfMText(o, LayerOf(o))]);
        foreach (var o in dxf.Entities.Hatches)
            if (FromDxfHatch(o, LayerOf(o)) is { } hatch)
            {
                if (!o.Transparency.IsByLayer && !o.Transparency.IsByBlock && o.Transparency.Value > 0) hatch.TransparencyPercent = o.Transparency.Value;
                Add(o, hatch);
            }

        foreach (var o in dxf.Entities.Images)
            if (FromDxfImage(o, LayerOf(o), path) is { } image)
            {
                if (!o.Transparency.IsByLayer && !o.Transparency.IsByBlock && o.Transparency.Value > 0) image.TransparencyPercent = o.Transparency.Value;
                Add(o, image);
            }

        doc.Entities.AddRange(list);
        doc.NotifyChanged();
        return dxf.Entities.All.Count() - converted;
    }

    /// <summary>画像は埋め込まず、画像ファイルの場所 (絶対パス) を書く。同じ画像は 1 つの定義を共有する。</summary>
    private static Image ToDxfImage(ImageEntity img, Dictionary<string, netDxf.Objects.ImageDefinition> defs)
    {
        if (!defs.TryGetValue(img.Path, out var def))
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(img.Path);
            var unique = name;
            for (var i = 2; defs.Values.Any(d => string.Equals(d.Name, unique, StringComparison.OrdinalIgnoreCase)); i++) unique = $"{name}_{i}";
            def = new netDxf.Objects.ImageDefinition(unique, img.Path, img.PixelWidth, 72, img.PixelHeight, 72, netDxf.Units.ImageResolutionUnits.Unitless);
            defs[img.Path] = def;
        }
        var u = img.U.Normalized();
        var v = img.V.Normalized();
        var image = new Image(def, V(img.Origin), img.Width, img.Height)
        {
            Uvector = new Vector2(u.X, u.Y),
            Vvector = new Vector2(v.X, v.Y),
        };
        if (img.Page > 0)
        {
            // PDF のページ番号 (0 から) は XData に入れる
            var xd = new XData(new netDxf.Tables.ApplicationRegistry(AppId));
            xd.XDataRecord.Add(new XDataRecord(XDataCode.Int16, (short)img.Page));
            image.XData.Add(xd);
        }
        return image;
    }

    private static ImageEntity? FromDxfImage(Image o, Layer layer, string dxfPath)
    {
        var def = o.Definition;
        if (def == null || def.Width <= 0 || def.Height <= 0) return null;
        var file = def.File;
        // DXF と一緒にフォルダごと移した場合などは、DXF と同じフォルダの同じ名前の画像を使う
        var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(dxfPath)) ?? "";
        if (!System.IO.Path.IsPathRooted(file)) file = System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, file));
        if (!System.IO.File.Exists(file))
        {
            var near = System.IO.Path.Combine(dir, System.IO.Path.GetFileName(file));
            if (System.IO.File.Exists(near)) file = near;
        }
        var u = new Vec2(o.Uvector.X, o.Uvector.Y).Normalized() * o.Width;
        var v = new Vec2(o.Vvector.X, o.Vvector.Y).Normalized() * o.Height;
        var page = o.XData.TryGetValue(AppId, out var xd) && xd.XDataRecord.FirstOrDefault(r => r.Code == XDataCode.Int16)?.Value is short p ? p : 0;
        return new ImageEntity(layer, file, def.Width, def.Height, V(o.Position), u, v) { Page = Math.Max(0, (int)page) };
    }

    private static Text ToDxfText(TextEntity t)
    {
        var text = new Text(t.Text, V(t.Position), t.Height)
        {
            Rotation = t.Rotation * Deg,
            WidthFactor = t.WidthFactor,
        };
        if (t.FitWidth is double fit)
        {
            text.Alignment = TextAlignment.Fit;
            text.Width = fit;
            return text;
        }
        text.Alignment = (t.VAlign, t.HAlign) switch
        {
            (TextVAlign.Top, TextHAlign.Left) => TextAlignment.TopLeft,
            (TextVAlign.Top, TextHAlign.Center) => TextAlignment.TopCenter,
            (TextVAlign.Top, TextHAlign.Right) => TextAlignment.TopRight,
            (TextVAlign.Middle, TextHAlign.Left) => TextAlignment.MiddleLeft,
            (TextVAlign.Middle, TextHAlign.Center) => TextAlignment.MiddleCenter,
            (TextVAlign.Middle, TextHAlign.Right) => TextAlignment.MiddleRight,
            (TextVAlign.Bottom, TextHAlign.Left) => TextAlignment.BottomLeft,
            (TextVAlign.Bottom, TextHAlign.Center) => TextAlignment.BottomCenter,
            (TextVAlign.Bottom, TextHAlign.Right) => TextAlignment.BottomRight,
            (_, TextHAlign.Center) => TextAlignment.BaselineCenter,
            (_, TextHAlign.Right) => TextAlignment.BaselineRight,
            _ => TextAlignment.BaselineLeft,
        };
        return text;
    }

    /// <summary>
    /// TEXT の変換。netDxf の Position は、左下揃え以外では「配置基準点」(DXF の 11 番) なので、
    /// 揃え方も合わせて取り込まないと文字がずれる。
    /// </summary>
    private static TextEntity FromDxfText(Text o, Layer layer)
    {
        var t = new TextEntity(layer, V(o.Position), o.Value, o.Height, o.Rotation / Deg)
        {
            WidthFactor = o.WidthFactor > 0 ? o.WidthFactor : 1,
        };
        (t.HAlign, t.VAlign) = o.Alignment switch
        {
            TextAlignment.TopLeft => (TextHAlign.Left, TextVAlign.Top),
            TextAlignment.TopCenter => (TextHAlign.Center, TextVAlign.Top),
            TextAlignment.TopRight => (TextHAlign.Right, TextVAlign.Top),
            TextAlignment.MiddleLeft => (TextHAlign.Left, TextVAlign.Middle),
            TextAlignment.MiddleCenter or TextAlignment.Middle => (TextHAlign.Center, TextVAlign.Middle),
            TextAlignment.MiddleRight => (TextHAlign.Right, TextVAlign.Middle),
            TextAlignment.BottomLeft => (TextHAlign.Left, TextVAlign.Bottom),
            TextAlignment.BottomCenter => (TextHAlign.Center, TextVAlign.Bottom),
            TextAlignment.BottomRight => (TextHAlign.Right, TextVAlign.Bottom),
            TextAlignment.BaselineCenter => (TextHAlign.Center, TextVAlign.Baseline),
            TextAlignment.BaselineRight => (TextHAlign.Right, TextVAlign.Baseline),
            _ => (TextHAlign.Left, TextVAlign.Baseline),
        };
        // 両端揃え・フィット: 始点 (Position) から Width の範囲に収める
        if (o.Alignment is TextAlignment.Aligned or TextAlignment.Fit && o.Width > 0) t.FitWidth = o.Width;
        return t;
    }

    /// <summary>MTEXT は行ごとの TEXT に分解する (基準点の位置・行間を反映)。</summary>
    private static IEnumerable<TextEntity> FromDxfMText(MText o, Layer layer)
    {
        var lines = o.PlainText().Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var h = o.Height;
        var spacing = h * 5.0 / 3.0 * (o.LineSpacingFactor > 0 ? o.LineSpacingFactor : 1);
        var total = h + (lines.Length - 1) * spacing;
        var name = o.AttachmentPoint.ToString();
        var halign = name.EndsWith("Center") ? TextHAlign.Center : name.EndsWith("Right") ? TextHAlign.Right : TextHAlign.Left;
        // 文字ブロックの上端が基準点からどれだけ上にあるか
        var top = name.StartsWith("Top") ? 0 : name.StartsWith("Middle") ? total / 2 : total;
        var rot = o.Rotation / Deg;
        var up = Vec2.FromAngle(rot).LeftNormal;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length == 0) continue;
            yield return new TextEntity(layer, V(o.Position) + up * (top - i * spacing), lines[i], h, rot)
            {
                HAlign = halign,
                VAlign = TextVAlign.Top,
            };
        }
    }

    /// <summary>ハッチング: 境界は閉じたポリライン、模様はパターンの線の定義 (破線は無視) か塗りつぶし。</summary>
    private static Hatch ToDxfHatch(HatchEntity h)
    {
        var paths = h.Loops.Select(l => new HatchBoundaryPath([new Polyline2D(l.Select(p => new Polyline2DVertex(V(p))), true)]));
        DxfHatchPattern pattern;
        if (h.Solid || h.Patterns.Count == 0)
        {
            pattern = DxfHatchPattern.Solid;
        }
        else
        {
            var defs = h.Patterns.Select(p => new HatchPatternLineDefinition
            {
                Angle = GeoMath.NormalizeAngle(p.Angle) * Deg,
                Origin = V(p.Origin),
                Delta = new Vector2(0, p.Spacing),
            });
            pattern = new DxfHatchPattern(string.IsNullOrEmpty(h.PatternName) ? "CIVILCAD" : SafePatternName(h.PatternName), defs) { Type = HatchType.Custom };
        }
        return new Hatch(pattern, paths, false);
    }

    private static string SafePatternName(string name)
    {
        var s = new string(name.Where(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-').ToArray());
        return s.Length > 0 ? s.ToUpperInvariant() : "CIVILCAD";
    }

    /// <summary>
    /// HATCH の取り込み。境界の辺 (線・円弧・楕円・ポリライン・スプライン) をつないで閉じた点列にし、
    /// パターンの線の定義から角度・間隔を求める (netDxf では定義は尺度 1・回転 0 の値で、全体の尺度と角度が別にある)。
    /// </summary>
    private static HatchEntity? FromDxfHatch(Hatch o, Layer layer)
    {
        var loops = new List<List<Vec2>>();
        foreach (var path in o.BoundaryPaths)
        {
            var loop = new List<Vec2>();
            foreach (var edge in path.Edges)
            {
                var seg = EdgePoints(edge.ConvertTo()).ToList();
                if (seg.Count == 0) continue;
                // 辺の向きがそろっていないことがあるので、近い方の端からつなぐ
                if (loop.Count > 0 && loop[^1].DistanceTo(seg[^1]) < loop[^1].DistanceTo(seg[0])) seg.Reverse();
                foreach (var p in seg)
                    if (loop.Count == 0 || loop[^1].DistanceTo(p) > 1e-9) loop.Add(p);
            }
            if (loop.Count >= 2 && loop[0].DistanceTo(loop[^1]) < 1e-9) loop.RemoveAt(loop.Count - 1);
            if (loop.Count >= 3) loops.Add(loop);
        }
        if (loops.Count == 0) return null;

        var pat = o.Pattern;
        var solid = pat.Fill == HatchFillType.SolidFill || pat.LineDefinitions.Count == 0;
        var scale = pat.Scale > 0 ? pat.Scale : 1;
        var patterns = solid
            ? []
            : pat.LineDefinitions.Select(d => new HatchPattern(
                (d.Angle + pat.Angle) / Deg,
                Math.Abs(d.Delta.Y) * scale,
                V(pat.Origin) + V(d.Origin).Rotate(pat.Angle / Deg) * scale)).Where(p => p.Spacing > 0).ToList();
        return new HatchEntity(layer, loops, patterns, solid || patterns.Count == 0) { PatternName = pat.Name };
    }

    private static IEnumerable<Vec2> EdgePoints(EntityObject e)
    {
        switch (e)
        {
            case Line l:
                return [V(l.StartPoint), V(l.EndPoint)];
            case Arc a:
                var sweep = GeoMath.SweepAngle(a.StartAngle / Deg, a.EndAngle / Deg);
                var n = Math.Max(4, (int)(sweep / (Math.PI / 36)));
                return Enumerable.Range(0, n + 1).Select(k => V(a.Center) + Vec2.FromAngle(a.StartAngle / Deg + sweep * k / n) * a.Radius);
            case Polyline2D pl:
                var pts = ExpandBulges(pl).ToList();
                if (pl.IsClosed && pts.Count > 0) pts.Add(pts[0]);
                return pts;
            case Ellipse el:
                return el.PolygonalVertexes(72).Select(v => V(el.Center) + new Vec2(v.X, v.Y).Rotate(el.Rotation / Deg));
            case Spline sp:
                return sp.PolygonalVertexes(64).Select(V);
            default:
                return [];
        }
    }

    /// <summary>ふくらみ (bulge) 付きの頂点を円弧の折れ線に展開する。</summary>
    private static IEnumerable<Vec2> ExpandBulges(Polyline2D pl)
    {
        var v = pl.Vertexes;
        for (var i = 0; i < v.Count; i++)
        {
            var a = V(v[i].Position);
            yield return a;
            var last = i == v.Count - 1;
            if (last && !pl.IsClosed) break;
            var bulge = v[i].Bulge;
            if (Math.Abs(bulge) < 1e-9) continue;
            var b = V(v[(i + 1) % v.Count].Position);
            var theta = 4 * Math.Atan(bulge); // 中心角 (正 = 反時計回り)
            var chord = b - a;
            var r = chord.Length / (2 * Math.Sin(Math.Abs(theta) / 2));
            var mid = (a + b) / 2;
            var h = r * Math.Cos(theta / 2);
            var center = mid + chord.Normalized().LeftNormal * (Math.Sign(bulge) * h);
            var n = Math.Max(4, (int)(Math.Abs(theta) / (Math.PI / 36)));
            for (var k = 1; k < n; k++) yield return center + (a - center).Rotate(theta * k / n);
        }
    }

    /// <summary>mm → DXF の線幅 (定義済みの値のうち最も近いもの)。</summary>
    private static Lineweight ToDxfWeight(double mm) =>
        Enum.GetValues<Lineweight>().Where(v => (short)v > 0).MinBy(v => Math.Abs((short)v - mm * 100));

    private static Vector2 V(Vec2 p) => new(p.X, p.Y);
    private static Vec2 V(Vector2 p) => new(p.X, p.Y);
    private static Vec2 V(Vector3 p) => new(p.X, p.Y);

    private static AciColor ToAci(RgbColor c) =>
        c == RgbColor.White ? AciColor.Default : new AciColor(c.R, c.G, c.B);

    private static RgbColor? ToRgb(AciColor c) =>
        c.IsByLayer || c.IsByBlock ? null : c.Index == 7 && !c.UseTrueColor ? RgbColor.White : new RgbColor(c.R, c.G, c.B);
}
