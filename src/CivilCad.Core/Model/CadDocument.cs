using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;

namespace CivilCad.Core.Model;

public sealed class CadDocument : INotifyPropertyChanged
{
    private Layer _currentLayer;
    private bool _onlyCurrentLayerEditable;
    private double _scaleDenominator = 500;
    private int? _planeZone;

    public CadDocument()
    {
        Layers.CollectionChanged += OnLayersChanged;
        _currentLayer = new Layer("0", RgbColor.White) { IsCurrent = true };
        Layers.Add(_currentLayer);
        History = new UndoStack(this);
    }

    public ObservableCollection<Layer> Layers { get; } = [];

    /// <summary>図形リスト。直接変更せず、Undo 可能なコマンド経由で変更する。</summary>
    public List<Entity> Entities { get; } = [];

    public UndoStack History { get; }

    public Layer CurrentLayer
    {
        get => _currentLayer;
        set
        {
            if (_currentLayer == value) return;
            _currentLayer.IsCurrent = false;
            _currentLayer = value;
            value.IsCurrent = true;
            // 現在レイヤは非表示・ロックにしない
            value.IsVisible = true;
            value.IsLocked = false;
            OnPropertyChanged();
            NotifyChanged();
        }
    }

    /// <summary>図面の縮尺の分母 (1:500 なら 500)。文字高・図枠など「用紙上の mm」で指定するものの換算に使う。</summary>
    public double ScaleDenominator
    {
        get => _scaleDenominator;
        set
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "縮尺は正の値にしてください。");
            if (_scaleDenominator == value) return;
            _scaleDenominator = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ScaleText));
            NotifyChanged();
        }
    }

    private double? _verticalScaleDenominator;

    /// <summary>
    /// 縦の縮尺の分母 (縦断図など、縦と横で縮尺が違う図。例: 横 1:1000・縦 1:100 なら 100)。null なら横と同じ。
    /// 図形は用紙どおりの形 (横の縮尺で換算した大きさ) のまま持ち、計測・寸法で縦の長さを縦の縮尺に直す。
    /// </summary>
    public double? VerticalScaleDenominator
    {
        get => _verticalScaleDenominator;
        set
        {
            if (value is double v && !(v > 0)) throw new ArgumentOutOfRangeException(nameof(value), "縮尺は正の値にしてください。");
            if (_verticalScaleDenominator == value) return;
            _verticalScaleDenominator = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ScaleText));
            NotifyChanged();
        }
    }

    /// <summary>縦と横で縮尺が違う。</summary>
    public bool HasVerticalScale => VerticalScaleDenominator is double v && Math.Abs(v - ScaleDenominator) > 1e-9;

    /// <summary>図面上の縦の長さ (m) → 実際の縦の長さ (m) の倍率 (縦の縮尺 / 横の縮尺。横 1:1000・縦 1:100 なら 0.1)。</summary>
    public double VerticalFactor => VerticalScaleDenominator is double v ? v / ScaleDenominator : 1;

    /// <summary>図面上の差 (Δx, Δy) → 実際の差 (縦は縦の縮尺で直す)。</summary>
    public Vec2 ToRealDelta(Vec2 d) => new(d.X, d.Y * VerticalFactor);

    /// <summary>縮尺の表示 (1:500、縦横で違えば H=1:1000 V=1:100)。</summary>
    public string ScaleText => HasVerticalScale ? $"H=1:{ScaleDenominator:0.##} V=1:{VerticalScaleDenominator:0.##}" : $"1:{ScaleDenominator:0.##}";

    /// <summary>平面直角座標系の系番号 (1〜19)。null は未設定。</summary>
    public int? PlaneZone
    {
        get => _planeZone;
        set
        {
            if (value is < 1 or > 19) throw new ArgumentOutOfRangeException(nameof(value), "系番号は 1〜19 です。");
            if (_planeZone == value) return;
            _planeZone = value;
            OnPropertyChanged();
        }
    }

    /// <summary>これから描く図形の線幅 (mm)。null は既定 (細線)。</summary>
    public double? CurrentLineWeight
    {
        get => _currentLineWeight;
        set
        {
            if (_currentLineWeight == value) return;
            _currentLineWeight = value;
            OnPropertyChanged();
        }
    }

    /// <summary>これから描く図形の線種。</summary>
    public LineType CurrentLineType
    {
        get => _currentLineType;
        set
        {
            if (_currentLineType == value) return;
            _currentLineType = value;
            OnPropertyChanged();
        }
    }

    private LineType _currentLineType;

    /// <summary>これから描く図形の色。null はレイヤ色。</summary>
    public RgbColor? CurrentColor
    {
        get => _currentColor;
        set
        {
            if (_currentColor == value) return;
            _currentColor = value;
            OnPropertyChanged();
        }
    }

    private double? _currentLineWeight;
    private RgbColor? _currentColor;

    /// <summary>用紙上の mm を図面上の m に換算 (1:500 なら 1mm = 0.5m)。</summary>
    public double PaperMmToMeters(double mm) => mm * ScaleDenominator / 1000;

    /// <summary>true のとき、現在レイヤ以外の図形は選択・編集できない。</summary>
    public bool OnlyCurrentLayerEditable
    {
        get => _onlyCurrentLayerEditable;
        set
        {
            if (_onlyCurrentLayerEditable == value) return;
            _onlyCurrentLayerEditable = value;
            OnPropertyChanged();
            NotifyChanged();
        }
    }

    /// <summary>図形・レイヤ・表示状態のいずれかが変わったときに発生。</summary>
    public event EventHandler? Changed;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void NotifyChanged()
    {
        AlignNorthArrows();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ---- 測量座標の向き ----

    /// <summary>
    /// 測量座標の向き (ラジアン、反時計回り)。座標設定を 2 点で行ったとき、図面は回さずにここへ角度を持つ。
    /// 図面上の点 p の測量座標 (CAD の向き x=東, y=北) は SurveyPivot を中心に p を SurveyRotation だけ回した位置。0 なら図面の座標がそのまま測量座標。
    /// </summary>
    public double SurveyRotation { get; private set; }

    /// <summary>測量座標の向きの回転の中心 (図面座標 = 測量座標が一致する点)。</summary>
    public Vec2 SurveyPivot { get; private set; }

    /// <summary>測量座標の向きを変える (元に戻すは <see cref="Commands.SetSurveyFrameCommand"/>)。</summary>
    public void SetSurveyFrame(double rotation, Vec2 pivot)
    {
        SurveyRotation = GeoMath.NormalizeSignedAngle(rotation);
        SurveyPivot = pivot;
        OnPropertyChanged(nameof(SurveyRotation));
        NotifyChanged();
    }

    /// <summary>図面上の点 → 測量座標 (CAD の向き x=東, y=北)。</summary>
    public Vec2 ToSurveyCad(Vec2 p) => SurveyRotation == 0 ? p : SurveyPivot + (p - SurveyPivot).Rotate(SurveyRotation);

    /// <summary>測量座標 (CAD の向き x=東, y=北) → 図面上の点。</summary>
    public Vec2 FromSurveyCad(Vec2 s) => SurveyRotation == 0 ? s : SurveyPivot + (s - SurveyPivot).Rotate(-SurveyRotation);

    /// <summary>図面上の点 → 測量座標 (X = 北, Y = 東)。</summary>
    public (double X, double Y) ToSurvey(Vec2 p) => ToSurveyCad(p).ToSurvey();

    /// <summary>測量座標 (X = 北, Y = 東) → 図面上の点。</summary>
    public Vec2 FromSurvey(double surveyX, double surveyY) => FromSurveyCad(Vec2.FromSurvey(surveyX, surveyY));

    /// <summary>図面上の向き (東から反時計回り) → 測量座標での向き (東から反時計回り)。方向角はこれを Angles.ToSurveyAzimuth に渡す。</summary>
    public double ToSurveyAngle(double drawingAngle) => drawingAngle + SurveyRotation;

    /// <summary>測量座標で作った図形 (路線など) を図面に置く向きにする。</summary>
    public void PlaceFromSurvey(IEnumerable<Entity> entities)
    {
        if (SurveyRotation == 0) return;
        foreach (var e in entities) e.Rotate(SurveyPivot, -SurveyRotation);
    }

    // ---- 北の向き ----

    /// <summary>
    /// 真北の向きを求める関数: (系番号, 図面上の位置) → 真北の向き (座標北 = 図面の上 から反時計回り、ラジアン)。
    /// 緯度経度の計算は CivilCad.Alignment にあるので、アプリの起動時に設定する。
    /// </summary>
    public static Func<int, Vec2, double?>? TrueNorthProvider { get; set; }

    /// <summary>true = 方位記号は真北 (系が設定されていれば子午線収差を補正)、false = 座標北 (図面の上)。</summary>
    public bool UseTrueNorth
    {
        get => _useTrueNorth;
        set
        {
            if (_useTrueNorth == value) return;
            _useTrueNorth = value;
            OnPropertyChanged();
            NotifyChanged();
        }
    }

    private bool _useTrueNorth = true;

    /// <summary>真北で求められるか (系が設定されている)。</summary>
    public bool CanUseTrueNorth => PlaneZone != null && TrueNorthProvider != null;

    /// <summary>
    /// 位置 p での北の向き (図面の上から反時計回り、ラジアン)。座標北は測量座標の向き (SurveyRotation) の分だけ図面の上からずれ、
    /// 真北はさらに子午線収差 (系と測量座標の位置から) だけずれる。
    /// </summary>
    public double NorthRotation(Vec2 p) => -SurveyRotation +
        (UseTrueNorth && PlaneZone is int zone && TrueNorthProvider?.Invoke(zone, ToSurveyCad(p)) is double r && double.IsFinite(r) ? r : 0);

    /// <summary>方位記号 (自動のもの) の向きを、置いてある位置の北に合わせる (移動・回転・系の変更のたびに呼ばれる)。</summary>
    public void AlignNorthArrows()
    {
        foreach (var e in Entities)
            if (e is SymbolEntity { AutoNorth: true } s && s.Definition.PointsNorth) s.Rotation = NorthRotation(s.Position);
    }

    public bool IsVisible(Entity e) => e.Layer.IsVisible;

    public bool IsEditable(Entity e) =>
        e.Layer.IsVisible && !e.Layer.IsLocked && (!OnlyCurrentLayerEditable || e.Layer == CurrentLayer);

    public Layer? FindLayer(string name) =>
        Layers.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));

    public Layer GetOrCreateLayer(string name, RgbColor color)
    {
        var layer = FindLayer(name);
        if (layer != null) return layer;
        layer = new Layer(name, color);
        Layers.Add(layer);
        return layer;
    }

    public string NextLayerName()
    {
        for (var i = 1; ; i++)
        {
            var name = $"レイヤ{i}";
            if (FindLayer(name) == null) return name;
        }
    }

    /// <summary>レイヤを削除できるか (図形が無く、"0" でも現在レイヤでもない)。</summary>
    public bool CanRemoveLayer(Layer layer) =>
        layer.Name != "0" && layer != CurrentLayer && Entities.All(e => e.Layer != layer);

    public Box2 VisibleExtents()
    {
        var box = Box2.Empty;
        foreach (var e in Entities)
            if (IsVisible(e)) box = box.Union(e.Bounds);
        return box;
    }

    /// <summary>
    /// 全体表示用の範囲。図形の大部分が集まっている範囲を求め、そこから大きく離れた図形
    /// (他 CAD の図面にある用紙外のゴミなど) は除く。excluded に除いた数を返す。
    /// </summary>
    public Box2 MainExtents(out int excluded)
    {
        excluded = 0;
        var items = Entities.Where(IsVisible).Select(e => e.Bounds).Where(b => !b.IsEmpty).ToList();
        var full = items.Aggregate(Box2.Empty, (acc, b) => acc.Union(b));
        if (items.Count < 20) return full;

        // 図形の中心の 5〜95% の範囲を「中心部」とし、その大きさ分だけ外側まで含める
        var xs = items.Select(b => b.Center.X).OrderBy(v => v).ToList();
        var ys = items.Select(b => b.Center.Y).OrderBy(v => v).ToList();
        double Pct(List<double> s, double p) => s[(int)Math.Clamp(Math.Round(p * (s.Count - 1)), 0, s.Count - 1)];
        var core = new Box2(new Vec2(Pct(xs, 0.05), Pct(ys, 0.05)), new Vec2(Pct(xs, 0.95), Pct(ys, 0.95)));
        var reach = core.Inflate(Math.Max(Math.Max(core.Width, core.Height), 1e-6));

        var main = Box2.Empty;
        foreach (var b in items)
        {
            if (reach.Contains(b.Center)) main = main.Union(b);
            else excluded++;
        }
        if (main.IsEmpty || excluded == 0) return full;

        // 除いても範囲がほとんど変わらないなら全部を見せる
        if (full.Width <= main.Width * 1.5 && full.Height <= main.Height * 1.5)
        {
            excluded = 0;
            return full;
        }
        return main;
    }

    /// <summary>新規作成・読み込み時に全消去する (Undo 履歴もクリア)。</summary>
    public void Clear()
    {
        Entities.Clear();
        var layer0 = FindLayer("0") ?? new Layer("0", RgbColor.White);
        CurrentLayer = layer0;
        foreach (var l in Layers.Where(l => l != layer0).ToList()) Layers.Remove(l);
        if (!Layers.Contains(layer0)) Layers.Add(layer0);
        OnlyCurrentLayerEditable = false;
        PlaneZone = null;
        VerticalScaleDenominator = null;
        SurveyRotation = 0;
        SurveyPivot = Vec2.Zero;
        History.Clear();
        NotifyChanged();
    }

    private void OnLayersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (Layer l in e.NewItems) l.PropertyChanged += OnLayerPropertyChanged;
        if (e.OldItems != null)
            foreach (Layer l in e.OldItems) l.PropertyChanged -= OnLayerPropertyChanged;
        NotifyChanged();
    }

    private void OnLayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 現在レイヤを非表示・ロックにされた場合は戻す
        if (sender == CurrentLayer && (!CurrentLayer.IsVisible || CurrentLayer.IsLocked))
        {
            CurrentLayer.IsVisible = true;
            CurrentLayer.IsLocked = false;
        }
        NotifyChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
