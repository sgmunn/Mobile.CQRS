// --------------------------------------------------------------------------------------------------------------------
// <copyright file=".cs" company="sgmunn">
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

using Mobile.CQRS.Serialization;
using Mobile.CQRS.SQLite;
using System.IO;
using Mobile.CQRS.SQLite.Domain;
using Mobile.CQRS;

namespace Sample.Domain
{
    using System;
    using System.Reflection;
    using Mobile.CQRS.Domain;
    using Mobile.CQRS.Reactive;

    
    public class Client1DB : SQLiteConnection
    {
        private static Client1DB MainDatabase = new Client1DB();

        public static string SampleDatabasePath()
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Client1.db");
            return path;
        }

        public static Client1DB Main
        {
            get
            {
                return MainDatabase;
            }
        }

        protected Client1DB() : base(SampleDatabasePath())
        {
            // TODO: add tables for read model once we have sync working
        }
    }
    
    public class Client2DB : SQLiteConnection
    {
        private static Client2DB MainDatabase = new Client2DB();

        public static string SampleDatabasePath()
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Client2.db");
            return path;
        }

        public static Client2DB Main
        {
            get
            {
                return MainDatabase;
            }
        }

        protected Client2DB() : base(SampleDatabasePath())
        {
            // TODO: add tables for read model once we have sync working
        }
    }
    
    public class RemoteDB : SQLiteConnection
    {
        private static RemoteDB MainDatabase = new RemoteDB();

        public static string SampleDatabasePath()
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Remote.db");
            return path;
        }

        public static RemoteDB Main
        {
            get
            {
                return MainDatabase;
            }
        }

        protected RemoteDB() : base(SampleDatabasePath())
        {
        }
    }

    
    public static class SyncSample
    {
        public static EventSourcingDomainContext Remote;

        public static EventSourcingDomainContext Client1;

        public static EventSourcingDomainContext Client2;
        
        public static Guid TestId;

        public static void InitSample()
        {
            if (Remote == null)
            {
                TestId = Guid.NewGuid();

                var eventSerializer = new DataContractSerializer<EventBase>(TypeHelpers.FindSerializableTypes(typeof(EventBase), Assembly.GetCallingAssembly()));
                var commandSerializer = new DataContractSerializer<CommandBase>(TypeHelpers.FindSerializableTypes(typeof(CommandBase), Assembly.GetCallingAssembly()));

                Remote = new EventSourcingDomainContext(RemoteDB.Main, eventSerializer);
                Client1 = new EventSourcingDomainContext(Client1DB.Main, eventSerializer, commandSerializer);
                Client2 = new EventSourcingDomainContext(Client2DB.Main, eventSerializer, commandSerializer);

                //var registration = AggregateRegistration.ForType<EventSourcedRoot>()
                //    .WithImmediateReadModel(c => new TransactionReadModelBuilder(new SqlRepository<TransactionDataContract>(EventSourcedDB.Main, "TestId")));
                //context.Register(registration);
            }
        }
        
        public static void Reset(SQLiteConnection connection)
        {
            connection.CreateTable<AggregateEvent>();
            connection.CreateTable<AggregateManifest>();
            connection.CreateTable<AggregateSnapshot>();
            connection.CreateTable<PendingCommand>();
            connection.CreateTable<SyncState>();

            connection.Execute("delete from AggregateEvent");
            connection.Execute("delete from AggregateManifest");
            connection.Execute("delete from AggregateSnapshot");
            connection.Execute("delete from PendingCommand");
            connection.Execute("delete from SyncState");
        }

        public static void ResetSample()
        {
            TestId = Guid.Empty;
            Remote = null;
            Client1 = null;
            Client2 = null;
            Reset(RemoteDB.Main);
            Reset(Client1DB.Main);
            Reset(Client2DB.Main);
        }

        public static void CreateRootClient1()
        {
            InitSample();
            Client1.Execute<EventSourcedRoot>(new TestCommand1 
            { 
                AggregateId = TestId,
                Name = "Name",
            });
        }
        
        public static void EditClient1()
        {
            InitSample();
            Client1.Execute<EventSourcedRoot>(new TestCommand1 
                                              { 
                AggregateId = TestId,
                Name = "Client 1 Edit " + DateTime.Now.Second.ToString(),
            });
        }
        
        public static void EditClient2()
        {
            InitSample();
            Client2.Execute<EventSourcedRoot>(new TestCommand1 
                                              { 
                AggregateId = TestId,
                Name = "Client 2 Edit " + DateTime.Now.Second.ToString(),
            });
        }
        
        public static void Client1SyncWithRemote()
        {
            InitSample();

            var mm = new SyncAgent();
            mm.LocalEventStore = Client1.EventStore;
            mm.PendingCommands = Client1.PendingCommands;
            mm.RemoteEventStore = Remote.EventStore;
            mm.SyncState = Client1.SyncState;

            mm.SyncWithRemote<EventSourcedRoot>(TestId);
        }
        
        public static void Client2SyncWithRemote()
        {
            InitSample();
            
            var mm = new SyncAgent();
            mm.LocalEventStore = Client2.EventStore;
            mm.PendingCommands = Client2.PendingCommands;
            mm.RemoteEventStore = Remote.EventStore;
            mm.SyncState = Client2.SyncState;

            mm.SyncWithRemote<EventSourcedRoot>(TestId);
        }
    }
}