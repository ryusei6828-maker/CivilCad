using System.Globalization;
using System.Windows;
using CivilCad.Alignment;
using HorizontalAlignment = CivilCad.Alignment.HorizontalAlignment;
using CivilCad.Core.Geometry;

namespace CivilCad.App.Alignment;

public partial class AlignmentWindow : Window
{
    private readonly AlignmentInput _input;
    private HorizontalAlignment? _result;

    public AlignmentWindow(AlignmentInput input)
    {
        InitializeComponent();
        _input = input.Clone();
        NameBox.Text = _input.Name;
        StartBox.Text = _input.StartStation.ToString(CultureInfo.InvariantCulture);
        PitchBox.Text = _input.Pitch.ToString(CultureInfo.InvariantCulture);
        TextHeightBox.Text = _input.TextHeightMm.ToString(CultureInfo.InvariantCulture);
        LeftWidthBox.Text = _input.LeftWidths;
        RightWidthBox.Text = _input.RightWidths;
        ShoulderCheck.IsChecked = _input.DrawShoulders;
        StakeCheck.IsChecked = _input.DrawStakes;
        IpGrid.ItemsSource = _input.Rows;
        Loaded += (_, _) => TryCompute();
    }

    /// <summary>「作図して閉じる」で確定した入力と計算結果。</summary>
    public AlignmentInput? ResultInput { get; private set; }
    public HorizontalAlignment? ResultAlignment { get; private set; }

    private bool TryCompute()
    {
        IpGrid.CommitEdit();
        IpGrid.CommitEdit();
        ErrorText.Text = "";
        try
        {
            _input.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? "路線1" : NameBox.Text.Trim();
            _input.StartStation = ParseDouble(StartBox.Text, "開始追加距離");
            _input.Pitch = ParseDouble(PitchBox.Text, "測点間隔");
            _input.TextHeightMm = ParseDouble(TextHeightBox.Text, "文字高");
            if (_input.Pitch <= 0) throw new AlignmentException("測点間隔は正の値にしてください。");
            if (_input.TextHeightMm <= 0) throw new AlignmentException("文字高は正の値にしてください。");
            AlignmentInput.ParseWidths(LeftWidthBox.Text, "左の幅");
            AlignmentInput.ParseWidths(RightWidthBox.Text, "右の幅");
            _input.LeftWidths = LeftWidthBox.Text.Trim();
            _input.RightWidths = RightWidthBox.Text.Trim();
            _input.DrawShoulders = ShoulderCheck.IsChecked == true;
            _input.DrawStakes = StakeCheck.IsChecked == true;

            _result = _input.Compute();
            ShowResult(_result);
            return true;
        }
        catch (AlignmentException ex)
        {
            ErrorText.Text = ex.Message;
            _result = null;
            return false;
        }
    }

    private void ShowResult(HorizontalAlignment al)
    {
        var pitch = _input.Pitch;
        MainPointGrid.ItemsSource = al.MainPoints.Select(m =>
        {
            var (x, y) = m.Point.ToSurvey();
            return new
            {
                名称 = m.Name,
                測点 = Station.Format(m.Station, pitch),
                追加距離 = m.Station.ToString("F3"),
                X = x.ToString("F3"),
                Y = y.ToString("F3"),
                方向角 = Angles.ToDms(Angles.ToSurveyAzimuth(m.Direction)),
            };
        }).ToList();

        CurveGrid.ItemsSource = al.Curves.Select(c => new
        {
            IP = c.Name,
            方向 = c.Radius <= 0 ? "折点" : c.Turn > 0 ? "左" : "右",
            交角IA = Angles.ToDms(Math.Abs(c.IA)),
            R = c.Radius,
            A1 = c.A1,
            A2 = c.A2,
            L1 = c.L1.ToString("F3"),
            L2 = c.L2.ToString("F3"),
            円曲線長 = c.Lc.ToString("F3"),
            曲線全長 = c.CurveLength.ToString("F3"),
            TL1 = c.T1.ToString("F3"),
            TL2 = c.T2.ToString("F3"),
        }).ToList();

        var rows = new List<StationRow>();
        var first = Math.Ceiling((al.StartStation - 1e-9) / pitch) * pitch;
        for (var st = first; st <= al.EndStation + 1e-9; st += pitch) rows.Add(MakeStationRow(al, st, ""));
        foreach (var m in al.MainPoints) rows.Add(MakeStationRow(al, m.Station, m.Name));
        StationGrid.ItemsSource = rows.OrderBy(r => r.追加距離).ToList();
    }

    private sealed record StationRow(string 測点, string 主要点, double 追加距離, string X, string Y, string 方向角);

    private StationRow MakeStationRow(HorizontalAlignment al, double st, string name)
    {
        var (p, dir) = al.Evaluate(st);
        var (x, y) = p.ToSurvey();
        return new StationRow(Station.Format(st, _input.Pitch), name, Math.Round(st, 3),
            x.ToString("F3"), y.ToString("F3"), Angles.ToDms(Angles.ToSurveyAzimuth(dir)));
    }

    private static double ParseDouble(string text, string label) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : throw new AlignmentException($"{label} が数値ではありません。");

    private void Compute_Click(object sender, RoutedEventArgs e) => TryCompute();

    /// <summary>XML 出力に書く平面直角座標系の系番号 (図面に設定してあれば)。</summary>
    public int? PlaneZone { get; set; }

    private void SaveXml_Click(object sender, RoutedEventArgs e)
    {
        if (!TryCompute()) return;
        if (LandXmlExport.Run(this, _input, PlaneZone) is { } path) ErrorText.Text = $"XML に書き出しました: {path}";
    }

    private void Draw_Click(object sender, RoutedEventArgs e)
    {
        if (!TryCompute() || _result == null) return;
        ResultInput = _input;
        ResultAlignment = _result;
        DialogResult = true;
    }

    private void Query_Click(object sender, RoutedEventArgs e)
    {
        if (!TryCompute() || _result == null) return;
        if (!Station.TryParse(QueryStationBox.Text, out var st, _input.Pitch))
        {
            QueryResult.Text = "測点の形式が正しくありません。";
            return;
        }
        if (!double.TryParse(QueryOffsetBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var off))
        {
            QueryResult.Text = "幅が数値ではありません。";
            return;
        }
        if (st < _result.StartStation - 1e-9 || st > _result.EndStation + 1e-9)
        {
            QueryResult.Text = $"測点が路線の範囲外です ({Station.Format(_result.StartStation, _input.Pitch)} 〜 {Station.Format(_result.EndStation, _input.Pitch)})。";
            return;
        }
        var (x, y) = _result.PointAt(st, off).ToSurvey();
        var dir = _result.Evaluate(st).Direction;
        QueryResult.Text =
            $"{Station.Format(st, _input.Pitch)} (追加距離 {st:F3})  幅 {(off >= 0 ? "右" : "左")} {Math.Abs(off):F3} m\n" +
            $"X = {x:F3}   Y = {y:F3}\n" +
            $"中心線の方向角 = {Angles.ToDms(Angles.ToSurveyAzimuth(dir))}";
    }

    private void InsertRow_Click(object sender, RoutedEventArgs e)
    {
        var index = IpGrid.SelectedItem is IpRow r ? _input.Rows.IndexOf(r) : Math.Max(0, _input.Rows.Count - 1);
        _input.Rows.Insert(Math.Max(0, index), new IpRow { Name = $"IP{_input.Rows.Count - 1}" });
    }

    private void DeleteRow_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in IpGrid.SelectedItems.OfType<IpRow>().ToList()) _input.Rows.Remove(r);
    }

    private void LoadXml_Click(object sender, RoutedEventArgs e)
    {
        var height = double.TryParse(TextHeightBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var h) && h > 0 ? h : _input.TextHeightMm;
        if (LandXmlImport.Run(this, height) is { } loaded) Apply(loaded);
    }

    /// <summary>読み込んだ路線で入力欄と IP 表を置き換えて計算し直す。</summary>
    private void Apply(AlignmentInput loaded)
    {
        NameBox.Text = loaded.Name;
        StartBox.Text = loaded.StartStation.ToString(CultureInfo.InvariantCulture);
        PitchBox.Text = loaded.Pitch.ToString(CultureInfo.InvariantCulture);
        TextHeightBox.Text = loaded.TextHeightMm.ToString(CultureInfo.InvariantCulture);
        _input.Rows.Clear();
        foreach (var r in loaded.Rows) _input.Rows.Add(r);
        TryCompute();
    }

    /// <summary>クリップボードの表 (タブ/カンマ区切り: 名称, X, Y, R, A1, A2) で IP 表を置き換える。</summary>
    private void PasteCsv_Click(object sender, RoutedEventArgs e)
    {
        var text = Clipboard.GetText();
        var rows = new List<IpRow>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var f = line.Split(['\t', ','], StringSplitOptions.TrimEntries);
            double Num(int i) => i < f.Length && double.TryParse(f[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
            if (f.Length < 3 || !double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out _)) continue; // 見出し行など
            rows.Add(new IpRow { Name = f[0], X = Num(1), Y = Num(2), R = Num(3), A1 = Num(4), A2 = Num(5) });
        }
        if (rows.Count < 2)
        {
            ErrorText.Text = "クリップボードに 名称, X, Y, R, A1, A2 の形式の行が 2 行以上ありません。";
            return;
        }
        _input.Rows.Clear();
        foreach (var r in rows) _input.Rows.Add(r);
        TryCompute();
    }
}
