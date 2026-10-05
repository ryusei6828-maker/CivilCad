using System.Windows;
using CivilCad.Alignment;
using CivilCad.Core.Geometry;

namespace CivilCad.App.Drawing;

public partial class ZoneWindow : Window
{
    private readonly Vec2? _drawingCenter;

    public sealed record Item(int? Number, string Title, string Detail);

    /// <param name="drawingCenter">図面の中心 (CAD 座標)。選んだ系での緯度経度の確認表示に使う。</param>
    public ZoneWindow(int? current, bool isDefault, Vec2? drawingCenter)
    {
        InitializeComponent();
        _drawingCenter = drawingCenter;
        var items = new List<Item> { new(null, "設定しない", "緯度・経度は表示しません") };
        items.AddRange(PlaneRectangular.Zones.Select(z => new Item(z.Number,
            $"{z.Name}  (原点 北緯 {PlaneRectangular.ToDms(z.OriginLatitude, 0)}  東経 {PlaneRectangular.ToDms(z.OriginLongitude, 0)})", z.Area)));
        ZoneList.ItemsSource = items;
        ZoneList.SelectedItem = items.First(i => i.Number == current);
        DefaultCheck.IsChecked = isDefault;
        Loaded += (_, _) => ZoneList.ScrollIntoView(ZoneList.SelectedItem);
    }

    public int? Result { get; private set; }
    public bool MakeDefault { get; private set; }

    private void ZoneList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ZoneList.SelectedItem is not Item { Number: int n } || PlaneRectangular.Find(n) is not { } zone || _drawingCenter is not Vec2 c)
        {
            CheckText.Text = "";
            return;
        }
        var (x, y) = c.ToSurvey();
        var (lat, lon) = PlaneRectangular.ToLatLon(zone, x, y);
        CheckText.Text = $"確認: 図面の中心 (X {x:F1}, Y {y:F1}) は {zone.Name}では 北緯 {PlaneRectangular.ToDms(lat, 1)}、東経 {PlaneRectangular.ToDms(lon, 1)} です。" +
                         (lat is < 20 or > 46 || lon is < 122 or > 154 ? "  ※日本の範囲外です。系か座標 (座標設定) を確認してください。" : "");
    }

    private void ZoneList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => Ok_Click(sender, e);

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (ZoneList.SelectedItem is not Item item) return;
        Result = item.Number;
        MakeDefault = DefaultCheck.IsChecked == true;
        DialogResult = true;
    }
}
