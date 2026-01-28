// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;

namespace Tests.Http;

public sealed class HttpStatusCodeExtensionsTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.Accepted)]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData((HttpStatusCode)299)]
    public void IsSuccessStatusCode_should_return_true_for_2xx(HttpStatusCode code)
    {
        code.IsSuccessStatusCode().Should().BeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.Continue)] // 100
    [InlineData(HttpStatusCode.MovedPermanently)] // 301
    [InlineData(HttpStatusCode.BadRequest)] // 400
    [InlineData(HttpStatusCode.InternalServerError)] // 500
    [InlineData((HttpStatusCode)199)]
    [InlineData((HttpStatusCode)300)]
    public void IsSuccessStatusCode_should_return_false_for_non_2xx(HttpStatusCode code)
    {
        code.IsSuccessStatusCode().Should().BeFalse();
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData((HttpStatusCode)299)]
    public void EnsureSuccessStatusCode_should_not_throw_for_2xx(HttpStatusCode code)
    {
        var act = () => code.EnsureSuccessStatusCode();

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData((HttpStatusCode)199)]
    [InlineData((HttpStatusCode)300)]
    public void EnsureSuccessStatusCode_should_throw_for_non_2xx(HttpStatusCode code)
    {
        var act = () => code.EnsureSuccessStatusCode();

        act.Should().Throw<HttpRequestException>();
    }

    [Fact]
    public void EnsureSuccessStatusCode_should_include_status_code_in_exception_message()
    {
        // given
        const HttpStatusCode code = HttpStatusCode.NotFound;

        // when
        var act = () => code.EnsureSuccessStatusCode();

        // then
        act.Should().Throw<HttpRequestException>().WithMessage("*404*");
    }

    [Fact]
    public void IsSuccessStatusCode_should_return_true_for_boundary_200()
    {
        ((HttpStatusCode)200).IsSuccessStatusCode().Should().BeTrue();
    }

    [Fact]
    public void IsSuccessStatusCode_should_return_true_for_boundary_299()
    {
        ((HttpStatusCode)299).IsSuccessStatusCode().Should().BeTrue();
    }

    [Fact]
    public void IsSuccessStatusCode_should_return_false_for_boundary_199()
    {
        ((HttpStatusCode)199).IsSuccessStatusCode().Should().BeFalse();
    }

    [Fact]
    public void IsSuccessStatusCode_should_return_false_for_boundary_300()
    {
        ((HttpStatusCode)300).IsSuccessStatusCode().Should().BeFalse();
    }
}
