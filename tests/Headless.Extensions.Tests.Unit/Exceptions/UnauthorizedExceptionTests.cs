// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Exceptions;
using Headless.Primitives;

namespace Tests.Exceptions;

public sealed class UnauthorizedExceptionTests
{
    [Fact]
    public void should_use_shared_general_code_for_message_constructor()
    {
        // when
        var exception = new UnauthorizedException("Session expired");

        // then
        exception.Error.Code.Should().Be(ApiResultErrorCodes.Default);
        exception.Error.Description.Should().Be("Session expired");
    }
}
