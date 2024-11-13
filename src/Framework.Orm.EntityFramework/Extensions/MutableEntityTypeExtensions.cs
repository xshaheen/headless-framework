// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Kernel.Domains;
using Microsoft.EntityFrameworkCore.Metadata;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

public static class MutableEntityTypeExtensions
{
    public static void ConfigureFrameworkValueGenerated(this IMutableEntityType entityType)
    {
        if (entityType.ClrType.IsAssignableTo(typeof(IEntity<long>)))
        {
            entityType.GetProperty(nameof(IEntity<long>.Id)).ValueGenerated = ValueGenerated.Never;
        }

        if (entityType.ClrType.IsAssignableTo(typeof(IEntity<string>)))
        {
            entityType.GetProperty(nameof(IEntity<string>.Id)).ValueGenerated = ValueGenerated.Never;
        }

        if (entityType.ClrType.IsAssignableTo(typeof(IEntity<Guid>)))
        {
            entityType.GetProperty(nameof(IEntity<Guid>.Id)).ValueGenerated = ValueGenerated.Never;
        }
    }
}
