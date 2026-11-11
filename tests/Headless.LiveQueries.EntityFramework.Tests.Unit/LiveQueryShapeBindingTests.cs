// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.LiveQueries.Internal;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// How a query's filter takes its value from the command of one execution. No provider sends a command with a
/// parameter missing a value or named twice, so these cases are put to the binding directly.
/// </summary>
public sealed class LiveQueryShapeBindingTests : TestBase
{
    [Fact]
    public async Task should_bind_the_filter_to_the_one_parameter_with_the_querys_name()
    {
        // given - the shape "owner equals the parameter named owner"
        await using var host = await LiveEfTestHost.CreateRowLevelAsync();
        await using var scope = host.CreateScope();
        var (shape, sql, layout) = _OwnerShape(scope.ServiceProvider.GetRequiredService<LiveDb>());
        await using var command = new SqliteCommand();
        command.Parameters.AddWithValue("@other", 9);
        command.Parameters.AddWithValue("@owner", 3L);

        // when
        var filter = shape.Bind(command, sql);

        // then - the value was brought to the column's type and decides which rows pass
        filter.Should().NotBeNull();
        filter.Matches(_Row(layout, owner: 3)).Should().BeTrue();
        filter.Matches(_Row(layout, owner: 4)).Should().BeFalse();
    }

    public static TheoryData<string> UnreadableParameters =>
        ["missing", "named twice", "null", "of another kind", "outside the column's range"];

    [Theory]
    [MemberData(nameof(UnreadableParameters))]
    public async Task should_not_bind_the_filter_when_the_parameters_value_is_not_certain(string parameter)
    {
        // given
        await using var host = await LiveEfTestHost.CreateRowLevelAsync();
        await using var scope = host.CreateScope();
        var (shape, sql, _) = _OwnerShape(scope.ServiceProvider.GetRequiredService<LiveDb>());
        await using var command = new SqliteCommand();

        switch (parameter)
        {
            case "named twice":
                command.Parameters.AddWithValue("@owner", 3);
                command.Parameters.AddWithValue("owner", 4);

                break;
            case "null":
                command.Parameters.AddWithValue("@owner", DBNull.Value);

                break;
            case "of another kind":
                command.Parameters.AddWithValue("@owner", "3");

                break;
            case "outside the column's range":
                command.Parameters.AddWithValue("@owner", long.MaxValue);

                break;
            default:
                command.Parameters.AddWithValue("@owner0", 3);

                break;
        }

        // when
        var filter = shape.Bind(command, sql);

        // then - no filter: the caller records the whole table
        filter.Should().BeNull();
    }

    private static (LiveQueryShape Shape, ISqlGenerationHelper Sql, LiveRowLayout Layout) _OwnerShape(LiveDb db)
    {
        var ticket = db.Model.FindEntityType(typeof(Ticket))!;
        var owner = ticket.FindProperty(nameof(Ticket.OwnerId))!;
        var layout = LiveRowLayout.For(ticket);
        var shape = new LiveQueryShape(
            layout,
            [new LiveQueryShape.Condition(layout.IndexOf(owner), owner.ClrType, Value: null, Parameter: "owner")]
        );

        return (shape, db.GetService<ISqlGenerationHelper>(), layout);
    }

    private static LiveRow _Row(LiveRowLayout layout, int owner)
    {
        return layout.ReadMaterialized(property => property.Name is nameof(Ticket.OwnerId) ? owner : null)!;
    }
}
