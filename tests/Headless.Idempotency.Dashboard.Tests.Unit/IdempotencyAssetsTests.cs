// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Idempotency;
using Headless.Testing.Tests;

namespace Tests;

public sealed class IdempotencyAssetsTests : TestBase
{
    [Fact]
    public async Task should_embed_a_manifest_naming_the_module_in_the_platform_product()
    {
        // given
        var files = IdempotencyAssets.Create().Files;

        // when
        var manifest = files.GetFileInfo("manifest.json");

        // then
        manifest.Exists.Should().BeTrue();
        await using var stream = manifest.CreateReadStream();
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: AbortToken);
        document.RootElement.GetProperty("id").GetString().Should().Be("idempotency");
        document.RootElement.GetProperty("product").GetString().Should().Be("platform");
    }
}
