// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Headless.Checks;

namespace Headless.Reliability;

/// <summary>Writes a <see cref="FailurePolicyDefinition"/> for a host description or another diagnostic document.</summary>
[PublicAPI]
public static class FailurePolicyDefinitionExtensions
{
    extension(FailurePolicyDefinition policy)
    {
        /// <summary>
        /// Writes the policy to <paramref name="writer"/> as one JSON object value: <c>immediateRetries</c> and
        /// <c>delayedRetries</c>, then <c>delayedInitialDelaySeconds</c> and <c>delayedMaxDelaySeconds</c> when the
        /// policy has delayed retries, then <c>failOn</c> (the full names of the fail-on exception types, in
        /// declaration order) and <c>failWhenRules</c> (the number of fail-when predicates) when either is declared.
        /// </summary>
        /// <param name="writer">
        /// The writer, positioned where a value may start, such as after a property name or inside an array.
        /// </param>
        /// <remarks>
        /// Fail-when predicates are code, so only their count is written. Settings a policy does not use are omitted
        /// instead of being written as zero or empty.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="policy"/> or <paramref name="writer"/> is <see langword="null"/>.
        /// </exception>
        public void WriteDescription(Utf8JsonWriter writer)
        {
            Argument.IsNotNull(policy);
            Argument.IsNotNull(writer);

            writer.WriteStartObject();
            writer.WriteNumber("immediateRetries", policy.ImmediateRetries);
            writer.WriteNumber("delayedRetries", policy.DelayedRetries);

            // Delays only pace delayed retries, so they mean nothing to a policy that has none.
            if (policy.DelayedRetries > 0)
            {
                writer.WriteNumber("delayedInitialDelaySeconds", policy.DelayedInitialDelay.TotalSeconds);
                writer.WriteNumber("delayedMaxDelaySeconds", policy.DelayedMaxDelay.TotalSeconds);
            }

            if (policy.FailOnExceptionTypes.Count != 0)
            {
                writer.WriteStartArray("failOn");

                foreach (var type in policy.FailOnExceptionTypes)
                {
                    writer.WriteStringValue(type.FullName ?? type.Name);
                }

                writer.WriteEndArray();
            }

            if (policy.FailWhenRuleCount != 0)
            {
                writer.WriteNumber("failWhenRules", policy.FailWhenRuleCount);
            }

            writer.WriteEndObject();
        }
    }
}
