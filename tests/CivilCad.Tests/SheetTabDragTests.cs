using CivilCad.App;

namespace CivilCad.Tests;

public class SheetTabDragTests
{
    // タブの真ん中: 0 番 50、1 番 150、2 番 300 (幅の違うタブ)、3 番 450
    private static readonly double?[] Centers = [50, 150, 300, 450];

    [Theory]
    [InlineData(1, 120, 1)]  // 少し左へ動かしただけ (左のタブの真ん中を越えていない) → そのまま
    [InlineData(1, 40, 0)]   // 0 番の真ん中より左 → 先頭へ
    [InlineData(1, 310, 2)]  // 2 番の真ん中を越えた → 2 番の位置へ
    [InlineData(1, 500, 3)]  // いちばん右まで → 最後へ
    [InlineData(2, 140, 1)]  // 右から左へ
    [InlineData(2, 290, 2)]  // 入れ替わった直後の位置では戻らない
    public void TabDropIndex_MovesPastNeighborCenter(int from, double x, int expected) =>
        Assert.Equal(expected, MainWindow.TabDropIndex(from, Centers, x));

    [Fact]
    public void TabDropIndex_SkipsHiddenTabs() =>
        Assert.Equal(0, MainWindow.TabDropIndex(2, [50, null, 300], 40));
}
