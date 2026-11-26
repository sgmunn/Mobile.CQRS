// --------------------------------------------------------------------------------------------------------------------
// <copyright file="InMemoryUnitOfWorkScope.cs" company="sgmunn">
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

namespace Mobile.CQRS.Domain
{
    using System;
    using System.Collections.Generic;

    public class InMemoryUnitOfWorkScope : IUnitOfWorkScope
    {
        private readonly List<IUnitOfWork> scopedWork;

        private readonly Dictionary<Type, object> registeredObjects;
        
        private enum ScopeState { Active, Committing, Committed, Failed, Disposed }

        private ScopeState state;
 
        public InMemoryUnitOfWorkScope()
        {
            this.scopedWork = new List<IUnitOfWork>();
            this.registeredObjects = new Dictionary<Type, object>();
        }

        public void RegisterObject<T>(T instance)
        {
            this.EnsureActive();
            this.registeredObjects[typeof(T)] = instance;
        }

        public T GetRegisteredObject<T>()
        {
            this.ThrowIfDisposed();
            object instance;
            if (this.registeredObjects.TryGetValue(typeof(T), out instance))
            {
                return (T)instance;
            }

            object match = null;
            foreach (var candidate in this.registeredObjects.Values)
            {
                if (candidate is not T) continue;
                if (match != null && !ReferenceEquals(match, candidate))
                    throw new InvalidOperationException($"Multiple registered instances implement {typeof(T)}.");
                match = candidate;
            }
            if (match != null) return (T)match;

            return default(T);
        }

        public void Add(IUnitOfWork uow)
        {
            this.EnsureActive();
            if (uow == null) throw new ArgumentNullException(nameof(uow));
            this.scopedWork.Add(uow);
        }

        public void Commit()
        {
            this.ThrowIfDisposed();
            if (this.state == ScopeState.Committed) return;
            this.EnsureActive();
            this.state = ScopeState.Committing;
            try
            {
                foreach (var uow in this.scopedWork) uow.Commit();
                this.AfterCommit();
                this.state = ScopeState.Committed;
            }
            catch
            {
                this.state = ScopeState.Failed;
                throw;
            }
        }

        public void Dispose()
        {
            if (this.state == ScopeState.Disposed) return;
            if (this.state == ScopeState.Committing)
                throw new InvalidOperationException("Cannot dispose a scope during commit.");

            this.state = ScopeState.Disposed;
            var errors = new List<Exception>();
            foreach (var uow in this.scopedWork)
            {
                try { uow.Dispose(); }
                catch (Exception ex) { errors.Add(ex); }
            }
            try { this.FinallyDispose(); }
            catch (Exception ex) { errors.Add(ex); }
            this.scopedWork.Clear();
            this.registeredObjects.Clear();
            if (errors.Count != 0) throw new AggregateException("Unit of work cleanup failed.", errors);
        }

        protected virtual void AfterCommit()
        {
        }

        // Called exactly once on disposal, after either success or failure.
        protected virtual void FinallyDispose()
        {
        }

        private void ThrowIfDisposed()
        {
            if (this.state == ScopeState.Disposed) throw new ObjectDisposedException(nameof(InMemoryUnitOfWorkScope));
        }

        private void EnsureActive()
        {
            this.ThrowIfDisposed();
            if (this.state != ScopeState.Active)
                throw new InvalidOperationException("The unit of work is no longer active.");
        }
    }
}
