using System.IO;
using System.Windows;
using CivilCad.Alignment;
using CivilCad.Core.Geometry;
using Microsoft.Win32;
using HorizontalAlignment = CivilCad.Alignment.HorizontalAlignment;

namespace CivilCad.App.Alignment;

/// <summary>路線を LandXML (J-LandXML の並び) に書き出す。</summary>
public static class LandXmlExport
{
    /// <summary>保存先を選んで書き出す。書き出したファイルの場所 (キャンセル・失敗なら null、失敗はメッセージを出す)。</summary>
    public static string? Run(Window? owner, AlignmentInput input, int? planeZone)
    {
        HorizontalAlignment al;
        try
        {
            al = input.Compute();
        }
        catch (AlignmentException ex)
        {
            MessageBox.Show(owner!, ex.Message, "路線 XML の書き出し", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
        var dlg = new SaveFileDialog
        {
            Title = "路線を XML (LandXML) に書き出す",
            Filter = "LandXML (*.xml)|*.xml",
            FileName = SafeName(input.Name) + ".xml",
        };
        if (dlg.ShowDialog(owner) != true) return null;
        try
        {
            Write(dlg.FileName, input, al, planeZone);
            return dlg.FileName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(owner!, ex.Message, "路線 XML の書き出し", MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
    }

    public static void Write(string path, AlignmentInput input, HorizontalAlignment al, int? planeZone)
    {
        var ips = input.Rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Name) || r.X != 0 || r.Y != 0)
            .Select((r, i) => new IntersectionPoint(string.IsNullOrWhiteSpace(r.Name) ? $"IP{i}" : r.Name.Trim(), Vec2.FromSurvey(r.X, r.Y), r.R, r.A1, r.A2))
            .ToList();
        var version = typeof(LandXmlExport).Assembly.GetName().Version?.ToString(3);
        LandXmlWriter.Write(path, input.Name, al, ips, input.Pitch, planeZone, "CivilCad", version);
    }

    private static string SafeName(string name)
    {
        var s = string.Concat((string.IsNullOrWhiteSpace(name) ? "路線" : name.Trim()).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return s.Length > 0 ? s : "路線";
    }
}
