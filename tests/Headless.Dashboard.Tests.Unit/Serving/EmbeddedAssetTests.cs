// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Dashboard;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.FileProviders;
using Tests.Serving.Fixtures;

namespace Tests.Serving;

public sealed class EmbeddedAssetTests : TestBase
{
    private const string _EntryPath = "/headless/assets/shell-a1b2c3.js";

    [Fact]
    public async Task should_serve_a_hashed_asset_as_immutable_for_a_year()
    {
        await using var app = await ServingHost.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(_EntryPath, AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/javascript");
        response.Headers.CacheControl!.Public.Should().BeTrue();
        response.Headers.CacheControl.MaxAge.Should().Be(TimeSpan.FromDays(365));
        response.Headers.CacheControl.ToString().Should().Contain("immutable");
        response.Headers.ETag.Should().NotBeNull();
        (await response.Content.ReadAsStringAsync(AbortToken)).Should().Contain("identity");
    }

    [Fact]
    public async Task should_serve_the_brotli_file_when_the_request_accepts_br()
    {
        await using var app = await ServingHost.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        using var response = await _GetAsync(client, _EntryPath, "gzip, deflate, br");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentEncoding.Should().Equal("br");
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/javascript");
        response.Headers.Vary.Should().Contain("Accept-Encoding");
        (await response.Content.ReadAsStringAsync(AbortToken)).Should().StartWith("brotli body");
    }

    [Fact]
    public async Task should_serve_the_gzip_file_when_the_request_accepts_only_gzip()
    {
        await using var app = await ServingHost.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        using var response = await _GetAsync(client, _EntryPath, "gzip");

        response.Content.Headers.ContentEncoding.Should().Equal("gzip");
        (await response.Content.ReadAsStringAsync(AbortToken)).Should().StartWith("gzip body");
    }

    [Theory]
    [InlineData("br;q=0, gzip;q=0")]
    [InlineData("identity")]
    public async Task should_serve_the_file_itself_when_the_request_accepts_no_compressed_sibling(string acceptEncoding)
    {
        await using var app = await ServingHost.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        using var response = await _GetAsync(client, _EntryPath, acceptEncoding);

        response.Content.Headers.ContentEncoding.Should().BeEmpty();
        response.Headers.Vary.Should().Contain("Accept-Encoding");
        (await response.Content.ReadAsStringAsync(AbortToken)).Should().Contain("identity");
    }

    [Fact]
    public async Task should_answer_304_when_if_none_match_names_the_served_etag()
    {
        await using var app = await ServingHost.StartAsync(AbortToken);
        using var client = app.GetTestClient();
        using var first = await _GetAsync(client, _EntryPath, "br");
        using var request = new HttpRequestMessage(HttpMethod.Get, _EntryPath);
        request.Headers.AcceptEncoding.ParseAdd("br");
        request.Headers.IfNoneMatch.Add(first.Headers.ETag!);

        using var response = await client.SendAsync(request, AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotModified);
        response.Headers.ETag.Should().Be(first.Headers.ETag);
    }

    [Fact]
    public async Task should_give_the_brotli_and_identity_files_different_etags()
    {
        await using var app = await ServingHost.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        using var brotli = await _GetAsync(client, _EntryPath, "br");
        using var identity = await _GetAsync(client, _EntryPath, "identity");

        brotli.Headers.ETag.Should().NotBe(identity.Headers.ETag);
    }

    [Theory]
    [InlineData("/headless/assets/missing-000000.js")]
    [InlineData("/headless/assets/")]
    [InlineData("/headless/m/diag/manifest.json")]
    [InlineData("/headless/m/legacy/entry.js")]
    [InlineData("/headless/m/unknown/entry.js")]
    public async Task should_answer_404_when_no_servable_file_matches(string path)
    {
        await using var app = await ServingHost.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(path, AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task should_serve_a_loaded_module_file_as_immutable()
    {
        await using var app = await ServingHost.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/headless/m/diag/runs-4d5e6f.js", AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.MaxAge.Should().Be(TimeSpan.FromDays(365));
        (await response.Content.ReadAsStringAsync(AbortToken)).Should().Contain("diag runs");
    }

    [Fact]
    public async Task should_version_each_module_url_by_its_content_when_the_document_lists_it()
    {
        await using var app = await ServingHost.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        var document = ServingHost.Parse(await client.GetStringAsync("/headless/", AbortToken));
        var diag = document
            .State!.Value.GetProperty("modules")
            .EnumerateArray()
            .Single(module => string.Equals(module.GetProperty("id").GetString(), "diag", StringComparison.Ordinal));
        var entry = diag.GetProperty("entry").GetString()!;
        using var entryResponse = await client.GetAsync(entry, AbortToken);

        entry.Should().StartWith("/headless/m/diag/entry.js?v=");
        entryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        entryResponse
            .Headers.ETag!.Tag.Should()
            .Be($"\"{entry[(entry.IndexOf('=', StringComparison.Ordinal) + 1)..]}\"");
        diag.GetProperty("routes")[0]
            .GetProperty("chunk")
            .GetString()
            .Should()
            .StartWith("/headless/m/diag/runs-4d5e6f.js?v=");
        diag.GetProperty("css")[0].GetString().Should().StartWith("/headless/m/diag/diag-1a2b3c.css?v=");
    }

    [Theory]
    [InlineData("assets/../shell.json")]
    [InlineData("./shell.json")]
    [InlineData("/shell.json")]
    [InlineData("assets\\shell-a1b2c3.js")]
    [InlineData("assets//shell-a1b2c3.js")]
    public void should_find_no_file_when_the_path_is_not_a_plain_relative_path(string path)
    {
        var assets = new EmbeddedAssetSet(
            new EmbeddedFileProvider(typeof(EmbeddedAssetTests).Assembly, "Tests.Serving.Fixtures.dist")
        );

        assets.Find(path).Should().BeNull();
        assets.Find("shell.json").Should().NotBeNull();
    }

    private static async Task<HttpResponseMessage> _GetAsync(HttpClient client, string path, string acceptEncoding)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("Accept-Encoding", acceptEncoding);

        return await client.SendAsync(request, AbortToken);
    }
}
