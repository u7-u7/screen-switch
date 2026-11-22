using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

internal enum DockEdge { None, Left, Right, Top, Bottom }

internal sealed class BallPlacement
{
    internal readonly string DevicePath, ScreenName;
    internal readonly int RightOffset, BottomOffset;
    internal readonly DockEdge Edge;

    internal BallPlacement(string devicePath, string screenName, int rightOffset, int bottomOffset, DockEdge edge)
    {
        DevicePath = devicePath;
        ScreenName = screenName;
        RightOffset = rightOffset;
        BottomOffset = bottomOffset;
        Edge = edge;
    }

    internal static string DefaultPath
    {
        get { return Path.Combine(Path.GetDirectoryName(AppearancePreferences.DefaultPath), "ball-position.txt"); }
    }

    internal static BallPlacement Load(string file)
    {
        try
        {
            if (!File.Exists(file))
                return null;
            var lines = File.ReadAllLines(file);
            int right, bottom;
            DockEdge edge;
            if (lines.Length != 5 || (string.IsNullOrWhiteSpace(lines[0]) && string.IsNullOrWhiteSpace(lines[1]))
                || !int.TryParse(lines[2], NumberStyles.None, CultureInfo.InvariantCulture, out right)
                || !int.TryParse(lines[3], NumberStyles.None, CultureInfo.InvariantCulture, out bottom)
                || !Enum.TryParse(lines[4], out edge) || !Enum.IsDefined(typeof(DockEdge), edge))
                return null;
            return new BallPlacement(lines[0], lines[1], right, bottom, edge);
        }
        catch (IOException error)
        {
            Debug.WriteLine(error);
            return null;
        }
        catch (UnauthorizedAccessException error)
        {
            Debug.WriteLine(error);
            return null;
        }
    }

    internal void Save(string file)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));
            var temporary = file + ".tmp";
            File.WriteAllLines(temporary, new[] { DevicePath ?? "", ScreenName ?? "",
                RightOffset.ToString(CultureInfo.InvariantCulture), BottomOffset.ToString(CultureInfo.InvariantCulture),
                Edge.ToString() });
            if (File.Exists(file))
                File.Replace(temporary, file, null);
            else
                File.Move(temporary, file);
        }
        catch (IOException error)
        {
            Debug.WriteLine(error);
        }
        catch (UnauthorizedAccessException error)
        {
            Debug.WriteLine(error);
        }
    }
}
