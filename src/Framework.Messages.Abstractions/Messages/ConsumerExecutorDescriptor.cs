// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Framework.Messages.Messages;

/// <summary>
/// A descriptor of user definition method.
/// </summary>
public class ConsumerExecutorDescriptor
{
    public TypeInfo? ServiceTypeInfo { get; init; }

    public required MethodInfo MethodInfo { get; init; }

    public required TypeInfo ImplTypeInfo { get; init; }

    public required TopicAttribute Attribute { get; init; }

    public TopicAttribute? ClassAttribute { get; init; }

    public List<ParameterDescriptor> Parameters { get; init; } = [];

    public string? TopicNamePrefix { get; init; }

    /// <summary>
    /// Topic name based on both <see cref="Attribute" /> and <see cref="ClassAttribute" />.
    /// </summary>
    public string TopicName
    {
        get
        {
            if (field == null)
            {
                if (ClassAttribute != null && Attribute.IsPartial)
                {
                    // Allows class level attribute name to end with a '.' and allows methods level attribute to start with a '.'.
                    field = $"{ClassAttribute.Name.TrimEnd('.')}.{Attribute.Name.TrimStart('.')}";
                }
                else
                {
                    field = Attribute.Name;
                }

                if (!string.IsNullOrEmpty(TopicNamePrefix) && !string.IsNullOrEmpty(field))
                {
                    field = $"{TopicNamePrefix}.{field}";
                }
            }

            return field;
        }
    }
}

public sealed class ParameterDescriptor
{
    public required string? Name { get; init; }

    public required Type ParameterType { get; init; }

    public required bool IsFromCap { get; init; }
}
