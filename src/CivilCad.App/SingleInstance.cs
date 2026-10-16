using System.IO;
using System.IO.Pipes;
using System.Text;

namespace CivilCad.App;

/// <summary>
/// 起動中の CivilCad へ、開くファイルを渡す (名前付きパイプ)。
/// CivilCadPoint の「CivilCad で開く」などで CivilCad を起動したとき、すでに CivilCad が開いていれば
/// 新しい窓を出さずに、開いている CivilCad の新しいシートで開く。
/// </summary>
public static class SingleInstance
{
    /// <summary>ユーザーごとのパイプ名 (別のユーザーの CivilCad には渡さない)。テストでは別の名前にする。</summary>
    public static string PipeName { get; set; } = $"CivilCad.App.{Environment.UserName}";

    /// <summary>起動中の CivilCad があれば args を渡して true。無ければ false。</summary>
    public static bool TrySend(string[] args, int timeoutMs = 500)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(timeoutMs);
            // 相手が相対パスを開けるよう、ファイルは絶対パスにして渡す
            var lines = args.Select(a => a.StartsWith("--", StringComparison.Ordinal) || !File.Exists(a) ? a : Path.GetFullPath(a));
            var bytes = Encoding.UTF8.GetBytes(string.Join("\n", lines));
            client.Write(bytes, 0, bytes.Length);
            client.Flush();
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>ほかの CivilCad から渡されるファイルを受け取り続ける (受け取ったら onArgs を呼ぶ。呼ぶのは別のスレッド)。</summary>
    public static void StartServer(Action<string[]> onArgs, CancellationToken cancel = default)
    {
        _ = Task.Run(async () =>
        {
            while (!cancel.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(cancel);
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var text = await reader.ReadToEndAsync(cancel);
                    var args = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (args.Length > 0) onArgs(args);
                }
                catch (OperationCanceledException) { return; }
                catch (IOException)
                {
                    // 同じパイプ名をほかの CivilCad が使っている (先に起動した方が受け取る) か、途中で切れた: 少し待って続ける
                    await Task.Delay(1000, cancel).ContinueWith(_ => { });
                }
            }
        }, cancel);
    }
}
