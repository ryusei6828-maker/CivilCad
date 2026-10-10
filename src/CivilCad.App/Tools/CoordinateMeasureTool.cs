using System.Windows.Media;
using CivilCad.App.IO;
using CivilCad.App.Views;
using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.Tools;

/// <summary>
/// 座標計測 (ID): クリックした点 (スナップ可) の測量座標 X (北)・Y (東) を表示する。
/// Register なら点名を付けた点にして座標管理の一覧に登録する (点名は P1, P2 …。続けて点名を入力すると直前の点の名前を変える)。
/// 続けて計測でき、一つ戻すで直前に登録した点を取り消す。
/// </summary>
public sealed class CoordinateMeasureTool : CadTool
{
    /// <summary>計測した点を座標管理に登録する。次の座標計測でも同じ設定を使う。</summary>
    public static bool Register { get; set; } = true;

    private readonly List<(IUndoableCommand Command, PointEntity Point)> _registered = [];
    private Vec2? _last;

    public override string Name => "座標計測";
    public override Vec2? BasePoint => _last;
    public override bool CanStepBack => _registered.Count > 0;

    /// <summary>直前に登録した点 (点名の入力で名前を変える)。</summary>
    public PointEntity? LastRegistered => _registered.Count > 0 ? _registered[^1].Point : null;

    protected override void OnActivated() => SetPrompt(Register
        ? "座標を計測する点を指定 (座標管理に登録。点名を入力すると直前の点の名前を変える / 右クリックで登録の切替)"
        : "座標を計測する点を指定 (登録しない / 右クリックで登録の切替)");

    public override IEnumerable<(string Label, Action Run)> MenuItems =>
    [
        (Register ? "座標管理に登録する: オン → オフにする (見るだけ)" : "座標管理に登録する: オフ → オンにする", () =>
        {
            Register = !Register;
            OnActivated();
        }),
    ];

    public override void OnPoint(Vec2 p)
    {
        _last = p;
        var (x, y) = Doc.ToSurvey(p);
        var latLon = "";
        if (Doc.PlaneZone is int n && CivilCad.Alignment.PlaneRectangular.Find(n) is { } zone)
        {
            var (lat, lon) = CivilCad.Alignment.PlaneRectangular.ToLatLon(zone, x, y);
            latLon = $"   北緯 {CivilCad.Alignment.PlaneRectangular.ToDms(lat, 2)}  東経 {CivilCad.Alignment.PlaneRectangular.ToDms(lon, 2)}";
        }
        var text = $"座標計測: X(北) = {x:F3}  Y(東) = {y:F3}{latLon}";
        if (Register)
        {
            var point = new PointEntity(Doc.CurrentLayer, p) { Name = CoordinateRegistry.NextName(Doc), Tags = CoordinateRegistry.DefaultTag };
            point.LineWeight = Doc.CurrentLineWeight;
            point.Color = Doc.CurrentColor;
            var command = new AddEntitiesCommand([point], "座標計測");
            Execute(command);
            _registered.Add((command, point));
            text += $"   → 「{point.Name}」として座標管理に登録しました (点名を入力すると名前を変えられます)";
        }
        Canvas.ShowMessage(text);
    }

    /// <summary>点名の入力: 直前に登録した点の名前を変える。</summary>
    public override bool OnText(string text)
    {
        var name = text.Trim();
        if (LastRegistered is not { } last || name.Length == 0 || CoordinateInput.TryParse(name, BasePoint, out _, Doc)) return false;
        var renamed = (PointEntity)last.Clone();
        renamed.Name = name;
        var command = new ReplaceEntitiesCommand([(last, renamed)], "点名の変更");
        Execute(command);
        _registered[^1] = (_registered[^1].Command, renamed);
        Canvas.ShowMessage($"座標計測: 点名を「{name}」にしました。");
        return true;
    }

    public override void StepBack()
    {
        if (_registered.Count == 0) return;
        var (command, point) = _registered[^1];
        _registered.RemoveAt(_registered.Count - 1);
        if (Doc.Entities.Contains(point)) Execute(new RemoveEntitiesCommand([point], "座標計測の取り消し"));
        else Retract(command, [point]);
        Canvas.ShowMessage($"座標計測: 「{point.Name}」の登録を取り消しました。");
    }

    public override void DrawPreview(DrawingContext dc)
    {
        if (_last is not Vec2 p) return;
        var c = Canvas.WorldToScreen(p);
        dc.DrawLine(CadCanvas.PreviewPen, new System.Windows.Point(c.X - 8, c.Y), new System.Windows.Point(c.X + 8, c.Y));
        dc.DrawLine(CadCanvas.PreviewPen, new System.Windows.Point(c.X, c.Y - 8), new System.Windows.Point(c.X, c.Y + 8));
    }
}
