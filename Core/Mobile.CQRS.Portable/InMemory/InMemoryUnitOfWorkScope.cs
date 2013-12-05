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
using System.Reflection;

namespace Mobile.CQRS.Domain
{
    using System;
    using System.Collections.Generic;

    public class InMemoryUnitOfWorkScope : IUnitOfWorkScope
    {
        private readonly List<IUnitOfWork> scopedWork;

        private readonly Dictionary<Type, object> registeredObjects;
        
        private bool committed;

        private bool disposed;
 
        public InMemoryUnitOfWorkScope()
        {
            this.scopedWork = new List<IUnitOfWork>();
            this.registeredObjects = new Dictionary<Type, object>();
        }

        public void RegisterObject<T>(T instance)
        {
            this.registeredObjects[typeof(T)] = instance;
        }

        public T GetRegisteredObject<T>()
        {
            object instance;
            if (this.registeredObjects.TryGetValue(typeof(T), out instance))
            {
                return (T)instance;
            }

            foreach (var t in this.registeredObjects.Keys)
            {
                if (t.GetTypeInfo().IsAssignableFrom(typeof(T).GetTypeInfo()))
                {
                    return (T)this.registeredObjects[t];
                }
            }

            return default(T);
        }

        public void Add(IUnitOfWork uow)
        {
            this.scopedWork.Add(uow);
        }

        public void Commit()
        {
            if (this.committed)
            {
                return;
            }

            try
            {
                this.committed = true;

                foreach (var uow in this.scopedWork)
                {
                    uow.Commit();
                }

                this.AfterCommit();
            }
            catch (Exception ex)
            {
                // TODO: Console.WriteLine("UnitOfWorkScope Exception \n{0}", ex);
                throw;
            }
        }

        public void Dispose()
        {
            if (this.disposed)
            {
                throw new ObjectDisposedException("UnitOfWorkScope");
            }

            if (!this.committed)
            {
                this.disposed = true;
                this.DisposeScopedWork();
            }
            foreach (var uow in this.scopedWork)
            {
                uow.Dispose();
            }
        }

        protected virtual void AfterCommit()
        {
        }
        
        protected virtual void FinallyDispose()
        {
        }

        private void DisposeScopedWork()
        {
            try
            {
                foreach (var uow in this.scopedWork)
                {
                    uow.Dispose();
                }
            }
            finally
            {
                this.FinallyDispose();
            }
        }

    }
}