// --------------------------------------------------------------------------------------------------------------------
// <copyright file="ReadModelBuilderAgent.cs" company="sgmunn">
//   (c) sgmunn 2012  
//
//   Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated 
//   documentation files (the "Software"), to deal in the Software without restriction, including without limitation 
//   the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and 
//   to permit persons to whom the Software is furnished to do so, subject to the following conditions:
//
//   The above copyright notice and this permission notice shall be included in all copies or substantial portions of 
//   the Software.
// 
//   THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO 
//   THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
//   AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF 
//   CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS 
//   IN THE SOFTWARE.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mobile.CQRS.Reactive;

namespace Mobile.CQRS.Domain;

/// <summary>One worker with durable, checkpointed, at-least-once delivery.</summary>
public sealed class ReadModelBuilderAgent : IReadModelBuilderAgent
{
    private readonly IDomainContext context;
    private readonly List<IAggregateRegistration> registrations;
    private readonly Func<IUnitOfWorkScope> scopeFactory;
    private readonly Func<IUnitOfWorkScope, IReadModelQueue> queueFactory;
    private readonly object lifecycle = new();
    private CancellationTokenSource cancel;
    private SemaphoreSlim wake;
    private IDisposable subscription;
    private Task worker = Task.CompletedTask;
    private bool started, faulted;
    private Exception fault;
    private IReadModelWorkItem currentWork;

    // queueFactory receives an EVENT scope; scopeFactory creates a PROJECTION
    // scope containing an IReadModelCheckpointStore on the same transaction.
    public ReadModelBuilderAgent(IDomainContext context, List<IAggregateRegistration> registrations,
        Func<IUnitOfWorkScope> scopeFactory, Func<IUnitOfWorkScope, IReadModelQueue> queueFactory)
    {
        this.context = context;
        this.registrations = registrations;
        this.scopeFactory = scopeFactory;
        this.queueFactory = queueFactory;
    }

    public bool IsStarted { get { lock (lifecycle) return started; } }
    public bool IsFaulted { get { lock (lifecycle) return faulted; } }
    public Exception Fault { get { lock (lifecycle) return fault; } }

    public void Start()
    {
        lock (lifecycle)
        {
            if (started) return;
            if (!worker.IsCompleted) throw new InvalidOperationException("Await StopAsync before restarting.");
            cancel?.Dispose();
            wake?.Dispose();
            cancel = new CancellationTokenSource();
            wake = new SemaphoreSlim(1, 1); // startup scans durable work without needing an event
            faulted = false;
            fault = null;
            started = true;
            subscription = context.EventBus.Subscribe(notification => {
                if (notification.Event is IReadModelWorkItem || notification.Event is IAggregateEvent) Trigger();
            });
            var token = cancel.Token;
            var signal = wake;
            worker = Task.Run(() => RunAsync(signal, token));
        }
    }

    public void Stop()
    {
        lock (lifecycle) StopCore();
    }

    public Task StopAsync()
    {
        lock (lifecycle)
        {
            StopCore();
            return worker;
        }
    }

    private void StopCore()
    {
        if (!started) return;
        started = false;
        subscription.Dispose();
        cancel.Cancel();
    }

    public void Trigger()
    {
        lock (lifecycle)
        {
            if (!started) return;
            if (wake.CurrentCount == 0) wake.Release();
        }
    }

    private async Task RunAsync(SemaphoreSlim signal, CancellationToken token)
    {
        var attempts = 0;
        try
        {
            while (true)
            {
                // Polling recovers a commit whose in-process notification was lost.
                var signaled = await signal.WaitAsync(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
                if (signaled) attempts = 0;
                if (IsFaulted && attempts >= 3 && !signaled) continue;
                token.ThrowIfCancellationRequested();
                try
                {
                    lock (lifecycle) { faulted = false; fault = null; }
                    Drain(token);
                    attempts = 0;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    attempts++;
                    lock (lifecycle) { faulted = true; fault = ex; }
                    var id = currentWork?.Identity ?? Guid.Empty;
                    // Diagnostics are best effort and never control durable delivery.
                    try { context.EventBus.Publish(new DomainNotification(
                        new DomainTopic(typeof(ReadModelBuilderAgent), id), new ReadModelBuilderFaultedEvent(id, ex))); }
                    catch { }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private void Drain(CancellationToken token)
    {
        currentWork = null;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            IList<IReadModelWorkItem> items;
            using (var source = context.BeginUnitOfWork()) items = queueFactory(source).Peek(50);
            if (items.Count == 0) return;
            foreach (var item in items)
            {
                currentWork = item;
                token.ThrowIfCancellationRequested();
                var registration = registrations.SingleOrDefault(x =>
                    AggregateRootBase.GetAggregateTypeDescriptor(x.AggregateType) == item.AggregateType)
                    ?? throw new InvalidOperationException($"No projection registration for {item.AggregateType} ({item.Identity}).");
                IList<IAggregateEvent> events;
                using (var source = context.BeginUnitOfWork())
                    events = source.GetRegisteredObject<IEventStore>().GetAllEvents(item.Identity);
                var notifications = new List<IDomainNotification>();
                using (var target = scopeFactory())
                {
                    var checkpoints = target.GetRegisteredObject<IReadModelCheckpointStore>()
                        ?? throw new InvalidOperationException("Projection scope requires a transactional checkpoint store.");
                    var checkpoint = checkpoints.Get(item.Identity);
                    if (checkpoint == null || checkpoint.DeliveryId != item.DeliveryId)
                    {
                        var rebuild = item.FromVersion == 0 || checkpoint == null;
                        var version = rebuild ? 0 : checkpoint.Version;
                        if (item.FromVersion > (events.LastOrDefault()?.Version ?? 0))
                            throw new InvalidOperationException("Outbox refers to events missing from committed history.");
                        if (version > (events.LastOrDefault()?.Version ?? 0))
                            throw new InvalidOperationException("Projection is ahead of history; request a rebuild.");
                        var pending = events.Where(x => x.Version > version).ToList();
                        foreach (var evt in pending)
                        {
                            if (evt.AggregateId != item.Identity || evt.Version != ++version)
                                throw new InvalidOperationException("Projection history is not contiguous for this aggregate.");
                        }
                        foreach (var builder in registration.DelayedReadModels(target))
                        {
                            if (rebuild) builder.DeleteForAggregate(item.Identity);
                            notifications.AddRange(builder.Process(pending));
                        }
                        checkpoints.Save(item.Identity, version, item.DeliveryId);
                    }
                    token.ThrowIfCancellationRequested();
                    target.Commit();
                }
                // A crash here leaves a durable receipt, so redelivery skips application.
                token.ThrowIfCancellationRequested();
                using (var source = context.BeginUnitOfWork())
                {
                    queueFactory(source).Dequeue(item);
                    source.Commit();
                }
                foreach (var notification in notifications) context.EventBus.Publish(notification);
            }
        }
    }
}
