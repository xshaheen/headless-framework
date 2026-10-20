// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FileSignatures;
using FileSignatures.Formats;
using FluentValidation;
using FluentValidation.TestHelper;
using Headless.FluentValidation;
using Headless.IO;
using Headless.Testing.Tests;

namespace Tests;

public sealed class FileValidatorsTests : TestBase
{
    private const int _OneMegabyte = 1024 * 1024;

    private static readonly byte[] _PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00];

    private static readonly IFileFormatInspector _Inspector = new FileFormatInspector();

    [Fact]
    public void should_reject_with_not_empty_code_when_file_has_no_bytes()
    {
        var validator = _Validator(rule => rule.FileNotEmpty());

        var result = validator.TestValidate(new Command(new BytesFile([])));

        result.Errors.Should().ContainSingle().Which.ErrorCode.Should().Be("g:file_not_empty");
    }

    [Fact]
    public async Task should_pass_every_rule_when_file_is_null()
    {
        var validator = _Validator(rule =>
            rule.FileNotEmpty()
                .GreaterThanOrEqualTo(10)
                .LessThanOrEqualTo(20)
                .ContentTypes(["image/png"])
                .HaveSignatures(_Inspector, format => format is Png)
        );

        var result = await validator.TestValidateAsync(new Command(null), cancellationToken: AbortToken);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void should_report_bounds_in_megabytes_when_file_is_below_minimum()
    {
        using var _ = new InvariantCultureScope();
        var validator = _Validator(rule => rule.GreaterThanOrEqualTo(5L * _OneMegabyte));

        var result = validator.TestValidate(new Command(new BytesFile(new byte[_OneMegabyte])));

        var error = result.Errors.Should().ContainSingle().Subject;
        error.ErrorCode.Should().Be("g:file_greater_than_or_equal_to");
        error.FormattedMessagePlaceholderValues["MinSize"].Should().Be("5.0");
        error.FormattedMessagePlaceholderValues["TotalLength"].Should().Be("1.0");
    }

    [Fact]
    public void should_report_bounds_in_megabytes_when_file_exceeds_maximum()
    {
        using var _ = new InvariantCultureScope();
        var validator = _Validator(rule => rule.LessThanOrEqualTo(_OneMegabyte));

        var result = validator.TestValidate(new Command(new BytesFile(new byte[2 * _OneMegabyte])));

        var error = result.Errors.Should().ContainSingle().Subject;
        error.ErrorCode.Should().Be("g:file_less_than_or_equal_to");
        error.FormattedMessagePlaceholderValues["MaxSize"].Should().Be("1.0");
        error.FormattedMessagePlaceholderValues["TotalLength"].Should().Be("2.0");
    }

    [Fact]
    public void should_accept_a_listed_content_type_when_casing_differs()
    {
        var validator = _Validator(rule => rule.ContentTypes(["image/png"]));

        var result = validator.TestValidate(new Command(new BytesFile(_PngBytes, contentType: "IMAGE/PNG")));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void should_list_accepted_types_when_content_type_is_not_listed()
    {
        var validator = _Validator(rule => rule.ContentTypes(["image/png", "image/jpeg"]));

        var result = validator.TestValidate(new Command(new BytesFile(_PngBytes, contentType: "application/pdf")));

        var error = result.Errors.Should().ContainSingle().Subject;
        error.ErrorCode.Should().Be("g:file_unexpected_signature");
        error.FormattedMessagePlaceholderValues["ContentTypes"].Should().Be("'image/png', 'image/jpeg'");
    }

    [Fact]
    public async Task should_accept_when_content_signature_matches()
    {
        var validator = _Validator(rule => rule.HaveSignatures(_Inspector, format => format is Png));

        var result = await validator.TestValidateAsync(
            new Command(new BytesFile(_PngBytes)),
            cancellationToken: AbortToken
        );

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task should_reject_when_content_signature_does_not_match_declared_type()
    {
        var validator = _Validator(rule => rule.HaveSignatures(_Inspector, format => format is Png));
        var notAnImage = new BytesFile("plain text, renamed"u8.ToArray(), contentType: "image/png", fileName: "a.png");

        var result = await validator.TestValidateAsync(new Command(notAnImage), cancellationToken: AbortToken);

        result.Errors.Should().ContainSingle().Which.ErrorCode.Should().Be("g:file_unexpected_signature");
    }

    [Fact]
    public async Task should_leave_the_content_readable_when_signature_rule_has_run()
    {
        var file = new BytesFile(_PngBytes);
        var validator = _Validator(rule => rule.HaveSignatures(_Inspector, format => format is Png));

        await validator.TestValidateAsync(new Command(file), cancellationToken: AbortToken);

        file.Opened.Should().Be(1);
        await using var stream = file.OpenReadStream();
        stream.GetAllBytes().Should().Equal(_PngBytes);
    }

    [Fact]
    public void should_validate_a_property_typed_as_a_concrete_implementation()
    {
        var validator = new InlineValidator<ConcreteCommand>();
        validator.RuleFor(x => x.Avatar).FileNotEmpty();

        var result = validator.TestValidate(new ConcreteCommand(new BytesFile([])));

        result.Errors.Should().ContainSingle().Which.ErrorCode.Should().Be("g:file_not_empty");
    }

    [Fact]
    public void should_expose_size_bounds_and_content_types_to_documentation()
    {
        var validator = _Validator(rule =>
            rule.FileNotEmpty().LessThanOrEqualTo(_OneMegabyte).ContentTypes(["image/png"])
        );

        var rules = validator
            .CreateDescriptor()
            .GetValidatorsForMember(nameof(Command.Avatar))
            .Select(x => x.Validator)
            .ToList();

        rules
            .OfType<IFileSizeValidator>()
            .Select(x => (x.MinBytes, x.MaxBytes))
            .Should()
            .Equal((1L, (long?)null), (null, _OneMegabyte));
        rules.OfType<IFileContentTypeValidator>().Single().ContentTypes.Should().Equal("image/png");
    }

    private static InlineValidator<Command> _Validator(Action<IRuleBuilderInitial<Command, IUploadedFile?>> configure)
    {
        var validator = new InlineValidator<Command>();
        configure(validator.RuleFor(x => x.Avatar));

        return validator;
    }

    private sealed record Command(IUploadedFile? Avatar);

    private sealed record ConcreteCommand(BytesFile? Avatar);

    private sealed class BytesFile(byte[] content, string contentType = "image/png", string fileName = "avatar.png")
        : IUploadedFile
    {
        public int Opened { get; private set; }

        public string FileName => fileName;

        public string ContentType => contentType;

        public long Length => content.Length;

        public Stream OpenReadStream()
        {
            Opened++;

            return new MemoryStream(content, writable: false);
        }
    }

    private sealed class InvariantCultureScope : IDisposable
    {
        private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

        public InvariantCultureScope()
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _culture;
            CultureInfo.CurrentUICulture = _uiCulture;
        }
    }
}
