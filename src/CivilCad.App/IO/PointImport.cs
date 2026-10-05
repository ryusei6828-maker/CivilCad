using System.Globalization;
using System.IO;
using System.Text;

namespace CivilCad.App.IO;

/// <summary>読み込んだ座標点。X = 北, Y = 東 (測量座標)、Z = 標高 (無ければ null)。</summary>
public sealed record SurveyPoint(string Number, string Name, double X, double Y, double? Z)
{
    public string Label => Name.Length > 0 ? Name : Number;
}

/// <summary>読み込み結果。Lots は SIMA の画地 (区画名と、点番号の並び)。</summary>
public sealed record PointFile(List<SurveyPoint> Points, List<(string Name, List<string> PointNumbers)> Lots, int SkippedLines);

/// <summary>
/// 座標ファイルの読み込み。
/// SIMA (.sim): A01,点番号,点名,X,Y,H の座標と、D00〜D99 の画地 (B01,点番号 の並び)。
/// CSV: 1 行 1 点。区切りはカンマ / タブ / 空白。並びは 点名,X,Y[,Z] / 点番号,点名,X,Y[,Z] / X,Y[,Z] / 点番号,X,Y,Z を自動判別。
/// 見出し行など座標の無い行は飛ばす。座標の順は測量の X (北), Y (東)。
/// </summary>
public static class PointImport
{
    public static PointFile Load(string path)
    {
        var lines = ReadLines(path);
        return Path.GetExtension(path).Equals(".sim", StringComparison.OrdinalIgnoreCase) || LooksLikeSima(lines)
            ? ParseSima(lines)
            : ParseCsv(lines);
    }

    /// <summary>UTF-8 (BOM 付き含む) として正しければ UTF-8、そうでなければ Shift_JIS。</summary>
    public static string[] ReadLines(string path)
    {
        var bytes = File.ReadAllBytes(path);
        string text;
        try
        {
            text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            text = Encoding.GetEncoding(932).GetString(bytes);
        }
        return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    }

    private static bool LooksLikeSima(string[] lines) =>
        lines.Any(l => l.TrimStart().StartsWith("A01,", StringComparison.OrdinalIgnoreCase));

    public static PointFile ParseSima(IEnumerable<string> lines)
    {
        var points = new List<SurveyPoint>();
        var lots = new List<(string, List<string>)>();
        (string Name, List<string> Numbers)? lot = null;
        var skipped = 0;
        foreach (var raw in lines)
        {
            var f = raw.Split(',').Select(s => s.Trim()).ToArray();
            if (f.Length == 0 || f[0].Length == 0) continue;
            switch (f[0].ToUpperInvariant())
            {
                case "A01":
                    if (f.Length >= 5 && Num(f[3]) is double x && Num(f[4]) is double y)
                        points.Add(new SurveyPoint(f[1], f[2], x, y, f.Length > 5 ? Num(f[5]) : null));
                    else
                        skipped++;
                    break;
                case "D00":
                    lot = (f.Length > 2 && f[2].Length > 0 ? f[2] : f.ElementAtOrDefault(1) ?? "", []);
                    break;
                case "B01":
                    if (lot is { } current && f.Length > 1 && f[1].Length > 0) current.Numbers.Add(f[1]);
                    break;
                case "D99":
                    if (lot is { Numbers.Count: >= 2 } done) lots.Add(done);
                    lot = null;
                    break;
            }
        }
        return new PointFile(points, lots, skipped);
    }

    public static PointFile ParseCsv(IEnumerable<string> lines)
    {
        var points = new List<SurveyPoint>();
        var skipped = 0;
        var n = 0;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var f = (line.Contains(',') ? line.Split(',') : line.Contains('\t') ? line.Split('\t') : line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Select(s => s.Trim().Trim('"')).ToArray();
            if (ParseCsvFields(f, ++n) is { } p) points.Add(p);
            else skipped++;
        }
        return new PointFile(points, [], skipped);
    }

    /// <summary>1 行分。座標の組 (連続する 2 つの数値) が見つからなければ null (見出し行など)。</summary>
    private static SurveyPoint? ParseCsvFields(string[] f, int lineNo)
    {
        // 文字の列 (点名) があれば、その後ろで最初に連続する 2 つの数値が X, Y (次も数値なら Z)。後ろの備考列は無視
        var firstText = Array.FindIndex(f, s => s.Length > 0 && Num(s) == null);
        if (firstText >= 0)
        {
            for (var i = firstText + 1; i + 1 < f.Length; i++)
            {
                if (Num(f[i]) is not double x || Num(f[i + 1]) is not double y) continue;
                var nameIndex = Array.FindLastIndex(f, i - 1, s => s.Length > 0 && Num(s) == null);
                // 点名の直前が数値なら点番号 (No,点番,点名,X,Y,Z のような並びでは 点番)
                var number = nameIndex > 0 && Num(f[nameIndex - 1]) != null ? f[nameIndex - 1] : "";
                return new SurveyPoint(number, f[nameIndex], x, y, i + 2 < f.Length ? Num(f[i + 2]) : null);
            }
            return null;
        }

        // 数値だけの行
        var nums = f.Where(s => s.Length > 0).Select(Num).ToArray();
        if (nums.Any(v => v == null)) return null;
        var v = nums.Select(x => x!.Value).ToArray();
        return v.Length switch
        {
            2 => new SurveyPoint(lineNo.ToString(CultureInfo.InvariantCulture), "", v[0], v[1], null),
            // 3 列: 先頭が整数なら 点番号,X,Y、そうでなければ X,Y,Z
            3 when IsInteger(f.First(s => s.Length > 0)) => new SurveyPoint(Fmt(v[0]), "", v[1], v[2], null),
            3 => new SurveyPoint(lineNo.ToString(CultureInfo.InvariantCulture), "", v[0], v[1], v[2]),
            // 4 列以上: 後ろの 3 つが X,Y,Z、その前が点番号 (点名) — No,点番,点名(数字),X,Y,Z なども
            >= 4 => new SurveyPoint(Fmt(v[^4]), "", v[^3], v[^2], v[^1]),
            _ => null,
        };
    }

    private static bool IsInteger(string s) => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);

    private static string Fmt(double v) => v.ToString(CultureInfo.InvariantCulture);

    private static double? Num(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
}
