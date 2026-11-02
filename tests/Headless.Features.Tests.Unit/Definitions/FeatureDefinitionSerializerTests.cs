// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.EntityFramework;
using Headless.Features;
using Headless.Primitives;
using Headless.Testing.Tests;

namespace Tests.Definitions;

public sealed class FeatureDefinitionSerializerTests : TestBase
{
    private readonly FeatureDefinitionSerializer _sut;

    public FeatureDefinitionSerializerTests()
    {
        var guidGenerator = Substitute.For<IGuidGenerator>();
        guidGenerator.Create().Returns(_ => Guid.NewGuid());
        _sut = new FeatureDefinitionSerializer(guidGenerator);
    }

    [Fact]
    public void should_match_the_stored_records_after_a_storage_round_trip_of_their_extra_properties()
    {
        // given — properties the store reads back as other CLR types (an int as a long, an enum as its name, an array
        // as JSON)
        var group = new FeatureGroupDefinition("Billing");
        group["Order"] = 5;
        group["Kind"] = DayOfWeek.Monday;
        var feature = group.AddChild(new FeatureDefinitionCreateOptions { Name = "Invoices" });
        feature["Group"] = "Billing";
        feature["Tags"] = new[] { "a", "b" };
        var groupRecord = _sut.Serialize(group);
        var featureRecord = _sut.Serialize(feature, group);
        var storedGroup = _sut.Serialize(group);
        var storedFeature = _sut.Serialize(feature, group);
        _ReplaceWithStoredBag(storedGroup);
        _ReplaceWithStoredBag(storedFeature);

        // when
        var sameGroup = groupRecord.HasSameData(storedGroup);
        var sameFeature = featureRecord.HasSameData(storedFeature);

        // then — otherwise every seed rewrites the definitions and seed verification reports the store stale forever
        sameGroup.Should().BeTrue();
        sameFeature.Should().BeTrue();
    }

    private static void _ReplaceWithStoredBag(IHasExtraProperties record)
    {
        var converter = new ExtraPropertiesValueConverter();
        var json = (string)converter.ConvertToProvider(record.ExtraProperties)!;
        record.ExtraProperties.Clear();

        foreach (var (key, value) in (ExtraProperties)converter.ConvertFromProvider(json)!)
        {
            record.ExtraProperties[key] = value;
        }
    }
}
