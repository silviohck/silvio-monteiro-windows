namespace Orthck.Core;

public sealed record TemplateInfo(string FullPath, string Clinic, string Category, string Subpath, string DisplayName);

/// <summary>Leitura conservadora: pastas indisponíveis geram erro, nunca exclusão de modelos.</summary>
public static class TemplateCatalog
{
    public static IReadOnlyList<TemplateInfo> Scan(string root)
    {
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Pasta dos modelos indisponível: {root}");
        var result = new List<TemplateInfo>();
        foreach (var file in Directory.EnumerateFiles(root, "*.docx", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith("~$", StringComparison.Ordinal) || name.StartsWith('.')) continue;
            var relative = Path.GetRelativePath(root, file);
            var sections = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (sections.Length < 3) continue; // Local / Tipo / Arquivo.docx
            result.Add(new TemplateInfo(file, sections[0], sections[1],
                sections.Length > 3 ? string.Join(" / ", sections.Skip(2).SkipLast(1)) : "",
                Path.GetFileNameWithoutExtension(file)));
        }
        return result.OrderBy(x => x.Clinic).ThenBy(x => x.Category).ThenBy(x => x.Subpath).ThenBy(x => x.DisplayName).ToArray();
    }
}

public static class PathRules
{
    public static string SafeComponent(string input)
    {
        var bad = Path.GetInvalidFileNameChars().ToHashSet();
        var clean = new string(input.Select(c => bad.Contains(c) || c is '/' or '\\' or ':' ? '-' : c).ToArray()).Trim().TrimEnd('.');
        if (clean.Length == 0) return "Sem nome";
        return clean.Length > 100 ? clean[..100] : clean;
    }
    public static string NewDocumentPath(string root, string clinic, string category, string template, string patient, DateTime now)
    {
        var dir = Path.Combine(root, SafeComponent(clinic), SafeComponent(category));
        Directory.CreateDirectory(dir);
        var parts = new[] { template, patient, now.ToString("dd-MM-yyyy") }.Where(x => !string.IsNullOrWhiteSpace(x));
        var basename = SafeComponent(string.Join(" - ", parts));
        var candidate = Path.Combine(dir, basename + ".docx");
        for (var n = 2; File.Exists(candidate); n++) candidate = Path.Combine(dir, $"{basename} ({n}).docx");
        return candidate;
    }
}
