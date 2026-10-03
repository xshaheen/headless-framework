// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sql;

/// <summary>
/// The outcome of an <see cref="IConnectionStringChecker.CheckAsync"/> probe.
/// </summary>
/// <param name="Connected">
/// <see langword="true"/> when the server accepted the connection; otherwise <see langword="false"/>.
/// </param>
/// <param name="DatabaseExists">
/// <see langword="true"/> when the target database was found on the server; otherwise <see langword="false"/>.
/// </param>
[PublicAPI]
public readonly record struct ConnectionCheckResult(bool Connected, bool DatabaseExists);
