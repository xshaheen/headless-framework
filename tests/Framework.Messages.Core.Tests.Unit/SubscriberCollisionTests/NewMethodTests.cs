using ExternalModuleSubscriberClass = Tests.SubscriberClass;

namespace Tests.SubscriberCollisionTests;

public class NewMethodTests
{
    [Fact]
    public void NoCollision_SameClassAndMethod_DifferentAssemblies()
    {
        var methodInfo = typeof(SubscriberClass).GetMethod(nameof(SubscriberClass.TestSubscriber));
        var externalMethodInfo = typeof(ExternalModuleSubscriberClass).GetMethod(
            nameof(ExternalModuleSubscriberClass.TestSubscriber)
        );

        methodInfo.Should().NotBeNull();
        externalMethodInfo.Should().NotBeNull();
        Assert.NotEqual(methodInfo.MethodHandle, externalMethodInfo.MethodHandle);
    }

    [Fact]
    public void Collision_Subclass_SameAssembly_MethodHandleOnly()
    {
        var methodInfo1 = typeof(Subclass1OfSubscriberClass).GetMethod(nameof(SubscriberClass.TestSubscriber));
        var methodInfo2 = typeof(Subclass2OfSubscriberClass).GetMethod(nameof(SubscriberClass.TestSubscriber));

        methodInfo1.Should().NotBeNull();
        methodInfo2.Should().NotBeNull();
        Assert.Equal(methodInfo1.MethodHandle.Value, methodInfo2.MethodHandle.Value);
    }

    [Fact]
    public void NoCollision_Subclass_SameAssembly_TypeAndMethodHandle()
    {
        var methodInfo1 = typeof(Subclass1OfSubscriberClass).GetMethod(nameof(SubscriberClass.TestSubscriber));
        var methodInfo2 = typeof(Subclass2OfSubscriberClass).GetMethod(nameof(SubscriberClass.TestSubscriber));

        methodInfo1.Should().NotBeNull();
        methodInfo2.Should().NotBeNull();
        methodInfo1.ReflectedType.Should().NotBeNull();
        methodInfo2.ReflectedType.Should().NotBeNull();
        $"{methodInfo2.MethodHandle.Value}_{methodInfo2.ReflectedType.TypeHandle.Value}"
            .Should()
            .NotBe($"{methodInfo1.MethodHandle.Value}_{methodInfo1.ReflectedType.TypeHandle.Value}");
    }

    [Fact]
    public void NoCollision_SubclassOfGenericOpenType_SameAssembly_Handle()
    {
        var methodInfo1 = typeof(BaseClass<>)
            .MakeGenericType(typeof(MessageType1))
            .GetMethod(nameof(BaseClass<>.Handle));
        var methodInfo2 = typeof(BaseClass<>)
            .MakeGenericType(typeof(MessageType2))
            .GetMethod(nameof(BaseClass<>.Handle));

        methodInfo1.Should().NotBeNull();
        methodInfo2.Should().NotBeNull();
        methodInfo1.ReflectedType.Should().NotBeNull();
        methodInfo2.ReflectedType.Should().NotBeNull();
        $"{methodInfo2.MethodHandle.Value}_{methodInfo2.ReflectedType.TypeHandle.Value}"
            .Should()
            .NotBe($"{methodInfo1.MethodHandle.Value}_{methodInfo1.ReflectedType.TypeHandle.Value}");
    }

    private sealed class Subclass1OfSubscriberClass : SubscriberClass;

    private sealed class Subclass2OfSubscriberClass : SubscriberClass;

    private sealed class MessageType1;

    private sealed class MessageType2;

    private abstract class BaseClass<T>
    {
        public void Handle() { }
    }
}
