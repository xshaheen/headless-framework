// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Generator.Primitives;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Headless.Orm.EntityFramework.Configurations;

/// <summary>Generic ValueConverter for <see cref="IPrimitive{TValue}"/> types</summary>
/// <typeparam name="TPrimitive">The primitive type that implements <see cref="IPrimitive{TValue}"/></typeparam>
/// <typeparam name="TValue">The underlying value type</typeparam>
public class PrimitiveValueConverter<TPrimitive, TValue> : ValueConverter<TPrimitive, TValue>
    where TPrimitive : IPrimitive<TValue>
    where TValue : IEquatable<TValue>, IComparable, IComparable<TValue>
{
    public PrimitiveValueConverter()
        : base(v => v.GetUnderlyingPrimitiveValue(), v => (TPrimitive)(object)v) { }

    public PrimitiveValueConverter(ConverterMappingHints? mappingHints)
        : base(v => v.GetUnderlyingPrimitiveValue(), v => (TPrimitive)(object)v, mappingHints) { }
}
