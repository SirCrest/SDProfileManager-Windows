namespace SDProfileManager.Helpers;

public static class FileHelper
{
    public static string? ResolveCaseInsensitivePath(string basePath, string relativePath)
    {
        var current = basePath;
        var parts = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            var direct = Path.Combine(current, part);
            if (File.Exists(direct) || Directory.Exists(direct))
            {
                current = direct;
                continue;
            }

            if (!Directory.Exists(current))
                return null;

            var match = Directory.EnumerateFileSystemEntries(current)
                .FirstOrDefault(e => string.Equals(Path.GetFileName(e), part, StringComparison.OrdinalIgnoreCase));

            if (match is null)
                return null;

            current = match;
        }

        return current;
    }

    public static void CopyDirectoryRecursive(string source, string destination)
    {
        if (!Directory.Exists(source)) return;
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.GetFiles(source))
        {
            var destFile = Path.Combine(destination, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (var dir in Directory.GetDirectories(source))
        {
            var destDir = Path.Combine(destination, Path.GetFileName(dir));
            CopyDirectoryRecursive(dir, destDir);
        }
    }

    public static void MergeDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.GetFiles(source))
        {
            var destFile = Path.Combine(destination, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (var dir in Directory.GetDirectories(source))
        {
            var destDir = Path.Combine(destination, Path.GetFileName(dir));
            MergeDirectory(dir, destDir);
        }
    }
}
