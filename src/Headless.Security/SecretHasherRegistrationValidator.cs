// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Validation;

namespace Headless.Security;

/// <summary>Fails startup when the algorithm the secret-hasher builder selected has no registered implementation.</summary>
/// <remarks>
/// The setup builder already guarantees exactly one selected algorithm; this catches a <c>Use*</c> extension that
/// selected an id without registering a matching <see cref="ISecretHashAlgorithm" />. A hasher that cannot hash is
/// always a startup failure, so <see cref="SecretHasherCostCheckOptions.Mode" /> does not govern this check.
/// </remarks>
internal sealed class SecretHasherRegistrationValidator(
    SecretHasherAlgorithmSelection selection,
    IEnumerable<ISecretHashAlgorithm> algorithms
) : IHeadlessStartupValidator
{
    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The selected algorithm is not registered.</exception>
    public Task ValidateAsync(CancellationToken cancellationToken)
    {
        if (!algorithms.Any(a => string.Equals(a.Id, selection.AlgorithmId, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(SecretHasherErrors.AlgorithmNotRegistered(selection.AlgorithmId));
        }

        return Task.CompletedTask;
    }
}
