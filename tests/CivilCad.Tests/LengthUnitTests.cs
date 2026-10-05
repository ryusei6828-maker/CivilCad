using CivilCad.App.Tools;

namespace CivilCad.Tests;

public class LengthUnitTests
{
    [Fact]
    public void CycleAndFactors()
    {
        Assert.Equal(LengthUnit.Cm, LengthUnit.Mm.Next());
        Assert.Equal(LengthUnit.M, LengthUnit.Cm.Next());
        Assert.Equal(LengthUnit.Mm, LengthUnit.M.Next());
        Assert.Equal(0.001, LengthUnit.Mm.Meters());
        Assert.Equal(0.01, LengthUnit.Cm.Meters());
        Assert.Equal(1, LengthUnit.M.Meters());
        Assert.Equal(["mm", "cm", "m"], new[] { LengthUnit.Mm, LengthUnit.Cm, LengthUnit.M }.Select(u => u.Label()));
    }
}
