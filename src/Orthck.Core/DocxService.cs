using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Orthck.Core;

/// <summary>
/// Processamento OpenXML sem iniciar o Word. Funciona com {{CAMPOS}} partidos entre runs.
/// A substituição mantém o estilo do primeiro run e nunca edita o modelo original.
/// </summary>
public static partial class DocxService
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace XmlNs = XNamespace.Xml;

    [GeneratedRegex(@"\{\{\s*([^{}]+?)\s*\}\}")]
    private static partial Regex Placeholder();

    public static IReadOnlyList<string> Fields(string templatePath)
    {
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        using var zip = ZipFile.OpenRead(templatePath);
        EnsureDocx(zip);
        foreach (var entry in zip.Entries.Where(e => IsEditableXml(e.FullName)))
        {
            var doc = ReadXml(entry);
            foreach (var paragraph in doc.Descendants(W + "p"))
            {
                var combined = string.Concat(paragraph.Descendants(W + "t").Select(t => t.Value));
                foreach (Match m in Placeholder().Matches(combined))
                    if (seen.Add(m.Groups[1].Value)) found.Add(m.Groups[1].Value);
            }
        }
        return found;
    }

    public static void Fill(string templatePath, string outputPath, IReadOnlyDictionary<string, string> values)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath))!;
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, $".{Guid.NewGuid():N}.docx.tmp");
        try
        {
            using (var source = ZipFile.OpenRead(templatePath))
            using (var destination = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                EnsureDocx(source);
                foreach (var entry in source.Entries)
                {
                    var target = destination.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                    target.LastWriteTime = entry.LastWriteTime;
                    if (entry.Name.Length == 0) continue;
                    using var input = entry.Open();
                    using var output = target.Open();
                    if (IsEditableXml(entry.FullName))
                    {
                        using var reader = XmlReader.Create(input, SafeXmlSettings());
                        var doc = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
                        foreach (var p in doc.Descendants(W + "p")) ReplaceFields(p, values);
                        using var writer = XmlWriter.Create(output, new XmlWriterSettings { Encoding = new System.Text.UTF8Encoding(false), CloseOutput = false });
                        doc.Save(writer);
                    }
                    else input.CopyTo(output);
                }
            }
            // Verificar antes da publicação: não deixar .docx truncado/ilegível.
            using (var verify = ZipFile.OpenRead(temp)) EnsureDocx(verify);
            if (File.Exists(outputPath)) throw new IOException("Já existe um documento com este nome. O original foi preservado.");
            File.Move(temp, outputPath);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static void ReplaceFields(XElement paragraph, IReadOnlyDictionary<string, string> values)
    {
        var nodes = paragraph.Descendants(W + "t").ToArray();
        if (nodes.Length == 0) return;
        var original = nodes.Select(n => n.Value).ToArray();
        var modified = (string[])original.Clone();
        var start = new int[nodes.Length];
        int count = 0;
        for (var i = 0; i < nodes.Length; i++) { start[i] = count; count += original[i].Length; }
        var combined = string.Concat(original);
        var matches = Placeholder().Matches(combined).Cast<Match>().Reverse();
        foreach (var match in matches)
        {
            if (!values.TryGetValue(match.Groups[1].Value, out var replacement)) continue;
            var begin = match.Index;
            var end = begin + match.Length;
            var first = true;
            for (var i = 0; i < nodes.Length; i++)
            {
                var low = Math.Max(begin, start[i]);
                var high = Math.Min(end, start[i] + original[i].Length);
                if (high <= low) continue;
                var localStart = low - start[i];
                modified[i] = modified[i].Remove(localStart, high - low)
                    .Insert(localStart, first ? replacement ?? "" : "");
                first = false;
            }
        }
        for (var i = 0; i < nodes.Length; i++)
        {
            if (original[i] == modified[i]) continue;
            // A quebra de linha em um campo vira w:br, não um texto invisível ao Word.
            var lines = modified[i].Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            nodes[i].Value = lines[0];
            nodes[i].SetAttributeValue(XmlNs + "space", "preserve");
            XNode after = nodes[i];
            for (var j = 1; j < lines.Length; j++)
            {
                var lineBreak = new XElement(W + "br");
                after.AddAfterSelf(lineBreak);
                var nextText = new XElement(W + "t", new XAttribute(XmlNs + "space", "preserve"), lines[j]);
                lineBreak.AddAfterSelf(nextText);
                after = nextText;
            }
        }
    }

    private static XmlReaderSettings SafeXmlSettings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, CloseInput = false
    };
    private static XDocument ReadXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, SafeXmlSettings());
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }
    private static bool IsEditableXml(string name) =>
        name.Equals("word/document.xml", StringComparison.OrdinalIgnoreCase) ||
        Regex.IsMatch(name, @"^word/(header|footer)\d+\.xml$", RegexOptions.IgnoreCase) ||
        name.Equals("word/footnotes.xml", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("word/endnotes.xml", StringComparison.OrdinalIgnoreCase);
    private static void EnsureDocx(ZipArchive zip)
    {
        if (zip.GetEntry("[Content_Types].xml") == null || zip.GetEntry("word/document.xml") == null)
            throw new InvalidDataException("Arquivo não é um DOCX válido ou está incompleto.");
    }
}
