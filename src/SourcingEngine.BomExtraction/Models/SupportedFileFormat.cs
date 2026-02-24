namespace SourcingEngine.BomExtraction.Models;

/// <summary>
/// Supported file formats for the Bedrock Converse API DocumentBlock.
/// Values match the API's DocumentFormat enum.
/// </summary>
public enum SupportedFileFormat
{
    Pdf,
    Csv,
    Doc,
    Docx,
    Xls,
    Xlsx,
    Html,
    Txt,
    Md
}

/// <summary>
/// Extension methods for <see cref="SupportedFileFormat"/>.
/// </summary>
public static class SupportedFileFormatExtensions
{
    private static readonly Dictionary<string, SupportedFileFormat> ExtensionMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = SupportedFileFormat.Pdf,
        [".csv"] = SupportedFileFormat.Csv,
        [".doc"] = SupportedFileFormat.Doc,
        [".docx"] = SupportedFileFormat.Docx,
        [".xls"] = SupportedFileFormat.Xls,
        [".xlsx"] = SupportedFileFormat.Xlsx,
        [".html"] = SupportedFileFormat.Html,
        [".htm"] = SupportedFileFormat.Html,
        [".txt"] = SupportedFileFormat.Txt,
        [".md"] = SupportedFileFormat.Md,
    };

    /// <summary>
    /// Try to determine the <see cref="SupportedFileFormat"/> from a file extension.
    /// </summary>
    /// <param name="extension">File extension including the leading dot (e.g. ".pdf").</param>
    /// <param name="format">The resolved format, if successful.</param>
    /// <returns>True if the extension is supported.</returns>
    public static bool TryFromExtension(string extension, out SupportedFileFormat format)
    {
        return ExtensionMap.TryGetValue(extension, out format);
    }

    /// <summary>
    /// Get the Bedrock Converse API document format string for this format.
    /// </summary>
    public static string ToBedrockFormat(this SupportedFileFormat format) => format switch
    {
        SupportedFileFormat.Pdf => "pdf",
        SupportedFileFormat.Csv => "csv",
        SupportedFileFormat.Doc => "doc",
        SupportedFileFormat.Docx => "docx",
        SupportedFileFormat.Xls => "xls",
        SupportedFileFormat.Xlsx => "xlsx",
        SupportedFileFormat.Html => "html",
        SupportedFileFormat.Txt => "txt",
        SupportedFileFormat.Md => "md",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported format")
    };

    /// <summary>
    /// Returns all supported file extensions (with leading dot).
    /// </summary>
    public static IReadOnlyCollection<string> AllSupportedExtensions => ExtensionMap.Keys;
}
