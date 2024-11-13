// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

public static class ModelBuilderExtensions
{
    /// <summary>
    /// Applies configuration from all <see cref="IEntityTypeConfiguration{T}" />
    /// instances that are defined in provided assembly and satisfy provided predicate.
    /// </summary>
    [RequiresUnreferencedCode(
        "This API isn't safe for trimming, since it searches for types in an arbitrary assembly."
    )]
    public static ModelBuilder ApplyConfigurationsFromAssembly(
        this ModelBuilder builder,
        Assembly assembly,
        IServiceProvider serviceProvider,
        Func<Type, bool>? predicate = null
    )
    {
        var methodInfo = typeof(ModelBuilder)
            .GetMethods()
            .Single(info =>
                info is { Name: nameof(ModelBuilder.ApplyConfiguration), ContainsGenericParameters: true }
                && info.GetParameters().SingleOrDefault()?.ParameterType.GetGenericTypeDefinition()
                    == typeof(IEntityTypeConfiguration<>)
            );

        foreach (
            var typeInfo in assembly.GetConstructibleDefinedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal)
        )
        {
            if (typeInfo.GetConstructor(Type.EmptyTypes) != null && (predicate == null || predicate(typeInfo)))
            {
                foreach (var type in typeInfo.GetInterfaces())
                {
                    if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEntityTypeConfiguration<>))
                    {
                        methodInfo
                            .MakeGenericMethod(type.GenericTypeArguments[0])
                            .Invoke(builder, [ActivatorUtilities.CreateInstance(serviceProvider, typeInfo)]);
                    }
                }
            }
        }

        return builder;
    }
}
