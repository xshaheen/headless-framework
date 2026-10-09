// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Imaging.Internal;

namespace Tests;

public sealed class VipsRuntimeTests
{
    [Fact]
    public void should_pass_when_the_bundled_libvips_loads()
    {
        Action act = VipsRuntime.EnsureAvailable;

        act.Should().NotThrow();
    }

    [Fact]
    public void should_name_both_fixes_when_libvips_did_not_load()
    {
        // given
        var cause = new DllNotFoundException("libvips-42");

        // when
        var act = () => VipsRuntime.EnsureAvailable(initialized: false, cause, 0, 0);

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*NetVips.Native*libvips 8.15 or later*")
            .WithInnerException<DllNotFoundException>();
    }

    [Theory]
    [InlineData(8, 14)]
    [InlineData(7, 99)]
    public void should_refuse_a_libvips_older_than_8_15(int major, int minor)
    {
        var act = () => VipsRuntime.EnsureAvailable(initialized: true, error: null, major, minor);

        act.Should().Throw<InvalidOperationException>().WithMessage($"*loaded libvips {major}.{minor}*");
    }

    [Theory]
    [InlineData(8, 15)]
    [InlineData(9, 0)]
    public void should_accept_libvips_8_15_or_later(int major, int minor)
    {
        var act = () => VipsRuntime.EnsureAvailable(initialized: true, error: null, major, minor);

        act.Should().NotThrow();
    }
}
