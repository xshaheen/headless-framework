// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Urls;

/// <summary>
/// Defines a read-only ordered collection of Name/Value pairs where duplicate names are allowed but aren't typical.
/// </summary>
[PublicAPI]
public interface IReadOnlyNameValueList<TValue>
    : IReadOnlyList<(string Name, TValue Value)>,
        INameValueListBase<TValue>;
