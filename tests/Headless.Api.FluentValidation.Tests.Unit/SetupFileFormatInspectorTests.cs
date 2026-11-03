// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FileSignatures;
using FileSignatures.Formats;
using FluentValidation;
using Headless.FluentValidation;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class SetupFileFormatInspectorTests : TestBase
{
    private static readonly byte[] _PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00];

    [Fact]
    public void should_register_one_singleton_inspector_when_called_twice()
    {
        var services = new ServiceCollection();

        services.AddHeadlessFileFormatInspector().AddHeadlessFileFormatInspector();

        services
            .Should()
            .ContainSingle(descriptor => descriptor.ServiceType == typeof(IFileFormatInspector))
            .Which.Lifetime.Should()
            .Be(ServiceLifetime.Singleton);

        using var provider = services.BuildServiceProvider();
        provider
            .GetRequiredService<IFileFormatInspector>()
            .Should()
            .BeSameAs(provider.GetRequiredService<IFileFormatInspector>());
    }

    [Fact]
    public void should_detect_built_in_formats_when_inspector_is_resolved()
    {
        using var provider = new ServiceCollection().AddHeadlessFileFormatInspector().BuildServiceProvider();
        var inspector = provider.GetRequiredService<IFileFormatInspector>();
        using var content = new MemoryStream(_PngBytes, writable: false);

        inspector.DetermineFileFormat(content).Should().BeOfType<Png>();
    }

    [Fact]
    public void should_keep_the_application_inspector_when_one_is_registered_first()
    {
        var custom = new FileFormatInspector([new Png()]);
        var services = new ServiceCollection();
        services.AddSingleton<IFileFormatInspector>(custom);

        services.AddHeadlessFileFormatInspector();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IFileFormatInspector>().Should().BeSameAs(custom);
    }

    [Fact]
    public async Task should_resolve_a_signature_validator_when_only_the_inspector_is_registered()
    {
        var services = new ServiceCollection().AddHeadlessFileFormatInspector();
        services.AddTransient<IValidator<UploadAvatar>, UploadAvatarValidator>();
        await using var provider = services.BuildServiceProvider();
        var validator = provider.GetRequiredService<IValidator<UploadAvatar>>();

        var accepted = await validator.ValidateAsync(new UploadAvatar(_File(_PngBytes)), AbortToken);
        var rejected = await validator.ValidateAsync(new UploadAvatar(_File([0x25, 0x50, 0x44, 0x46])), AbortToken);

        accepted.IsValid.Should().BeTrue();
        rejected.Errors.Should().ContainSingle().Which.ErrorCode.Should().Be("g:file_unexpected_signature");
    }

    private static FormFile _File(byte[] content) =>
        new(new MemoryStream(content, writable: false), 0, content.Length, "Avatar", "avatar.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png",
        };

    private sealed record UploadAvatar(IFormFile Avatar);

    private sealed class UploadAvatarValidator : AbstractValidator<UploadAvatar>
    {
        public UploadAvatarValidator(IFileFormatInspector inspector)
        {
            RuleFor(x => x.Avatar).HaveSignatures(inspector, format => format is Png);
        }
    }
}
