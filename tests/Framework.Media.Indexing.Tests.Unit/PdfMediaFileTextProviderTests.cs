using Framework.Checks;
using Framework.Media.Indexing;

namespace Tests;

public sealed class PdfMediaFileTextProviderTests
{
    [Fact]
    public async Task get_text_async_should_extract_text_from_real_pdf_file()
    {
        // given
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var pdfFilePath = Path.Combine(baseDirectory, @"..\..\..\Files\TestPdf.pdf");

        // when
        await using var fileStream = File.OpenRead(pdfFilePath);
        Argument.CanSeek(fileStream);
        Argument.CanRead(fileStream);
        var extractor = new PdfMediaFileTextProvider();
        var result = await extractor.GetTextAsync(pdfFilePath, fileStream);

        // then
        result.Should().Contain("Test pdf"); // Replace with actual expected content
    }
}
