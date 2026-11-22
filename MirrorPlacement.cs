using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;

internal sealed class MirrorPlacement
{
    internal readonly string DevicePath;
    internal readonly Rectangle Bounds;

    internal MirrorPlacement(string devicePath, Rectangle bounds)
    {
        DevicePath = devicePath;
        Bounds = bounds;
    }

    internal static MirrorPlacement Load(string file)
    {
        try
        {
            if (!File.Exists(file))
                return null;
            var lines = File.ReadAllLines(file);
            if (lines.Length != 5 || string.IsNullOrWhiteSpace(lines[0]))
                return null;
            var values = new int[4];
            for (int i = 0; i < values.Length; i++)
            {
                if (!int.TryParse(lines[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i]))
                    return null;
            }
            return values[2] > 0 && values[3] > 0
                ? new MirrorPlacement(lines[0], new Rectangle(values[0], values[1], values[2], values[3]))
                : null;
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
            var directory = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            var temporary = file + ".tmp";
            File.WriteAllLines(temporary, new[]
            {
                DevicePath,
                Bounds.X.ToString(CultureInfo.InvariantCulture),
                Bounds.Y.ToString(CultureInfo.InvariantCulture),
                Bounds.Width.ToString(CultureInfo.InvariantCulture),
                Bounds.Height.ToString(CultureInfo.InvariantCulture)
            });
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
