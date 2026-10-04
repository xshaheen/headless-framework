// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Sms;

/// <summary>Represents the outcome of a bulk SMS send containing results in request order.</summary>
/// <remarks>
/// Providers that return per-recipient detail, such as Infobip, populate each result individually. Providers that
/// report a single status for the entire batch use <see cref="FromAggregate"/> to apply that outcome to each recipient.
/// In that case, <see cref="AllSucceeded"/> reflects batch status, but per-recipient entries are identical.
/// </remarks>
[PublicAPI]
public sealed class SendBulkSmsResponse
{
    private SendBulkSmsResponse(IReadOnlyList<SmsRecipientResult> results, string? providerBatchId)
    {
        Results = results;
        ProviderBatchId = providerBatchId;
    }

    /// <summary>Gets per-recipient outcomes in request order.</summary>
    public IReadOnlyList<SmsRecipientResult> Results { get; }

    /// <summary>
    /// Gets the provider-assigned identifier for the batch, when returned by the backend.
    /// </summary>
    public string? ProviderBatchId { get; }

    /// <summary>Gets a value indicating whether the provider accepted every recipient.</summary>
    public bool AllSucceeded => Results.All(static r => r.Result.Success);

    /// <summary>Gets a value indicating whether the provider accepted at least one recipient.</summary>
    public bool AnySucceeded => Results.Any(static r => r.Result.Success);

    /// <summary>Creates a bulk send response from explicit per-recipient results.</summary>
    /// <param name="results">One result per recipient.</param>
    /// <param name="providerBatchId">The provider-assigned batch identifier, when available.</param>
    /// <returns>A new <see cref="SendBulkSmsResponse"/> instance.</returns>
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
    /// Creates a response that applies one aggregate outcome to every recipient.
    /// </summary>
    /// <param name="destinations">The recipients to which the outcome applies.</param>
    /// <param name="outcome">The outcome to apply to each recipient.</param>
    /// <param name="providerBatchId">The provider-assigned batch identifier, when available.</param>
    /// <returns>A new <see cref="SendBulkSmsResponse"/> instance.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="destinations"/> or <paramref name="outcome"/> is <see langword="null"/>.
    /// </exception>
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

/// <summary>Represents the send outcome for one recipient within a bulk send.</summary>
/// <param name="Destination">The recipient associated with the outcome.</param>
/// <param name="Result">The single-send outcome for the recipient.</param>
[PublicAPI]
public sealed record SmsRecipientResult(SmsRequestDestination Destination, SendSingleSmsResponse Result);
