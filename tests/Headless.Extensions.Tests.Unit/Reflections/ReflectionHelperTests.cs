// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Reflection;

namespace Tests.Reflections;

public sealed class ReflectionHelperTests
{
    [Fact]
    public void is_flags_enum_returns_true_for_flags_enum()
    {
        typeof(SampleFlags).IsFlagsEnum().Should().BeTrue();
        ReflectionHelper.IsFlagsEnum<SampleFlags>().Should().BeTrue();
    }

    [Fact]
    public void is_flags_enum_returns_false_for_non_flags_enum()
    {
        typeof(SamplePlain).IsFlagsEnum().Should().BeFalse();
        ReflectionHelper.IsFlagsEnum<SamplePlain>().Should().BeFalse();
    }

    [Fact]
    public void is_flags_enum_throws_for_null_type()
    {
        var act = () => ((Type)null!).IsFlagsEnum();

        act.Should().Throw<ArgumentNullException>();
    }

    // ----- GetFirstOrDefaultAttribute -----

    [Fact]
    public void get_first_or_default_attribute_returns_attribute_when_present()
    {
        var member = typeof(AnnotatedClass).GetProperty(
            nameof(AnnotatedClass.Tagged),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
        )!;

        var result = member.GetFirstOrDefaultAttribute<SampleAttribute>();

        result.Should().NotBeNull();
        result!.Label.Should().Be("hello");
    }

    [Fact]
    public void get_first_or_default_attribute_returns_default_when_absent()
    {
        var member = typeof(AnnotatedClass).GetProperty(
            nameof(AnnotatedClass.Bare),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
        )!;

        var result = member.GetFirstOrDefaultAttribute<SampleAttribute>(defaultValue: null);

        result.Should().BeNull();
    }

    [Fact]
    public void get_first_or_default_attribute_returns_supplied_default_when_absent()
    {
        var member = typeof(AnnotatedClass).GetProperty(
            nameof(AnnotatedClass.Bare),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
        )!;
        var fallback = new SampleAttribute("fallback");

        var result = member.GetFirstOrDefaultAttribute(defaultValue: fallback);

        result.Should().BeSameAs(fallback);
    }

    [Fact]
    public void get_first_or_default_attribute_respects_inherit_false()
    {
        // The attribute is only on the parent class member; with inherit:false the child override should not see it.
        var member = typeof(DerivedClass).GetProperty(
            nameof(DerivedClass.Tagged),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
        )!;

        // inherit: true => finds the attribute declared on the base
        member.GetFirstOrDefaultAttribute<SampleAttribute>(inherit: true).Should().NotBeNull();

        // inherit: false on the child's own PropertyInfo — the child has no attribute directly
        typeof(DerivedClass)
            .GetProperty(
                nameof(DerivedClass.OwnProp),
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
            )!
            .GetFirstOrDefaultAttribute<SampleAttribute>(inherit: false)
            .Should()
            .BeNull();
    }

    // ----- GetSingleAttributeOfMemberOrDeclaringTypeOrDefault -----

    [Fact]
    public void get_single_attribute_of_member_returns_member_attribute_when_present()
    {
        var member = typeof(AnnotatedClass).GetProperty(
            nameof(AnnotatedClass.Tagged),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
        )!;

        var result = member.GetSingleAttributeOfMemberOrDeclaringTypeOrDefault<SampleAttribute>();

        result.Should().NotBeNull();
        result!.Label.Should().Be("hello");
    }

    [Fact]
    public void get_single_attribute_of_member_falls_back_to_declaring_type_attribute()
    {
        // AnnotatedClass itself carries a SampleAttribute("class"); the Bare property does not.
        var member = typeof(AnnotatedClassWithTypeAttr).GetProperty(
            nameof(AnnotatedClassWithTypeAttr.Bare),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
        )!;

        var result = member.GetSingleAttributeOfMemberOrDeclaringTypeOrDefault<SampleAttribute>();

        result.Should().NotBeNull();
        result!.Label.Should().Be("class");
    }

    [Fact]
    public void get_single_attribute_of_member_returns_default_when_neither_member_nor_type_has_attribute()
    {
        var member = typeof(PlainClass).GetProperty(
            nameof(PlainClass.Value),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
        )!;

        var result = member.GetSingleAttributeOfMemberOrDeclaringTypeOrDefault<SampleAttribute>();

        result.Should().BeNull();
    }

    // ----- GetAttributesOfMemberOrDeclaringType -----

    [Fact]
    public void get_attributes_of_member_or_declaring_type_returns_combined_unique_attributes()
    {
        // The property has SampleAttribute("prop") and the class has SampleAttribute("class").
        var member = typeof(ClassWithBothAttrs).GetProperty(
            nameof(ClassWithBothAttrs.Prop),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
        )!;

        var results = member.GetAttributesOfMemberOrDeclaringType<SampleAttribute>().ToList();

        results.Should().HaveCount(2);
        results.Select(a => a.Label).Should().Contain("prop").And.Contain("class");
    }

    [Fact]
    public void get_attributes_of_member_or_declaring_type_deduplicates_same_attribute_instance()
    {
        // If the same attribute instance appears on both member and declaring type, Distinct() removes it.
        var member = typeof(AnnotatedClass).GetProperty(
            nameof(AnnotatedClass.Tagged),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
        )!;

        // AnnotatedClass itself has no SampleAttribute; Tagged has one. No duplication possible here.
        var results = member.GetAttributesOfMemberOrDeclaringType<SampleAttribute>().ToList();

        results.Should().ContainSingle();
    }

    // ----- Test support types -----

    [Flags]
    private enum SampleFlags
    {
        None = 0,
        First = 1,
        Second = 2,
    }

    private enum SamplePlain
    {
        First,
        Second,
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    private sealed class SampleAttribute(string label) : Attribute
    {
        public string Label { get; } = label;
    }

    private sealed class AnnotatedClass
    {
        [Sample("hello")]
        public string? Tagged { get; set; }

        public string? Bare { get; set; }
    }

    private class BaseClass
    {
        [Sample("hello")]
        public virtual string? Tagged { get; set; }
    }

    private sealed class DerivedClass : BaseClass
    {
        public override string? Tagged { get; set; }

        public string? OwnProp { get; set; }
    }

    [Sample("class")]
    private sealed class AnnotatedClassWithTypeAttr
    {
        public string? Bare { get; set; }
    }

    private sealed class PlainClass
    {
        public string? Value { get; set; }
    }

    [Sample("class")]
    private sealed class ClassWithBothAttrs
    {
        [Sample("prop")]
        public string? Prop { get; set; }
    }
}
