// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Primitives;

namespace Tests.Exceptions;

public sealed class ForbiddenExceptionTests
{
    [Fact]
    public void should_use_shared_general_code_for_message_constructor()
    {
        // when
        var exception = new ForbiddenException("Only owners can edit.");

        // then
        exception.Error.Code.Should().Be(ApiResultErrorCodes.Default);
        exception.Error.Description.Should().Be("Only owners can edit.");
    }

    [Fact]
    public void should_keep_the_descriptor_it_was_given()
    {
        // given
        var error = new ErrorDescriptor("item:locked", "The item is locked.");

        // when
        var exception = new ForbiddenException(error);

        // then
        exception.Error.Should().BeSameAs(error);
    }

    [Fact]
    public void should_refuse_a_null_descriptor()
    {
        // when
        var create = () => new ForbiddenException((ErrorDescriptor)null!);

        // then
        create.Should().Throw<ArgumentNullException>();
    }
}
