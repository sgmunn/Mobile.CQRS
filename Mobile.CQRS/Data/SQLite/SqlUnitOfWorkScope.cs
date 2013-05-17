// --------------------------------------------------------------------------------------------------------------------
// <copyright file="SqlUnitOfWorkScope.cs" company="sgmunn">
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

namespace Mobile.CQRS.Data.SQLite
{
    using System;
    using System.Collections.Generic;
    using System.Threading;

    public class SqlUnitOfWorkScope : IUnitOfWorkScope
    {
        private readonly SQLiteConnection connection;
        
        private readonly List<IUnitOfWork> scopedWork;

        private bool inTransaction;

        private bool committed;

        private bool disposed;

        public SqlUnitOfWorkScope(SQLiteConnection connection)
        {
            this.connection = connection;
            this.scopedWork = new List<IUnitOfWork>();
            this.BeginTransaction();
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

                this.EndTransaction();
            }
            catch (Exception ex)
            {
                Console.WriteLine("SqlUnitOfWork Exception \n{0}", ex);
                throw;
            }
        }

        public void Dispose()
        {
            if (this.disposed)
            {
                throw new ObjectDisposedException("SqlUnitOfWorkScope");
            }

            if (!this.committed)
            {
                this.disposed = true;
                this.DisposeScopedWork();
            }
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
                this.Rollback();
            }
        }

        private void BeginTransaction()
        {
            if (!this.connection.IsInTransaction)
            {
                Monitor.Enter(this.connection);
                this.connection.BeginTransaction();
                this.inTransaction = true;
            }
        }

        private void EndTransaction()
        {
            if (this.inTransaction)
            {
                this.connection.Commit();
                this.inTransaction = false;
                Monitor.Exit(this.connection);
            }
        }

        private void Rollback()
        {
            if (this.inTransaction)
            {
                try
                {
                    this.connection.Rollback();
                    this.inTransaction = false;
                }
                finally
                {
                    Monitor.Exit(this.connection);
                }
            }
        }
    }
}