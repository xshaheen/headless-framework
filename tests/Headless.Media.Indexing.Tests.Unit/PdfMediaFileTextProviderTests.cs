using Headless.Media.Indexing;

namespace Tests;

public sealed class PdfMediaFileTextProviderTests
{
    private readonly PdfMediaFileTextProvider _sut = new();

    [Fact]
    public async Task get_text_async_should_extract_text_from_real_pdf_file()
    {
        // given
        var separator = Path.DirectorySeparatorChar;
        var pdfFilePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            $"..{separator}..{separator}..{separator}Files{separator}TestPdf.pdf"
        );
        await using var fileStream = File.OpenRead(pdfFilePath);

        // when
        var result = await _sut.GetTextAsync(fileStream);

        // then
        await Verify(result).UseDirectory("Snapshots");
    }
}
