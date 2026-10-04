// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;

namespace Tests.Extensions;

public sealed class HeadlessStatusCodeRejectionExtensionsTests : TestBase
{
    [Fact]
    public void should_throw_when_context_null()
    {
        // given
        HttpContext context = null!;
        var rejection = Substitute.For<IStatusCodeRejectionFeature>();

        // when
        var act = () => context.TrySetStatusCodeRejection(rejection);

        // then
        act.Should().Throw<ArgumentNullException>().WithParameterName("context");
    }

    [Fact]
    public void should_throw_when_rejection_null()
    {
        // given
        var context = new DefaultHttpContext();

        // when
        var act = () => context.TrySetStatusCodeRejection(null!);

        // then
        act.Should().Throw<ArgumentNullException>().WithParameterName("rejection");
    }

    [Fact]
    public void should_store_the_rejection_under_the_interface_type()
    {
        // given - a concrete-typed Features.Set would be invisible to the rewriter, which reads the interface
        var context = new DefaultHttpContext();
        var rejection = Substitute.For<IStatusCodeRejectionFeature>();

        // when
        var stored = context.TrySetStatusCodeRejection(rejection);

        // then
        stored.Should().BeTrue();
        context.Features.Get<IStatusCodeRejectionFeature>().Should().BeSameAs(rejection);
    }
}
