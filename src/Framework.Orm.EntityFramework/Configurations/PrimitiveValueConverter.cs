// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Generator.Primitives;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Framework.Orm.EntityFramework.Configurations;

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
