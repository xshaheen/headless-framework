// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.TestHost;
using Tests.Serving.Fixtures;

namespace Tests.Serving;

/// <summary>How the test host puts the dashboard behind a prefix.</summary>
public enum HostShape
{
    Root = 0,
    UsePathBase = 1,
    ForwardedPrefix = 2,
}

public sealed class BasePathMatrixTests : TestBase
{
    /// <summary>
    /// The host shape, the configured base path, the path a request uses to reach the dashboard, and the absolute path
    /// the browser sees, which every URL in the document must start with.
    /// </summary>
    public static TheoryData<HostShape, string?, string, string> Matrix =>
        new()
        {
            { HostShape.Root, null, "/headless", "/headless" },
            { HostShape.UsePathBase, null, "/app/headless", "/app/headless" },
            { HostShape.ForwardedPrefix, null, "/headless", "/proxy/headless" },
            { HostShape.Root, "/ops/", "/ops", "/ops" },
            { HostShape.Root, "/ops", "/ops", "/ops" },
            { HostShape.UsePathBase, "/ops/", "/app/ops", "/app/ops" },
        };

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task should_build_every_url_under_the_path_base_and_base_path_and_serve_deep_links(
        HostShape shape,
        string? basePath,
        string requestPrefix,
        string browserPrefix
    )
    {
        await using var app = await ServingHost.StartAsync(
            basePath is null ? null : dashboard => dashboard.SetBasePath(basePath),
            session: null,
            beforeRouting: shape switch
            {
                HostShape.UsePathBase => static host => host.UsePathBase("/app"),
                HostShape.ForwardedPrefix => static host =>
                {
                    // The forwarded headers are honored only from a known proxy; the test server reports no address.
                    host.Use(
                        static (context, next) =>
                        {
                            context.Connection.RemoteIpAddress = IPAddress.Loopback;

                            return next(context);
                        }
                    );
                    host.UseForwardedHeaders(
                        new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedPrefix }
                    );
                },
                _ => null,
            },
            AbortToken
        );
        using var client = app.GetTestClient();

        if (shape == HostShape.ForwardedPrefix)
        {
            client.DefaultRequestHeaders.Add("X-Forwarded-Prefix", "/proxy");
        }

        foreach (var path in new[] { "/", "/diag", "/diag/runs/42" })
        {
            using var response = await client.GetAsync(requestPrefix + path, AbortToken);

            response.StatusCode.Should().Be(HttpStatusCode.OK, "{0} is a client route", path);
            var document = ServingHost.Parse(await response.Content.ReadAsStringAsync(AbortToken));

            document.Imports.Should().NotBeEmpty();
            document
                .Imports.Values.Should()
                .OnlyContain(url => url.StartsWith(browserPrefix + "/assets/", StringComparison.Ordinal));
            document.EntrySource.Should().StartWith(browserPrefix + "/assets/");
            document
                .Preloads.Should()
                .OnlyContain(preload => preload.Href.StartsWith(browserPrefix + "/assets/", StringComparison.Ordinal));
            document.Stylesheets.Should().ContainSingle().Which.Should().StartWith(browserPrefix + "/assets/");

            var state = document.State!.Value;
            state.GetProperty("basePath").GetString().Should().Be(browserPrefix);
            state.GetProperty("apiBase").GetString().Should().Be(browserPrefix + "/api");
            state.GetProperty("streamUrl").GetString().Should().Be(browserPrefix + "/api/stream");

            var diag = state
                .GetProperty("modules")
                .EnumerateArray()
                .Single(module =>
                    string.Equals(module.GetProperty("id").GetString(), "diag", StringComparison.Ordinal)
                );
            diag.GetProperty("entry").GetString().Should().StartWith(browserPrefix + "/m/diag/");

            // The browser's URL reaches the file once the proxy strips its prefix.
            var entryPath = requestPrefix + document.EntrySource![browserPrefix.Length..];
            using var entry = await client.GetAsync(entryPath, AbortToken);
            entry.StatusCode.Should().Be(HttpStatusCode.OK, "the entry script {0} must resolve", document.EntrySource);
        }
    }
}
