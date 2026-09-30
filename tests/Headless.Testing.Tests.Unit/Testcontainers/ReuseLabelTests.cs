// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;
using Headless.Testing.Tests;

namespace Tests.Testcontainers;

public sealed class ReuseLabelTests : TestBase, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "reuse-label-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void should_use_the_main_checkout_root_that_holds_a_git_directory()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".git"));
        var binaries = Directory.CreateDirectory(Path.Combine(_root, "tests", "X.Tests.Integration", "bin", "Release"));

        ReuseLabel.ForCheckout(binaries.FullName).Should().Be(_Full(_root));
    }

    [Fact]
    public void should_use_the_worktree_root_that_holds_a_git_file()
    {
        // A worktree nested inside the main checkout (.worktrees/<name>) must not resolve to the main checkout.
        Directory.CreateDirectory(Path.Combine(_root, ".git"));
        var worktree = Directory.CreateDirectory(Path.Combine(_root, ".worktrees", "feature"));
        File.WriteAllText(Path.Combine(worktree.FullName, ".git"), "gitdir: ../../.git/worktrees/feature");
        var binaries = Directory.CreateDirectory(Path.Combine(worktree.FullName, "tests", "X", "bin"));

        ReuseLabel.ForCheckout(binaries.FullName).Should().Be(_Full(worktree.FullName));
    }

    [Fact]
    public void should_give_two_checkouts_of_one_project_different_labels()
    {
        Directory.CreateDirectory(Path.Combine(_root, "a", ".git"));
        Directory.CreateDirectory(Path.Combine(_root, "b", ".git"));
        var first = Directory.CreateDirectory(Path.Combine(_root, "a", "tests", "X", "bin"));
        var second = Directory.CreateDirectory(Path.Combine(_root, "b", "tests", "X", "bin"));

        ReuseLabel.ForCheckout(first.FullName).Should().NotBe(ReuseLabel.ForCheckout(second.FullName));
    }

    [Fact]
    public void should_fall_back_to_the_directory_itself_outside_a_repository()
    {
        var binaries = Directory.CreateDirectory(Path.Combine(_root, "loose", "bin")).FullName;

        // Only when no ancestor of the temp root is itself inside a repository, which a temp directory never is.
        ReuseLabel.ForCheckout(binaries + Path.DirectorySeparatorChar).Should().Be(_Full(binaries));
    }

    [Fact]
    public void should_resolve_the_running_tests_to_this_repository_checkout()
    {
        var checkout = ReuseLabel.Checkout;

        Path.Exists(Path.Combine(checkout, ".git")).Should().BeTrue();
        AppContext.BaseDirectory.Should().StartWith(checkout);
    }

    [Fact]
    public void should_key_a_fixture_by_its_test_assembly_name()
    {
        ReuseLabel.For(this).Should().Be(typeof(ReuseLabelTests).Assembly.GetName().Name);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static string _Full(string path)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
}
