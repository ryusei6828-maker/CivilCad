using CivilCad.Alignment;
using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.Alignment;

/// <summary>路線計算結果を図形 (中心線・IP・測点・主要点) にする。</summary>
public static class AlignmentDrawer
{
    public const string LayerIp = "路線-IP";
    public const string LayerCenter = "路線-中心線";
    public const string LayerStation = "路線-測点";
    public const string LayerMainPoint = "路線-主要点";

    /// <summary>同じ路線名で以前作図した図形を置き換えるコマンドを作る。</summary>
    public static IUndoableCommand CreateDrawCommand(CadDocument doc, HorizontalAlignment al, AlignmentInput input)
    {
        var old = doc.Entities.Where(e => e.Tag == input.Tag).ToList();
        var entities = Build(doc, al, input);
        return new CompositeCommand($"路線作図 {input.Name}",
            new RemoveEntitiesCommand(old), new AddEntitiesCommand(entities));
    }

    private static List<Entity> Build(CadDocument doc, HorizontalAlignment al, AlignmentInput input)
    {
        var ipLayer = doc.GetOrCreateLayer(LayerIp, RgbColor.Gray);
        var centerLayer = doc.GetOrCreateLayer(LayerCenter, RgbColor.Yellow);
        var stationLayer = doc.GetOrCreateLayer(LayerStation, RgbColor.White);
        var mainLayer = doc.GetOrCreateLayer(LayerMainPoint, RgbColor.Cyan);
        var h = doc.PaperMmToMeters(input.TextHeightMm);
        var list = new List<Entity>();

        // IP 線と IP 名
        var ips = input.Rows.Where(r => !string.IsNullOrWhiteSpace(r.Name) || r.X != 0 || r.Y != 0).ToList();
        list.Add(new PolylineEntity(ipLayer, ips.Select(r => Vec2.FromSurvey(r.X, r.Y))));
        foreach (var r in ips)
        {
            var p = Vec2.FromSurvey(r.X, r.Y);
            list.Add(new CircleEntity(ipLayer, p, h * 0.4));
            list.Add(new TextEntity(ipLayer, p + new Vec2(h * 0.6, h * 0.6), r.Name, h));
        }

        // 中心線
        foreach (var e in al.Elements)
        {
            switch (e)
            {
                case LineElement:
                    list.Add(new LineEntity(centerLayer, e.StartPoint, e.EndPoint));
                    break;
                case ArcElement a:
                    var sa = (a.StartPoint - a.Center).Angle;
                    var ea = (a.EndPoint - a.Center).Angle;
                    list.Add(a.Turn > 0
                        ? new ArcEntity(centerLayer, a.Center, a.Radius, sa, ea)
                        : new ArcEntity(centerLayer, a.Center, a.Radius, ea, sa));
                    break;
                case ClothoidElement c:
                    var n = Math.Max(8, (int)Math.Ceiling(c.Length));
                    list.Add(new PolylineEntity(centerLayer, Enumerable.Range(0, n + 1).Select(k => c.Evaluate(c.Length * k / n).Point)));
                    break;
            }
        }

        // 測点 (ピッチごと)
        var first = Math.Ceiling((al.StartStation - 1e-9) / input.Pitch) * input.Pitch;
        for (var st = first; st <= al.EndStation + 1e-9; st += input.Pitch)
        {
            var left = al.PointAt(st, -h);
            list.Add(new LineEntity(stationLayer, left, al.PointAt(st, h)));
            var dir = Vec2.FromAngle(al.Evaluate(st).Direction);
            list.Add(PlaceText(stationLayer, al.PointAt(st, -h * 1.5), dir.LeftNormal, Station.Format(st, input.Pitch, 0), h));
        }

        // 主要点 (中心線の右側に表示)
        foreach (var m in al.MainPoints)
        {
            var right = -Vec2.FromAngle(m.Direction).LeftNormal;
            list.Add(new CircleEntity(mainLayer, m.Point, h * 0.3));
            list.Add(new LineEntity(mainLayer, m.Point, m.Point + right * h * 2));
            list.Add(PlaceText(mainLayer, m.Point + right * h * 2.5, right, $"{m.Name} {Station.Format(m.Station, input.Pitch)}", h));
        }

        foreach (var e in list) e.Tag = input.Tag;
        return list;
    }

    /// <summary>anchor から outward 方向へ読める向きで文字を置く (逆さ文字にならないよう反転)。</summary>
    private static TextEntity PlaceText(Layer layer, Vec2 anchor, Vec2 outward, string text, double h)
    {
        var t = new TextEntity(layer, anchor, text, h);
        var w = t.ApproxWidth;
        var rot = outward.Angle;
        var start = anchor;
        if (Math.Cos(rot) < -1e-9)
        {
            rot += Math.PI;
            start = anchor + outward * w;
        }
        t.Rotation = rot;
        t.Position = start - Vec2.FromAngle(rot).LeftNormal * (h / 2);
        return t;
    }
}
