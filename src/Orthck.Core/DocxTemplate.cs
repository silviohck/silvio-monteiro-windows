using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Orthck.Core;

/// <summary>
/// Processamento OpenXML sem iniciar o Word. Funciona com {{CAMPOS}} partidos entre runs.
/// A substituição mantém o estilo do primeiro run e nunca edita o modelo original.
/// </summary>
public static partial class DocxTemplate
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace XmlNs = XNamespace.Xml;

    [GeneratedRegex(@"\{\{\s*([^{}]+?)\s*\}\}")]
    private static partial Regex Placeholder();

    /// <summary>Campos {{CAMPO}} do documento, do cabeçalho, do rodapé e das notas, na ordem, sem repetição.</summary>
    public static IReadOnlyList<string> Placeholders(string templatePath)
    {
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        using var zip = OpenShared(templatePath);
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

    /// <summary>Gera <paramref name="outputPath"/> a partir do modelo, trocando cada {{CAMPO}} que tem valor; os outros ficam como estão.</summary>
    public static void Fill(string templatePath, string outputPath, IReadOnlyDictionary<string, string> values)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath))!;
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, $".{Guid.NewGuid():N}.docx.tmp");
        try
        {
            using (var source = OpenShared(templatePath))
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
            replacement = Sanitize(replacement);
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
            if (modified[i].Contains(Negrito.Marca) && nodes[i].Parent is { } run && run.Name == W + "r")
                WriteWithBold(nodes[i], run, modified[i]);
            else
                WriteText(nodes[i], modified[i]);
        }
    }

    /// <summary>Texto no w:t; quebra de linha e tabulação viram w:br e w:tab (dentro de w:t o Word não as exibe).
    /// Devolve o último nó escrito.</summary>
    private static XElement WriteText(XElement node, string text)
    {
        var parts = BreakOrTab().Split(text);
        node.Value = parts[0];
        node.SetAttributeValue(XmlNs + "space", "preserve");
        var after = node;
        for (var j = 1; j + 1 < parts.Length; j += 2)
        {
            var separator = new XElement(W + (parts[j] == "\t" ? "tab" : "br"));
            after.AddAfterSelf(separator);
            var nextText = new XElement(W + "t", new XAttribute(XmlNs + "space", "preserve"), parts[j + 1]);
            separator.AddAfterSelf(nextText);
            after = nextText;
        }
        return after;
    }

    /// <summary>
    /// Negrito, como no Mac: o texto entre "**" (ex.: "DOR **INTENSA** À NOITE") vai para um run em negrito com a
    /// mesma formatação do run original; o resto continua em runs iguais ao original. Marcas sem par são removidas.
    /// </summary>
    private static void WriteWithBold(XElement node, XElement run, string text)
    {
        var rPr = run.Element(W + "rPr");
        // O que vinha depois deste texto no run vai para o último run criado.
        var rest = node.NodesAfterSelf().ToList();
        foreach (var n in rest) n.Remove();
        var parts = text.Split(Negrito.Marca);
        var currentRun = run;
        XElement? last = null;
        for (var k = 0; k < parts.Length; k++)
        {
            var bold = k % 2 == 1 && k < parts.Length - (parts.Length % 2 == 0 ? 1 : 0);
            if (bold)
            {
                if (parts[k].Length == 0) continue;
                var boldRun = new XElement(W + "r", BoldRunProperties(rPr));
                currentRun.AddAfterSelf(boldRun);
                var t = new XElement(W + "t");
                boldRun.Add(t);
                WriteText(t, parts[k]);
                var continuation = new XElement(W + "r", rPr == null ? null : new XElement(rPr));
                boldRun.AddAfterSelf(continuation);
                currentRun = continuation;
                last = null;
            }
            else
            {
                if (last == null && currentRun == run) last = WriteText(node, parts[k]);
                else
                {
                    var t = new XElement(W + "t");
                    if (last == null) currentRun.Add(t); else last.AddAfterSelf(t);
                    last = WriteText(t, parts[k]);
                }
            }
        }
        currentRun.Add(rest);
    }

    /// <summary>As mesmas propriedades do run, com negrito (w:b e w:bCs depois de rStyle/rFonts, na ordem do Word).</summary>
    internal static XElement BoldRunProperties(XElement? rPr)
    {
        var bold = rPr == null ? new XElement(W + "rPr") : new XElement(rPr);
        // Tira negrito já existente (ex.: <w:b w:val="0"/>) para não haver dois.
        bold.Elements().Where(e => e.Name == W + "b" || e.Name == W + "bCs").Remove();
        var anchor = bold.Elements().LastOrDefault(e => e.Name == W + "rStyle" || e.Name == W + "rFonts");
        var marks = new object[] { new XElement(W + "b"), new XElement(W + "bCs") };
        if (anchor != null) anchor.AddAfterSelf(marks); else bold.AddFirst(marks);
        return bold;
    }

    [GeneratedRegex(@"(\r\n|\r|\n|\t)")]
    private static partial Regex BreakOrTab();

    /// <summary>
    /// Texto colado do Word/Excel pode trazer caracteres proibidos em XML (ex.: 0x0B, a quebra manual do Word),
    /// que impediam gerar o documento. Quebras viram nova linha; os demais são removidos.
    /// </summary>
    private static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var sb = new System.Text.StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c is '\v' or '\f' or '\u2028' or '\u2029') sb.Append('\n');
            else if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1])) { sb.Append(c).Append(value[++i]); }
            else if (XmlConvert.IsXmlChar(c)) sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Lê o DOCX mesmo aberto no Word (que mantém o arquivo aberto para escrita).</summary>
    private static ZipArchive OpenShared(string path) =>
        new(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete), ZipArchiveMode.Read, leaveOpen: false);

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
