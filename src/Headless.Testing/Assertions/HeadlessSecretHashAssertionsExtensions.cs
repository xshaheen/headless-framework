// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Security;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace AwesomeAssertions.Collections;

/// <summary>AwesomeAssertions extensions for columns that store secret hashes.</summary>
[PublicAPI]
public static class HeadlessSecretHashAssertionsExtensions
{
    /// <summary>
    /// Asserts that every value is a PHC-encoded secret hash of <paramref name="algorithmId" /> — for example, that a
    /// PIN column never holds plaintext or a hash from a retired algorithm.
    /// </summary>
    /// <remarks>
    /// Run your own query and pass the column values. A failure names the first offending index and why it failed,
    /// never the value itself, so a plaintext secret does not leak into test output.
    /// </remarks>
    /// <param name="assertions">The assertion subject.</param>
    /// <param name="algorithmId">The expected PHC id, such as <see cref="SecretHashAlgorithms.Argon2id" />.</param>
    /// <param name="because">Reason text forwarded to the assertion failure message.</param>
    /// <param name="becauseArgs">Arguments for the <paramref name="because" /> format string.</param>
    /// <returns>Assertion object for further chaining.</returns>
    public static AndConstraint<StringCollectionAssertions> AllBeSecretHashes(
        this StringCollectionAssertions assertions,
        string algorithmId,
        string because = "",
        params object[] becauseArgs
    )
    {
        // A null collection means the query never ran; passing it would report a column as checked when nothing was.
        assertions
            .CurrentAssertionChain.BecauseOf(because, becauseArgs)
            .ForCondition(assertions.Subject is not null)
            .FailWith(
                "Expected {context:collection} to hold only {0} secret hashes{reason}, but found <null>.",
                algorithmId
            );

        if (assertions.Subject is null)
        {
            return new AndConstraint<StringCollectionAssertions>(assertions);
        }

        var index = 0;

        foreach (var value in assertions.Subject)
        {
            string? problem = null;

            if (!PhcString.TryParse(value, out var parsed))
            {
                problem = "is not a PHC string";
            }
            else if (!string.Equals(parsed.Id, algorithmId, StringComparison.Ordinal))
            {
                problem = $"uses algorithm \"{parsed.Id}\"";
            }

            assertions
                .CurrentAssertionChain.BecauseOf(because, becauseArgs)
                .ForCondition(problem is null)
                .FailWith(
                    "Expected {context:collection} to hold only {0} secret hashes{reason}, but the value at index {1} {2}.",
                    algorithmId,
                    index,
                    problem
                );

            if (problem is not null)
            {
                break;
            }

            index++;
        }

        return new AndConstraint<StringCollectionAssertions>(assertions);
    }
}
