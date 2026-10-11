using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using YTSPlayer.Contracts.Localization;

namespace YTSPlayer.Tests;

/// <summary>Prüft den Übersetzer (Parameter, Mehrzahl, Zahlen- und Datumsformat) und die Übersetzungsdateien im Quellcode.</summary>
public class LocalizationTests
{
    static string Xml(string language, string country, params (string Key, string Value)[] entries)
    {
        var sb = new StringBuilder($"<?xml version=\"1.0\" encoding=\"utf-8\"?><translations language=\"{language}\" country=\"{country}\">");
        foreach (var (k, v) in entries) sb.Append($"<t key=\"{k}\">{System.Security.SecurityElement.Escape(v)}</t>");
        return sb.Append("</translations>").ToString();
    }

    static Localizer Make()
    {
        var loc = new Localizer();
        loc.AddXml(new MemoryStream(Encoding.UTF8.GetBytes(Xml("de", "DE",
            ("format.decimal", ","), ("format.thousands", "."), ("format.date", "dd.MM.yyyy"), ("format.time", "HH:mm"), ("format.longdate", "%1 · %2. %3"),
            ("date.day.0", "Sonntag"), ("date.month.10", "Oktober"),
            ("a", "Hallo %1, du hast %2 Nachrichten (100%%)"), ("b", "nur de"),
            ("n.one", "%1 Titel"), ("n.other", "%1 Titel"), ("m.one", "%1 Datei zu %2"), ("m.other", "%1 Dateien zu %2")))));
        loc.AddXml(new MemoryStream(Encoding.UTF8.GetBytes(Xml("en", "GB",
            ("format.decimal", "."), ("format.thousands", ","), ("format.date", "dd/MM/yyyy"), ("format.time", "HH:mm"), ("format.longdate", "%1 · %2 %3"),
            ("date.day.0", "Sunday"), ("date.month.10", "October"),
            ("a", "Hello %2, you have %1 messages"),
            ("n.one", "%1 track"), ("n.other", "%1 tracks")))));
        return loc;
    }

    [Fact]
    public void Parameter_und_Prozentzeichen()
    {
        var loc = Make();
        Assert.Equal("Hallo Max, du hast 3 Nachrichten (100%)", loc.Format("a", "Max", 3));
        // Fehlt ein Argument, bleibt der Platzhalter stehen (fällt sofort auf).
        Assert.Equal("Hallo Max, du hast %2 Nachrichten (100%)", loc.Format("a", "Max"));
    }

    [Fact]
    public void Reihenfolge_der_Parameter_darf_je_Sprache_wechseln()
    {
        var loc = Make();
        loc.SetLanguage("en-GB");
        Assert.Equal("Hello 5, you have Max messages", loc.Format("a", "Max", 5));
    }

    [Fact]
    public void Fehlender_Schluessel_faellt_auf_Standardsprache_dann_auf_den_Schluessel()
    {
        var loc = Make();
        loc.SetLanguage("en-GB");
        Assert.Equal("nur de", loc["b"]);
        Assert.Equal("gibt.es.nicht", loc["gibt.es.nicht"]);
    }

    [Fact]
    public void Sprache_ohne_Land_nimmt_die_erste_passende()
    {
        var loc = Make();
        loc.SetLanguage("en");
        Assert.Equal("en-GB", loc.Language);
        loc.SetLanguage("xx-YY");
        Assert.Equal(Localizer.DefaultLanguage, loc.Language);
    }

    [Fact]
    public void Mehrzahl_waehlt_one_oder_other()
    {
        var loc = Make();
        Assert.Equal("1 Titel", loc.PluralFormat("n", 1));
        Assert.Equal("5 Titel", loc.PluralFormat("n", 5));
        Assert.Equal("1 Datei zu A", loc.PluralFormat("m", 1, "A"));
        Assert.Equal("2 Dateien zu A", loc.PluralFormat("m", 2, "A"));
        loc.SetLanguage("en-GB");
        Assert.Equal("1 track", loc.PluralFormat("n", 1));
        Assert.Equal("1,234 tracks", loc.PluralFormat("n", 1234));
    }

    [Fact]
    public void Zahlen_nutzen_Dezimal_und_Tausenderzeichen_der_Sprache()
    {
        var loc = Make();
        Assert.Equal("1.234.567,89", loc.Number(1234567.89, 2));
        Assert.Equal("-1.234,5", loc.Number(-1234.5, 1));
        Assert.Equal("0", loc.Number(0));
        Assert.Equal("999", loc.Integer(999));
        Assert.Equal("1.000", loc.Integer(1000));
        loc.SetLanguage("en-GB");
        Assert.Equal("1,234,567.89", loc.Number(1234567.89, 2));
        Assert.Equal("-1,234.5", loc.Number(-1234.5, 1));
    }

    [Fact]
    public void Zahlen_koennen_wieder_gelesen_werden()
    {
        var loc = Make();
        Assert.Equal(1234.5, loc.ParseNumber("1.234,5"));
        Assert.Equal(-3.25, loc.ParseNumber("-3,25"));
        Assert.Null(loc.ParseNumber("abc"));
        Assert.Null(loc.ParseNumber(" "));
        loc.SetLanguage("en-GB");
        Assert.Equal(1234.5, loc.ParseNumber("1,234.5"));
    }

    [Fact]
    public void Datum_und_Uhrzeit_folgen_den_Mustern_der_Sprache()
    {
        var loc = Make();
        var t = new DateTime(2026, 10, 11, 13, 5, 0);
        Assert.Equal("11.10.2026", loc.Date(t));
        Assert.Equal("13:05", loc.Time(t));
        Assert.Equal("Sonntag · 11. Oktober", loc.LongDate(t));
        loc.SetLanguage("en-GB");
        Assert.Equal("11/10/2026", loc.Date(t));
        Assert.Equal("Sunday · 11 October", loc.LongDate(t));
    }

    [Fact]
    public void Kaputte_Dateien_werden_abgelehnt()
    {
        var loc = new Localizer();
        Assert.Throws<InvalidDataException>(() => loc.AddXml(new MemoryStream(Encoding.UTF8.GetBytes("<translations><t key=\"a\">x</t></translations>"))));
    }

    // ---- Dateien im Quellcode ----

    static string SrcDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "YT-SPlayer.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src");
    }

    /// <summary>Alle Übersetzungsdateien, gruppiert nach Projekt: Projekt → Sprache → (Schlüssel → Text).</summary>
    static Dictionary<string, Dictionary<string, Dictionary<string, string>>> LoadAll()
    {
        var result = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();
        foreach (var file in Directory.GetFiles(SrcDir(), "*.xml", SearchOption.AllDirectories))
        {
            var norm = file.Replace('\\', '/');
            if (!norm.Contains("/Strings/") || norm.Contains("/obj/") || norm.Contains("/bin/")) continue;
            var project = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(file))!);
            var root = XDocument.Load(file).Root!;
            var lang = (string?)root.Attribute("language");
            var country = (string?)root.Attribute("country");
            Assert.False(string.IsNullOrEmpty(lang) || string.IsNullOrEmpty(country), $"Kopf ohne language/country: {file}");
            Assert.Equal($"{lang}-{country}", Path.GetFileNameWithoutExtension(file));
            var table = new Dictionary<string, string>();
            foreach (var t in root.Elements("t"))
            {
                var key = (string?)t.Attribute("key");
                Assert.False(string.IsNullOrEmpty(key), $"Eintrag ohne Schlüssel in {file}");
                Assert.True(table.TryAdd(key!, t.Value), $"Doppelter Schlüssel {key} in {file}");
            }
            if (!result.TryGetValue(project, out var langs)) result[project] = langs = [];
            langs[$"{lang}-{country}"] = table;
        }
        return result;
    }

    [Fact]
    public void Alle_Sprachen_eines_Projekts_haben_dieselben_Schluessel_und_Parameter()
    {
        var all = LoadAll();
        Assert.NotEmpty(all);
        foreach (var (project, langs) in all)
        {
            Assert.True(langs.ContainsKey(Localizer.DefaultLanguage), $"{project}: {Localizer.DefaultLanguage} fehlt");
            var reference = langs[Localizer.DefaultLanguage];
            foreach (var (lang, table) in langs)
            {
                Assert.True(reference.Keys.ToHashSet().SetEquals(table.Keys), $"{project}/{lang}: Schlüssel weichen ab: {string.Join(", ", reference.Keys.Except(table.Keys).Concat(table.Keys.Except(reference.Keys)))}");
                foreach (var (key, text) in table)
                    Assert.True(Localizer.ParametersOf(text).SequenceEqual(Localizer.ParametersOf(reference[key])), $"{project}/{lang}: Parameter von {key} weichen ab");
            }
        }
    }

    [Fact]
    public void Jede_Mehrzahl_hat_one_und_other()
    {
        foreach (var (project, langs) in LoadAll())
            foreach (var (lang, table) in langs)
                foreach (var key in table.Keys.Where(k => k.EndsWith(".one", StringComparison.Ordinal)))
                    Assert.True(table.ContainsKey(key[..^4] + ".other"), $"{project}/{lang}: {key} ohne .other");
    }

    [Fact]
    public void Alle_im_Code_benutzten_Schluessel_gibt_es_in_der_Standardsprache()
    {
        var known = LoadAll().Values.SelectMany(l => l[Localizer.DefaultLanguage].Keys).ToHashSet();
        string[] ignore = [];
        var used = new List<(string Key, string File, bool Plural)>();
        foreach (var file in Directory.GetFiles(SrcDir(), "*.*", SearchOption.AllDirectories))
        {
            var norm = file.Replace('\\', '/');
            if (norm.Contains("/obj/") || norm.Contains("/bin/") || !(norm.EndsWith(".cs") || norm.EndsWith(".axaml"))) continue;
            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"\{loc:T\s+([A-Za-z0-9_.]+)\}")) used.Add((m.Groups[1].Value, norm, false));
            foreach (Match m in Regex.Matches(text, @"Loc\.(?:T|F)\(""([A-Za-z0-9_.]+)""")) used.Add((m.Groups[1].Value, norm, false));
            foreach (Match m in Regex.Matches(text, @"Loc\.P\(""([A-Za-z0-9_.]+)""")) used.Add((m.Groups[1].Value, norm, true));
            foreach (Match m in Regex.Matches(text, @"Loc(?:\.Current)?\[""([A-Za-z0-9_.]+)""\]")) used.Add((m.Groups[1].Value, norm, false));
            foreach (Match m in Regex.Matches(text, @"new NavPage\(""[^""]+"", ""([A-Za-z0-9_.]+)""")) used.Add((m.Groups[1].Value, norm, false));
            foreach (Match m in Regex.Matches(text, @"DisplayName => ""([a-z]+\.[A-Za-z0-9_.]+)""")) used.Add((m.Groups[1].Value, norm, false));
        }
        Assert.NotEmpty(used);
        var missing = used.Where(u => !ignore.Contains(u.Key) && !(u.Plural ? known.Contains(u.Key + ".other") : known.Contains(u.Key)))
            .Select(u => $"{u.Key} ({Path.GetFileName(u.File)})").Distinct().ToList();
        Assert.True(missing.Count == 0, "Fehlende Schlüssel in de-DE: " + string.Join(", ", missing));
    }
}
