using System;
using System.IO;

namespace WorldSphereMod.Bridge
{
    public static class SandboxSavePath
    {
        public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "agents", "sandbox");

        public static string ResolveNew(string? requested, string root)
        {
            root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(requested))
                requested = Path.Combine(root, "worldspheremod-saves", "save-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
            foreach (string raw in requested.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                if (raw.Length > 0 && raw != "." && raw != ".." && raw.TrimEnd('.', ' ') != raw)
                    throw new ArgumentException("Invalid save directory component.");
            string path = Path.GetFullPath(requested).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, comparison)) throw new ArgumentException("Save must be inside agents/sandbox, not its root.");
            string relative = path.Substring(root.Length + 1);
            foreach (string part in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            {
                if (part.Length == 0 || part.TrimEnd('.', ' ') != part || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    throw new ArgumentException("Invalid save directory component.");
                string stem = part.Split('.')[0].ToUpperInvariant();
                if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" ||
                    (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] >= '1' && stem[3] <= '9'))
                    throw new ArgumentException("Reserved save directory name.");
            }
            if (Directory.Exists(path) || File.Exists(path)) throw new IOException("Save destination already exists; overwriting is refused.");
            DirectoryInfo? ancestor = new DirectoryInfo(Path.GetDirectoryName(path)!);
            while (ancestor != null)
            {
                if (ancestor.Exists && (ancestor.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked save ancestors are refused.");
                ancestor = ancestor.Parent;
            }
            return path;
        }
    }
}
