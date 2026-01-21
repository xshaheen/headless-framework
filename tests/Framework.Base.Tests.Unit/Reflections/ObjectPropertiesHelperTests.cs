using Framework.Reflection;

namespace Tests.Reflections;

public sealed class ObjectPropertiesHelperTests
{
    private sealed class TestClass
    {
        public string? Name { get; set; }

        public int Age { get; private init; }

        [IgnoreMe]
        public string? IgnoredProperty { get; set; }

#pragma warning disable CA1822 // Mark members as static
        public string ReadOnlyProp => "readonly";
#pragma warning restore CA1822 // Mark members as static

        public string InitOnlyProp { get; init; } = string.Empty;

        public static string StaticProp { get; set; } = string.Empty;

        public string this[int index] => "indexed";
    }

    [AttributeUsage(AttributeTargets.Property)]
    private sealed class IgnoreMeAttribute : Attribute;

    [Fact]
    public void should_set_public_property_successfully()
    {
        // given
        var obj = new TestClass();

        // when
        ObjectPropertiesHelper.TrySetProperty(obj, x => x.Name, () => "John");

        // then
        obj.Name.Should().Be("John");
    }

    [Fact]
    public void should_set_private_property_successfully()
    {
        // given
        var obj = new TestClass();

        // when
        ObjectPropertiesHelper.TrySetProperty(obj, x => x.Age, _ => 42);

        // then
        obj.Age.Should().Be(42);
    }

    [Fact]
    public void should_not_set_property_with_ignored_attribute()
    {
        // given
        var obj = new TestClass();

        // when
        ObjectPropertiesHelper.TrySetProperty(
            obj,
            x => x.IgnoredProperty,
            () => "IgnoredValue",
            typeof(IgnoreMeAttribute)
        );

        // then
        obj.IgnoredProperty.Should().BeNull();
    }

    [Fact]
    public void should_set_nullable_property_to_null()
    {
        // given
        var obj = new TestClass { Name = "Initial" };

        // when
        ObjectPropertiesHelper.TrySetPropertyToNull(obj, nameof(TestClass.Name));

        // then
        obj.Name.Should().BeNull();
    }

    [Fact]
    public void should_not_throw_when_setting_already_null_property_to_null()
    {
        // given
        var obj = new TestClass();

        // when
        Action act = () => ObjectPropertiesHelper.TrySetPropertyToNull(obj, nameof(TestClass.Name));

        // then
        act.Should().NotThrow();
        obj.Name.Should().BeNull();
    }

    [Fact]
    public void should_ignore_non_nullable_property_when_setting_null()
    {
        // given
        var obj = new TestClass();

        // when
        ObjectPropertiesHelper.TrySetPropertyToNull(obj, "Age");

        // then
        obj.Age.Should().Be(0); // unchanged
    }

    [Fact]
    public void should_not_throw_for_non_existent_property()
    {
        // given
        var obj = new TestClass();

        // when
        Action act = () => ObjectPropertiesHelper.TrySetPropertyToNull(obj, "NonExistentProperty");

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void should_not_set_readonly_property()
    {
        // given
        var obj = new TestClass();

        // when
        ObjectPropertiesHelper.TrySetProperty(obj, x => x.ReadOnlyProp, () => "NewValue");

        // then
        obj.ReadOnlyProp.Should().Be("readonly"); // unchanged
    }

    [Fact]
    public void should_set_init_only_property()
    {
        // given
        var obj = new TestClass { InitOnlyProp = "Original" };

        // when
        ObjectPropertiesHelper.TrySetProperty(obj, x => x.InitOnlyProp, () => "Changed");

        // then
        obj.InitOnlyProp.Should().Be("Changed");
    }

    [Fact]
    public void should_set_static_property()
    {
        // given
        TestClass.StaticProp = "Original";

        // when
        ObjectPropertiesHelper.TrySetProperty(new TestClass(), x => TestClass.StaticProp, () => "Updated");

        // then
        TestClass.StaticProp.Should().Be("Updated");
    }

    [Fact]
    public void should_not_set_indexer_property()
    {
        // given
        var obj = new TestClass();

        // when
        ObjectPropertiesHelper.TrySetProperty(obj, x => x[0], () => "Updated");

        // then
        obj[0].Should().Be("indexed"); // unchanged
    }
}
