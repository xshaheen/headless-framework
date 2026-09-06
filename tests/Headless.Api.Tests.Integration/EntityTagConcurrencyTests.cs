// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Abstractions;
using Headless.Api;
using Headless.Checks;
using Headless.Constants;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tests;

public sealed class EntityTagConcurrencyTests : TestBase
{
    [Fact]
    public async Task should_apply_entity_tag_concurrency_through_the_minimal_api_pipeline()
    {
        await using var app = await _CreateAppAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        using var getResponse = await client.GetAsync("/orders/42", AbortToken);
        using var missingResponse = await client.PutAsync("/orders/42", content: null, AbortToken);
        using var invalidRequest = new HttpRequestMessage(HttpMethod.Put, "/orders/42");
        invalidRequest.Headers.TryAddWithoutValidation("If-Match", "W/\"version-42\"");
        using var invalidResponse = await client.SendAsync(invalidRequest, AbortToken);
        using var validRequest = new HttpRequestMessage(HttpMethod.Put, "/orders/42");
        validRequest.Headers.TryAddWithoutValidation("If-Match", "\"version-42\"");
        using var validResponse = await client.SendAsync(validRequest, AbortToken);

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        getResponse.Headers.ETag!.Tag.Should().Be("\"version-42\"");
        (await getResponse.Content.ReadAsStringAsync(AbortToken)).Should().NotContain("entityTag");
        missingResponse.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired);
        invalidResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        validResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await validResponse.Content.ReadAsStringAsync(AbortToken)).Should().Contain("version-42");
    }

    private async Task<WebApplication> _CreateAppAsync()
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = EnvironmentNames.Test }
        );
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddHttpContextAccessor();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<IBuildInformationAccessor, BuildInformationAccessor>();
        builder.Services.AddHeadlessProblemDetails();
        builder.Services.AddHeadlessMinimalApiEntityTagConcurrency();

        var app = builder.Build();
        app.MapGet(
                "/orders/{id}",
                (string id) => TypedResults.Ok(new EntityTaggedOrder(id, EntityTag.CreateStrong($"version-{id}")))
            )
            .WithEntityTag();
        app.MapPut(
                "/orders/{id}",
                (string id, IIfMatchContext ifMatch) =>
                    TypedResults.Ok(new { Id = id, ETag = ifMatch.EntityTag!.OpaqueValue })
            )
            .RequireIfMatch();

        await app.StartAsync(AbortToken);
        return app;
    }

    private sealed record EntityTaggedOrder(string Id, EntityTag Tag) : IHasEntityTag
    {
        public EntityTag GetEntityTag() => Tag;
    }
}
