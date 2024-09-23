// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.BuildingBlocks.Helpers.Ar;

namespace Tests.Extensions.System;

public sealed class StringExtensionsSearchStringTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("    ")]
    [InlineData(" \n\n\r\n ")]
    public void SearchString__should_returns_white_spaces_with_no_changes(string? value)
    {
        _Test(value, string.Empty);
    }

    [Theory]
    [InlineData("٠", "0")]
    [InlineData("١", "1")]
    [InlineData("٢", "2")]
    [InlineData("٩", "9")]
    [InlineData("١٢٨", "128")]
    [InlineData("This ١٢٨", "this 128")]
    public void SearchString__should_change_arabic_numeral_to_arabic_latin_numeral(string value, string expected)
    {
        _Test(value, expected);
    }

    [Theory]
    // Alef
    [InlineData("آ", "ا")]
    [InlineData("إ", "ا")]
    [InlineData("أ", "ا")]
    // Common spelling error
    [InlineData("ة", "ه")]
    [InlineData("ى", "ي")]
    // Kaf like
    [InlineData("ڮ", "ك")]
    [InlineData("ػ", "ك")]
    [InlineData("ڪ", "ك")]
    [InlineData("ڴ", "ك")]
    // Waw like
    [InlineData("ۈ", "و")]
    //
    [InlineData("ؠ", "ي")]
    [InlineData("ۮ", "د")]
    [InlineData("ﺞ", "ج")]
    [InlineData("ﺑ", "ب")]
    [InlineData("ۻ", "ض")]
    public void SearchString__should_replace_equivalent_characters_with_one_shape(string value, string expected)
    {
        _Test(value, expected);
    }

    [Theory]
    [InlineData(ArabicLetters.Semicolon)]
    [InlineData(ArabicLetters.StarOfRubElHizb)]
    [InlineData(ArabicLetters.EndOfAyah)]
    [InlineData(ArabicLetters.Comma)]
    [InlineData('?')]
    [InlineData('۩')]
    [InlineData('﴾')]
    [InlineData('؏')]
    [InlineData('؁')]
    [InlineData('؃')]
    public void SearchString__should_remove_punctuation_and_ornaments(char value)
    {
        _Test(value.ToString(), "");
    }

    [Theory]
    [InlineData("ء", "ء")]
    [InlineData(" محمود ", "محمود")]
    [InlineData("أحمد", "احمد")]
    [InlineData("إحمد", "احمد")]
    [InlineData("آحمد", "احمد")]
    [InlineData("يمني", "يمني")]
    [InlineData("ىمنى", "يمني")]
    [InlineData("شاطئ", "شاطي")]
    [InlineData("لؤ", "لو")]
    [InlineData("بسم الله الرحمن الرحيم", "بسم الله الرحمن الرحيم")]
    [InlineData("بِسْمِ اللَّهِ الرَّحْمَنِ الرَّحِيمِ", "بسم الله الرحمن الرحيم")]
    [InlineData("بِسْمِ اللَّـهِ الرَّحْمَـٰنِ الرَّحِيمِ", "بسم الله الرحمن الرحيم")]
    public void SearchString__should_work_with_arabic(string value, string expected)
    {
        _Test(value, expected);
    }

    [Theory]
    [InlineData("m", "m")]
    [InlineData(" Mahmoud ", "mahmoud")]
    [InlineData(" Mahmoud Shaheen", "mahmoud shaheen")]
    [InlineData("crème brûlée", "creme brulee")]
    public void SearchString__should_work_with_latin(string value, string expected)
    {
        _Test(value, expected);
    }

    private void _Test(string? value, string? expected)
    {
        // act
        var result = value.SearchString();

        _output.WriteLine($"result   =>{result}");
        _output.WriteLine($"expected =>{expected}");

        // assert
        result.Should().Be(expected);
    }
}
