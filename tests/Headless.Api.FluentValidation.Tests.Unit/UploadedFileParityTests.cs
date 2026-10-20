// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FileSignatures;
using FileSignatures.Formats;
using FluentValidation;
using FluentValidation.TestHelper;
using Headless.IO;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;

namespace Tests;

public sealed class UploadedFileParityTests : TestBase
{
    private static readonly IFileFormatInspector _Inspector = new FileFormatInspector();

    [Fact]
    public void should_read_through_to_the_form_file_when_adapted()
    {
        var formFile = _FormFile([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], "image/png", "avatar.png");

        var uploaded = formFile.ToUploadedFile();

        uploaded.FileName.Should().Be("avatar.png");
        uploaded.ContentType.Should().Be("image/png");
        uploaded.Length.Should().Be(8);
        using var stream = uploaded.OpenReadStream();
        stream.GetAllBytes().Should().Equal(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);
    }

    [Fact]
    public async Task should_report_the_same_errors_when_the_same_file_arrives_as_form_file_or_uploaded_file()
    {
        var formFile = _FormFile("not an image"u8.ToArray(), "application/pdf", "avatar.png");

        var formValidator = new InlineValidator<FormRequest>();
        formValidator
            .RuleFor(x => x.Avatar)
            .LessThanOrEqualTo(4)
            .ContentTypes(["image/png"])
            .HaveSignatures(_Inspector, format => format is Png);

        var commandValidator = new InlineValidator<Command>();
        commandValidator
            .RuleFor(x => x.Avatar)
            .LessThanOrEqualTo(4)
            .ContentTypes(["image/png"])
            .HaveSignatures(_Inspector, format => format is Png);

        var formResult = await formValidator.TestValidateAsync(
            new FormRequest(formFile),
            cancellationToken: AbortToken
        );
        var commandResult = await commandValidator.TestValidateAsync(
            new Command(formFile.ToUploadedFile()),
            cancellationToken: AbortToken
        );

        var formErrors = formResult.Errors.ConvertAll(x => (x.ErrorCode, x.ErrorMessage));
        var commandErrors = commandResult.Errors.ConvertAll(x => (x.ErrorCode, x.ErrorMessage));

        formErrors.Should().HaveCount(3);
        commandErrors.Should().Equal(formErrors);
    }

    [Fact]
    public void should_throw_when_adapting_a_null_form_file()
    {
        IFormFile formFile = null!;

        var act = () => formFile.ToUploadedFile();

        act.Should().Throw<ArgumentNullException>();
    }

    private static FormFile _FormFile(byte[] content, string contentType, string fileName)
    {
        return new FormFile(new MemoryStream(content), 0, content.Length, "avatar", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
    }

    private sealed record FormRequest(IFormFile? Avatar);

    private sealed record Command(IUploadedFile? Avatar);
}
