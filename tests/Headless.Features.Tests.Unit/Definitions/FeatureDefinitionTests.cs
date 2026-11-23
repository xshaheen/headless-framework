// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features;
using Headless.Testing.Tests;

namespace Tests.Definitions;

public sealed class FeatureDefinitionTests : TestBase
{
    [Fact]
    public void should_detach_only_the_named_child_when_removing_a_feature()
    {
        var parent = new FeatureDefinition("Reports");
        var removed = parent.AddChild(new FeatureDefinitionCreateOptions { Name = "Reports.Export" });
        var retained = parent.AddChild(new FeatureDefinitionCreateOptions { Name = "Reports.History" });
        removed.Parent.Should().BeSameAs(parent);
        retained.Parent.Should().BeSameAs(parent);

        parent.RemoveChild(removed.Name);

        removed.Parent.Should().BeNull();
        parent.Children.Should().ContainSingle().Which.Should().BeSameAs(retained);
        retained.Parent.Should().BeSameAs(parent);
        var removeAgain = () => parent.RemoveChild(removed.Name);
        removeAgain.Should().ThrowExactly<InvalidOperationException>();
        parent.Children.Should().ContainSingle().Which.Should().BeSameAs(retained);
    }
}
