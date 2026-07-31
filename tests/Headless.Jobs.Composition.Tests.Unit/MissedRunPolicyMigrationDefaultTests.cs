// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Tests;

/// <summary>
/// Pins the two facts that together make an upgraded database read its recovery policy correctly.
/// </summary>
/// <remarks>
/// <c>OnMissedRun</c> is mapped with <c>HasConversion&lt;string&gt;()</c> and is NOT NULL, so EF's migration scaffolder
/// backfilled every pre-existing row with the CLR default for <see cref="string"/> — the empty string, which is not a
/// member name. That reads back correctly today only because EF's enum converter falls back to
/// <c>default(TEnum)</c> and <see cref="MissedRunPolicy.Coalesce"/> is ordinal zero.
/// <para>
/// Both halves are load-bearing and neither is self-evident at the call site, so they are asserted rather than
/// assumed. Reordering the enum, or an EF change to that fallback, would silently switch every legacy definition's
/// recovery policy with nothing failing — the exact shape of defect a passing test suite would otherwise hide.
/// </para>
/// </remarks>
public sealed class MissedRunPolicyMigrationDefaultTests : TestBase
{
    [Fact]
    public void coalesce_must_stay_ordinal_zero()
    {
        ((int)MissedRunPolicy.Coalesce)
            .Should()
            .Be(
                0,
                "rows migrated from before the OnMissedRun column existed carry an empty string, which resolves to "
                    + "default(MissedRunPolicy); if Coalesce stops being ordinal zero every one of them silently "
                    + "changes recovery policy"
            );
    }

    [Fact]
    public void the_migrated_empty_string_default_must_resolve_to_the_documented_default_policy()
    {
        var converter = new EnumToStringConverter<MissedRunPolicy>();

        converter
            .ConvertFromProvider(string.Empty)
            .Should()
            .Be(
                MissedRunPolicy.Coalesce,
                "the generated migration backfills existing rows with an empty string, so this conversion is what "
                    + "every pre-existing cron definition is read through after an upgrade"
            );
    }

    [Fact]
    public void a_real_policy_name_must_still_round_trip()
    {
        var converter = new EnumToStringConverter<MissedRunPolicy>();

        foreach (var policy in Enum.GetValues<MissedRunPolicy>())
        {
            var stored = converter.ConvertToProvider(policy);
            converter.ConvertFromProvider(stored).Should().Be(policy);
        }
    }
}
