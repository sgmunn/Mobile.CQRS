// --------------------------------------------------------------------------------------------------------------------
// <copyright file="StateSourcedDomainContext.cs" company="sgmunn">
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

namespace Mobile.CQRS.SQLite.Domain
{
    using System;
    using Mobile.CQRS.Domain;
    using Mobile.CQRS.Serialization;
    
    /// <summary>
    /// A domain context set up for typical state sourcing.
    /// This will not support syncing with a remote event store
    /// </summary>
    public class StateSourcedDomainContext : DomainContextBase
    {
        public StateSourcedDomainContext(SQLiteConnection connection, ISerializer<ISnapshot> snapshotSerializer) 
        {
            if (connection == null)
                throw new ArgumentNullException("connection");
            if (snapshotSerializer == null)
                throw new ArgumentNullException("snapshotSerializer");

            this.Connection = connection;
            this.ReadModelConnection = connection;
            this.SnapshotSerializer = snapshotSerializer;
        }
        
        public StateSourcedDomainContext(SQLiteConnection connection, SQLiteConnection readModelConnection, ISerializer<ISnapshot> snapshotSerializer) 
        {
            if (connection == null)
                throw new ArgumentNullException("connection");
            if (readModelConnection == null)
                throw new ArgumentNullException("readModelConnection");
            if (snapshotSerializer == null)
                throw new ArgumentNullException("snapshotSerializer");

            this.Connection = connection;
            this.ReadModelConnection = readModelConnection;
            this.SnapshotSerializer = snapshotSerializer;
        }
        
        public SQLiteConnection Connection { get; private set; }

        public SQLiteConnection ReadModelConnection { get; private set; }

        public ISerializer<ISnapshot> SnapshotSerializer { get; private set; }

        protected override IDomainUnitOfWorkScope BeginUnitOfWork()
        {
            return new SqlDomainScope(this.Connection, null, null, this.SnapshotSerializer);
        }

        protected override IReadModelQueue GetReadModelQueue()
        {
            // state sourced, this won't work
            return null;
        }
    }
}