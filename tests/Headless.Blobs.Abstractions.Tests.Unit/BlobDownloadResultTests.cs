// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs;
using Headless.Testing.Tests;

namespace Tests;

public sealed class BlobDownloadResultTests : TestBase
{
    #region Dispose Tests

    [Fact]
    public void should_dispose_stream_when_dispose_is_called()
    {
        // given
        var stream = new MemoryStream([1, 2, 3]);
        var result = new BlobDownloadResult(stream, "test.txt");

        // when
        result.Dispose();

        // then - verify stream is disposed by trying to read
        var act = stream.ReadByte;
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task should_dispose_stream_async_when_dispose_async_is_called()
    {
        // given
        var stream = new MemoryStream([1, 2, 3]);
        var result = new BlobDownloadResult(stream, "test.txt");

        // when
        await result.DisposeAsync();

        // then - verify stream is disposed by trying to read
        var act = stream.ReadByte;
        act.Should().Throw<ObjectDisposedException>();
    }

    #endregion
}
