namespace YTSPlayer.Platform.Windows;

/// <summary>Findet den Ordner mit libvlc.dll: neben der EXE oder (Single-File) im Entpack-Ordner der eingebetteten Dateien.</summary>
public static class LibVlcLocator
{
    public static string? Find()
    {
        static bool Ok(string dir) => File.Exists(Path.Combine(dir, "libvlc.dll"));
        var next = Path.Combine(AppContext.BaseDirectory, "libvlc", "win-x64");
        if (Ok(next)) return next;
        var dirs = AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string;
        foreach (var d in dirs?.Split(Path.PathSeparator) ?? [])
        {
            var c = Path.Combine(d, "libvlc", "win-x64");
            if (Ok(c)) return c;
        }
        return null;
    }
}
