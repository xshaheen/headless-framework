// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Azure;
using Azure.Core;
using Headless.Blobs.Azure;

namespace Tests;

/// <summary>
/// Pins the <c>AzureBlobStorage.MapDeleteResponse</c> contract deterministically. The live Azurite integration path
/// cannot exercise the 404 branch — Azurite reports an already-absent blob as success, not 404 — so the two
/// <c>bulk_delete_reports_*</c> conformance tests are skipped there. This unit test drives the mapping directly with a
/// fake batch sub-response so the 404 → <c>Ok(false)</c> and error → <c>Fail</c> branches stay covered.
/// </summary>
public sealed class AzureBlobStorageDeleteMappingTests
{
    [Theory]
    [InlineData(200)] // deleted
    [InlineData(202)] // Azure batch delete accepts with 202
    public void maps_a_successful_subresponse_to_ok_true(int status)
    {
        var result = AzureBlobStorage.MapDeleteResponse(new FakeResponse(status));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue("a non-error sub-response means the blob was deleted");
    }

    [Fact]
    public void maps_a_404_subresponse_to_ok_false_not_found()
    {
        var result = AzureBlobStorage.MapDeleteResponse(new FakeResponse(404));

        result.IsSuccess.Should().BeTrue("a 404 is a benign 'already gone', not a failure");
        result.Value.Should().BeFalse("404 means the blob did not exist -> not found");
    }

    [Theory]
    [InlineData(403)] // forbidden
    [InlineData(429)] // throttled
    [InlineData(500)] // server error
    public void maps_a_non_404_error_subresponse_to_fail(int status)
    {
        var result = AzureBlobStorage.MapDeleteResponse(new FakeResponse(status));

        result.IsFailure.Should().BeTrue("a non-404 error must surface the real cause, not a misleading 'not found'");
        result.Error.Should().BeOfType<RequestFailedException>();
        ((RequestFailedException)result.Error).Status.Should().Be(status);
    }

    // Minimal hand-rolled Azure.Response so the mapping can be driven off a chosen status without a live backend.
    // IsError mirrors HTTP semantics (>= 400) because the real batch pipeline sets it from the response classifier.
    private sealed class FakeResponse(int status) : Response
    {
        public override int Status => status;

        public override string ReasonPhrase => "fake";

        public override bool IsError => Status >= 400;

        public override Stream? ContentStream { get; set; }

        public override string ClientRequestId { get; set; } = "fake";

        public override void Dispose() { }

        protected override bool TryGetHeader(string name, [NotNullWhen(true)] out string? value)
        {
            value = null;

            return false;
        }

        protected override bool TryGetHeaderValues(string name, [NotNullWhen(true)] out IEnumerable<string>? values)
        {
            values = null;

            return false;
        }

        protected override bool ContainsHeader(string name) => false;

        protected override IEnumerable<HttpHeader> EnumerateHeaders() => [];
    }
}
