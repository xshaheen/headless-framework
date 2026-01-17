// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Framework.Messages.Messages;

namespace Framework.Messages.Internal;

/// <inheritdoc />
/// <summary>
/// A <see cref="T:IConsumerServiceSelector" /> implementation that scanning subscribers from
/// the assembly.
/// </summary>
public class AssemblyConsumerServiceSelector(IServiceProvider serviceProvider, Assembly[] assemblies)
    : ConsumerServiceSelector(serviceProvider)
{
    protected override IEnumerable<ConsumerExecutorDescriptor> FindConsumersFromInterfaceTypes(
        IServiceProvider provider
    )
    {
        var descriptors = new List<ConsumerExecutorDescriptor>();

        descriptors.AddRange(base.FindConsumersFromInterfaceTypes(provider));

        var assembliesToScan = assemblies.Distinct().ToArray();

        var capSubscribeTypeInfo = typeof(IConsumer).GetTypeInfo();

        foreach (var type in assembliesToScan.SelectMany(a => a.DefinedTypes))
        {
            if (!capSubscribeTypeInfo.IsAssignableFrom(type))
            {
                continue;
            }

            descriptors.AddRange(GetTopicAttributesDescription(type));
        }

        return descriptors;
    }
}
