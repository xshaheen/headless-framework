// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.IO;

namespace Tests.IO;

public sealed class DirectoryHelperTests
{
    [Fact]
    public void create_if_not_exists_creates_missing_directory()
    {
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            DirectoryHelper.CreateIfNotExists(directory);

            Directory.Exists(directory).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory);
            }
        }
    }

    [Fact]
    public void create_if_not_exists_allows_existing_directory()
    {
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            Directory.CreateDirectory(directory);

            DirectoryHelper.CreateIfNotExists(directory);
            DirectoryHelper.CreateIfNotExists(new DirectoryInfo(directory));

            Directory.Exists(directory).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory);
            }
        }
    }
}
