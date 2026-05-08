// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// Test stand-in for EF Core's <c>DbUpdateConcurrencyException</c>. Declared in the EF Core
/// namespace so its FullName matches the duck-typed match in <c>HeadlessApiExceptionHandler</c>
/// without forcing the test project to depend on EF Core.
/// </summary>
internal sealed class DbUpdateConcurrencyException(string message) : Exception(message);
