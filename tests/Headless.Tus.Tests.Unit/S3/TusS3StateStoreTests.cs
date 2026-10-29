// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Testing.Tests;
using Headless.Tus.Internal;

namespace Tests.S3;

public sealed class TusS3StateStoreTests : TestBase
{
    [Theory]
    [InlineData(HttpStatusCode.PreconditionFailed, "PreconditionFailed", true)]
    [InlineData(HttpStatusCode.Conflict, "ConditionalRequestConflict", true)]
    [InlineData(HttpStatusCode.Conflict, "OperationAborted", false)]
    [InlineData(HttpStatusCode.NotFound, "NoSuchKey", true)]
    [InlineData(HttpStatusCode.NotFound, "NoSuchBucket", false)]
    [InlineData(HttpStatusCode.InternalServerError, "InternalError", false)]
    public void should_tell_a_stale_write_from_other_failures(HttpStatusCode status, string errorCode, bool stale)
    {
        // given
        var exception = InMemoryS3.Error(status, errorCode);

        // when
        var result = TusS3StateStore.IsStaleWrite(exception);

        // then - a missing bucket or an aborted operation is a fault to surface, not a lost race to absorb
        result.Should().Be(stale);
    }
}
