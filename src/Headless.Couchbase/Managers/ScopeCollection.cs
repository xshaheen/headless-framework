// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Couchbase.Linq;
using Headless.Couchbase.Context;

namespace Headless.Couchbase.Managers;

/// <summary>A Couchbase scope and collection name pair.</summary>
[PublicAPI]
public sealed record ScopeCollection(string Scope, string Collection);
