// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace Headless.Tus.Internal;

/// <summary>
/// The upload endpoints declared inside <c>AddHeadlessTus</c>, by name. <c>AddHeadlessTus</c> always registers it,
/// so its absence tells <c>MapHeadlessTus</c> that the registration is missing, and a name it does not hold was
/// never declared.
/// </summary>
internal sealed class TusEndpointRegistry(IEnumerable<TusEndpointRegistration> endpoints)
{
    private readonly FrozenDictionary<string, TusEndpointRegistration> _endpoints = endpoints.ToFrozenDictionary(
        static endpoint => endpoint.Name,
        StringComparer.Ordinal
    );

    public bool TryGet(string name, [NotNullWhen(true)] out TusEndpointRegistration? endpoint)
    {
        return _endpoints.TryGetValue(name, out endpoint);
    }
}

/// <summary>
/// One declared upload endpoint: what the configuration factory needs beyond the endpoint's options, which are the
/// named <see cref="TusUploadEndpointOptions"/> instance with the same name.
/// </summary>
/// <param name="name">
/// The endpoint's name, which is also its options name. The default endpoint uses the default options name.
/// </param>
internal sealed class TusEndpointRegistration(string name)
{
    public string Name { get; } = name;
}
