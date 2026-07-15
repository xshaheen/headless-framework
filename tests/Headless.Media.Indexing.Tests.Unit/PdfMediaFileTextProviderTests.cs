using Headless.Media.Indexing;
using Headless.Testing.Tests;

namespace Tests;

public sealed class PdfMediaFileTextProviderTests : TestBase
{
    private readonly PdfMediaFileTextProvider _sut = new();

    [Fact]
    public async Task should_extract_text_from_real_pdf_file_when_get_text_async()
    {
        // given
        var separator = Path.DirectorySeparatorChar;
        var pdfFilePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            $"..{separator}..{separator}..{separator}Files{separator}TestPdf.pdf"
        );
        await using var fileStream = File.OpenRead(pdfFilePath);

        // when
        var result = await _sut.GetTextAsync(fileStream, AbortToken);

        // then
        await Verify(result).UseDirectory("Snapshots");
    }
}
