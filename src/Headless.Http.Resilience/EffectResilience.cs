// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace Headless.Http.Resilience;

/// <summary>
/// Derives an HTTP resilience pipeline from a declared <see cref="OutboundEffect"/>: the single
/// place where the effect decision (a property of the provider's API) becomes retry behavior.
/// </summary>
/// <remarks>
/// <para>
/// <b>Stacking.</b> A host-wide default handler (for example
/// <c>ConfigureHttpClientDefaults(b => b.AddStandardResilienceHandler())</c> in service defaults)
/// becomes an <i>outer</i> pipeline around any handler the provider adds, and its stock retry
/// would defeat an <see cref="OutboundEffect.Unsafe"/> declaration. This helper removes earlier
/// resilience handlers from the builder first (<c>RemoveAllResilienceHandlers</c>), so the
/// declared effect always wins regardless of what the host applied to every client.
/// </para>
/// <para>
/// <b>Unsafe.</b> Retry is disabled for the RFC-unsafe methods (POST, PUT, PATCH, DELETE, CONNECT)
/// — the ones a provider's API actually re-executes. Reads through the same client stay retryable,
/// because a provider whose reads are also unsafe declares <see cref="OutboundEffect.Unsafe"/> for
/// the whole client. A consumer-supplied resilience tuning runs after the derived defaults and
/// can still re-enable retry: opting in is an explicit act.
/// </para>
/// <para>
/// <b>Idempotent.</b> Retry stays on and each logical call carries one stable key in the
/// provider's deduplication header (Stripe semantics): the key is minted once per send and
/// reused across the retries of that send, so the provider sees the same key twice, not two
/// keys. <b>Safe.</b> Stock standard-resilience retry, unchanged.
/// </para>
/// </remarks>
[PublicAPI]
public static class EffectResilience
{
    /// <summary>Applies the resilience pipeline derived from <paramref name="effect"/> to <paramref name="builder"/>.</summary>
    /// <param name="builder">The HTTP client builder whose pipeline is derived.</param>
    /// <param name="effect">The declared side-effect class of the provider's operations.</param>
    /// <param name="idempotencyHeader">
    /// The provider header that deduplicates retries (used only for <see cref="OutboundEffect.Idempotent"/>).
    /// </param>
    /// <param name="configureResilience">
    /// Optional consumer tuning applied after the derived defaults, so it can still override them.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="effect"/> is <see cref="OutboundEffect.Idempotent"/> and
    /// <paramref name="idempotencyHeader"/> is null or empty.
    /// </exception>
    public static IHttpClientBuilder AddEffectResilienceHandler(
        this IHttpClientBuilder builder,
        OutboundEffect effect,
        string? idempotencyHeader = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        Argument.IsNotNull(builder);

        // The declared effect must win over any host-wide default handler already on this builder;
        // a second standard handler would stack as an outer pipeline and retry regardless of it.
        // EXTEXP0001: RemoveAllResilienceHandlers is the only supported way to clear a previously
        // added (host-wide default) resilience handler in Microsoft.Extensions.Http.Resilience 10.
#pragma warning disable EXTEXP0001 // evaluation-only API; the alternative is silently broken stacking
        builder.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

        if (effect is OutboundEffect.Idempotent)
        {
            Argument.IsNotNullOrEmpty(idempotencyHeader);

            _AddIdempotencyKeyHandler(builder, idempotencyHeader!);
        }

        builder.AddStandardResilienceHandler(options =>
        {
            if (effect is OutboundEffect.Unsafe)
            {
                // The provider declared a re-sent request may take effect twice on its mutating
                // methods: no automatic retry for those. Reads stay retryable.
                options.Retry.DisableForUnsafeHttpMethods();
            }

            configureResilience?.Invoke(options);
        });

        return builder;
    }

    // One stable key per logical call, reused across the retries of that send: minted on first
    // sight of the request and stored in the request's Properties so a replayed request (Polly
    // re-sends the same HttpRequestMessage instance) carries the same key.
    private static void _AddIdempotencyKeyHandler(IHttpClientBuilder builder, string headerName)
    {
        builder.AddHttpMessageHandler(sp => new IdempotencyKeyHandler(headerName));
    }

    private sealed class IdempotencyKeyHandler(string headerName) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (!request.Headers.Contains(headerName))
            {
                request.Headers.TryAddWithoutValidation(headerName, $"hl_{Guid.CreateVersion7():N}");
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
