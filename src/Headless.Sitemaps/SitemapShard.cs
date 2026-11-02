// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Sitemaps;

/// <summary>
/// One sitemap file of at most <see cref="SitemapConstants.MaxSitemapUrls"/> URLs, handed to the
/// <c>onShard</c> callback of <see cref="SitemapUrls.WriteAsync(IEnumerable{SitemapUrl}, Func{SitemapShard, CancellationToken, ValueTask}, CancellationToken)"/>.
/// </summary>
/// <remarks>
/// A shard is valid only until its <c>onShard</c> call returns, because the writer reuses the shard's URL
/// buffer for the next shard. Await <see cref="WriteToAsync"/> inside the callback; calling it after the
/// callback has returned throws <see cref="InvalidOperationException"/>.
/// </remarks>
[PublicAPI]
public sealed class SitemapShard
{
    // Cleared when the onShard callback returns, so a late WriteToAsync fails loudly instead of
    // writing whatever URLs the next shard has put into the shared buffer.
    private List<SitemapUrl>? _urls;

    internal SitemapShard(int index, List<SitemapUrl> urls)
    {
        Index = index;
        Count = urls.Count;
        _urls = urls;
    }

    /// <summary>The 0-based position of this shard in the generated sequence.</summary>
    public int Index { get; }

    /// <summary>The number of <see cref="SitemapUrl"/> entries in this shard.</summary>
    public int Count { get; }

    /// <summary>Writes this shard's sitemap XML straight into <paramref name="output"/>.</summary>
    /// <param name="output">The output stream receiving the XML content.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the sitemap has been written to the stream.</returns>
    /// <exception cref="InvalidOperationException">The <c>onShard</c> callback that received this shard has returned.</exception>
    public Task WriteToAsync(Stream output, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(output);

        var urls = _urls;

        Ensure.True(
            urls is not null,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Sitemap shard {Index} is no longer valid: its URL buffer is reused for the next shard once the onShard callback returns. Await WriteToAsync inside the callback."
            )
        );

        return urls.WriteToAsync(output, cancellationToken);
    }

    internal void Invalidate()
    {
        _urls = null;
    }
}
