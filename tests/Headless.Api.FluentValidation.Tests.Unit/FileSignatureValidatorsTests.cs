// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FileSignatures;
using FileSignatures.Formats;
using FluentValidation;
using FluentValidation.TestHelper;
using Headless.IO;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;

namespace Tests;

public sealed class FileSignatureValidatorsTests : TestBase
{
    private static readonly byte[] _PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00];

    private static readonly IFileFormatInspector _Inspector = new FileFormatInspector();

    [Fact]
    public async Task should_pass_when_file_is_null()
    {
        var validator = _Validator(rule => rule.HaveSignatures(_Inspector, format => format is Png));

        var result = await validator.TestValidateAsync(new Command(null), cancellationToken: AbortToken);

        result.IsValid.Should().BeTrue();
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
        var notAnImage = new BytesFile("plain text, renamed"u8.ToArray(), contentType: "image/png");

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
    public async Task should_not_open_the_content_when_validation_is_cancelled()
    {
        var file = new BytesFile(_PngBytes);
        var validator = _Validator(rule => rule.HaveSignatures(_Inspector, format => format is Png));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = () => validator.ValidateAsync(new Command(file), cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        file.Opened.Should().Be(0);
    }

    [Fact]
    public void should_reject_a_null_inspector_when_the_rule_is_built()
    {
        var validator = new InlineValidator<Command>();

        var act = () => validator.RuleFor(x => x.Avatar).HaveSignatures(null!, _ => true);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task should_chain_with_the_base_file_rules()
    {
        var validator = new UpdateAvatarValidator(_Inspector);

        var accepted = await validator.ValidateAsync(new UpdateAvatar(new BytesFile(_PngBytes)), AbortToken);
        var rejected = await validator.ValidateAsync(
            new UpdateAvatar(new BytesFile([0x25, 0x50, 0x44, 0x46])),
            AbortToken
        );

        accepted.IsValid.Should().BeTrue();
        rejected.Errors.Should().ContainSingle().Which.ErrorCode.Should().Be("g:file_unexpected_signature");
    }

    private sealed record UpdateAvatar(IFormFile Avatar);

    private sealed class UpdateAvatarValidator : AbstractValidator<UpdateAvatar>
    {
        public UpdateAvatarValidator(IFileFormatInspector inspector)
        {
            RuleFor(x => x.Avatar)
                .FileNotEmpty()
                .LessThanOrEqualTo(5 * 1024 * 1024)
                .HaveSignatures(inspector, format => format is Jpeg or Png);
        }
    }

    private static InlineValidator<Command> _Validator(Action<IRuleBuilderInitial<Command, IFormFile?>> configure)
    {
        var validator = new InlineValidator<Command>();
        configure(validator.RuleFor(x => x.Avatar));

        return validator;
    }

    private sealed record Command(IFormFile? Avatar);

    // Counts how often the rule opens the content, which FormFile does not expose.
    private sealed class BytesFile(byte[] content, string contentType = "image/png") : IFormFile
    {
        public int Opened { get; private set; }

        public string FileName => "avatar.png";

        public string Name => "Avatar";

        public string ContentType => contentType;

        public string ContentDisposition => "form-data; name=\"Avatar\"; filename=\"avatar.png\"";

        public IHeaderDictionary Headers { get; } = new HeaderDictionary();

        public long Length => content.Length;

        public Stream OpenReadStream()
        {
            Opened++;

            return new MemoryStream(content, writable: false);
        }

        public void CopyTo(Stream target) => target.Write(content);

        public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default) =>
            target.WriteAsync(content, cancellationToken).AsTask();
    }
}
