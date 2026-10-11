using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace YTSPlayer.Contracts.Localization;

/// <summary>
/// Übersetzungen und Zahlen-/Datumsformate. Texte stehen in XML-Dateien (<c>Strings/de-DE.xml</c> …), je Sprache eine Datei je Projekt.
/// Parameter im Text: <c>%1</c>, <c>%2</c> … (<c>%%</c> = Prozentzeichen). Zahlen formatiert der Aufrufer vorher über <see cref="Number"/> / <see cref="Integer"/>.
/// </summary>
public interface ILocalizer : INotifyPropertyChanged
{
    /// <summary>Aktive Sprache, z. B. <c>de-DE</c>.</summary>
    string Language { get; }

    /// <summary>Alle Sprachen, für die Dateien gefunden wurden (z. B. de-DE, en-GB).</summary>
    IReadOnlyList<string> Languages { get; }

    /// <summary>Text zum Schlüssel. Fehlt er überall, kommt der Schlüssel selbst zurück (so fällt ein Fehler sofort auf).</summary>
    string this[string key] { get; }

    /// <summary>Text mit eingesetzten Parametern (<c>%1</c> = erstes Argument).</summary>
    string Format(string key, params object?[] args);

    /// <summary>
    /// Text mit Mehrzahl: wählt <c>key.one</c> (bei 1) oder <c>key.other</c>. <c>%1</c> ist die Zahl (formatiert), weitere Argumente folgen ab <c>%2</c>.
    /// </summary>
    string PluralFormat(string key, long count, params object?[] args);

    /// <summary>Zahl mit Dezimalzeichen und Tausender-Zeichen der Sprache, z. B. 1.234,5 (de) oder 1,234.5 (en).</summary>
    string Number(double value, int decimals = 0);

    /// <summary>Ganze Zahl mit Tausender-Zeichen der Sprache.</summary>
    string Integer(long value);

    /// <summary>Liest eine vom Nutzer getippte Zahl im Format der Sprache.</summary>
    double? ParseNumber(string? text);

    /// <summary>Datum im Kurzformat der Sprache.</summary>
    string Date(DateTime value);

    /// <summary>Uhrzeit im Format der Sprache.</summary>
    string Time(DateTime value);

    /// <summary>Datum für den Startbereich, z. B. „Sonntag · 11. Oktober“.</summary>
    string LongDate(DateTime value);

    /// <summary>Name des Wochentags.</summary>
    string DayName(DayOfWeek day);

    /// <summary>Name des Monats (1–12).</summary>
    string MonthName(int month);

    /// <summary>Wechselt die Sprache. Eine Sprache ohne Land (z. B. „en“) nimmt die erste passende (en-GB). Unbekannt = Standardsprache.</summary>
    void SetLanguage(string language);

    /// <summary>Liest alle <c>Strings/*.xml</c> aus den eingebetteten Ressourcen einer Assembly dazu.</summary>
    void AddAssembly(Assembly assembly);

    /// <summary>Liest eine einzelne Übersetzungsdatei (XML) dazu. Für Tests und Dateien außerhalb der EXE.</summary>
    void AddXml(Stream xml);

    /// <summary>Wird ausgelöst, wenn die Sprache wechselt.</summary>
    event Action? LanguageChanged;
}

/// <summary>Zugriff auf den <see cref="ILocalizer"/> für Stellen ohne Dienste (XAML-Erweiterung, einfache Helfer). Die App tauscht ihn beim Start gegen den echten aus.</summary>
public static class Loc
{
    /// <summary>Der aktuelle Übersetzer. Nie null.</summary>
    public static ILocalizer Current { get; set; } = new Localizer();

    /// <summary>Kurzform für <c>Current[key]</c>.</summary>
    public static string T(string key) => Current[key];

    /// <summary>Kurzform für <c>Current.Format(key, args)</c> (<c>%1</c> = erstes Argument).</summary>
    public static string F(string key, params object?[] args) => Current.Format(key, args);

    /// <summary>Kurzform für <c>Current.PluralFormat(key, count, args)</c> (<c>%1</c> = die Zahl, weitere Argumente ab <c>%2</c>).</summary>
    public static string P(string key, long count, params object?[] args) => Current.PluralFormat(key, count, args);
}

/// <summary>Standard-Übersetzer: XML-Dateien, Rückfall auf die Standardsprache, eigene Zahlen- und Datumsformate (ohne CultureInfo).</summary>
public sealed partial class Localizer : ILocalizer
{
    /// <summary>Sprache, auf die zurückgefallen wird.</summary>
    public const string DefaultLanguage = "de-DE";

    readonly object _gate = new();
    readonly Dictionary<string, Dictionary<string, string>> _tables = new(StringComparer.OrdinalIgnoreCase);
    string _language = DefaultLanguage;

    /// <summary>Neuer Übersetzer ohne Texte. Texte kommen über <see cref="AddAssembly"/> oder <see cref="AddXml"/>.</summary>
    public Localizer() { }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? LanguageChanged;

    public string Language => _language;

    public IReadOnlyList<string> Languages
    {
        get { lock (_gate) return _tables.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList(); }
    }

    public string this[string key]
    {
        get
        {
            lock (_gate)
            {
                foreach (var lang in Chain())
                    if (_tables.TryGetValue(lang, out var table) && table.TryGetValue(key, out var value)) return value;
            }
            return key;
        }
    }

    /// <summary>Suchreihenfolge: gewählte Sprache, dann gleiche Sprache anderes Land, dann Standardsprache.</summary>
    IEnumerable<string> Chain()
    {
        yield return _language;
        var lang = _language.Split('-')[0];
        foreach (var other in _tables.Keys)
            if (!other.Equals(_language, StringComparison.OrdinalIgnoreCase) && other.StartsWith(lang + "-", StringComparison.OrdinalIgnoreCase)) yield return other;
        if (!_language.Equals(DefaultLanguage, StringComparison.OrdinalIgnoreCase)) yield return DefaultLanguage;
    }

    public void SetLanguage(string language)
    {
        string resolved;
        lock (_gate)
        {
            resolved = _tables.Keys.FirstOrDefault(k => k.Equals(language, StringComparison.OrdinalIgnoreCase))
                       ?? _tables.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).FirstOrDefault(k => k.StartsWith(language.Split('-')[0] + "-", StringComparison.OrdinalIgnoreCase))
                       ?? DefaultLanguage;
        }
        if (resolved.Equals(_language, StringComparison.OrdinalIgnoreCase)) return;
        _language = resolved;
        Raise();
    }

    void Raise()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        LanguageChanged?.Invoke();
    }

    public void AddAssembly(Assembly assembly)
    {
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!Regex.IsMatch(name, @"\.Strings\.[a-z]{2,3}-[A-Za-z]{2,4}\.xml$")) continue;
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream != null) AddXml(stream);
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    public void AddXml(Stream xml)
    {
        var doc = XDocument.Load(xml);
        var root = doc.Root ?? throw new InvalidDataException("Übersetzungsdatei ohne Wurzel.");
        var language = (string?)root.Attribute("language");
        var country = (string?)root.Attribute("country");
        if (string.IsNullOrWhiteSpace(language) || string.IsNullOrWhiteSpace(country))
            throw new InvalidDataException("Übersetzungsdatei ohne language/country im Kopf.");
        var id = $"{language}-{country}";
        lock (_gate)
        {
            if (!_tables.TryGetValue(id, out var table)) _tables[id] = table = new Dictionary<string, string>();
            foreach (var t in root.Elements("t"))
            {
                var key = (string?)t.Attribute("key");
                if (!string.IsNullOrEmpty(key)) table[key] = t.Value;
            }
        }
    }

    // ---- Parameter ----

    public string Format(string key, params object?[] args) => Substitute(this[key], args);

    public string PluralFormat(string key, long count, params object?[] args)
    {
        var suffix = count == 1 ? ".one" : ".other";
        var template = this[key + suffix];
        if (template == key + suffix) template = this[key + ".other"];
        if (template == key + ".other") template = this[key];
        var all = new object?[args.Length + 1];
        all[0] = Integer(count);
        Array.Copy(args, 0, all, 1, args.Length);
        return Substitute(template, all);
    }

    /// <summary>Ersetzt <c>%1</c> … und <c>%%</c>. Fehlt ein Argument, bleibt <c>%N</c> im Text stehen.</summary>
    internal static string Substitute(string template, object?[] args)
    {
        if (template.IndexOf('%') < 0) return template;
        var sb = new StringBuilder(template.Length + 16);
        for (var i = 0; i < template.Length; i++)
        {
            var c = template[i];
            if (c != '%') { sb.Append(c); continue; }
            if (i + 1 < template.Length && template[i + 1] == '%') { sb.Append('%'); i++; continue; }
            var j = i + 1;
            while (j < template.Length && char.IsAsciiDigit(template[j])) j++;
            if (j == i + 1) { sb.Append('%'); continue; }
            var n = int.Parse(template.AsSpan(i + 1, j - i - 1), CultureInfo.InvariantCulture);
            if (n >= 1 && n <= args.Length) sb.Append(Convert.ToString(args[n - 1], CultureInfo.InvariantCulture));
            else sb.Append(template, i, j - i);
            i = j - 1;
        }
        return sb.ToString();
    }

    /// <summary>Alle Parameter-Nummern eines Textes (für den Test, dass Sprachen dieselben Parameter nutzen).</summary>
    public static IReadOnlyCollection<int> ParametersOf(string template)
    {
        var set = new SortedSet<int>();
        for (var i = 0; i < template.Length; i++)
        {
            if (template[i] != '%') continue;
            if (i + 1 < template.Length && template[i + 1] == '%') { i++; continue; }
            var j = i + 1;
            while (j < template.Length && char.IsAsciiDigit(template[j])) j++;
            if (j > i + 1) set.Add(int.Parse(template.AsSpan(i + 1, j - i - 1), CultureInfo.InvariantCulture));
            i = j - 1;
        }
        return set;
    }

    // ---- Zahlen und Datum (ohne CultureInfo, Muster stehen in der Sprachdatei) ----

    string DecimalSep => this["format.decimal"] is { Length: > 0 } d && d != "format.decimal" ? d : ".";
    string ThousandsSep => this["format.thousands"] is var t && t != "format.thousands" ? t : "";

    public string Number(double value, int decimals = 0)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return value.ToString(CultureInfo.InvariantCulture);
        var raw = Math.Abs(value).ToString("F" + Math.Max(0, decimals), CultureInfo.InvariantCulture);
        var dot = raw.IndexOf('.');
        var intPart = dot < 0 ? raw : raw[..dot];
        var frac = dot < 0 ? "" : raw[(dot + 1)..];
        var sb = new StringBuilder();
        var sep = ThousandsSep;
        for (var i = 0; i < intPart.Length; i++)
        {
            if (i > 0 && (intPart.Length - i) % 3 == 0) sb.Append(sep);
            sb.Append(intPart[i]);
        }
        if (frac.Length > 0) sb.Append(DecimalSep).Append(frac);
        var negative = value < 0 && raw.Any(ch => ch is >= '1' and <= '9');
        return negative ? "-" + sb : sb.ToString();
    }

    public string Integer(long value) => Number(value, 0);

    public double? ParseNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.Trim().Replace(" ", "").Replace(" ", "");
        var thousands = ThousandsSep;
        if (thousands.Length > 0) s = s.Replace(thousands.Trim(), "");
        s = s.Replace(DecimalSep, ".");
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    string Pattern(string key, string fallback) => this[key] is var p && p != key ? p : fallback;

    public string Date(DateTime value) => value.ToString(Pattern("format.date", "yyyy-MM-dd"), CultureInfo.InvariantCulture);

    public string Time(DateTime value) => value.ToString(Pattern("format.time", "HH:mm"), CultureInfo.InvariantCulture);

    public string DayName(DayOfWeek day) => this["date.day." + (int)day];

    public string MonthName(int month) => this["date.month." + month];

    public string LongDate(DateTime value) => Format("format.longdate", DayName(value.DayOfWeek), value.Day.ToString(CultureInfo.InvariantCulture), MonthName(value.Month));
}
