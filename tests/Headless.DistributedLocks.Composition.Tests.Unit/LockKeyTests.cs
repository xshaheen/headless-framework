// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.DistributedLocks;
using Headless.Testing.Tests;

namespace Tests;

public sealed class LockKeyTests : TestBase
{
    [Fact]
    public void should_join_the_short_type_name_and_a_string_id()
    {
        LockKey.For<Wallet>("abc-1").Should().Be("Wallet:abc-1");
    }

    [Fact]
    public void should_format_a_guid_id_as_lowercase_hyphenated_hex()
    {
        var id = Guid.Parse("8F14E45F-CEEA-467A-9575-3A3A0C1B0F2B");

        LockKey.For<Wallet>(id).Should().Be("Wallet:8f14e45f-ceea-467a-9575-3a3a0c1b0f2b");
    }

    [Fact]
    public void should_format_numeric_ids_with_the_invariant_culture_whatever_the_current_culture()
    {
        // given — a culture whose native digits and negative sign differ from the invariant ones
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-EG");

        try
        {
            // when / then — an int widens to the long overload
            LockKey.For<Wallet>(-42).Should().Be("Wallet:-42");
            LockKey.For<Wallet>(9_007_199_254_740_993L).Should().Be("Wallet:9007199254740993");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void should_give_different_entity_types_different_names_for_one_id()
    {
        LockKey.For<Wallet>(7).Should().NotBe(LockKey.For<Ledger>(7));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void should_reject_a_blank_string_id(string id)
    {
        var act = () => LockKey.For<Wallet>(id);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_reject_a_null_string_id()
    {
        var act = () => LockKey.For<Wallet>((string)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private sealed class Wallet;

    private sealed class Ledger;
}
