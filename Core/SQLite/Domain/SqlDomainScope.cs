// --------------------------------------------------------------------------------------------------------------------
// <copyright file="SqlDomainScope.cs" company="sgmunn">
//   (c) sgmunn 2013  
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
    using Mobile.CQRS.SQLite;
    using Mobile.CQRS.Serialization;

    public class SqlDomainScope : SqlUnitOfWorkScope, IDomainUnitOfWorkScope
    {
        public SqlDomainScope(SQLiteConnection connection, ISerializer<IAggregateEvent> eventSerializer, ISerializer<IAggregateCommand> commandSerializer) 
            : base(connection)
        {
            if (connection == null)
                throw new ArgumentNullException("connection");
            if (eventSerializer == null)
                throw new ArgumentNullException("eventSerializer");

            this.EventStore = new EventStore(connection, eventSerializer);

            if (commandSerializer != null)
            {
                this.PendingCommands = new PendingCommandRepository(connection, commandSerializer);
                this.SyncState = new SyncStateRepository(connection);
            }
        }

        public IEventStore EventStore { get; private set; } 

        public IPendingCommandRepository PendingCommands { get; private set; }
        
        public IRepository<ISyncState> SyncState { get; private set; }

        public IRepository<T> GetRepository<T>() where T : IUniqueId, new()
        {
            // TODO: we need to register the scope field name for rebuilding read models
            return new SqlRepository<T>(this.Connection);
        }
    }
}