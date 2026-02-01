// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Tests.Fixture;
using Tests.Tests;
using Xunit;

namespace Tests;

/// <summary>
/// Integration tests for HeadlessIdentityDbContext SaveChanges behavior.
/// Inherits from harness base to verify Identity context has same behavior as HeadlessDbContext.
/// </summary>
[Collection<IdentityTestFixture>]
public sealed class HeadlessIdentityDbContextSaveChangesTests(IdentityTestFixture fixture)
    : HeadlessDbContextSaveChangesTestBase<IdentityTestFixture, TestIdentityDbContext>(fixture);
