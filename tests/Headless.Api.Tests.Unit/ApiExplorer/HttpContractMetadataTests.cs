// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Testing.Tests;

namespace Tests.ApiExplorer;

public sealed class HttpContractMetadataTests : TestBase
{
    [Fact]
    public void should_start_with_no_headers_or_responses()
    {
        var contract = new HttpContractMetadata();

        contract.RequestHeaders.Should().BeEmpty();
        contract.Responses.Should().BeEmpty();
        contract.ResponseHeaders.Should().BeEmpty();
    }

    [Fact]
    public void should_describe_a_header_carried_by_one_status_as_a_one_status_range()
    {
        var header = new HttpResponseHeaderDescription("Retry-After", 409, "Seconds to wait.");

        header.MinStatusCode.Should().Be(409);
        header.MaxStatusCode.Should().Be(409);
        header.Required.Should().BeFalse();
    }

    [Theory]
    [InlineData(99, 200)]
    [InlineData(200, 600)]
    [InlineData(300, 299)]
    public void should_reject_a_response_header_range_outside_http_status_codes(int min, int max)
    {
        var create = () => new HttpResponseHeaderDescription("X-Header", min, max, "A header.");

        create.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(99)]
    [InlineData(600)]
    public void should_reject_a_response_status_outside_http_status_codes(int statusCode)
    {
        var create = () => new HttpResponseDescription(statusCode, "A response.");

        create.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("", "A header.")]
    [InlineData(" ", "A header.")]
    [InlineData("X-Header", "")]
    public void should_reject_a_blank_request_header_name_or_description(string name, string description)
    {
        var create = () => new HttpRequestHeaderDescription(name, required: true, description);

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_keep_the_request_header_values()
    {
        var header = new HttpRequestHeaderDescription("Accept-Language", required: true, "The language.");

        header.Name.Should().Be("Accept-Language");
        header.Required.Should().BeTrue();
        header.Description.Should().Be("The language.");
    }
}
