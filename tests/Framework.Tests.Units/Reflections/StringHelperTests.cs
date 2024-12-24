// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Core;

namespace Tests.Reflections;

public class StringHelperTests
{
    [Fact]
    public void convert_from_bytes_without_bom_should_return_string_without_bom()
    {
        // given
        var bytesWithBom = new byte[] { 0xEF, 0xBB, 0xBF, 0x48, 0x65, 0x6C, 0x6C, 0x6F };
        const string expected = "Hello";

        // when
        var result = StringHelper.ConvertFromBytesWithoutBom(bytesWithBom);

        // then
        result.Should().Be(expected);
    }

    [Fact]
    public void convert_from_bytes_without_bom_should_return_string_with_default_encoding_when_bom_absent()
    {
        // given
        var bytesWithoutBom = "BuildingBlocks"u8.ToArray();
        const string expected = "BuildingBlocks";

        // when
        var result = StringHelper.ConvertFromBytesWithoutBom(bytesWithoutBom);

        // then
        result.Should().Be(expected);
    }
}
