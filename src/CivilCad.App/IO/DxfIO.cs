using CivilCad.Core.Geometry;
using CivilCad.Core.Model;
using netDxf;
using netDxf.Entities;
using DxfLayer = netDxf.Tables.Layer;
using Layer = CivilCad.Core.Model.Layer;

namespace CivilCad.App.IO;

/// <summary>DXF の読み書き (netDxf)。線・ポリライン・円・円弧・文字とレイヤに対応。</summary>
public static class DxfIO
{
    private const double Deg = 180 / Math.PI;
    private const string PlaneZoneVariable = "$USERI1";

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

        foreach (var e in doc.Entities)
        {
            EntityObject? de = e switch
            {
                LineEntity l => new Line(V(l.Start), V(l.End)),
                PolylineEntity pl => new Polyline2D(pl.Points.Select(p => new Polyline2DVertex(V(p))), pl.IsClosed),
                CircleEntity c => new Circle(V(c.Center), c.Radius),
                ArcEntity a => new Arc(V(a.Center), a.Radius, GeoMath.NormalizeAngle(a.StartAngle) * Deg, GeoMath.NormalizeAngle(a.EndAngle) * Deg),
                TextEntity t => ToDxfText(t),
                _ => null,
            };
            if (de == null) continue;
            de.Layer = layers[e.Layer];
            de.Color = e.Color is RgbColor c2 ? ToAci(c2) : AciColor.ByLayer;
            de.Lineweight = e.LineWeight is double w ? ToDxfWeight(w) : Lineweight.ByLayer;
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

        doc.Entities.AddRange(list);
        doc.NotifyChanged();
        return dxf.Entities.All.Count() - converted;
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
