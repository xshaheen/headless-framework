// Copyright (c) Mahmoud Shaheen. All rights reserved.

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

    #region Helper Classes

    private sealed class FileUploadTestModel
    {
        public IFormFile? UploadedFile { get; init; }
    }

    private sealed class TestFileFormat(byte[] signature) : FileFormat(signature, "application/test", ".test");

    #endregion

    #region Helper Validator Classes

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

    private sealed class FileContentUploadValidator : AbstractValidator<FileUploadTestModel>
    {
        public FileContentUploadValidator()
        {
            RuleFor(x => x.UploadedFile).ContentTypes(["image/jpeg", "image/png"]);
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

    #endregion
}
