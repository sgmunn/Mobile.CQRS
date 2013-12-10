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

namespace Mobile.CQRS.SQLite
{
    using System;
    using System.Threading;
    using Mobile.CQRS.InMemory;

    /// <summary>
    /// Implements a unit of work scope for SQLite.
    /// An instance of this class should be short lived as it creates a transaction on creation
    /// </summary>
    public class SqlUnitOfWorkScope : InMemoryUnitOfWorkScope
    {
        private readonly SQLiteConnection connection;

        private readonly IDisposable sqlLock;

        private bool inTransaction;

        public SqlUnitOfWorkScope(SQLiteConnection connection, IDisposable sqlLock = null)
        {
            if (connection == null)
                throw new ArgumentNullException("connection");

            this.RegisterObject<SQLiteConnection>(connection);

            this.sqlLock = sqlLock;
            this.connection = connection;

            // IMPROVE: we really shouldn't be starting a transaction in here
            this.BeginTransaction();
        }

        public SQLiteConnection Connection
        {
            get
            {
                return this.connection;
            }
        }

        protected override void AfterCommit()
        {
            this.EndTransaction();
        }
        
        protected override void FinallyDispose()
        {
            this.Rollback();
            if (this.sqlLock != null)
            {
                this.sqlLock.Dispose();
            }
        }

        private void BeginTransaction()
        {
            Monitor.Enter(this.connection);

            if (!this.connection.IsInTransaction)
            {
                this.connection.BeginTransaction();
                this.inTransaction = true;
            }
        }

        private void EndTransaction()
        {
            try
            {
                if (this.inTransaction)
                {
                    this.connection.Commit();
                    this.inTransaction = false;
                }
            }
            finally
            {
                Monitor.Exit(this.connection);
            }
        }

        private void Rollback()
        {
            try
            {
                if (this.inTransaction)
                {
                    this.connection.Rollback();
                    this.inTransaction = false;
                }
            }
            finally
            {
                Monitor.Exit(this.connection);
            }
        }
    }
}