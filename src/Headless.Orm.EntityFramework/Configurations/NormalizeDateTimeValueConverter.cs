// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Headless.EntityFramework.Configurations;

[PublicAPI]
public sealed class NormalizeDateTimeValueConverter(IClock clock, ConverterMappingHints? mappingHints = null)
    : ValueConverter<DateTime, DateTime>(x => clock.Normalize(x), x => clock.Normalize(x), mappingHints);

[PublicAPI]
public sealed class NullableNormalizeDateTimeValueConverter(IClock clock, ConverterMappingHints? mappingHints = null)
    : ValueConverter<DateTime?, DateTime?>(
        x => x.HasValue ? clock.Normalize(x.Value) : x,
        x => x.HasValue ? clock.Normalize(x.Value) : x,
        mappingHints
    );
