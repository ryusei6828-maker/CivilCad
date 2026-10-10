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
    public const string LayerShoulder = "路線-路肩線";
    public const string LayerStake = "路線-幅杭";

    /// <summary>同じ路線名で以前作図した図形を置き換えるコマンドを作る。</summary>
    public static IUndoableCommand CreateDrawCommand(CadDocument doc, HorizontalAlignment al, AlignmentInput input)
    {
        var old = doc.Entities.Where(e => e.Tag == input.Tag).ToList();
        var entities = Build(doc, al, input);
        doc.PlaceFromSurvey(entities); // 路線は測量座標で作るので、座標設定の向きで図面に置く
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

        AddWidths(doc, al, input, h, list);

        foreach (var e in list) e.Tag = input.Tag;
        return list;
    }


    /// <summary>
    /// 幅: 幅ごとに中心線と平行な線 (路肩線など)、測点ごとにいちばん外の幅の点 (幅杭、小さな円) と左右の幅杭を結ぶ線。
    /// 平行な線は中心線を細かく区切った点 (主要点を含む) を幅だけずらして結ぶ (円曲線・クロソイドにも沿う)。
    /// </summary>
    private static void AddWidths(CadDocument doc, HorizontalAlignment al, AlignmentInput input, double h, List<Entity> list)
    {
        var left = AlignmentInput.ParseWidths(input.LeftWidths, "左の幅");
        var right = AlignmentInput.ParseWidths(input.RightWidths, "右の幅");
        if (left.Count == 0 && right.Count == 0) return;
        var offsets = left.Select(w => -w).Concat(right).ToList(); // 右が正

        if (input.DrawShoulders)
        {
            var layer = doc.GetOrCreateLayer(LayerShoulder, RgbColor.Green);
            var stations = SampleStations(al, input.Pitch);
            foreach (var off in offsets)
                list.Add(new PolylineEntity(layer, stations.Select(st => al.PointAt(st, off))));
        }

        if (input.DrawStakes)
        {
            var layer = doc.GetOrCreateLayer(LayerStake, RgbColor.Magenta);
            var outerLeft = left.Count > 0 ? -left[^1] : 0;
            var outerRight = right.Count > 0 ? right[^1] : 0;
            var first = Math.Ceiling((al.StartStation - 1e-9) / input.Pitch) * input.Pitch;
            var stakes = new List<double>();
            for (var st = first; st <= al.EndStation + 1e-9; st += input.Pitch) stakes.Add(st);
            // 起点・終点も (測点の位置でなくても)
            if (stakes.Count == 0 || stakes[0] - al.StartStation > 1e-6) stakes.Insert(0, al.StartStation);
            if (al.EndStation - stakes[^1] > 1e-6) stakes.Add(al.EndStation);
            foreach (var st in stakes)
            {
                var a = al.PointAt(st, outerLeft);
                var b = al.PointAt(st, outerRight);
                list.Add(new LineEntity(layer, a, b));
                // 幅杭は名前を付けた点にもして、座標管理の一覧に出す (座標を書き出せる)
                var name = $"{input.Name} {Station.Format(st, input.Pitch)}";
                if (left.Count > 0)
                {
                    list.Add(new CircleEntity(layer, a, h * 0.3));
                    list.Add(new PointEntity(layer, a) { Name = $"{name} 左{-outerLeft:0.###}" });
                }
                if (right.Count > 0)
                {
                    list.Add(new CircleEntity(layer, b, h * 0.3));
                    list.Add(new PointEntity(layer, b) { Name = $"{name} 右{outerRight:0.###}" });
                }
            }
        }
    }

    /// <summary>中心線をなめらかにたどるための追加距離 (1m ごと・主要点・起点・終点)。</summary>
    private static List<double> SampleStations(HorizontalAlignment al, double pitch)
    {
        var step = Math.Min(1.0, pitch / 4);
        var set = new SortedSet<double> { al.StartStation, al.EndStation };
        for (var st = al.StartStation; st < al.EndStation; st += step) set.Add(st);
        foreach (var m in al.MainPoints) set.Add(Math.Clamp(m.Station, al.StartStation, al.EndStation));
        // ごく近い点は除く (同じ点が並ぶとポリラインが乱れる)
        var list = new List<double>();
        foreach (var st in set)
            if (list.Count == 0 || st - list[^1] > 1e-6) list.Add(st);
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
