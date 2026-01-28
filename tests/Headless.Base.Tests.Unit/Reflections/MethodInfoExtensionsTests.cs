using System.Reflection;

namespace Tests.Reflections;

public sealed class MethodInfoExtensionsTests
{
    [Fact]
    public void is_async_should_return_true_for_async_methods()
    {
        _ReadMethodInfo(nameof(TestClass.TaskMethod)).IsAsync().Should().BeTrue();
        _ReadMethodInfo(nameof(TestClass.ValueTaskMethod)).IsAsync().Should().BeTrue();
        _ReadMethodInfo(nameof(TestClass.AsyncTaskMethod)).IsAsync().Should().BeTrue();
        _ReadMethodInfo(nameof(TestClass.AsyncValueTaskMethod)).IsAsync().Should().BeTrue();
        _ReadMethodInfo(nameof(TestClass.IAsyncEnumeratorMethod)).IsAsync().Should().BeTrue();
        _ReadMethodInfo(nameof(TestClass.IAsyncEnumerableMethod)).IsAsync().Should().BeTrue();
        _ReadMethodInfo(nameof(TestClass.AsyncVoidMethod)).IsAsync().Should().BeTrue();
    }

    [Fact]
    public void is_async_should_return_false_for_non_async_methods()
    {
        _ReadMethodInfo(nameof(TestClass.NonAsyncMethod)).IsAsync().Should().BeFalse();
    }

    private static MethodInfo _ReadMethodInfo(string methodName)
    {
        return typeof(TestClass).GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null
        )!;
    }

    private static class TestClass
    {
        public static void NonAsyncMethod() { }

#pragma warning disable AsyncFixer03, VSTHRD100 // ReSharper disable once AsyncVoidMethod
        public static async void AsyncVoidMethod() => await Task.Delay(10);
#pragma warning restore AsyncFixer03, VSTHRD100

        public static async Task AsyncTaskMethod() => await Task.CompletedTask;

        public static Task TaskMethod() => Task.CompletedTask;

        public static ValueTask ValueTaskMethod() => ValueTask.CompletedTask;

        public static async ValueTask AsyncValueTaskMethod() => await Task.Delay(10);

        public static IAsyncEnumerable<int> IAsyncEnumerableMethod()
        {
            return new List<int> { 0 }.ToAsyncEnumerable();
        }

        public static async IAsyncEnumerator<int> IAsyncEnumeratorMethod()
        {
            var result = await Task.FromResult(0);

            yield return result;
        }
    }
}
