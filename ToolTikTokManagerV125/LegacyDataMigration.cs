namespace ToolTikTokManagerV125;

static class LegacyDataMigration
{
    public static void TryImportV12Catalog(string baseDir)
    {
        var target = Path.Combine(baseDir, "profiles.json");
        if (File.Exists(target)) return;
        var parent = Directory.GetParent(baseDir.TrimEnd(Path.DirectorySeparatorChar))?.FullName;
        if (string.IsNullOrWhiteSpace(parent)) return;
        var source = Path.Combine(parent, "dist_v12", "profiles.json");
        if (File.Exists(source))
        {
            Directory.CreateDirectory(baseDir);
            File.Copy(source, target, false);
        }
    }

    public static void TryImportV12ProfileData(string baseDir, string profileName, string dataRoot)
    {
        var target = Path.GetFullPath(dataRoot);
        var parent = Directory.GetParent(baseDir.TrimEnd(Path.DirectorySeparatorChar))?.FullName;
        if (string.IsNullOrWhiteSpace(parent)) return;
        var source = Path.Combine(parent, "dist_v12", "profiles", profileName);
        if (!Directory.Exists(source)) return;
        CopyMissing(source, target);
    }

    static void CopyMissing(string source, string target)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, file);
            var dest = Path.Combine(target, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            if (!File.Exists(dest)) File.Copy(file, dest, false);
        }
    }
}
