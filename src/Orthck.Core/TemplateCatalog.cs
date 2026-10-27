using System.Globalization;

namespace Orthck.Core;

/// <param name="Clinic">Pasta do local; vazio = pasta compartilhada, vale para todos os locais.</param>
public sealed record TemplateInfo(string FullPath, string Clinic, string Category, string Subpath, string DisplayName)
{
    public string ClinicLabel => Clinic.Length == 0 ? "Todos os locais" : Clinic;
    public bool AppliesTo(string? clinic) => Clinic.Length == 0 || TemplateCatalog.SameName(Clinic, clinic);
}

/// <summary>Leitura conservadora: pastas indisponíveis geram erro, nunca exclusão de modelos.</summary>
public static class TemplateCatalog
{
    /// <summary>Como no Mac: sem diferenciar maiúsculas e acentos ("1 - Apac" = "1 - APAC").</summary>
    public static bool SameName(string? a, string? b) =>
        string.Compare(a, b, CultureInfo.InvariantCulture, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) == 0;

    /// <param name="ignored">Subpastas principais que não são locais (pastasIgnoradas do Mac).</param>
    /// <param name="shared">Subpastas principais que valem para todos os locais (pastasCompartilhadas do Mac).</param>
    public static IReadOnlyList<TemplateInfo> Scan(string root, IEnumerable<string>? ignored = null, IEnumerable<string>? shared = null)
    {
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Pasta dos modelos indisponível: {root}");
        var ignoredSet = (ignored ?? []).ToArray();
        var sharedSet = (shared ?? []).ToArray();
        // Uma subpasta sem permissão de leitura não pode esconder todos os outros modelos.
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true, IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
        };
        var result = new List<TemplateInfo>();
        foreach (var file in Directory.EnumerateFiles(root, "*.docx", options))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith("~$", StringComparison.Ordinal) || name.StartsWith('.')) continue;
            var relative = Path.GetRelativePath(root, file);
            var sections = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (sections.Length < 2 || ignoredSet.Any(x => x == sections[0])) continue;
            var display = Path.GetFileNameWithoutExtension(file);
            if (sharedSet.Any(x => SameName(x, sections[0])))
            {
                // Pasta compartilhada / [Subpastas /] Arquivo.docx: o nome da pasta vira o tipo, em todos os locais.
                result.Add(new TemplateInfo(file, "", sections[0], string.Join(" / ", sections.Skip(1).SkipLast(1)), display));
                continue;
            }
            if (sections.Length < 3) continue; // Local / Tipo / Arquivo.docx
            result.Add(new TemplateInfo(file, sections[0], sections[1],
                sections.Length > 3 ? string.Join(" / ", sections.Skip(2).SkipLast(1)) : "", display));
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
        if (clean.Length > 100) clean = clean[..100].TrimEnd(' ', '.');
        // CON, PRN, NUL, COM1… são nomes reservados no Windows, mesmo com extensão.
        var stem = clean.Split('.')[0].Trim().ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3])))
            clean = "_" + clean;
        return clean;
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
