// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using FileSignatures;
using FluentValidation;
using FluentValidation.TestHelper;
using Framework.Testing.Tests;
using Microsoft.AspNetCore.Http;

namespace Tests;

public sealed class FileValidatorsTests : TestBase
{
    private const int _MinimalFileSize = 512;
    private const int _MaximalFileSize = 2048;
    private const int _OneMegabyte = 1024 * 1024;
    private const int _FiveMegabytes = 5 * _OneMegabyte;

    private static readonly byte[] _FileSignatureBytes =
    [
        0x25,
        0xE2,
        0xE3,
        0xCF,
        0xD3,
        0x11,
        0x62,
        0x61,
        0x20,
        0xA0,
        0xC9,
        0x6,
        0xE,
        0x0,
        0x25,
        0x5,
    ];

    [Fact]
    public void file_not_empty_should_pass_validation()
    {
        // given
        FileNotEmptyWithSpecificMinimumSizeValidator validator = new();
        var file = Substitute.For<IFormFile>();
        file.Length.Returns(_MinimalFileSize);

        var model = new FileUploadTestModel { UploadedFile = file };

        // when
        var result = validator.TestValidate(model);

        // then
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.ShouldNotHaveValidationErrorFor(x => x.UploadedFile);
    }

    [Fact]
    public void empty_file_should_not_pass_validation()
    {
        // given
        FileNotEmptyWithSpecificMinimumSizeValidator validator = new();
        var file = Substitute.For<IFormFile>();
        file.Length.Returns(0);

        var model = new FileUploadTestModel { UploadedFile = file };

        // when
        var result = validator.TestValidate(model);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
        result.ShouldHaveValidationErrorFor(x => x.UploadedFile);
    }

    [Fact]
    public void file_with_less_than_minimal_size_should_not_pass_validation()
    {
        // given
        FileNotEmptyWithSpecificMinimumSizeValidator validator = new();
        var file = Substitute.For<IFormFile>();
        file.Length.Returns(_MinimalFileSize - 1);

        var model = new FileUploadTestModel { UploadedFile = file };

        // when
        var result = validator.TestValidate(model);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
        result.ShouldHaveValidationErrorFor(x => x.UploadedFile);
    }

    [Fact]
    public void file_greater_than_minimal_size_should_pass_validation()
    {
        // given
        FileNotEmptyWithSpecificMinimumSizeValidator validator = new();
        var file = Substitute.For<IFormFile>();
        file.Length.Returns(_MinimalFileSize + 1);

        var model = new FileUploadTestModel { UploadedFile = file };

        // when
        var result = validator.TestValidate(model);

        // then
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.ShouldNotHaveValidationErrorFor(x => x.UploadedFile);
    }

    [Fact]
    public void file_equal_to_minimal_size_should_pass_validation()
    {
        // given
        FileNotEmptyWithSpecificMinimumSizeValidator validator = new();
        var file = Substitute.For<IFormFile>();
        file.Length.Returns(_MinimalFileSize);

        var model = new FileUploadTestModel { UploadedFile = file };

        // when
        var result = validator.TestValidate(model);

        // then
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.ShouldNotHaveValidationErrorFor(x => x.UploadedFile);
    }

    [Fact]
    public void file_with_greater_than_maximal_size_should_not_pass_validation()
    {
        // given
        FileNotEmptyWithSpecificMaximumSizeValidator validator = new();
        var file = Substitute.For<IFormFile>();
        file.Length.Returns(_MaximalFileSize + 1);

        var model = new FileUploadTestModel { UploadedFile = file };

        // when
        var result = validator.TestValidate(model);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
        result.ShouldHaveValidationErrorFor(x => x.UploadedFile);
    }

    [Fact]
    public void file_within_maximal_size_should_pass_validation()
    {
        // given
        FileNotEmptyWithSpecificMaximumSizeValidator validator = new();
        var file = Substitute.For<IFormFile>();
        file.Length.Returns(_MaximalFileSize - 1);

        var model = new FileUploadTestModel { UploadedFile = file };

        // when
        var result = validator.TestValidate(model);

        // then
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.ShouldNotHaveValidationErrorFor(x => x.UploadedFile);
    }

    [Fact]
    public void file_equal_to_maximal_size_should_pass_validation()
    {
        // given
        FileNotEmptyWithSpecificMaximumSizeValidator validator = new();
        var file = Substitute.For<IFormFile>();
        file.Length.Returns(_MaximalFileSize);

        var model = new FileUploadTestModel { UploadedFile = file };

        // when
        var result = validator.TestValidate(model);

        // then
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.ShouldNotHaveValidationErrorFor(x => x.UploadedFile);
    }

    [Fact]
    public void file_with_valid_content_type_should_pass()
    {
        // given
        var validator = new FileContentUploadValidator();
        var file = Substitute.For<IFormFile>();
        file.ContentType.Returns("image/jpeg");

        var model = new FileUploadTestModel { UploadedFile = file };

        // when
        var result = validator.TestValidate(model);

        // then
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.ShouldNotHaveValidationErrorFor(x => x.UploadedFile);
    }

    [Fact]
    public void file_with_invalid_content_type_should_not_pass()
    {
        // given
        var validator = new FileContentUploadValidator();
        var file = Substitute.For<IFormFile>();
        file.ContentType.Returns("application/pdf");

        var model = new FileUploadTestModel { UploadedFile = file };

        // when
        var result = validator.TestValidate(model);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
        result.ShouldHaveValidationErrorFor(x => x.UploadedFile);
    }

    [Fact]
    public void null_file_should_pass_content_validation()
    {
        // given
        var validator = new FileContentUploadValidator();
        IFormFile? file = null;

        var model = new FileUploadTestModel { UploadedFile = file };

        // when
        var result = validator.TestValidate(model);

        // then
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.ShouldNotHaveValidationErrorFor(x => x.UploadedFile);
    }

    [Fact]
    public async Task file_with_valid_signature_should_pass()
    {
        // given
        var fileInspectorMock = Substitute.For<IFileFormatInspector>();
        FileSignatureUploadValidator validator = new(fileInspectorMock);

        var fileStream = new MemoryStream(_FileSignatureBytes);
        var mockFile = Substitute.For<IFormFile>();
        mockFile.OpenReadStream().Returns(fileStream);
        var fileFormat = new TestFileFormat(_FileSignatureBytes);
        fileInspectorMock.DetermineFileFormat(Arg.Any<Stream>()).Returns(fileFormat);

        var model = new FileUploadTestModel { UploadedFile = mockFile };

        // when
        var result = await validator.TestValidateAsync(model, cancellationToken: AbortToken);

        // then
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.ShouldNotHaveValidationErrorFor(x => x.UploadedFile);
    }

    [Fact]
    public async Task file_with_invalid_signature_should_not_pass()
    {
        // given
        var fileInspectorMock = Substitute.For<IFileFormatInspector>();
        FileSignatureUploadValidator validator = new(fileInspectorMock);

        var fileStream = new MemoryStream(_FileSignatureBytes);
        var mockFile = Substitute.For<IFormFile>();
        mockFile.OpenReadStream().Returns(fileStream);
        var fileFormat = new TestFileFormat(_FileSignatureBytes.Where((_, i) => i != 2).ToArray());
        fileInspectorMock.DetermineFileFormat(Arg.Any<Stream>()).Returns(fileFormat);

        var model = new FileUploadTestModel { UploadedFile = mockFile };

        // when
        var result = await validator.TestValidateAsync(model, cancellationToken: AbortToken);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
        result.ShouldHaveValidationErrorFor(x => x.UploadedFile);
    }

    #region Null File Handling Tests

    [Fact]
    public void file_not_empty_should_pass_when_file_null()
    {
        // given
        FileNotEmptyValidator validator = new();
        var model = new FileUploadTestModel { UploadedFile = null };

        // when
        var result = validator.TestValidate(model);

        // then
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void greater_than_or_equal_to_should_pass_when_file_null()
    {
        // given
        FileNotEmptyWithSpecificMinimumSizeValidator validator = new();
        var model = new FileUploadTestModel { UploadedFile = null };

        // when
        var result = validator.TestValidate(model);

        // then
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void less_than_or_equal_to_should_pass_when_file_null()
    {
        // given
        FileNotEmptyWithSpecificMaximumSizeValidator validator = new();
        var model = new FileUploadTestModel { UploadedFile = null };

        // when
        var result = validator.TestValidate(model);

        // then
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task have_signatures_should_pass_when_file_null()
    {
        // given
        var fileInspectorMock = Substitute.For<IFileFormatInspector>();
        FileSignatureUploadValidator validator = new(fileInspectorMock);
        var model = new FileUploadTestModel { UploadedFile = null };

        // when
        var result = await validator.TestValidateAsync(model, cancellationToken: AbortToken);

        // then
        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region FileNotEmpty Edge Cases

    [Fact]
    public void file_not_empty_should_pass_when_file_length_one()
    {
        // given
        FileNotEmptyValidator validator = new();
        var file = Substitute.For<IFormFile>();
        file.Length.Returns(1);

        var model = new FileUploadTestModel { UploadedFile = file };

        // when
        var result = validator.TestValidate(model);

        // then
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void file_not_empty_should_fail_with_correct_error_descriptor()
    {
        // given
        FileNotEmptyValidator validator = new();
        var file = Substitute.For<IFormFile>();
        file.Length.Returns(0);

        var model = new FileUploadTestModel { UploadedFile = file };

        // when
        var result = validator.TestValidate(model);

        // then
        result.Errors.Should().ContainSingle().Which.ErrorCode.Should().Be("file:not_empty");
    }

    #endregion

    #region GreaterThanOrEqualTo Message Formatting Tests

    [Fact]
    public void greater_than_or_equal_to_should_format_message_in_megabytes()
    {
        // given
        FileSizeMinimumValidator validator = new(_FiveMegabytes);
        var file = Substitute.For<IFormFile>();
        file.Length.Returns(_OneMegabyte);

        var model = new FileUploadTestModel { UploadedFile = file };

        // when
        var result = validator.TestValidate(model);

        // then
        var error = result.Errors.Should().ContainSingle().Subject;
        error.FormattedMessagePlaceholderValues.Should().ContainKey("MinSize");
        error.FormattedMessagePlaceholderValues.Should().ContainKey("TotalLength");
    }

    [Fact]
    public void greater_than_or_equal_to_should_format_values_correctly()
    {
        // given
        using var _ = new CultureScope(CultureInfo.InvariantCulture);
        FileSizeMinimumValidator validator = new(_FiveMegabytes);
        var file = Substitute.For<IFormFile>();
        file.Length.Returns(_OneMegabyte);

        var model = new FileUploadTestModel { UploadedFile = file };

        // when
        var result = validator.TestValidate(model);

        // then
        var error = result.Errors.Should().ContainSingle().Subject;
        error.FormattedMessagePlaceholderValues["MinSize"].Should().Be("5.0");
        error.FormattedMessagePlaceholderValues["TotalLength"].Should().Be("1.0");
    }

    #endregion

    #region LessThanOrEqualTo Message Formatting Tests

    [Fact]
    public void less_than_or_equal_to_should_format_message_in_megabytes()
    {
        // given
        using var _ = new CultureScope(CultureInfo.InvariantCulture);
        FileSizeMaximumValidator validator = new(_OneMegabyte);
        var file = Substitute.For<IFormFile>();
        file.Length.Returns(_FiveMegabytes);

        var model = new FileUploadTestModel { UploadedFile = file };

        // when
        var result = validator.TestValidate(model);

        // then
        var error = result.Errors.Should().ContainSingle().Subject;
        error.FormattedMessagePlaceholderValues["MaxSize"].Should().Be("1.0");
        error.FormattedMessagePlaceholderValues["TotalLength"].Should().Be("5.0");
    }

    #endregion

    #region ContentTypes Edge Cases

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("IMAGE/JPEG")]
    [InlineData("Image/Jpeg")]
    public void content_types_should_be_case_insensitive(string contentType)
    {
        // given
        var validator = new FileContentUploadValidator();
        var file = Substitute.For<IFormFile>();
        file.ContentType.Returns(contentType);

        var model = new FileUploadTestModel { UploadedFile = file };

        // when
        var result = validator.TestValidate(model);

        // then
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void content_types_should_work_with_single_content_type()
    {
        // given
        var validator = new SingleContentTypeValidator();
        var file = Substitute.For<IFormFile>();
        file.ContentType.Returns("application/pdf");

        var model = new FileUploadTestModel { UploadedFile = file };

        // when
        var result = validator.TestValidate(model);

        // then
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void content_types_should_format_error_message_with_all_types()
    {
        // given
        var validator = new FileContentUploadValidator();
        var file = Substitute.For<IFormFile>();
        file.ContentType.Returns("application/pdf");

        var model = new FileUploadTestModel { UploadedFile = file };

        // when
        var result = validator.TestValidate(model);

        // then
        var error = result.Errors.Should().ContainSingle().Subject;
        error.FormattedMessagePlaceholderValues.Should().ContainKey("ContentTypes");
    }

    #endregion

    #region HaveSignatures Edge Cases

    [Fact]
    public async Task have_signatures_should_pass_when_predicate_returns_true_for_null_format()
    {
        // given
        var fileInspectorMock = Substitute.For<IFileFormatInspector>();
        // Inspector returns null for unknown file
        fileInspectorMock.DetermineFileFormat(Arg.Any<Stream>()).Returns((FileFormat?)null);

        // Validator accepts null format (unknown files allowed)
        FileSignatureAcceptNullValidator validator = new(fileInspectorMock);

        var fileStream = new MemoryStream([0x00, 0x01, 0x02]);
        var mockFile = Substitute.For<IFormFile>();
        mockFile.OpenReadStream().Returns(fileStream);

        var model = new FileUploadTestModel { UploadedFile = mockFile };

        // when
        var result = await validator.TestValidateAsync(model, cancellationToken: AbortToken);

        // then
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task have_signatures_should_fail_when_predicate_returns_false_for_null_format()
    {
        // given
        var fileInspectorMock = Substitute.For<IFileFormatInspector>();
        // Inspector returns null for unknown file
        fileInspectorMock.DetermineFileFormat(Arg.Any<Stream>()).Returns((FileFormat?)null);

        // Validator rejects null format (unknown files not allowed)
        FileSignatureRejectNullValidator validator = new(fileInspectorMock);

        var fileStream = new MemoryStream([0x00, 0x01, 0x02]);
        var mockFile = Substitute.For<IFormFile>();
        mockFile.OpenReadStream().Returns(fileStream);

        var model = new FileUploadTestModel { UploadedFile = mockFile };

        // when
        var result = await validator.TestValidateAsync(model, cancellationToken: AbortToken);

        // then
        result.IsValid.Should().BeFalse();
        result.ShouldHaveValidationErrorFor(x => x.UploadedFile);
    }

    #endregion

    #region Helper Classes

    private sealed class FileUploadTestModel
    {
        public IFormFile? UploadedFile { get; init; }
    }

    private sealed class TestFileFormat(byte[] signature) : FileFormat(signature, "application/test", ".test");

    /// <summary>
    /// Sets the current culture temporarily for testing culture-sensitive formatting.
    /// </summary>
    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _originalCulture;
        private readonly CultureInfo _originalUiCulture;

        public CultureScope(CultureInfo culture)
        {
            _originalCulture = CultureInfo.CurrentCulture;
            _originalUiCulture = CultureInfo.CurrentUICulture;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _originalCulture;
            CultureInfo.CurrentUICulture = _originalUiCulture;
        }
    }

    #endregion

    #region Helper Validator Classes

    private sealed class FileNotEmptyValidator : AbstractValidator<FileUploadTestModel>
    {
        public FileNotEmptyValidator()
        {
            RuleFor(x => x.UploadedFile).FileNotEmpty();
        }
    }

    private sealed class FileNotEmptyWithSpecificMinimumSizeValidator : AbstractValidator<FileUploadTestModel>
    {
        public FileNotEmptyWithSpecificMinimumSizeValidator()
        {
            RuleFor(x => x.UploadedFile).FileNotEmpty().GreaterThanOrEqualTo(_MinimalFileSize);
        }
    }

    private sealed class FileNotEmptyWithSpecificMaximumSizeValidator : AbstractValidator<FileUploadTestModel>
    {
        public FileNotEmptyWithSpecificMaximumSizeValidator()
        {
            RuleFor(x => x.UploadedFile).FileNotEmpty().LessThanOrEqualTo(_MaximalFileSize);
        }
    }

    private sealed class FileSizeMinimumValidator : AbstractValidator<FileUploadTestModel>
    {
        public FileSizeMinimumValidator(int minBytes)
        {
            RuleFor(x => x.UploadedFile).GreaterThanOrEqualTo(minBytes);
        }
    }

    private sealed class FileSizeMaximumValidator : AbstractValidator<FileUploadTestModel>
    {
        public FileSizeMaximumValidator(int maxBytes)
        {
            RuleFor(x => x.UploadedFile).LessThanOrEqualTo(maxBytes);
        }
    }

    private sealed class FileContentUploadValidator : AbstractValidator<FileUploadTestModel>
    {
        public FileContentUploadValidator()
        {
            RuleFor(x => x.UploadedFile).ContentTypes(["image/jpeg", "image/png"]);
        }
    }

    private sealed class SingleContentTypeValidator : AbstractValidator<FileUploadTestModel>
    {
        public SingleContentTypeValidator()
        {
            RuleFor(x => x.UploadedFile).ContentTypes(["application/pdf"]);
        }
    }

    private sealed class FileSignatureUploadValidator : AbstractValidator<FileUploadTestModel>
    {
        public FileSignatureUploadValidator(IFileFormatInspector inspector)
        {
            RuleFor(x => x.UploadedFile)
                .HaveSignatures(
                    inspector,
                    format => format?.Signature.ToList().SequenceEqual(_FileSignatureBytes) == true
                );
        }
    }

    private sealed class FileSignatureAcceptNullValidator : AbstractValidator<FileUploadTestModel>
    {
        public FileSignatureAcceptNullValidator(IFileFormatInspector inspector)
        {
            // Accept null format (unknown files allowed)
            RuleFor(x => x.UploadedFile).HaveSignatures(inspector, _ => true);
        }
    }

    private sealed class FileSignatureRejectNullValidator : AbstractValidator<FileUploadTestModel>
    {
        public FileSignatureRejectNullValidator(IFileFormatInspector inspector)
        {
            // Reject null format (require known file types)
            RuleFor(x => x.UploadedFile).HaveSignatures(inspector, format => format is not null);
        }
    }

    #endregion
}
