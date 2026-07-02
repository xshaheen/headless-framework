// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Sms;

/// <summary>The outcome of a bulk send: one <see cref="SmsRecipientResult"/> per recipient, in request order.</summary>
/// <remarks>
/// Providers that return per-recipient detail (for example Infobip) populate each result individually.
/// Providers whose API reports a single status for the whole batch use <see cref="FromAggregate"/> to apply
/// that one outcome to every recipient — in that case <see cref="AllSucceeded"/> reflects the batch status,
/// but the per-recipient breakdown is identical for all entries.
/// </remarks>
[PublicAPI]
public sealed class SendBulkSmsResponse
{
    private SendBulkSmsResponse(IReadOnlyList<SmsRecipientResult> results, string? providerBatchId)
    {
        Results = results;
        ProviderBatchId = providerBatchId;
    }

    /// <summary>Per-recipient outcomes, in the order recipients were supplied in the request.</summary>
    public IReadOnlyList<SmsRecipientResult> Results { get; }

    /// <summary>
    /// Provider-assigned identifier for the batch when the backend returns one (for example the Infobip
    /// bulk id). May be <see langword="null"/> when the provider does not expose one.
    /// </summary>
    public string? ProviderBatchId { get; }

    /// <summary>Whether every recipient was accepted by the provider.</summary>
    public bool AllSucceeded => Results.All(static r => r.Result.Success);

    /// <summary>Whether at least one recipient was accepted by the provider.</summary>
    public bool AnySucceeded => Results.Any(static r => r.Result.Success);

    /// <summary>Creates a response from explicit per-recipient results.</summary>
    /// <param name="results">One result per recipient. Must not be <see langword="null"/> or empty.</param>
    /// <param name="providerBatchId">The provider-assigned batch id, when available.</param>
    /// <exception cref="ArgumentNullException"><paramref name="results"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="results"/> is empty.</exception>
    public static SendBulkSmsResponse FromResults(
        IReadOnlyList<SmsRecipientResult> results,
        string? providerBatchId = null
    )
    {
        Argument.IsNotNull(results);
        Argument.IsNotEmpty(results);

        return new SendBulkSmsResponse(results, providerBatchId);
    }

    /// <summary>
    /// Creates a response that applies one aggregate <paramref name="outcome"/> to every recipient. Used by
    /// providers whose API reports a single status for the whole batch rather than per-recipient detail.
    /// </summary>
    /// <param name="destinations">
    /// The recipients the outcome applies to. Must not be <see langword="null"/> or empty.
    /// </param>
    /// <param name="outcome">The single outcome to mirror onto every recipient. Must not be <see langword="null"/>.</param>
    /// <param name="providerBatchId">The provider-assigned batch id, when available.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="destinations"/> is empty.</exception>
    public static SendBulkSmsResponse FromAggregate(
        IEnumerable<SmsRequestDestination> destinations,
        SendSingleSmsResponse outcome,
        string? providerBatchId = null
    )
    {
        Argument.IsNotNull(destinations);
        Argument.IsNotNull(outcome);

        var results = destinations.Select(destination => new SmsRecipientResult(destination, outcome)).ToList();
        Argument.IsNotEmpty(results, paramName: nameof(destinations));

        return new SendBulkSmsResponse(results, providerBatchId);
    }
}

/// <summary>The outcome for one recipient within a <see cref="SendBulkSmsResponse"/>.</summary>
/// <param name="Destination">The recipient this outcome belongs to.</param>
/// <param name="Result">The single-send outcome for this recipient.</param>
[PublicAPI]
public sealed record SmsRecipientResult(SmsRequestDestination Destination, SendSingleSmsResponse Result);
