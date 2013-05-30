// --------------------------------------------------------------------------------------------------------------------
// <copyright file="EventSourcedDomainContext.cs" company="sgmunn">
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
    using Mobile.CQRS.Serialization;

    /// <summary>
    /// A domain context set up for typical event sourcing.
    /// You're going to need a db and a way to serialize events at the very least. Optionally you can have a different read model DB.
    /// In order to support sync with a remote event store you will need a command serializer.
    /// In order to support snapshots / momento's you'll need a snapshot serializer.
    /// </summary>
    public class EventSourcedDomainContext : DomainContextBase
    {
        public EventSourcedDomainContext(SQLiteConnection connection, ISerializer<IAggregateEvent> eventSerializer) 
        {
            if (connection == null)
                throw new ArgumentNullException("connection");
            if (eventSerializer == null)
                throw new ArgumentNullException("eventSerializer");

            this.Connection = connection;
            this.EventSerializer = eventSerializer;
            this.ReadModelConnection = connection;
        }

        public EventSourcedDomainContext(SQLiteConnection connection, SQLiteConnection readModelConnection, ISerializer<IAggregateEvent> eventSerializer) 
        {
            if (connection == null)
                throw new ArgumentNullException("connection");
            if (readModelConnection == null)
                throw new ArgumentNullException("readModelConnection");
            if (eventSerializer == null)
                throw new ArgumentNullException("eventSerializer");

            this.Connection = connection;
            this.EventSerializer = eventSerializer;
            this.ReadModelConnection = readModelConnection;
        }

        public SQLiteConnection Connection { get; private set; }
        
        public SQLiteConnection ReadModelConnection { get; private set; }

        public ISerializer<IAggregateEvent> EventSerializer { get; private set; }

        public ISerializer<IAggregateCommand> CommandSerializer { get; set; }
        
        public ISerializer<ISnapshot> SnapshotSerializer { get; set; }

        protected override IDomainUnitOfWorkScope BeginUnitOfWork()
        {
            return new SqlDomainScope(this.Connection, this.EventSerializer, this.CommandSerializer, this.SnapshotSerializer);
        }
        
        protected override IReadModelQueue GetReadModelQueue()
        {
            return new ReadModelBuilderQueue(this.ReadModelConnection);
        }
    }
}