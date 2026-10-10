// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Headless.IpGeolocation.Internal;

/// <summary>Talks to the MaxMind download API: release checks, archive downloads, and checksum verification.</summary>
internal sealed class MaxMindDownloader(IHttpClientFactory httpClientFactory, IOptions<MaxMindOptions> options)
{
    private readonly MaxMindOptions _options = options.Value;

    /// <summary>Returns when MaxMind last published <paramref name="editionId" />.</summary>
    /// <remarks>MaxMind counts only downloads against the account's daily limit, not these HEAD requests.</remarks>
    public async Task<DateTimeOffset?> GetLastModifiedAsync(string editionId, CancellationToken cancellationToken)
    {
        using var request = _CreateRequest(HttpMethod.Head, editionId, "tar.gz");
        using var response = await _Send(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        return response.Content.Headers.LastModified;
    }

    /// <summary>
    /// Downloads <paramref name="editionId" />, verifies it against MaxMind's published SHA-256, and extracts the
    /// database to <paramref name="destinationPath" />.
    /// </summary>
    /// <exception cref="InvalidDataException">The archive fails its checksum or holds no database.</exception>
    public async Task DownloadAsync(string editionId, string destinationPath, CancellationToken cancellationToken)
    {
        var expectedHash = await _GetExpectedHashAsync(editionId, cancellationToken).ConfigureAwait(false);
        var archivePath = destinationPath + ".tar.gz";

        try
        {
            using (var request = _CreateRequest(HttpMethod.Get, editionId, "tar.gz"))
            using (
                var response = await _Send(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                var archive = File.Create(archivePath);
                await using (archive.ConfigureAwait(false))
                {
                    var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    await using (body.ConfigureAwait(false))
                    {
                        await body.CopyToAsync(archive, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            await _VerifyHashAsync(archivePath, expectedHash, editionId, cancellationToken).ConfigureAwait(false);
            await _ExtractDatabaseAsync(archivePath, destinationPath, editionId, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            File.Delete(archivePath);
        }
    }

    private async Task<string> _GetExpectedHashAsync(string editionId, CancellationToken cancellationToken)
    {
        using var request = _CreateRequest(HttpMethod.Get, editionId, "tar.gz.sha256");
        using var response = await _Send(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);

        // The body is "<hex digest>  <file name>".
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var hash = body.Split(' ', 2, StringSplitOptions.TrimEntries)[0];

        if (hash.Length != SHA256.HashSizeInBytes * 2)
        {
            throw new InvalidDataException($"MaxMind returned no SHA-256 digest for the {editionId} database.");
        }

        return hash;
    }

    private static async Task _VerifyHashAsync(
        string archivePath,
        string expectedHash,
        string editionId,
        CancellationToken cancellationToken
    )
    {
        var archive = File.OpenRead(archivePath);
        await using (archive.ConfigureAwait(false))
        {
            var actual = await SHA256.HashDataAsync(archive, cancellationToken).ConfigureAwait(false);

            if (!string.Equals(Convert.ToHexString(actual), expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The downloaded {editionId} archive does not match the SHA-256 MaxMind published for it."
                );
            }
        }
    }

    private static async Task _ExtractDatabaseAsync(
        string archivePath,
        string destinationPath,
        string editionId,
        CancellationToken cancellationToken
    )
    {
        var archive = File.OpenRead(archivePath);
        await using (archive.ConfigureAwait(false))
        {
            var gzip = new GZipStream(archive, CompressionMode.Decompress);
            await using (gzip.ConfigureAwait(false))
            {
                var tar = new TarReader(gzip);
                await using (tar.ConfigureAwait(false))
                {
                    while (
                        await tar.GetNextEntryAsync(cancellationToken: cancellationToken).ConfigureAwait(false)
                            is { } entry
                    )
                    {
                        if (
                            entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile
                            && entry.Name.EndsWith(".mmdb", StringComparison.Ordinal)
                            && entry.DataStream is not null
                        )
                        {
                            var database = File.Create(destinationPath);
                            await using (database.ConfigureAwait(false))
                            {
                                await entry.DataStream.CopyToAsync(database, cancellationToken).ConfigureAwait(false);
                            }

                            return;
                        }
                    }
                }
            }
        }

        throw new InvalidDataException($"The downloaded {editionId} archive contains no .mmdb database.");
    }

    private HttpRequestMessage _CreateRequest(HttpMethod method, string editionId, string suffix)
    {
        var request = new HttpRequestMessage(
            method,
            $"geoip/databases/{Uri.EscapeDataString(editionId)}/download?suffix={suffix}"
        );
        var credentials = Encoding.UTF8.GetBytes($"{_options.AccountId}:{_options.LicenseKey}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(credentials));

        return request;
    }

    private async Task<HttpResponseMessage> _Send(
        HttpRequestMessage request,
        HttpCompletionOption completion,
        CancellationToken cancellationToken
    )
    {
        var client = httpClientFactory.CreateClient(MaxMindOptions.HttpClientName);
        var response = await client.SendAsync(request, completion, cancellationToken).ConfigureAwait(false);

        try
        {
            response.EnsureSuccessStatusCode();

            return response;
        }
        catch
        {
            response.Dispose();

            throw;
        }
    }
}
