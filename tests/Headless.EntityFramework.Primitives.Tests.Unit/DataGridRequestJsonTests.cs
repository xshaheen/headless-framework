// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Headless.EntityFramework;
using Headless.Serializer;
using Headless.Testing.Tests;

namespace Tests;

public sealed class DataGridRequestJsonTests : TestBase
{
    [Fact]
    public void should_bind_the_page_when_a_json_body_carries_index_and_size()
    {
        // given
        const string json = """{"page":{"index":1,"size":5},"orders":[{"property":"name","ascending":false}]}""";

        // when
        var request = JsonSerializer.Deserialize<GridRequest>(json, JsonConstants.CreateWebJsonOptions());

        // then
        request.Should().NotBeNull();
        request!.Page.Should().NotBeNull();
        request.Page!.Index.Should().Be(1);
        request.Page.Size.Should().Be(5);
        request.Orders.Should().ContainSingle().Which.Ascending.Should().BeFalse();
    }

    [Fact]
    public void should_reject_a_page_that_omits_a_required_member()
    {
        // given
        const string json = """{"page":{"index":1}}""";

        // when
        var deserialize = () => JsonSerializer.Deserialize<GridRequest>(json, JsonConstants.CreateWebJsonOptions());

        // then
        deserialize.Should().Throw<JsonException>().WithMessage("*size*");
    }

    private sealed class GridRequest : DataGridRequest;
}
