// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings;

namespace Tests.Models;

public sealed class SettingDefinitionTests
{
    [Fact]
    public void should_default_display_name_to_name_and_reject_null_display_name()
    {
        // given
        var definition = new SettingDefinition("App.Theme");

        // when
        var setNull = () => definition.DisplayName = null!;

        // then
        definition.DisplayName.Should().Be("App.Theme");
        setNull.Should().Throw<ArgumentNullException>();
    }
}
