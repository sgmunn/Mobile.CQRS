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
    using Mobile.CQRS.Domain;

    /// <summary>A synchronous, thread-affine transaction. Dispose on the creating thread.</summary>
    public class SqlUnitOfWorkScope : InMemoryUnitOfWorkScope
    {
        private readonly SQLiteConnection connection;
        private readonly bool ownsConnection;
        private bool inTransaction;

        public SqlUnitOfWorkScope(SQLiteConnection connection) : this(connection, false)
        {
        }

        /// <param name="connection">Connection exclusively used by this scope until disposal.</param>
        /// <param name="ownsConnection">Close the connection on disposal, including failed construction.</param>
        public SqlUnitOfWorkScope(SQLiteConnection connection, bool ownsConnection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            this.connection = connection;
            this.ownsConnection = ownsConnection;
            Monitor.Enter(connection);
            try
            {
                if (connection.IsInTransaction)
                    throw new InvalidOperationException("Nested unit of work scopes are not supported.");
                connection.BeginTransaction();
                this.inTransaction = true;
                this.RegisterObject<SQLiteConnection>(connection);
            }
            catch
            {
                Monitor.Exit(connection);
                // An existing transaction belongs to the caller, even if ownership
                // was requested for this rejected nested scope.
                if (ownsConnection && !connection.IsInTransaction) connection.Dispose();
                throw;
            }
        }

        public SQLiteConnection Connection => this.connection;

        protected override void AfterCommit()
        {
            this.connection.Commit();
            this.inTransaction = false;
        }

        protected override void FinallyDispose()
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
                try { if (this.ownsConnection) this.connection.Dispose(); }
                finally { Monitor.Exit(this.connection); }
            }
        }
    }
}
