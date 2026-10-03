// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Serializer.Converters;

/// <summary>
/// <see cref="SingleOrCollectionJsonConverter{TCollection,TItem}"/> specialization that produces a
/// <see cref="List{T}"/>.
/// </summary>
/// <typeparam name="TItem">The list element type.</typeparam>
[RequiresUnreferencedCode(
    "JSON serialization and deserialization might require types that cannot be statically analyzed."
)]
[RequiresDynamicCode("JSON serialization and deserialization might require runtime code generation.")]
public sealed class SingleOrListJsonConverter<TItem> : SingleOrCollectionJsonConverter<List<TItem?>, TItem>;
