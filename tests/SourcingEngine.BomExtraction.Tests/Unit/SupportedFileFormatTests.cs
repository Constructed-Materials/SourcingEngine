using SourcingEngine.BomExtraction.Models;

namespace SourcingEngine.BomExtraction.Tests.Unit;

public class SupportedFileFormatTests
{
    [Theory]
    [InlineData(".pdf", SupportedFileFormat.Pdf)]
    [InlineData(".csv", SupportedFileFormat.Csv)]
    [InlineData(".xlsx", SupportedFileFormat.Xlsx)]
    [InlineData(".xls", SupportedFileFormat.Xls)]
    [InlineData(".doc", SupportedFileFormat.Doc)]
    [InlineData(".docx", SupportedFileFormat.Docx)]
    [InlineData(".html", SupportedFileFormat.Html)]
    [InlineData(".htm", SupportedFileFormat.Html)]
    [InlineData(".txt", SupportedFileFormat.Txt)]
    [InlineData(".md", SupportedFileFormat.Md)]
    public void TryFromExtension_SupportedExtension_ReturnsTrue(string extension, SupportedFileFormat expected)
    {
        var result = SupportedFileFormatExtensions.TryFromExtension(extension, out var format);

        Assert.True(result);
        Assert.Equal(expected, format);
    }

    [Theory]
    [InlineData(".PDF")]
    [InlineData(".Csv")]
    [InlineData(".XLSX")]
    public void TryFromExtension_CaseInsensitive_ReturnsTrue(string extension)
    {
        var result = SupportedFileFormatExtensions.TryFromExtension(extension, out _);
        Assert.True(result);
    }

    [Theory]
    [InlineData(".zip")]
    [InlineData(".jpg")]
    [InlineData(".exe")]
    [InlineData("")]
    public void TryFromExtension_UnsupportedExtension_ReturnsFalse(string extension)
    {
        var result = SupportedFileFormatExtensions.TryFromExtension(extension, out _);
        Assert.False(result);
    }

    [Theory]
    [InlineData(SupportedFileFormat.Pdf, "pdf")]
    [InlineData(SupportedFileFormat.Csv, "csv")]
    [InlineData(SupportedFileFormat.Xlsx, "xlsx")]
    [InlineData(SupportedFileFormat.Xls, "xls")]
    [InlineData(SupportedFileFormat.Doc, "doc")]
    [InlineData(SupportedFileFormat.Docx, "docx")]
    [InlineData(SupportedFileFormat.Html, "html")]
    [InlineData(SupportedFileFormat.Txt, "txt")]
    [InlineData(SupportedFileFormat.Md, "md")]
    public void ToBedrockFormat_AllFormats_ReturnCorrectString(SupportedFileFormat format, string expected)
    {
        Assert.Equal(expected, format.ToBedrockFormat());
    }

    [Fact]
    public void AllSupportedExtensions_ContainsExpectedCount()
    {
        // .pdf, .csv, .doc, .docx, .xls, .xlsx, .html, .htm, .txt, .md = 10
        Assert.Equal(10, SupportedFileFormatExtensions.AllSupportedExtensions.Count);
    }
}
