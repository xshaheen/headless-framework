// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DevInbox.Dashboard;
using Headless.Testing.Tests;

namespace Tests;

public sealed class AssetsTests : TestBase
{
    [Fact]
    public async Task should_serve_the_embedded_manifest_of_the_dev_inbox_module_and_product()
    {
        // given
        var files = DevInboxAssets.Create().Files;

        // when
        var manifest = files.GetFileInfo("manifest.json");

        // then
        manifest.Exists.Should().BeTrue();
        await using var stream = manifest.CreateReadStream();
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: AbortToken);
        document.RootElement.GetProperty("id").GetString().Should().Be("dev-inbox");
        document.RootElement.GetProperty("product").GetString().Should().Be("dev-inbox");
    }
}
