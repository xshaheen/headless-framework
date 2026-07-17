// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Endpoints;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Dashboard;

public sealed class DashboardEndpointLimitsTests : TestBase
{
    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(1, 100, true)]
    [InlineData(0, 20, false)]
    [InlineData(1, 0, false)]
    [InlineData(1, 101, false)]
    public void should_validate_pagination_bounds(int pageNumber, int pageSize, bool expected)
    {
        DashboardEndpoints.IsValidPagination(pageNumber, pageSize).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(500, true)]
    [InlineData(501, false)]
    public void should_validate_batch_delete_bounds(int count, bool expected)
    {
        DashboardEndpoints.IsValidBatchSize(count).Should().Be(expected);
    }

    [Fact]
    public async Task should_allow_request_body_at_the_byte_limit()
    {
        await using var source = new MemoryStream(new byte[16]);
        await using var limited = new DashboardRequestBodyReader.SizeLimitedReadStream(source, 16);

        await limited.CopyToAsync(Stream.Null, AbortToken);
    }

    [Fact]
    public async Task should_reject_chunked_request_body_above_the_byte_limit()
    {
        await using var source = new MemoryStream(new byte[17]);
        await using var limited = new DashboardRequestBodyReader.SizeLimitedReadStream(source, 16);

        var act = async () => await limited.CopyToAsync(Stream.Null, AbortToken);

        await act.Should().ThrowAsync<DashboardRequestBodyReader.RequestBodyTooLargeException>();
    }

    [Fact]
    public async Task should_return_payload_too_large_before_reading_declared_oversize_body()
    {
        var context = new DefaultHttpContext();
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        context.RequestServices = services;
        context.Response.Body = new MemoryStream();
        context.Request.Body = new MemoryStream("{}"u8.ToArray());
        context.Request.ContentLength = DashboardOptionsBuilder.MaxRequestBodyBytes + 1;

        var (value, error) = await DashboardRequestBodyReader.ReadAsync<object>(context, null, AbortToken);
        await error!.ExecuteAsync(context);

        value.Should().BeNull();
        context.Response.StatusCode.Should().Be(StatusCodes.Status413PayloadTooLarge);
    }

    [Fact]
    public async Task should_return_bad_request_for_malformed_json()
    {
        var context = new DefaultHttpContext();
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        context.RequestServices = services;
        context.Response.Body = new MemoryStream();
        context.Request.Body = new MemoryStream("{"u8.ToArray());

        var (value, error) = await DashboardRequestBodyReader.ReadAsync<object>(context, null, AbortToken);
        await error!.ExecuteAsync(context);

        value.Should().BeNull();
        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }
}
