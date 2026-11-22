using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

internal sealed class AppearancePreferences
{
    internal const int MaximumTransparency = 100;
    internal const int DefaultTransparency = 31;

    internal PanelPalette Palette = PanelPalette.Options[0];
    internal int Transparency = DefaultTransparency;

    internal static string DefaultPath
    {
        get
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ScreenSwitch", "palette.txt");
        }
    }

    internal static AppearancePreferences Load(string path)
    {
        var preferences = new AppearancePreferences();
        try
        {
            if (!File.Exists(path))
                return preferences;
            var lines = File.ReadAllLines(path);
            if (lines.Length > 0)
            {
                string id = lines[0].Trim();
                preferences.Palette = PanelPalette.Options.FirstOrDefault(option => option.Id == id)
                    ?? preferences.Palette;
            }
            int transparency;
            if (lines.Length > 1 && int.TryParse(lines[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out transparency)
                && transparency >= 0 && transparency <= MaximumTransparency)
                preferences.Transparency = transparency;
        }
        catch (Exception error)
        {
            if (!(error is IOException) && !(error is UnauthorizedAccessException))
                throw;
            Debug.WriteLine(error);
        }
        return preferences;
    }

    internal void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllLines(path, new[] { Palette.Id, Transparency.ToString(CultureInfo.InvariantCulture) });
        }
        catch (Exception error)
        {
            if (!(error is IOException) && !(error is UnauthorizedAccessException))
                throw;
            Debug.WriteLine(error);
        }
    }
}
