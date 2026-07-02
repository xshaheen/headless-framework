// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.IO;
using Headless.Testing.Tests;

namespace Tests.IO;

public sealed class FileHelperTests : TestBase
{
    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("..\\escape.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("sub/../../escape.txt")]
    public async Task save_to_local_file_rejects_path_traversal_names(string maliciousName)
    {
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            await using var stream = new MemoryStream("data"u8.ToArray());

            var act = async () => await stream.SaveToLocalFileAsync(maliciousName, directory, AbortToken);

            await act.Should().ThrowAsync<ArgumentException>();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task save_to_local_file_writes_a_safe_name()
    {
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            await using var stream = new MemoryStream("data"u8.ToArray());

            await stream.SaveToLocalFileAsync("safe.txt", directory, AbortToken);

            var savedPath = Path.Combine(directory, "safe.txt");
            File.Exists(savedPath).Should().BeTrue();
            (await File.ReadAllTextAsync(savedPath, AbortToken)).Should().Be("data");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
