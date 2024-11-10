// Copyright (c) Mahmoud Shaheen, 2021. All rights reserved.

using System.Xml.Linq;

namespace Tests;

public class TestBase
{
    protected static void AssertEquivalentXml(string result, string expected)
    {
        result = XDocument.Parse(result).ToString();
        expected = XDocument.Parse(expected).ToString();
        result.Should().Be(expected);
    }
}
