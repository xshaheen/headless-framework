// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Headless.Search;
using Headless.Testing.Tests;
using Npgsql;

namespace Tests;

/// <summary>
/// The database function and the C# normalizer are two renderings of one rule table; these tests require them to
/// produce byte-identical keys, and the database to produce the hand-checked corpus keys.
/// </summary>
[Collection<PostgreSqlSearchFixture>]
public sealed class ArabicSearchParityTests(PostgreSqlSearchFixture fixture) : TestBase
{
    [Fact]
    public async Task should_match_the_csharp_normalizer_for_every_bmp_character_alone_and_inside_a_word()
    {
        await _AssertParityAsync(ArabicSearchCorpus.EveryBmpCharacter());
    }

    [Fact]
    public async Task should_match_the_csharp_normalizer_for_characters_outside_the_bmp()
    {
        await _AssertParityAsync(ArabicSearchCorpus.SupplementaryCharacters());
    }

    [Fact]
    public async Task should_match_the_csharp_normalizer_for_seeded_random_strings()
    {
        await _AssertParityAsync(ArabicSearchCorpus.Random(10_000, seed: 20261029));
    }

    [Fact]
    public async Task should_match_the_csharp_normalizer_for_strings_longer_than_its_stack_buffer()
    {
        await _AssertParityAsync(ArabicSearchCorpus.LongStrings());
    }

    [Fact]
    public async Task should_build_the_hand_checked_key_for_every_golden_corpus_row()
    {
        var rows = ArabicSearchCorpus.V1;
        var keys = await _NormalizeInDatabaseAsync([.. rows.Select(static r => r.Input)]);

        var mismatches = rows.Select((row, i) => (row, key: keys[i]))
            .Where(static x => !string.Equals(x.row.Expected, x.key, StringComparison.Ordinal))
            .Select(static x =>
                $"{x.row}: {_Escape(x.row.Input)} -> {_Escape(x.key)}, expected {_Escape(x.row.Expected)}"
            )
            .ToList();

        mismatches.Should().BeEmpty();
    }

    [Fact]
    public async Task should_return_null_when_value_is_null()
    {
        await using var connection = await _OpenAsync();
        await using var command = new NpgsqlCommand("SELECT headless.ar_norm_v1(NULL)", connection);

        (await command.ExecuteScalarAsync(AbortToken)).Should().Be(DBNull.Value);
    }

    private async Task _AssertParityAsync(IReadOnlyList<string> inputs)
    {
        var keys = await _NormalizeInDatabaseAsync(inputs);

        var mismatches = inputs
            .Select(
                (input, i) =>
                    (input, sql: keys[i], csharp: ArabicSearchNormalizer.Normalize(input, ArabicSearchVersion.V1))
            )
            .Where(static x => !string.Equals(x.sql, x.csharp, StringComparison.Ordinal))
            .Take(20)
            .Select(static x => $"{_Escape(x.input)}: sql {_Escape(x.sql)}, c# {_Escape(x.csharp)}")
            .ToList();

        mismatches.Should().BeEmpty();
    }

    private async Task<string[]> _NormalizeInDatabaseAsync(IReadOnlyList<string> inputs)
    {
        await using var connection = await _OpenAsync();
        var keys = new string[inputs.Count];

        // One round trip per chunk keeps each parameter a few megabytes.
        foreach (var chunk in inputs.Select((value, index) => (value, index)).Chunk(20_000))
        {
            await using var command = new NpgsqlCommand(
                "SELECT t.i, headless.ar_norm_v1(t.v) FROM unnest(@values) WITH ORDINALITY AS t(v, i)",
                connection
            );
            command.Parameters.AddWithValue("values", chunk.Select(static c => c.value).ToArray());
            await using var reader = await command.ExecuteReaderAsync(AbortToken);

            while (await reader.ReadAsync(AbortToken))
            {
                var ordinal = (int)reader.GetInt64(0) - 1;
                keys[chunk[ordinal].index] = reader.GetString(1);
            }
        }

        return keys;
    }

    private async Task<NpgsqlConnection> _OpenAsync()
    {
        var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        return connection;
    }

    private static string _Escape(string? value)
    {
        if (value is null)
        {
            return "null";
        }

        var builder = new StringBuilder("\"");

        foreach (var c in value)
        {
            builder.Append(c is >= ' ' and <= '~' ? c.ToString() : $"\\u{(int)c:X4}");
        }

        return builder.Append('"').ToString();
    }
}
