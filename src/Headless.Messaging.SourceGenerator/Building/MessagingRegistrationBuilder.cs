// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Immutable;
using Headless.Messaging.SourceGenerator.Models;
using Headless.Messaging.SourceGenerator.Validation;
using Headless.SourceGenerators;

namespace Headless.Messaging.SourceGenerator.Building;

/// <summary>
/// Combines the per-attribute results of one assembly into the emission model and the full diagnostic set. Only
/// cross-declaration rules live here: a class on both lanes, a duplicate identity, and a second Queue consumer.
/// </summary>
internal static class MessagingRegistrationBuilder
{
    public static MessagingGenerationResult Build(
        ImmutableArray<ConsumerResult> busConsumers,
        ImmutableArray<ConsumerResult> queueConsumers,
        string assemblyName
    )
    {
        // Source order is not stable across partial edits, so every rule that picks "the second" declaration orders by
        // location first.
        var consumers = busConsumers
            .Concat(queueConsumers)
            .OrderBy(x => x.AttributeLocation?.FilePath ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(x => x.AttributeLocation?.TextSpan.Start ?? 0)
            .ToList();

        var diagnostics = new List<DiagnosticInfo>();
        foreach (var consumer in consumers)
        {
            diagnostics.AddRange(consumer.Diagnostics);
        }

        // A collision would register an ambiguous consumer that startup rejects anyway, so nothing is emitted and the
        // build fails at the declarations instead.
        var collisions = _FindCollisions(consumers);
        if (collisions.Count > 0)
        {
            diagnostics.AddRange(collisions);
            return new(Model: null, diagnostics.ToEquatableArray());
        }

        var registrations = _NameDispatchers(consumers);

        // An assembly that declares nothing gets no module: a public MessagingModule type would otherwise appear in
        // every project that references Messaging.
        var model = registrations.Count == 0 ? null : new MessagingRegistrationModel(assemblyName, registrations);

        return new(model, diagnostics.ToEquatableArray());
    }

    private static List<DiagnosticInfo> _FindCollisions(List<ConsumerResult> consumers)
    {
        var collisions = new List<DiagnosticInfo>();
        var busTypes = new HashSet<string>(
            consumers.Where(x => x.Lane == ConsumerLane.Bus).Select(x => x.TypeName),
            StringComparer.Ordinal
        );
        var identities = new HashSet<(ConsumerLane, string)>();
        var queueMessages = new HashSet<string>(StringComparer.Ordinal);

        foreach (var consumer in consumers)
        {
            if (consumer.Lane == ConsumerLane.Queue && busTypes.Contains(consumer.TypeName))
            {
                collisions.Add(
                    new(
                        DiagnosticDescriptors.MultipleLaneAttributes,
                        consumer.AttributeLocation,
                        new EquatableArray<string>([consumer.TypeName.Replace("global::", string.Empty)])
                    )
                );
            }

            // Identities are unique per lane: a Bus identity is a subscription and a Queue identity keys point-to-point
            // inbox state, so one identity may name a consumer on each lane.
            if (HandlerIdentity.IsValid(consumer.Identity) && !identities.Add((consumer.Lane, consumer.Identity!)))
            {
                collisions.Add(
                    new(
                        DiagnosticDescriptors.DuplicateConsumerIdentity,
                        consumer.AttributeLocation,
                        new EquatableArray<string>([consumer.Lane.ToString(), consumer.Identity!])
                    )
                );
            }

            if (consumer.Lane != ConsumerLane.Queue)
            {
                continue;
            }

            foreach (var message in consumer.MessageTypeNames.Where(message => !queueMessages.Add(message)))
            {
                collisions.Add(
                    new(
                        DiagnosticDescriptors.DuplicateQueueConsumer,
                        consumer.AttributeLocation,
                        new EquatableArray<string>([message.Replace("global::", string.Empty)])
                    )
                );
            }
        }

        return collisions;
    }

    /// <summary>
    /// Names each class's dispatcher after its full name, ordered by identity so the emitted file does not change with
    /// declaration order. Nested and top-level classes can flatten to one name, so a repeat gets a numeric suffix.
    /// </summary>
    private static EquatableArray<ConsumerRegistrationModel> _NameDispatchers(List<ConsumerResult> consumers)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var registrations = new List<ConsumerRegistrationModel>();

        foreach (
            var consumer in consumers
                .Where(x => x.Consumer is not null)
                .Select(x => x.Consumer!)
                .OrderBy(x => x.Lane)
                .ThenBy(x => x.Identity, StringComparer.Ordinal)
                .ThenBy(x => x.TypeName, StringComparer.Ordinal)
        )
        {
            var baseName = HandlerSymbols.ToMemberName("Dispatch_", consumer.DisplayName);
            var name = baseName;
            for (var suffix = 2; !used.Add(name); suffix++)
            {
                name = baseName + "_" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            registrations.Add(new(consumer, name));
        }

        return registrations.ToEquatableArray();
    }
}
