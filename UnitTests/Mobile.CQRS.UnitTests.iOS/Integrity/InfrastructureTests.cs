using System;
using System.Threading.Tasks;
using System.Reflection;
using Mobile.CQRS.Domain;
using NUnit.Framework;

namespace Mobile.CQRS.UnitTests.Integrity;

[TestFixture]
public class InfrastructureTests
{
    [Test]
    public void RemovingOneCacheEntryPreservesUnrelatedEntries()
    {
        var cache = new ConcurrentCache<int, string>();
        cache.TryAdd(1, "one"); cache.TryAdd(2, "two");
        Assert.IsTrue(cache.TryRemove(1, out var removed));
        Assert.AreEqual("one", removed);
        Assert.IsTrue(cache.TryGetValue(2, out var remaining));
        Assert.AreEqual("two", remaining);
    }

    [Test]
    public void ScopeResolvesTheActualInstanceRegardlessOfRegistrationType()
    {
        using var scope = new InMemoryUnitOfWorkScope();
        var store = new InMemoryEventStore();
        scope.RegisterObject(store);
        Assert.AreSame(store, scope.GetRegisteredObject<IEventStore>());
    }

    [Test]
    public void NestedIdLocksKeepOneGateUntilTheLastOwnerExits()
    {
        var id = Guid.NewGuid();
        var first = new IdLock(id);
        using var second = new IdLock(id);
        first.Dispose();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiter = Task.Run(() => { using var held = new IdLock(id); entered.SetResult(true); });
        Assert.IsFalse(entered.Task.Wait(TimeSpan.FromMilliseconds(100)), "Another thread entered a still-owned ID lock.");
        second.Dispose();
        Assert.IsTrue(waiter.Wait(TimeSpan.FromSeconds(5)));
    }

    [Test]
    public void ScopeDeduplicatesAliasesAndRejectsAmbiguousFallbacks()
    {
        using var scope = new InMemoryUnitOfWorkScope();
        var first = new InMemoryEventStore();
        scope.RegisterObject<IEventStore>(first);
        scope.RegisterObject<object>(first);
        Assert.AreSame(first, scope.GetRegisteredObject<IMergableEventStore>());
        scope.RegisterObject<object>(new InMemoryEventStore());
        Assert.Throws<InvalidOperationException>(() => scope.GetRegisteredObject<IMergableEventStore>());
        Assert.AreSame(first, scope.GetRegisteredObject<IEventStore>());
    }

    [Test]
    public void DispatchFindsInheritedInstanceHandlersAndCachesMissingOnes()
    {
        var target = new DerivedHandler();
        Assert.IsTrue(MethodExecutor.ExecuteMethod(target, 3));
        Assert.AreEqual(3, target.Value);
        Assert.IsFalse(MethodExecutor.ExecuteMethod(target, "missing"));
        Assert.IsFalse(MethodExecutor.ExecuteMethod(target, "missing"));
        Assert.IsFalse(MethodExecutor.ExecuteMethod(new StaticOnly(), 3));
        Assert.IsFalse(MethodExecutor.ExecuteMethod(new ReturningOnly(), 3));
    }

    [Test]
    public void AmbiguousHandlersFailWithoutInvokingEither()
    {
        Assert.Throws<AmbiguousMatchException>(() => MethodExecutor.ExecuteMethod(new Ambiguous(), 1));
    }

    private class BaseHandler { public int Value; private void Apply(int value) { Value = value; } }
    private sealed class DerivedHandler : BaseHandler { }
    private sealed class StaticOnly { public static void Apply(int value) => throw new Exception("Static handler selected"); }
    private sealed class ReturningOnly { public int Apply(int value) => value; }
    private sealed class Ambiguous { private void A(int value) { } private void B(int value) { } }

}
