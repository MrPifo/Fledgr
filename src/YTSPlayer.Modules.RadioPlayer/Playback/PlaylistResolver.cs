namespace YTSPlayer.Modules.RadioPlayer.Playback;

/// <summary>Holt aus einer M3U- oder PLS-Liste die erste Stream-Adresse.</summary>
public static class PlaylistResolver
{
    public static string? FirstStreamUrl(string content)
    {
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            // PLS-Zeilen sehen so aus: File1=http://…
            var eq = line.IndexOf('=');
            if (eq > 0 && line.StartsWith("File", StringComparison.OrdinalIgnoreCase)) line = line[(eq + 1)..].Trim();
            if (RadioUrls.IsHttp(line)) return line;
        }
        return null;
    }
}
