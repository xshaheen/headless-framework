// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.Tus.Internal;
using tusdotnet.Models;

namespace Tests.S3;

public sealed class TusS3KeysTests : TestBase
{
    private const string _FileId = "0f8fad5bd9cb469fa165708d4a5e5e7b";

    private readonly TusS3Keys _keys = new("uploads/");

    [Fact]
    public void should_place_the_finished_object_directly_under_the_prefix()
    {
        _keys.FinishedObject(_FileId).Should().Be("uploads/" + _FileId);
    }

    [Fact]
    public void should_place_the_working_set_under_the_reserved_tus_folder()
    {
        _keys.WorkingSet(_FileId).Should().Be($"uploads/.tus/{_FileId}/");
        _keys.Info(_FileId).Should().Be($"uploads/.tus/{_FileId}/info");
        _keys.Segments(_FileId).Should().Be($"uploads/.tus/{_FileId}/seg/");
    }

    [Fact]
    public void should_place_the_completed_record_outside_the_working_set()
    {
        _keys.CompletedRecord(_FileId).Should().Be("uploads/.tus-completed/" + _FileId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData(".tus")]
    [InlineData(".tus-completed")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData(" leading")]
    [InlineData("trailing ")]
    [InlineData("tab\there")]
    public void should_reject_a_file_id_that_could_reach_other_keys(string fileId)
    {
        // when
        var act = () => _keys.Info(fileId);

        // then
        act.Should().Throw<TusStoreException>().WithMessage("Invalid TUS file id*");
    }

    [Fact]
    public void should_reject_a_null_file_id()
    {
        var act = () => TusS3Keys.EnsureValid(null);

        act.Should().Throw<TusStoreException>();
    }

    [Theory]
    [InlineData(_FileId)]
    [InlineData("upload.bin")]
    [InlineData("a-b_c")]
    public void should_accept_a_file_id_that_stays_in_its_own_keys(string fileId)
    {
        TusS3Keys.EnsureValid(fileId).Should().Be(fileId);
    }
}
