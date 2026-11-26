using System;
using Mobile.CQRS.Domain;
using NUnit.Framework;

namespace Mobile.CQRS.UnitTests.Transactions;

[TestFixture]
public class UnitOfWorkScopeTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void DisposeCleansEveryParticipantOnce(bool commit)
    {
        var scope = new TrackingScope();
        var work = new Work();
        scope.Add(work);
        if (commit) { scope.Commit(); scope.Commit(); }
        scope.Dispose();
        scope.Dispose();
        Assert.AreEqual(commit ? 1 : 0, work.Commits);
        Assert.AreEqual(1, work.Disposals);
        Assert.AreEqual(1, scope.Cleanups);
        Assert.Throws<ObjectDisposedException>(() => scope.Commit());
        Assert.Throws<ObjectDisposedException>(() => scope.Add(new Work()));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FailedCommitCannotBeRetriedAndStillCleansUp(bool failAfterCommit)
    {
        var scope = new TrackingScope { FailAfterCommit = failAfterCommit };
        var work = new Work { FailCommit = !failAfterCommit };
        scope.Add(work);
        Assert.Throws<InvalidOperationException>(() => scope.Commit());
        Assert.Throws<InvalidOperationException>(() => scope.Commit());
        scope.Dispose();
        Assert.AreEqual(1, work.Commits);
        Assert.AreEqual(1, work.Disposals);
        Assert.AreEqual(1, scope.Cleanups);
    }

    [Test]
    public void ThrowingDisposerDoesNotSkipOtherDisposersOrCleanup()
    {
        var scope = new TrackingScope();
        var first = new Work { FailDispose = true };
        var last = new Work();
        scope.Add(first); scope.Add(last);
        Assert.Throws<AggregateException>(() => scope.Dispose());
        scope.Dispose();
        Assert.AreEqual(1, first.Disposals);
        Assert.AreEqual(1, last.Disposals);
        Assert.AreEqual(1, scope.Cleanups);
    }

    private sealed class TrackingScope : InMemoryUnitOfWorkScope
    {
        public bool FailAfterCommit;
        public int Cleanups;
        protected override void AfterCommit() { if (FailAfterCommit) throw new InvalidOperationException(); }
        protected override void FinallyDispose() { Cleanups++; }
    }

    internal sealed class Work : IUnitOfWork
    {
        public bool FailCommit, FailDispose;
        public int Commits, Disposals;
        public void Commit() { Commits++; if (FailCommit) throw new InvalidOperationException("injected commit failure"); }
        public void Dispose() { Disposals++; if (FailDispose) throw new InvalidOperationException("injected dispose failure"); }
    }
}
