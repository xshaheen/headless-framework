// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.EntityFramework;

/// <summary>
/// Contract for data-grid query requests that carry an optional page descriptor and an ordered list
/// of sort columns.
/// </summary>
[PublicAPI]
public interface IDataGridRequest : IHasMultiOrderByRequest
{
    /// <summary>Optional page descriptor. When <see langword="null"/> the full result set is returned.</summary>
    IndexPageRequest? Page { get; }
}
