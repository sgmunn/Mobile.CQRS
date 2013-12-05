//  --------------------------------------------------------------------------------------------------------------------
//  <copyright file="ScheduledObserver_T.cs" company="sgmunn">
//    (c) sgmunn 2012  
// 
//    Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated 
//    documentation files (the "Software"), to deal in the Software without restriction, including without limitation 
//    the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and 
//    to permit persons to whom the Software is furnished to do so, subject to the following conditions:
// 
//    The above copyright notice and this permission notice shall be included in all copies or substantial portions of 
//    the Software.
//  
//    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO 
//    THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
//    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF 
//    CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS 
//    IN THE SOFTWARE.
//  </copyright>
//  --------------------------------------------------------------------------------------------------------------------

namespace Mobile.CQRS.Reactive
{
    using System;
    using System.Collections.Generic;

    public class ScheduledObserver<T> : IObserver<T>, IDisposable
    {
        private readonly Queue<Action> queue;

        private readonly IScheduler scheduler;

        private readonly IObserver<T> observer;

        public ScheduledObserver(IScheduler scheduler, IObserver<T> observer)
        {
            this.queue = new Queue<Action>();
            this.scheduler = scheduler;
            this.observer = observer;
        }

        public void OnCompleted()
        {
            lock (this.queue)
            {
                this.queue.Enqueue(() =>
                {
                    this.observer.OnCompleted();
                });
            }

            this.ScheduleFromQueue();
        }

        public void OnError(Exception error)
        {
            lock (this.queue)
            {
                this.queue.Enqueue(() =>
                {
                    this.observer.OnError(error);
                });
            }

            this.ScheduleFromQueue();
        }

        public void OnNext(T value)
        {
            lock (this.queue)
            {
                this.queue.Enqueue(() =>
                {
                    this.observer.OnNext(value);
                });
            }

            this.ScheduleFromQueue();
        }

        public void Dispose()
        {
            lock (this.queue)
            {
                this.queue.Clear();
            }
        }

        private void ScheduleFromQueue()
        {
            this.scheduler.Schedule(() =>
            {
                lock (this.queue)
                {
                    var action = this.queue.Dequeue();
                    if (action != null)
                    {
                        action();
                    }
                }
            });
        }
    }
}
