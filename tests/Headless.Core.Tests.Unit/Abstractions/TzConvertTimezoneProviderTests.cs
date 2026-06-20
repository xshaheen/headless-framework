// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;

namespace Tests.Abstractions;

public sealed class TzConvertTimezoneProviderTests
{
    private readonly TzConvertTimezoneProvider _sut = new();

    [Fact]
    public void get_windows_timezones_should_return_a_non_empty_list()
    {
        _sut.GetWindowsTimezones().Should().NotBeEmpty();
    }

    [Fact]
    public void get_iana_timezones_should_return_a_non_empty_list()
    {
        _sut.GetIanaTimezones().Should().NotBeEmpty();
    }

    [Fact]
    public void get_windows_timezones_should_not_share_mutable_instances_across_calls()
    {
        // given
        var first = _sut.GetWindowsTimezones();
        var originalName = first[0].Name;

        // when — mutating a returned element must not leak into a later call (no shared cache)
        first[0].Name = "MUTATED";
        var second = _sut.GetWindowsTimezones();

        // then
        second[0].Name.Should().Be(originalName);
    }

    [Fact]
    public void get_iana_timezones_should_not_share_mutable_instances_across_calls()
    {
        // given
        var first = _sut.GetIanaTimezones();
        var originalName = first[0].Name;

        // when
        first[0].Name = "MUTATED";
        var second = _sut.GetIanaTimezones();

        // then
        second[0].Name.Should().Be(originalName);
    }
}
