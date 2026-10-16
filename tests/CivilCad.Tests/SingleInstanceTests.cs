using CivilCad.App;

namespace CivilCad.Tests;

public class SingleInstanceTests
{
    [Fact]
    public async Task SendsArgsToRunningInstance()
    {
        var old = SingleInstance.PipeName;
        SingleInstance.PipeName = $"CivilCad.Tests.{Guid.NewGuid():N}";
        using var cancel = new CancellationTokenSource();
        try
        {
            // 起動中の CivilCad が無ければ渡せない
            Assert.False(SingleInstance.TrySend(["--sections", "a.xml"], 200));

            var got = new TaskCompletionSource<string[]>();
            SingleInstance.StartServer(args => got.TrySetResult(args), cancel.Token);
            var sent = false;
            for (var i = 0; i < 20 && !sent; i++) // サーバーが待ち始めるまで少し待つ
            {
                sent = SingleInstance.TrySend(["--sections", @"C:\data\本線.xml"], 200);
                if (!sent) await Task.Delay(50);
            }
            Assert.True(sent);
            var args = await got.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(["--sections", @"C:\data\本線.xml"], args);
        }
        finally
        {
            cancel.Cancel();
            SingleInstance.PipeName = old;
        }
    }
}
