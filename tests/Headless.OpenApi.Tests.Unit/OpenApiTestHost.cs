// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;
using Headless.OpenApi;
using Headless.Serializer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests;

/// <summary>Builds an in-memory host, serves its OpenAPI documents, and returns them parsed.</summary>
internal static class OpenApiTestHost
{
    public static async Task<JsonNode> GetDocumentAsync(
        Action<WebApplicationBuilder> configureServices,
        Action<WebApplication> mapEndpoints,
        string documentName,
        CancellationToken cancellationToken,
        Action<WebApplication>? configurePipeline = null
    )
    {
        var documents = await GetDocumentsAsync(
            configureServices,
            mapEndpoints,
            [documentName],
            cancellationToken,
            configurePipeline
        );

        return documents[documentName];
    }

    public static async Task<Dictionary<string, JsonNode>> GetDocumentsAsync(
        Action<WebApplicationBuilder> configureServices,
        Action<WebApplication> mapEndpoints,
        IEnumerable<string> documentNames,
        CancellationToken cancellationToken,
        Action<WebApplication>? configurePipeline = null
    )
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        // The Headless web JSON options read numbers from strings, which is what makes the native generator
        // describe numbers as integer-or-string unions.
        builder.Services.ConfigureHttpJsonOptions(options =>
            JsonConstants.ConfigureWebJsonOptions(options.SerializerOptions)
        );
        builder.Services.AddAuthentication();
        builder
            .Services.AddAuthorizationBuilder()
            .AddPolicy("orders.read", policy => policy.RequireClaim("scope", "orders.read"));
        configureServices(builder);

        await using var app = builder.Build();
        app.UseRouting();
        // Middleware that must sit between routing and authentication, such as tenant catalog resolution.
        configurePipeline?.Invoke(app);
        app.UseAuthentication();
        app.UseAuthorization();
        mapEndpoints(app);
        app.MapOpenApi().AllowAnonymous();

        await app.StartAsync(cancellationToken);

        try
        {
            using var client = app.GetTestClient();
            var documents = new Dictionary<string, JsonNode>(StringComparer.Ordinal);

            foreach (var documentName in documentNames)
            {
                using var response = await client.GetAsync($"/openapi/{documentName}.json", cancellationToken);
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                response.IsSuccessStatusCode.Should().BeTrue(json);
                documents[documentName] = JsonNode.Parse(json)!;
            }

            return documents;
        }
        finally
        {
            await app.StopAsync(cancellationToken);
        }
    }

    public static JsonNode Operation(this JsonNode document, string path, string method)
    {
        var operation = document["paths"]?[path]?[method];
        operation.Should().NotBeNull($"the document should describe {method.ToUpperInvariant()} {path}");

        return operation!;
    }

    public static JsonNode Schema(this JsonNode document, string name)
    {
        var schema = document["components"]?["schemas"]?[name];
        schema.Should().NotBeNull($"the document should publish the {name} component");

        return schema!;
    }

    public static string[] Required(this JsonNode schema)
    {
        return schema["required"]?.AsArray().Select(x => x!.GetValue<string>()).ToArray() ?? [];
    }

    public static string[] Types(this JsonNode schema)
    {
        return schema["type"] switch
        {
            JsonArray array => [.. array.Select(x => x!.GetValue<string>())],
            JsonValue value => [value.GetValue<string>()],
            _ => [],
        };
    }

    /// <summary>Asserts the node equals <paramref name="expected"/> as JSON, so escaping and member order do not matter.</summary>
    public static void ShouldBeJson(this JsonNode? node, [StringSyntax(StringSyntaxAttribute.Json)] string expected)
    {
        JsonNode.DeepEquals(node, JsonNode.Parse(expected)).Should().BeTrue(node?.ToJsonString());
    }

    public static JsonNode Parameter(this JsonNode operation, string name)
    {
        var parameter = operation["parameters"]
            ?.AsArray()
            .FirstOrDefault(x => string.Equals((string?)x?["name"], name, StringComparison.Ordinal));
        parameter.Should().NotBeNull($"the operation should declare the '{name}' parameter");

        return parameter!;
    }
}
