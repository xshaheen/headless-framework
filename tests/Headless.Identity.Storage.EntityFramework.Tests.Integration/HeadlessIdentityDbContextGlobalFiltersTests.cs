// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Tests.Fixture;
using Tests.Tests;
using Xunit;

namespace Tests;

/// <summary>
/// Integration tests for HeadlessIdentityDbContext global query filter behavior.
/// Inherits from harness base to verify Identity context has same filtering as HeadlessDbContext.
/// </summary>
[Collection<IdentityTestFixture>]
public sealed class HeadlessIdentityDbContextGlobalFiltersTests(IdentityTestFixture fixture)
    : HeadlessDbContextGlobalFiltersTestBase<IdentityTestFixture, TestIdentityDbContext>(fixture);
