using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace YTSPlayer.Tests;

/// <summary>Prüft die Theme-Dateien: jedes Theme hat dieselben Schlüssel, und alle in Views benutzten Tokens gibt es.</summary>
public class ThemeTests
{
    static string SrcDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "YT-SPlayer.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src");
    }

    static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    static HashSet<string> KeysOf(string file) =>
        [.. XDocument.Load(file).Root!.Elements().Select(e => (string?)e.Attribute(X + "Key")).Where(k => k != null).Select(k => k!)];

    static string[] ThemeFiles() => Directory.GetFiles(Path.Combine(SrcDir(), "YTSPlayer.Ui", "Theme", "Themes"), "*.axaml");

    [Fact]
    public void Jedes_Theme_hat_dieselben_Schluessel()
    {
        var files = ThemeFiles();
        Assert.NotEmpty(files);
        var reference = KeysOf(files[0]);
        foreach (var file in files.Skip(1))
        {
            var keys = KeysOf(file);
            Assert.True(reference.SetEquals(keys), $"{Path.GetFileName(file)}: Schlüssel weichen ab: {string.Join(", ", reference.Except(keys).Concat(keys.Except(reference)))}");
        }
    }

    [Fact]
    public void Alle_benutzten_Tokens_sind_definiert()
    {
        var defined = KeysOf(ThemeFiles()[0]);
        foreach (var key in KeysOf(Path.Combine(SrcDir(), "YTSPlayer.Ui", "Theme", "Metrics.axaml"))) defined.Add(key);
        var missing = new List<string>();
        foreach (var file in Directory.GetFiles(SrcDir(), "*.axaml", SearchOption.AllDirectories))
        {
            var norm = file.Replace('\\', '/');
            if (norm.Contains("/obj/") || norm.Contains("/bin/") || norm.Contains("/Theme/")) continue;
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"\{(?:Dynamic|Static)Resource\s+([A-Za-z]+\.[A-Za-z0-9.]+)\}"))
                if (!defined.Contains(m.Groups[1].Value)) missing.Add($"{m.Groups[1].Value} ({Path.GetFileName(file)})");
        }
        Assert.True(missing.Count == 0, "Nicht definierte Tokens: " + string.Join(", ", missing.Distinct()));
    }

    [Fact]
    public void Namen_beschreiben_Rollen_nicht_Farben()
    {
        string[] colorWords = ["Cream", "Amber", "Orange", "Blue", "Red", "Green", "Pink", "Graphite", "Grey", "Gray", "Black", "White"];
        var bad = KeysOf(ThemeFiles()[0]).Where(k => colorWords.Any(w => k.Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
        Assert.True(bad.Count == 0, "Farbnamen als Token: " + string.Join(", ", bad));
    }
}
