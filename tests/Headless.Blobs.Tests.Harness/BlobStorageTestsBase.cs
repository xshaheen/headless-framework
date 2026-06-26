// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Xml.Linq;
using Headless.Blobs;
using Headless.Blobs.Internals;
using Headless.Core;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;

// ReSharper disable AccessToDisposedClosure
namespace Tests;

/// <summary>
/// Cross-provider conformance suite for <see cref="IBlobStorage"/>. Every behavior here must hold for every provider;
/// leaf <c>*.Tests.Integration</c> classes attach <c>[Fact]</c>/<c>[Theory]</c> to <c>public override</c> delegations
/// of these methods. The base intentionally carries no test attributes.
/// </summary>
public abstract class BlobStorageTestsBase : TestBase
{
    /// <summary>The store under test. Each call returns a fresh, disposable instance.</summary>
    protected abstract IBlobStorage GetStorage();

    /// <summary>The top-level container every conformance scenario writes to.</summary>
    protected virtual string ContainerName => "storage";

    /// <summary>A query enumerating the whole <see cref="ContainerName"/> container.</summary>
    protected virtual BlobQuery Container => new(ContainerName);

    /// <summary>
    /// Container name used by the normalization round-trip scenario. Leaves whose <see cref="IBlobNamingNormalizer"/>
    /// transforms container names override this to a form their normalizer maps onto <see cref="ContainerName"/>'s
    /// backing container, so the test proves upload and bulk-delete/info route through the same resolve seam (H1/H2).
    /// Defaults to <see cref="ContainerName"/> (a safe no-op that still exercises the bulk/info round-trip).
    /// </summary>
    protected virtual string NormalizationSensitiveContainer => ContainerName;

    /// <summary>
    /// Whether the store under test exposes container lifecycle management. Capable leaves leave this <see langword="true"/>
    /// and override <see cref="GetContainerManager"/>; backends without lifecycle support (for example Cloudflare R2,
    /// whose buckets are provisioned out-of-band) set it <see langword="false"/> and return <see langword="null"/> from
    /// <see cref="GetContainerManager"/>.
    /// </summary>
    protected virtual bool SupportsContainerManagement => true;

    /// <summary>
    /// The <see cref="IBlobContainerManager"/> for the store under test, resolved by the leaf (constructed directly or
    /// via <c>GetKeyedService&lt;IBlobContainerManager&gt;</c>). Returns <see langword="null"/> when the backend has no
    /// lifecycle capability. The capability is resolved, never cast from <see cref="IBlobStorage"/>.
    /// </summary>
    protected virtual IBlobContainerManager? GetContainerManager() => null;

    /// <summary>Builds a <see cref="BlobLocation"/> in <see cref="ContainerName"/> from the given path segments.</summary>
    private BlobLocation _Loc(params string[] segments) => new(ContainerName, segments);

    #region List / Round-trip

    public virtual async Task can_get_empty_file_list_on_missing_directory()
    {
        await using var storage = GetStorage();

        await ResetAsync(storage);

        var list = await storage.GetBlobsListAsync(new BlobQuery(ContainerName, $"{Guid.NewGuid()}/"));

        list.Should().BeEmpty();
    }

    public virtual async Task can_get_file_list_for_single_folder()
    {
        await using var storage = GetStorage();

        await ResetAsync(storage);

        await storage.UploadContentAsync(_Loc("archived", "archived.txt"), "archived", AbortToken);
        await storage.UploadContentAsync(_Loc("q", "new.txt"), "new", AbortToken);
        await storage.UploadContentAsync(
            _Loc("long", "path", "in", "here", "1.hey.stuff-2.json"),
            "archived",
            AbortToken
        );

        (await storage.GetBlobsListAsync(Container)).Should().HaveCount(3);
        (await storage.GetBlobsListAsync(Container, limit: 1)).Should().ContainSingle();

        // Mid-pattern glob -> client-side glob extension.
        (await _GlobListAsync(storage, "long/path/in/here/*stuff*.json"))
            .Should()
            .ContainSingle();

        // Folder filters -> server-pushed prefix.
        (await storage.GetBlobsListAsync(new BlobQuery(ContainerName, "archived/")))
            .Should()
            .ContainSingle();
        (await storage.GetBlobsListAsync(new BlobQuery(ContainerName, "q/"))).Should().ContainSingle();

        (await storage.GetBlobContentAsync(_Loc("archived", "archived.txt"), AbortToken)).Should().Be("archived");
        (await storage.GetBlobContentAsync(_Loc("q", "new.txt"), AbortToken)).Should().Be("new");
    }

    public virtual async Task can_get_file_list_for_single_file()
    {
        await using var storage = GetStorage();

        await ResetAsync(storage);

        await storage.UploadContentAsync(_Loc("archived", "archived.txt"), "archived", AbortToken);
        await storage.UploadContentAsync(_Loc("archived", "archived.csv"), "archived", AbortToken);
        await storage.UploadContentAsync(_Loc("q", "new.txt"), "new", AbortToken);
        await storage.UploadContentAsync(
            _Loc("long", "path", "in", "here", "1.hey.stuff-2.json"),
            "archived",
            AbortToken
        );

        var list = await _GlobListAsync(storage, "archived/archived.txt");

        list.Should().ContainSingle();
        list[0].BlobKey.Should().Be("archived/archived.txt");
        list[0].Size.Should().BePositive();
        list[0].Created.Should().BeAfter(DateTimeOffset.MinValue);
    }

    public virtual async Task can_get_file_info()
    {
        await using var storage = GetStorage();

        await ResetAsync(storage);

        /* Not exist */
        var missing = _Loc("folder", Guid.NewGuid().ToString());
        (await storage.GetBlobInfoAsync(missing, AbortToken)).Should().BeNull();

        /* Exist one */
        var startTime = DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(1));
        var blobName = $"{Guid.NewGuid()}-nested.txt";
        var location = _Loc("folder", blobName);
        await storage.UploadContentAsync(location, "test", AbortToken);

        var fileInfo = await storage.GetBlobInfoAsync(location, AbortToken);
        fileInfo.Should().NotBeNull();
        fileInfo.BlobKey.Should().Be($"folder/{blobName}");
        fileInfo.Size.Should().BePositive("Should have file size");

        // NOTE: File creation time might not be accurate:
        // http://stackoverflow.com/questions/2109152/unbelievable-strange-file-creation-time-problem
        fileInfo
            .Created.Should()
            .BeAfter(DateTimeOffset.MinValue, "File creation time should be newer than the start time");

        fileInfo
            .Modified.Should()
            .BeOnOrAfter(
                startTime,
                $"File {blobName} modified time {fileInfo.Modified:O} should be newer than the start time {startTime:O}."
            );

        /* Exist multiple */
        blobName = $"{Guid.NewGuid()}-test.txt";
        location = _Loc("folder", blobName);
        await storage.UploadContentAsync(location, "test", AbortToken);
        fileInfo = await storage.GetBlobInfoAsync(location, AbortToken);

        fileInfo.Should().NotBeNull();
        fileInfo.BlobKey.Should().EndWith($"folder/{blobName}", "Incorrect file");
        fileInfo.Size.Should().BePositive("Incorrect file size");

        fileInfo
            .Created.Should()
            .BeOnOrAfter(DateTimeOffset.MinValue, "File creation time should be newer than the start time.");

        fileInfo
            .Modified.Should()
            .BeOnOrAfter(
                startTime,
                $"File {blobName} modified time {fileInfo.Modified:O} should be newer than the start time {startTime:O}."
            );
    }

    public virtual async Task can_get_non_existent_file_info()
    {
        await using var storage = GetStorage();

        await ResetAsync(storage);

        (await storage.GetBlobInfoAsync(_Loc(Guid.NewGuid().ToString()), AbortToken)).Should().BeNull();
    }

    public virtual async Task can_manage_files()
    {
        await using var storage = GetStorage();

        await ResetAsync(storage);

        await storage.UploadContentAsync(_Loc("test.txt"), "test", AbortToken);

        var file = (await storage.GetBlobsListAsync(Container)).Single();
        file.Should().NotBeNull();
        file.BlobKey.Should().Be("test.txt");

        (await storage.GetBlobContentAsync(_Loc("test.txt"), AbortToken)).Should().Be("test");

        (await storage.MoveAsync(_Loc("test.txt"), _Loc("new.txt"), AbortToken)).Should().BeTrue();
        (await storage.GetBlobsListAsync(Container)).Should().ContainSingle(x => x.BlobKey == "new.txt");
        (await storage.DeleteAsync(_Loc("new.txt"), AbortToken)).Should().BeTrue();
        (await storage.GetBlobsListAsync(Container)).Should().BeEmpty();
    }

    public virtual async Task can_move_files()
    {
        await using var storage = GetStorage();

        await ResetAsync(storage);

        // Move across folders
        await storage.UploadContentAsync(_Loc("test.txt"), "test", AbortToken);
        (await storage.MoveAsync(_Loc("test.txt"), _Loc("archive", "new.txt"), AbortToken)).Should().BeTrue();
        (await storage.GetBlobContentAsync(_Loc("archive", "new.txt"), AbortToken)).Should().Be("test");
        (await storage.GetBlobsListAsync(Container)).Should().ContainSingle();

        // Move & overwrite the destination
        await storage.UploadContentAsync(_Loc("test2.txt"), "test2", AbortToken);
        (await storage.MoveAsync(_Loc("test2.txt"), _Loc("archive", "new.txt"), AbortToken)).Should().BeTrue();
        (await storage.GetBlobContentAsync(_Loc("archive", "new.txt"), AbortToken)).Should().Be("test2");
        (await storage.GetBlobsListAsync(Container)).Should().ContainSingle();
    }

    public virtual async Task can_round_trip_seekable_stream()
    {
        await using var storage = GetStorage();

        await ResetAsync(storage);

        var location = _Loc("user.xml");

        // Create a stream of XML
        var element = XElement.Parse("<user>Blake</user>");
        await using var memoryStream = new MemoryStream();
        Logger.LogInformation("Saving xml to stream with position {Position}", memoryStream.Position);
        await element.SaveAsync(memoryStream, SaveOptions.DisableFormatting, AbortToken);
        memoryStream.Seek(0, SeekOrigin.Begin);

        // Save the stream to storage
        Logger.LogInformation("Saving contents with position {Position}", memoryStream.Position);
        await storage.UploadAsync(location, memoryStream, cancellationToken: AbortToken);
        Logger.LogInformation("Saved contents with position {Position}", memoryStream.Position);

        // Download the stream from storage
        await using var downloadResult = await storage.OpenReadStreamAsync(location, AbortToken);
        downloadResult.Should().NotBeNull();
        var actual = XElement.Load(downloadResult.Stream);
        actual.ToString(SaveOptions.DisableFormatting).Should().Be(element.ToString(SaveOptions.DisableFormatting));
    }

    public virtual async Task will_reset_stream_position()
    {
        await using var storage = GetStorage();

        await ResetAsync(storage);

        const string content = "EricBlake";
        var location = _Loc("test.txt");

        await using var memoryStream = new MemoryStream();

        await using (var writer = new StreamWriter(memoryStream, StringHelper.Utf8WithoutBom, 1024, true))
        {
            await writer.WriteAsync(content);
            await writer.FlushAsync(AbortToken);
        }

        // Position stream at an offset (not at the start). Upload must rewind a seekable stream and store the full content.
        memoryStream.Seek(4, SeekOrigin.Begin);
        await storage.UploadAsync(location, memoryStream, cancellationToken: AbortToken);

        (await storage.GetBlobContentAsync(location, AbortToken)).Should().Be(content);
    }

    public virtual async Task can_save_over_existing_stored_content()
    {
        await using var storage = GetStorage();

        await ResetAsync(storage);

        var location = _Loc("test.json");

        var longIdPost = new Post { ProjectId = "1234567890" };
        await storage.UploadContentAsync(location, longIdPost, AbortToken);
        (await storage.GetBlobContentAsync<Post>(location, cancellationToken: AbortToken))
            .Should()
            .BeEquivalentTo(longIdPost);

        var shortIdPost = new Post { ProjectId = "123" };
        await storage.UploadContentAsync(location, shortIdPost, AbortToken);
        (await storage.GetBlobContentAsync<Post>(location, cancellationToken: AbortToken))
            .Should()
            .BeEquivalentTo(shortIdPost);
    }

    public virtual async Task can_concurrently_manage_files()
    {
        await using var storage = GetStorage();

        await ResetAsync(storage);

        // Ensure querying a missing blob returns null and does not throw.
        (await storage.GetBlobInfoAsync(_Loc("nope"), AbortToken))
            .Should()
            .BeNull();

        using var queueItems = new BlockingCollection<int>();

        // Parallel upload 10 files under the "q/" folder.
        await Parallel.ForEachAsync(
            Enumerable.Range(1, 10),
            async (i, cancellationToken) =>
            {
                var projectId = i.ToString(CultureInfo.InvariantCulture);

                var post = new Post
                {
                    ApiVersion = 2,
                    CharSet = "utf8",
                    ContentEncoding = "application/json",
                    Data = "{}"u8.ToArray(),
                    IpAddress = "127.0.0.1",
                    MediaType = "gzip",
                    ProjectId = projectId,
                    UserAgent = "test",
                };

                await storage.UploadContentAsync(
                    new BlobLocation(ContainerName, "q", $"{projectId}.json"),
                    post,
                    cancellationToken
                );

                queueItems.Add(i, cancellationToken);
            }
        );

        (await storage.GetBlobsListAsync(Container)).Should().HaveCount(10);

        await Parallel.ForEachAsync(
            Enumerable.Range(1, 10),
            async (_, _) =>
            {
                var blobName = Random.Shared.GetItem(queueItems).ToString(CultureInfo.InvariantCulture) + ".json";

                var eventPost = await _GetPostAndSetWorkMarkAsync(
                    storage,
                    Faker.Random.Int(0, 25).ToString(CultureInfo.InvariantCulture) + ".json"
                );

                if (eventPost is null)
                {
                    return;
                }

                if (Faker.Random.Bool())
                {
                    await _CompletePostAsync(storage, blobName, eventPost.ProjectId, DateTime.UtcNow);
                }
                else
                {
                    await _DeleteWorkMarkerAsync(storage, blobName);
                }
            }
        );
    }

    #endregion

    #region Token Paging

    public virtual async Task token_paging_round_trips_across_serialization()
    {
        await using var storage = GetStorage();

        await ResetAsync(storage);

        const int total = 5;
        const int pageSize = 2;

        var expected = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < total; i++)
        {
            var key = "page/file-" + i.ToString("00", CultureInfo.InvariantCulture) + ".txt";
            expected.Add(key);
            await storage.UploadContentAsync(new BlobLocation(ContainerName, key), "x", AbortToken);
        }

        var seen = new List<string>();
        string? token = null;
        var guard = 0;

        do
        {
            // The continuation token is an opaque, serializable string (unlike the old closure cursor). Rebuild a
            // fresh BlobQuery from it each iteration to prove it survives a web-request boundary.
            var query = new BlobQuery(ContainerName, prefix: "page/", pageSize: pageSize, continuationToken: token);
            var page = await storage.ListAsync(query, AbortToken);

            seen.AddRange(page.Items.Select(b => b.BlobKey));
            token = page.ContinuationToken;

            guard++;
            guard.Should().BeLessThanOrEqualTo(total + 5, "token paging must terminate");
        } while (token is not null);

        token.Should().BeNull("the final page returns a null continuation token");
        // Tolerate the documented duplicate-across-rescan behaviour of emulated cursors (e.g. Redis HSCAN).
        seen.Distinct(StringComparer.Ordinal).Should().BeEquivalentTo(expected);
    }

    #endregion

    #region Delete by prefix / glob

    public virtual async Task delete_by_prefix_removes_only_matching_blobs()
    {
        await using var storage = GetStorage();

        await ResetAsync(storage);

        await storage.UploadContentAsync(_Loc("keep", "a.txt"), "a", AbortToken);
        await storage.UploadContentAsync(_Loc("drop", "b.txt"), "b", AbortToken);
        await storage.UploadContentAsync(_Loc("drop", "nested", "c.txt"), "c", AbortToken);

        (await storage.DeleteAllAsync(new BlobQuery(ContainerName, "drop/"), AbortToken)).Should().Be(2);

        var remaining = await storage.GetBlobsListAsync(Container);
        remaining.Should().ContainSingle();
        remaining[0].BlobKey.Should().Be("keep/a.txt");
    }

    public virtual async Task can_delete_entire_folder()
    {
        await using var storage = GetStorage();

        await ResetAsync(storage);

        await storage.UploadContentAsync(_Loc("x", "hello.txt"), "hello", AbortToken);
        await storage.UploadContentAsync(_Loc("x", "nested", "world.csv"), "nested world", AbortToken);

        (await storage.GetBlobsListAsync(Container)).Should().HaveCount(2);
        (await storage.DeleteAllAsync(new BlobQuery(ContainerName, "x/"), AbortToken)).Should().Be(2);
        (await storage.GetBlobsListAsync(Container)).Should().BeEmpty();
    }

    public virtual async Task can_delete_entire_folder_with_wildcard()
    {
        await using var storage = GetStorage();

        await ResetAsync(storage);

        await storage.UploadContentAsync(_Loc("x", "hello.txt"), "hello", AbortToken);
        await storage.UploadContentAsync(_Loc("x", "nested", "world.csv"), "nested world", AbortToken);

        (await storage.GetBlobsListAsync(Container)).Should().HaveCount(2);
        (await storage.GetBlobsListAsync(Container, limit: 1)).Should().ContainSingle();
        (await _GlobListAsync(storage, "x/*")).Should().HaveCount(2);
        (await _GlobListAsync(storage, "x/nested/*")).Should().ContainSingle();

        (await _GlobDeleteAsync(storage, "x/*")).Should().Be(2);
        (await storage.GetBlobsListAsync(Container)).Should().BeEmpty();
    }

    public virtual async Task can_delete_folder_with_multi_folder_wildcards()
    {
        await using var storage = GetStorage();

        await ResetAsync(storage);

        const int filesPerMonth = 5;

        for (var year = 2020; year <= 2021; year++)
        {
            for (var month = 1; month <= 12; month++)
            {
                for (var index = 0; index < filesPerMonth; index++)
                {
                    await storage.UploadContentAsync(
                        new BlobLocation(
                            ContainerName,
                            "archive",
                            $"year-{year.ToString("00", CultureInfo.InvariantCulture)}",
                            $"month-{month.ToString("00", CultureInfo.InvariantCulture)}",
                            $"file-{index.ToString("00", CultureInfo.InvariantCulture)}.txt"
                        ),
                        "hello",
                        AbortToken
                    );
                }
            }
        }

        Logger.LogInformation("List by glob: archive/*");
        (await _GlobListAsync(storage, "archive/*")).Should().HaveCount(2 * 12 * filesPerMonth);

        Logger.LogInformation("List by glob: archive/*month-01*");
        (await _GlobListAsync(storage, "archive/*month-01*")).Should().HaveCount(2 * filesPerMonth);

        Logger.LogInformation("List by glob: archive/year-2020/*month-01*");
        (await _GlobListAsync(storage, "archive/year-2020/*month-01*")).Should().HaveCount(filesPerMonth);

        Logger.LogInformation("Delete by glob: archive/*month-01*");
        (await _GlobDeleteAsync(storage, "archive/*month-01*")).Should().Be(2 * filesPerMonth);
        (await storage.GetBlobsListAsync(Container)).Should().HaveCount(2 * 11 * filesPerMonth);
    }

    public virtual async Task can_delete_specific_files()
    {
        await using var storage = GetStorage();

        await ResetAsync(storage);

        await storage.UploadContentAsync(_Loc("x", "hello.txt"), "hello", AbortToken);
        await storage.UploadContentAsync(_Loc("x", "nested", "world.csv"), "nested world", AbortToken);
        await storage.UploadContentAsync(_Loc("x", "nested", "hello.txt"), "nested hello", AbortToken);

        (await storage.GetBlobsListAsync(Container)).Should().HaveCount(3);
        (await _GlobListAsync(storage, "x/*")).Should().HaveCount(3);
        (await _GlobListAsync(storage, "x/nested/*")).Should().HaveCount(2);
        (await _GlobListAsync(storage, "x/*.txt")).Should().HaveCount(2);

        (await _GlobDeleteAsync(storage, "x/*.txt")).Should().Be(2);

        (await storage.GetBlobsListAsync(Container)).Should().ContainSingle();
        (await storage.ExistsAsync(_Loc("x", "hello.txt"), AbortToken)).Should().BeFalse();
        (await storage.ExistsAsync(_Loc("x", "nested", "hello.txt"), AbortToken)).Should().BeFalse();
        (await storage.ExistsAsync(_Loc("x", "nested", "world.csv"), AbortToken)).Should().BeTrue();
    }

    public virtual async Task can_delete_nested_folder()
    {
        await using var storage = GetStorage();

        await ResetAsync(storage);

        await storage.UploadContentAsync(_Loc("x", "hello.txt"), "hello", AbortToken);
        await storage.UploadContentAsync(_Loc("x", "nested", "world.csv"), "nested world", AbortToken);
        await storage.UploadContentAsync(_Loc("x", "nested", "hello.txt"), "nested hello", AbortToken);

        (await storage.GetBlobsListAsync(Container)).Should().HaveCount(3);

        (await storage.DeleteAllAsync(new BlobQuery(ContainerName, "x/nested/"), AbortToken)).Should().Be(2);

        (await storage.GetBlobsListAsync(Container)).Should().ContainSingle();
        (await storage.ExistsAsync(_Loc("x", "hello.txt"), AbortToken)).Should().BeTrue();
        (await storage.ExistsAsync(_Loc("x", "nested", "hello.txt"), AbortToken)).Should().BeFalse();
        (await storage.ExistsAsync(_Loc("x", "nested", "world.csv"), AbortToken)).Should().BeFalse();
    }

    public virtual async Task can_delete_specific_files_in_nested_folder()
    {
        await using var storage = GetStorage();

        await ResetAsync(storage);

        await storage.UploadContentAsync(_Loc("x", "hello.txt"), "hello", AbortToken);
        await storage.UploadContentAsync(_Loc("x", "world.csv"), "world", AbortToken);
        await storage.UploadContentAsync(_Loc("x", "nested", "world.csv"), "nested world", AbortToken);
        await storage.UploadContentAsync(_Loc("x", "nested", "hello.txt"), "nested hello", AbortToken);
        await storage.UploadContentAsync(_Loc("x", "nested", "again.txt"), "nested again", AbortToken);

        (await storage.GetBlobsListAsync(Container)).Should().HaveCount(5);
        (await _GlobListAsync(storage, "x/*")).Should().HaveCount(5);
        (await _GlobListAsync(storage, "x/nested/*")).Should().HaveCount(3);
        (await _GlobListAsync(storage, "x/*.txt")).Should().HaveCount(3);

        (await _GlobDeleteAsync(storage, "x/nested/*.txt")).Should().Be(2);

        (await storage.GetBlobsListAsync(Container)).Should().HaveCount(3);
        (await storage.ExistsAsync(_Loc("x", "hello.txt"), AbortToken)).Should().BeTrue();
        (await storage.ExistsAsync(_Loc("x", "world.csv"), AbortToken)).Should().BeTrue();
        (await storage.ExistsAsync(_Loc("x", "nested", "hello.txt"), AbortToken)).Should().BeFalse();
        (await storage.ExistsAsync(_Loc("x", "nested", "again.txt"), AbortToken)).Should().BeFalse();
        (await storage.ExistsAsync(_Loc("x", "nested", "world.csv"), AbortToken)).Should().BeTrue();
    }

    #endregion

    #region Metadata / Move with metadata

    public virtual async Task metadata_round_trips_and_sidecar_is_hidden()
    {
        await using var storage = GetStorage();

        await ResetAsync(storage);

        var location = _Loc("meta", "doc.txt");

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["author"] = "blake",
            ["category"] = "report",
        };

        await using (var content = new MemoryStream("hello"u8.ToArray()))
        {
            await storage.UploadAsync(location, content, metadata, AbortToken);
        }

        // GetBlobInfoAsync surfaces the stored metadata.
        var info = await storage.GetBlobInfoAsync(location, AbortToken);
        info.Should().NotBeNull();
        info.Metadata.Should().NotBeNull();
        info.Metadata!.ContainsKey("author").Should().BeTrue();
        info.Metadata!["author"].Should().Be("blake");
        info.Metadata!["category"].Should().Be("report");

        // OpenReadStreamAsync surfaces the stored metadata.
        await using (var download = await storage.OpenReadStreamAsync(location, AbortToken))
        {
            download.Should().NotBeNull();
            download.Metadata.Should().NotBeNull();
            download.Metadata!["author"].Should().Be("blake");
        }

        // A listing never surfaces a sidecar companion as a blob.
        var listed = await storage.GetBlobsListAsync(Container);
        listed.Should().ContainSingle();
        listed[0].BlobKey.Should().Be("meta/doc.txt");
        listed.Should().NotContain(b => b.BlobKey.EndsWith(BlobStorageHelpers.SidecarSuffix, StringComparison.Ordinal));

        // Deleting the blob removes its sidecar; re-uploading the same key without metadata resurrects nothing.
        (await storage.DeleteAsync(location, AbortToken))
            .Should()
            .BeTrue();

        await using (var content2 = new MemoryStream("world"u8.ToArray()))
        {
            await storage.UploadAsync(location, content2, metadata: null, AbortToken);
        }

        var info2 = await storage.GetBlobInfoAsync(location, AbortToken);
        info2.Should().NotBeNull();
        (info2.Metadata is null || info2.Metadata.Count == 0)
            .Should()
            .BeTrue("re-uploading without metadata must not resurrect the deleted sidecar");
    }

    public virtual async Task move_relocates_blob_and_metadata()
    {
        await using var storage = GetStorage();

        await ResetAsync(storage);

        var source = _Loc("src", "doc.txt");
        var destination = _Loc("dst", "moved.txt");

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal) { ["k"] = "v" };

        await using (var content = new MemoryStream("payload"u8.ToArray()))
        {
            await storage.UploadAsync(source, content, metadata, AbortToken);
        }

        (await storage.MoveAsync(source, destination, AbortToken)).Should().BeTrue();

        (await storage.ExistsAsync(source, AbortToken)).Should().BeFalse("the source is gone after a move");
        (await storage.ExistsAsync(destination, AbortToken)).Should().BeTrue();
        (await storage.GetBlobContentAsync(destination, AbortToken)).Should().Be("payload");

        var info = await storage.GetBlobInfoAsync(destination, AbortToken);
        info.Should().NotBeNull();
        info.Metadata.Should().NotBeNull();
        info.Metadata!["k"].Should().Be("v");
    }

    #endregion

    #region Normalization round-trip

    public virtual async Task normalization_round_trips_through_bulk_and_info()
    {
        await using var storage = GetStorage();

        await ResetAsync(storage);

        // Upload, then bulk-delete and GetBlobInfo using the *same* (possibly un-normalized) container. Every op must
        // route through the same resolve seam, so the un-normalized name still hits the real blob (H1/H2). With the
        // default NormalizationSensitiveContainer == ContainerName this still proves the bulk/info round-trip.
        var container = NormalizationSensitiveContainer;
        var location = new BlobLocation(container, "norm.txt");

        await storage.UploadContentAsync(location, "data", AbortToken);

        (await storage.GetBlobInfoAsync(location, AbortToken)).Should().NotBeNull();

        var results = await storage.BulkDeleteAsync(container, ["norm.txt"], AbortToken);
        results.Should().ContainSingle();
        results[0].Result.IsSuccess.Should().BeTrue();
        results[0]
            .Result.Value.Should()
            .BeTrue("bulk delete must normalize the container the same way the upload did (H1)");
        (await storage.ExistsAsync(location, AbortToken)).Should().BeFalse();
    }

    #endregion

    #region Bulk operations

    public virtual async Task bulk_upload_reports_per_blob_results()
    {
        await using var storage = GetStorage();

        await ResetAsync(storage);

        await using var stream1 = new MemoryStream("one"u8.ToArray());
        await using var stream2 = new MemoryStream("two"u8.ToArray());

        IReadOnlyCollection<BlobUploadRequest> blobs =
        [
            new BlobUploadRequest("one.txt", stream1),
            new BlobUploadRequest("two.txt", stream2),
        ];

        var results = await storage.BulkUploadAsync(ContainerName, blobs, AbortToken);

        results.Should().HaveCount(2);
        results.Should().OnlyContain(r => r.Result.IsSuccess);
        results.Select(r => r.Location.Path).Should().BeEquivalentTo(["one.txt", "two.txt"]);
        (await storage.GetBlobContentAsync(_Loc("one.txt"), AbortToken)).Should().Be("one");
        (await storage.GetBlobContentAsync(_Loc("two.txt"), AbortToken)).Should().Be("two");
    }

    public virtual async Task bulk_upload_failure_does_not_abort_batch()
    {
        await using var storage = GetStorage();

        await ResetAsync(storage);

        await using var ok1 = new MemoryStream("a"u8.ToArray());
        await using var bad = new MemoryStream("b"u8.ToArray());
        await using var ok2 = new MemoryStream("c"u8.ToArray());

        // The "../" entry cannot form a valid BlobLocation; it fails as a per-entry result without aborting the batch.
        IReadOnlyCollection<BlobUploadRequest> blobs =
        [
            new BlobUploadRequest("good-1.txt", ok1),
            new BlobUploadRequest("../escape.txt", bad),
            new BlobUploadRequest("good-2.txt", ok2),
        ];

        var results = await storage.BulkUploadAsync(ContainerName, blobs, AbortToken);

        results.Should().HaveCount(3);
        results.Count(r => r.Result.IsSuccess).Should().Be(2);
        results
            .Count(r => r.Result.IsFailure)
            .Should()
            .Be(1, "a path-traversal entry must fail without aborting the batch");
        (await storage.GetBlobContentAsync(_Loc("good-1.txt"), AbortToken)).Should().Be("a");
        (await storage.GetBlobContentAsync(_Loc("good-2.txt"), AbortToken)).Should().Be("c");
    }

    public virtual async Task bulk_delete_reports_per_entry_results()
    {
        await using var storage = GetStorage();

        await ResetAsync(storage);

        await storage.UploadContentAsync(_Loc("present.txt"), "present", AbortToken);

        var results = await storage.BulkDeleteAsync(ContainerName, ["present.txt", "absent.txt"], AbortToken);

        results.Should().HaveCount(2);

        // Correlate by identity (the new contract), not by position.
        var byPath = results.ToDictionary(r => r.Location.Path, r => r.Result, StringComparer.Ordinal);
        byPath["present.txt"].IsSuccess.Should().BeTrue();
        byPath["present.txt"].Value.Should().BeTrue("the blob existed and was deleted");
        byPath["absent.txt"].IsSuccess.Should().BeTrue();
        byPath["absent.txt"].Value.Should().BeFalse("the blob did not exist");
        (await storage.ExistsAsync(_Loc("present.txt"), AbortToken)).Should().BeFalse();
    }

    public virtual async Task bulk_delete_reports_each_blob_by_identity()
    {
        await using var storage = GetStorage();

        await ResetAsync(storage);

        const int count = 12;

        // Upload only the even-indexed entries; the odd ones are never created. Each result correlates to its input by
        // BlobLocation identity (not position): even -> Ok(true) deleted, odd -> Ok(false) not found.
        var names = new string[count];

        for (var i = 0; i < count; i++)
        {
            names[i] = "entry-" + i.ToString("00", CultureInfo.InvariantCulture) + ".txt";

            if (i % 2 == 0)
            {
                await storage.UploadContentAsync(_Loc(names[i]), "x", AbortToken);
            }
        }

        var results = await storage.BulkDeleteAsync(ContainerName, names, AbortToken);

        results.Should().HaveCount(count);

        var byPath = results.ToDictionary(r => r.Location.Path, r => r.Result, StringComparer.Ordinal);

        for (var i = 0; i < count; i++)
        {
            byPath.Should().ContainKey(names[i]);
            byPath[names[i]].IsSuccess.Should().BeTrue("entry {0} delete should not error", i);
            byPath[names[i]].Value.Should().Be(i % 2 == 0, "result for {0} must reflect whether it existed", names[i]);
        }
    }

    #endregion

    #region Container management capability

    public virtual async Task container_management_capability_matches_support_flag()
    {
        await using var storage = GetStorage();

        if (!SupportsContainerManagement)
        {
            // The capability is honestly absent (resolved, not cast) — e.g. Cloudflare R2.
            GetContainerManager().Should().BeNull("the backend does not support container management");
            return;
        }

        var manager = GetContainerManager();
        manager.Should().NotBeNull("SupportsContainerManagement is true");

        // EnsureContainerAsync creates the container and is idempotent.
        await manager.EnsureContainerAsync(ContainerName, AbortToken);
        await manager.EnsureContainerAsync(ContainerName, AbortToken);
        (await manager.ContainerExistsAsync(ContainerName, AbortToken)).Should().BeTrue();

        // After ensuring, the container is usable for data-plane operations.
        await ResetAsync(storage);
        await storage.UploadContentAsync(_Loc("ensure.txt"), "ok", AbortToken);
        (await storage.GetBlobContentAsync(_Loc("ensure.txt"), AbortToken)).Should().Be("ok");
    }

    #endregion

    #region Empty / missing container (no throw)

    public virtual async Task can_call_delete_all_async_with_empty_container()
    {
        await using var storage = GetStorage();

        await ResetAsync(storage);

        var query = new BlobQuery(Faker.Random.String2(5, 25));

        var action = () => storage.DeleteAllAsync(query, AbortToken).AsTask();

        await action.Should().NotThrowAsync();
    }

    public virtual async Task can_call_delete_with_empty_container()
    {
        await using var storage = GetStorage();

        await ResetAsync(storage);

        var location = new BlobLocation(Faker.Random.String2(5, 25), Faker.Random.String2(5, 25));

        var action = () => storage.DeleteAsync(location, AbortToken).AsTask();

        await action.Should().NotThrowAsync();
    }

    public virtual async Task can_call_bulk_Delete_with_empty_container()
    {
        await using var storage = GetStorage();

        await ResetAsync(storage);

        var container = Faker.Random.String2(5, 25);
        IReadOnlyCollection<string> paths = [Faker.Random.String2(5, 25)];

        var action = () => storage.BulkDeleteAsync(container, paths, AbortToken).AsTask();

        await action.Should().NotThrowAsync();
    }

    public virtual async Task can_call_move_with_empty_container()
    {
        await using var storage = GetStorage();

        await ResetAsync(storage);

        var source = new BlobLocation(Faker.Random.String2(5, 25), Faker.Random.String2(5, 25));
        var destination = new BlobLocation(Faker.Random.String2(5, 25), Faker.Random.String2(5, 25));

        var action = () => storage.MoveAsync(source, destination, AbortToken).AsTask();

        await action.Should().NotThrowAsync();
    }

    public virtual async Task can_call_copy_with_empty_container()
    {
        await using var storage = GetStorage();

        await ResetAsync(storage);

        var source = new BlobLocation(Faker.Random.String2(5, 25), Faker.Random.String2(5, 25));
        var destination = new BlobLocation(Faker.Random.String2(5, 25), Faker.Random.String2(5, 25));

        var action = () => storage.CopyAsync(source, destination, AbortToken).AsTask();

        await action.Should().NotThrowAsync();
    }

    public virtual async Task can_call_exists_with_empty_container()
    {
        await using var storage = GetStorage();

        await ResetAsync(storage);

        var location = new BlobLocation(Faker.Random.String2(5, 25), Faker.Random.String2(5, 25));

        var action = () => storage.ExistsAsync(location, AbortToken).AsTask();

        await action.Should().NotThrowAsync();
    }

    public virtual async Task can_call_download_with_empty_container()
    {
        await using var storage = GetStorage();

        await ResetAsync(storage);

        var location = new BlobLocation(Faker.Random.String2(5, 25), Faker.Random.String2(5, 25));

        var action = () => storage.OpenReadStreamAsync(location, AbortToken).AsTask();

        await action.Should().NotThrowAsync();
    }

    public virtual async Task can_call_get_blob_info_with_empty_container()
    {
        await using var storage = GetStorage();

        await ResetAsync(storage);

        var location = new BlobLocation(Faker.Random.String2(5, 25), Faker.Random.String2(5, 25));

        var action = () => storage.GetBlobInfoAsync(location, AbortToken).AsTask();

        await action.Should().NotThrowAsync();
    }

    public virtual async Task can_call_list_with_empty_container()
    {
        await using var storage = GetStorage();

        await ResetAsync(storage);

        var query = new BlobQuery(Faker.Random.String2(5, 25));

        var action = () => storage.ListAsync(query, AbortToken).AsTask();

        await action.Should().NotThrowAsync();
    }

    #endregion

    #region Path Traversal & Construction Security Tests

    public virtual Task blob_location_with_traversal_path_throws(string path)
    {
        FluentActions
            .Invoking(() => new BlobLocation(ContainerName, path))
            .Should()
            .Throw<ArgumentException>()
            .WithParameterName(nameof(path));

        return Task.CompletedTask;
    }

    public virtual Task blob_location_with_traversal_container_throws()
    {
        FluentActions
            .Invoking(() => new BlobLocation("uploads/../../etc", "passwd"))
            .Should()
            .Throw<ArgumentException>()
            .WithParameterName("container");

        return Task.CompletedTask;
    }

    public virtual Task blob_location_with_control_characters_throws()
    {
        // ReSharper disable once VariableLengthStringHexEscapeSequence
        // ReSharper disable once CanSimplifyStringEscapeSequence
        FluentActions
            .Invoking(() => new BlobLocation(ContainerName, "file\x00name.txt"))
            .Should()
            .Throw<ArgumentException>()
            .WithParameterName("path");

        return Task.CompletedTask;
    }

    public virtual Task blob_location_with_absolute_path_throws(string path)
    {
        FluentActions
            .Invoking(() => new BlobLocation(ContainerName, path))
            .Should()
            .Throw<ArgumentException>()
            .WithParameterName(nameof(path));

        return Task.CompletedTask;
    }

    public virtual Task blob_location_with_reserved_sidecar_suffix_throws()
    {
        FluentActions
            .Invoking(() => new BlobLocation(ContainerName, "report" + BlobStorageHelpers.SidecarSuffix))
            .Should()
            .Throw<ArgumentException>()
            .WithParameterName("path");

        return Task.CompletedTask;
    }

    public virtual Task blob_query_with_traversal_prefix_throws(string prefix)
    {
        FluentActions
            .Invoking(() => new BlobQuery(ContainerName, prefix))
            .Should()
            .Throw<ArgumentException>()
            .WithParameterName(nameof(prefix));

        return Task.CompletedTask;
    }

    public virtual Task blob_query_with_empty_container_throws()
    {
        FluentActions
            .Invoking(() => new BlobQuery(string.Empty))
            .Should()
            .Throw<ArgumentException>()
            .WithParameterName("container");

        return Task.CompletedTask;
    }

    public virtual async Task bulk_delete_with_traversal_path_reports_failure()
    {
        await using var storage = GetStorage();

        await ResetAsync(storage);

        await storage.UploadContentAsync(_Loc("ok.txt"), "ok", AbortToken);

        // A "../" item cannot form a valid BlobLocation; it surfaces as a per-entry failure, not a thrown exception,
        // and must not abort the rest of the batch.
        var results = await storage.BulkDeleteAsync(ContainerName, ["ok.txt", "../escape.txt"], AbortToken);

        results.Should().HaveCount(2);
        results.Count(r => r.Result.IsSuccess && r.Result.Value).Should().Be(1, "the valid entry is deleted");
        results
            .Count(r => r.Result.IsFailure)
            .Should()
            .Be(1, "the path-traversal entry fails without aborting the batch");
    }

    #endregion

    protected async Task ResetAsync(IBlobStorage? storage)
    {
        if (storage is null)
        {
            return;
        }

        // Ensure the container exists for backends that no longer auto-create it on upload. R2-style backends without
        // lifecycle support (GetContainerManager() == null) rely on out-of-band provisioning by the leaf fixture.
        var manager = GetContainerManager();

        if (manager is not null)
        {
            await manager.EnsureContainerAsync(ContainerName);
        }

        Logger.LogInformation("Deleting all files...");
        await storage.DeleteAllAsync(Container);

        Logger.LogInformation("Asserting empty files...");
        var list = await storage.GetBlobsListAsync(Container, limit: 10000);

        list.Should().BeEmpty();
    }

    #region Helpers

    private async Task<IReadOnlyList<BlobInfo>> _GlobListAsync(IBlobStorage storage, string globPattern)
    {
        var files = new List<BlobInfo>();

        await foreach (var blob in storage.GetBlobsAsync(new BlobQuery(ContainerName), globPattern, AbortToken))
        {
            files.Add(blob);
        }

        return files;
    }

    private async Task<int> _GlobDeleteAsync(IBlobStorage storage, string globPattern)
    {
        var keys = new List<string>();

        await foreach (var blob in storage.GetBlobsAsync(new BlobQuery(ContainerName), globPattern, AbortToken))
        {
            keys.Add(blob.BlobKey);
        }

        if (keys.Count == 0)
        {
            return 0;
        }

        var results = await storage.BulkDeleteAsync(ContainerName, keys, AbortToken);

        return results.Count(r => r.Result.IsSuccess && r.Result.Value);
    }

    private async Task _AddWorkMarkerIfNotExistAsync(IBlobStorage storage, string path)
    {
        var marker = _Loc(path + ".x");

        if (!await storage.ExistsAsync(marker, AbortToken))
        {
            await storage.UploadContentAsync(marker, string.Empty, AbortToken);
        }
    }

    private async Task _DeleteWorkMarkerAsync(IBlobStorage storage, string path)
    {
        try
        {
            _ = await storage.DeleteAsync(_Loc(path + ".x"), AbortToken);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error deleting work marker {Path}", path);
        }
    }

    private async Task<Post?> _GetPostAndSetWorkMarkAsync(IBlobStorage storage, string path)
    {
        Post? eventPost;

        try
        {
            eventPost = await storage.GetBlobContentAsync<Post?>(_Loc(path), cancellationToken: AbortToken);

            if (eventPost is null)
            {
                return null;
            }

            await _AddWorkMarkerIfNotExistAsync(storage, path);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error retrieving event post data {Path}", path);

            return null;
        }

        return eventPost;
    }

    private async Task _CompletePostAsync(
        IBlobStorage storage,
        string path,
        string? projectId,
        DateTime created,
        bool shouldArchive = true
    )
    {
        // Don't move files that are already in the archive.
        if (path.StartsWith("archive", StringComparison.Ordinal))
        {
            return;
        }

        var archivePath =
            $"archive/{projectId}/{created.ToString("yy/MM/dd", CultureInfo.InvariantCulture)}/{Path.GetFileName(path)}";

        try
        {
            if (shouldArchive)
            {
                if (!await storage.MoveAsync(_Loc(path), _Loc(archivePath), AbortToken))
                {
                    return;
                }
            }
            else
            {
                if (!await storage.DeleteAsync(_Loc(path), AbortToken))
                {
                    return;
                }
            }
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error archiving event post data {Path}", path);

            return;
        }

        await _DeleteWorkMarkerAsync(storage, path);
    }

    private sealed record Post
    {
        public int ApiVersion { get; init; }

        public string? CharSet { get; init; }

        public string? ContentEncoding { get; init; }

        public byte[]? Data { get; init; }

        public string? IpAddress { get; init; }

        public string? MediaType { get; init; }

        public string? ProjectId { get; init; }

        public string? UserAgent { get; init; }
    }

    #endregion
}
