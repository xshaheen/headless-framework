// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Text.Json.Serialization;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using File = Headless.Primitives.File;
using Image = Headless.Primitives.Image;

namespace Tests;

public sealed class MediaValueComparerTests : TestBase
{
    [Fact]
    public void file_comparer_should_treat_files_with_equal_members_as_equal()
    {
        var comparer = _Comparer(nameof(MediaRow.Attachment));

        comparer.Equals(_File(), _File()).Should().BeTrue();
        comparer.GetHashCode(_File()).Should().Be(comparer.GetHashCode(_File()));
        comparer.Equals(null, null).Should().BeTrue();
        comparer.Equals(_File(), null).Should().BeFalse();
    }

    [Fact]
    public void file_comparer_should_hash_a_file_whose_id_was_read_as_null()
    {
        var comparer = _Comparer(nameof(MediaRow.Attachment));
        // The column's JSON binds an explicit null to the required Id.
        var json = JsonSerializer.Serialize(_File()).Replace("\"Id\":\"f1\"", "\"Id\":null", StringComparison.Ordinal);
        var file = JsonSerializer.Deserialize<File>(json)!;
        file.Id.Should().BeNull();

        var hash = () => comparer.GetHashCode(file);

        hash.Should().NotThrow();
    }

    public static TheoryData<string> FileChanges =>
        [
            "Id",
            "DisplayName",
            "SavedName",
            "Url",
            "Container",
            "ContentType",
            "UploadedAt",
            "UploadedAtOffset",
            "Length",
            "Private",
            "Md5",
            "Metadata",
        ];

    [Theory]
    [MemberData(nameof(FileChanges))]
    public void file_comparer_should_detect_a_change_to_any_stored_member(string member)
    {
        var comparer = _Comparer(nameof(MediaRow.Attachment));

        comparer.Equals(_File(), _File(member)).Should().BeFalse();
    }

    [Fact]
    public void file_comparer_snapshot_should_detect_in_place_edits_of_container_and_metadata()
    {
        var comparer = _Comparer(nameof(MediaRow.Attachment));
        var current = _File();
        var snapshot = comparer.Snapshot(current);

        current.Container[0] = "other";
        comparer.Equals(current, snapshot).Should().BeFalse();

        current = _File();
        snapshot = comparer.Snapshot(current);
        current.Metadata!["pages"] = 4;
        comparer.Equals(current, snapshot).Should().BeFalse();
    }

    [Fact]
    public void file_comparer_should_compare_metadata_by_stored_json_across_clr_types()
    {
        var comparer = _Comparer(nameof(MediaRow.Attachment));
        var assigned = _File();
        var loaded = _File();
        // A stored int reads back as long and a nested object as JsonElement; both serialize to the same JSON.
        assigned.Metadata!["pages"] = 3;
        loaded.Metadata!["pages"] = 3L;
        assigned.Metadata["dims"] = new Dictionary<string, int>(StringComparer.Ordinal) { ["w"] = 1 };
        loaded.Metadata["dims"] = JsonSerializer.Deserialize<JsonElement>("""{"w":1}""");

        comparer.Equals(assigned, loaded).Should().BeTrue();
    }

    [Fact]
    public void image_comparer_should_compare_image_members_and_snapshot_as_image()
    {
        var comparer = _Comparer(nameof(MediaRow.Picture));
        var image = _Image();

        comparer.Equals(image, _Image()).Should().BeTrue();
        comparer.Equals(image, _Image(width: 65)).Should().BeFalse();
        comparer.Snapshot(image).Should().BeOfType<Image>().Which.Width.Should().Be(64);
    }

    // Every settable member of File and Image, inherited ones included: a member the type gains later joins this list
    // on its own, so the test fails until the comparer compares it and the snapshot copies it.
    public static TheoryData<string, string> SettableMediaMembers
    {
        get
        {
            var data = new TheoryData<string, string>();

            foreach (var type in new[] { typeof(File), typeof(Image) })
            {
                foreach (var property in _SettableMembers(type))
                {
                    data.Add(type.Name, property.Name);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(SettableMediaMembers))]
    public void media_comparer_should_compare_and_snapshot_every_settable_member(string typeName, string member)
    {
        // given
        var isImage = string.Equals(typeName, nameof(Image), StringComparison.Ordinal);
        var comparer = _Comparer(isImage ? nameof(MediaRow.Picture) : nameof(MediaRow.Attachment));
        File original = isImage ? _Image() : _File();
        var property = original.GetType().GetProperty(member)!;
        var changed = _CopyWith(original, property, _ChangedValue(property.PropertyType, property.GetValue(original)));

        // when
        var snapshot = comparer.Snapshot(changed);

        // then
        comparer.Equals(original, changed).Should().BeFalse($"the comparer must compare {typeName}.{member}");
        property
            .GetValue(snapshot)
            .Should()
            .BeEquivalentTo(property.GetValue(changed), $"the snapshot must copy {typeName}.{member}");
    }

    [Fact]
    public void file_comparer_should_not_serialize_when_comparing_or_snapshotting()
    {
        var comparer = _Comparer(nameof(MediaRow.Attachment));
        var left = _File();
        var right = _File();

        var equalsBytes = _AllocatedPerCall(() => comparer.Equals(left, right));
        var snapshotBytes = _AllocatedPerCall(() => comparer.Snapshot(left));

        // The JSON comparer allocated about 1.8 KB per Equals and 2.4 KB per Snapshot for this file.
        equalsBytes.Should().BeLessThan(200);
        snapshotBytes.Should().BeLessThan(800);
    }

    [Fact]
    public async Task should_save_in_place_metadata_edit_and_skip_unchanged_files()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(AbortToken);
        var commands = 0;
        var options = new DbContextOptionsBuilder<MediaDbContext>()
            .UseSqlite(connection)
            .LogTo(_ => commands++, (id, _) => id == RelationalEventId.CommandExecuted, DbContextLoggerOptions.None)
            .Options;
        await using var db = new MediaDbContext(options);
        await db.Database.EnsureCreatedAsync(AbortToken);
        var row = new MediaRow { Attachment = _File(), Picture = _Image() };
        db.Add(row);
        await db.SaveChangesAsync(AbortToken);

        commands = 0;
        await db.SaveChangesAsync(AbortToken);
        commands.Should().Be(0);

        row.Attachment!.Metadata!["pages"] = 9;
        await db.SaveChangesAsync(AbortToken);
        commands.Should().Be(1);

        await using var reader = new MediaDbContext(options);
        var saved = await reader.Set<MediaRow>().SingleAsync(AbortToken);
        saved.Attachment!.Metadata!["pages"].Should().Be(9L);
    }

    private static ValueComparer _Comparer(string property)
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        using var db = new MediaDbContext(new DbContextOptionsBuilder<MediaDbContext>().UseSqlite(connection).Options);

        return db.Model.FindEntityType(typeof(MediaRow))!.FindProperty(property)!.GetValueComparer();
    }

    private static long _AllocatedPerCall(Action call)
    {
        for (var i = 0; i < 100; i++)
        {
            call();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < 1000; i++)
        {
            call();
        }

        return (GC.GetAllocatedBytesForCurrentThread() - before) / 1000;
    }

    private static IEnumerable<PropertyInfo> _SettableMembers(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is { IsPublic: true } && !Attribute.IsDefined(p, typeof(JsonIgnoreAttribute)));

    private static File _CopyWith(File source, PropertyInfo changedProperty, object? changedValue)
    {
        var copy = (File)Activator.CreateInstance(source.GetType())!;

        foreach (var property in _SettableMembers(source.GetType()))
        {
            property.SetValue(copy, property == changedProperty ? changedValue : property.GetValue(source));
        }

        return copy;
    }

    private static object? _ChangedValue(Type type, object? current)
    {
        var valueType = Nullable.GetUnderlyingType(type) ?? type;

        return current switch
        {
            _ when valueType == typeof(string) => (current as string) + "-changed",
            _ when valueType == typeof(string[]) => (string[])[.. (string[]?)current ?? [], "changed"],
            _ when valueType == typeof(DateTimeOffset) => ((DateTimeOffset)current!).AddDays(1),
            _ when valueType == typeof(long) => (long)current! + 1,
            _ when valueType == typeof(int) => ((int?)current ?? 0) + 1,
            _ when valueType == typeof(bool) => !(bool)current!,
            _ when valueType == typeof(Dictionary<string, object?>) => new Dictionary<string, object?>(
                (Dictionary<string, object?>?)current ?? [],
                StringComparer.Ordinal
            )
            {
                ["changed"] = 1,
            },
            _ => throw new InvalidOperationException(
                $"Give {type} a changed value here so the comparer test covers the new member."
            ),
        };
    }

    private static bool _Is(string? changed, string member) => string.Equals(changed, member, StringComparison.Ordinal);

    private static File _File(string? changed = null)
    {
        return new File
        {
            Id = _Is(changed, "Id") ? "f2" : "f1",
            DisplayName = _Is(changed, "DisplayName") ? "other.pdf" : "invoice.pdf",
            SavedName = _Is(changed, "SavedName") ? "1.pdf" : "9f8e7d.pdf",
            Url = _Is(changed, "Url") ? "https://cdn.example.com/other.pdf" : "https://cdn.example.com/9f8e7d.pdf",
            Container = _Is(changed, "Container") ? ["tenants", "t2"] : ["tenants", "t1"],
            ContentType = _Is(changed, "ContentType") ? "text/plain" : "application/pdf",
            UploadedAt =
                _Is(changed, "UploadedAt") ? DateTimeOffset.UnixEpoch.AddDays(1)
                // Same instant, different offset: the stored JSON differs, so the comparer must too.
                : _Is(changed, "UploadedAtOffset") ? DateTimeOffset.UnixEpoch.ToOffset(TimeSpan.FromHours(3))
                : DateTimeOffset.UnixEpoch,
            Length = _Is(changed, "Length") ? 2 : 1,
            Private = !_Is(changed, "Private"),
            Md5 = _Is(changed, "Md5") ? "b" : "a",
            Metadata = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["pages"] = _Is(changed, "Metadata") ? 4 : 3,
                ["source"] = "upload",
            },
        };
    }

    private static Image _Image(int width = 64)
    {
        return new Image
        {
            Id = "i1",
            DisplayName = "logo.png",
            SavedName = "1a2b.png",
            Url = "https://cdn.example.com/1a2b.png",
            Container = ["tenants", "t1"],
            ContentType = "image/png",
            UploadedAt = DateTimeOffset.UnixEpoch,
            Length = 2048,
            Private = false,
            Width = width,
            Height = 64,
            Caption = "logo",
        };
    }

    public sealed class MediaRow
    {
        public int Id { get; set; }

        public File? Attachment { get; set; }

        public Image? Picture { get; set; }
    }

    private sealed class MediaDbContext(DbContextOptions<MediaDbContext> options) : DbContext(options)
    {
        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.AddBuildingBlocksPrimitivesConvertersMappings();
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<MediaRow>();
        }
    }
}
