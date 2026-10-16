// Copyright (c) Mahmoud Shaheen. All rights reserved.

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

    public static TheoryData<string> FileChanges =>
        [
            "Id",
            "DisplayName",
            "SavedName",
            "Url",
            "Container",
            "ContentType",
            "UploadedAt",
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
            UploadedAt = _Is(changed, "UploadedAt") ? DateTimeOffset.UnixEpoch.AddDays(1) : DateTimeOffset.UnixEpoch,
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
