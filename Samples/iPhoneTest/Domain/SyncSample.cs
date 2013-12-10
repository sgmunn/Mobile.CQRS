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
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System.Net.Http;
using Mobile.CQRS.EventStore;

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

    public interface IBusinessEntity
    {
        int ID { get; set; }
    }

    public class PanelLog : IBusinessEntity
    {
        public PanelLog()
        {

        }

        [PrimaryKey, AutoIncrement]
        public int ID { get; set; }

        public uint Sequence { get; set; }
        public DateTime Time { get; set; }
        public string Message { get; set; }
        public bool Alarm { get; set; }
    }

    public class CCC : PanelLog
    {

    }

    public class TestMe : SQLiteConnection
    {
        public TestMe(string connectionstr) : base(connectionstr, true)
        {


        }

        object locker = new object();
        public IEnumerable<T> GetItemsDescending<T>() where T : IBusinessEntity, new()
        {
            lock (locker) 
            {
                var xxx = Table<T>().Select(i => i).OrderByDescending(xx => xx.ID).ToList();
                return xxx;
            }
        }

    }

    public class AsyncTest
    {
        public async Task<bool> Test(int x)
        {
            await Task.Delay(1000);
            return true;
            //return Task.Delay(1000);
        }
    }



    [MonoTouch.Foundation.Preserve()]
    public static class SyncSample
    {
        public static EventSourcedDomainContext Remote;

        public static EventSourcedDomainContext Client1;

        public static EventSourcedDomainContext Client2;
        
        public static Guid TestId;

        public static void InitSample()
        {
            if (Remote == null)
            {
                TestId = Guid.NewGuid();

                var eventSerializer = new DataContractSerializer<EventBase>(TypeHelpers.FindSerializableTypes(typeof(EventBase), Assembly.GetCallingAssembly()));
                var commandSerializer = new DataContractSerializer<CommandBase>(TypeHelpers.FindSerializableTypes(typeof(CommandBase), Assembly.GetCallingAssembly()));

                Remote = new EventSourcedDomainContext(RemoteDB.SampleDatabasePath(), eventSerializer);
                Client1 = new EventSourcedDomainContext(Client1DB.SampleDatabasePath(), ReadModelDB1.SampleDatabasePath(), eventSerializer) { CommandSerializer = commandSerializer };
                Client2 = new EventSourcedDomainContext(Client2DB.SampleDatabasePath(), ReadModelDB2.SampleDatabasePath(), eventSerializer) { CommandSerializer = commandSerializer };

                var registration = AggregateRegistration.ForType<EventSourcedRoot>();
                //    .WithImmediateReadModel(c => new TransactionReadModelBuilder(new SqlRepository<TransactionDataContract>(EventSourcedDB.Main, "TestId")));
                Client1.Register(registration.WithDelayedReadModel(
                    // TODO: pass in read model db as a scope
                    scope => new TransactionReadModelBuilder(new SqlRepository<TransactionDataContract>(scope, "TestId"))));

                Client1.StartDelayedReadModels();

                Client2.Register(registration);
            }
        }

        public async static void SomeOtherTest()
        {
            var x = new AsyncTest();
            var t = MethodExecutor.ExecuteMethodAsync(x, 1);

            Console.WriteLine(">>>>>>");

            await t;

            Console.WriteLine(">>>>>>");


        }
        
        public static void Reset(SQLiteConnection connection, SQLiteConnection readModelConnection)
        {
            connection.CreateTable<AggregateEvent>();
            connection.CreateTable<AggregateIndex>();
            connection.CreateTable<AggregateSnapshot>();
            connection.CreateTable<PendingCommand>();
            connection.CreateTable<SyncState>();

            connection.Execute("delete from AggregateEvent");
            connection.Execute("delete from AggregateIndex");
            connection.Execute("delete from AggregateSnapshot");
            connection.Execute("delete from PendingCommand");
            connection.Execute("delete from SyncState");

            if (readModelConnection != null)
            {
                readModelConnection.CreateTable<ReadModelWorkItem>();
                readModelConnection.CreateTable<TransactionDataContract>();
                readModelConnection.Execute("delete from ReadModelWorkItem");
                readModelConnection.Execute("delete from TransactionDataContract");
            }
        }
        
        public static ReadModelBuilderAgent test;

        public static void ResetSample()
        {
            SomeOtherTest();
            //Client1DB.Main.Table<AggregateIndex>().Where(x => x.Version == 23).Delete();
            return;

            TestId = Guid.Empty;
            Remote = null;
            Client1 = null;
            Client2 = null;
            Reset(RemoteDB.Main, null);
            Reset(Client1DB.Main, ReadModelDB1.Main);
            Reset(Client2DB.Main, ReadModelDB2.Main);
        }

        public static async void CreateRootClient1()
        {
            InitSample();
            await Client1.ExecuteAsync<EventSourcedRoot>(new TestCommand1 
            { 
                AggregateId = TestId,
                Name = "Name",
            });
        }
        
        public static async void EditClient1()
        {
            InitSample();
            await Client1.ExecuteAsync<EventSourcedRoot>(new TestCommand1 
                                              { 
                AggregateId = TestId,
                Name = "Client 1 Edit " + DateTime.Now.Second.ToString(),
            });

            await Client1.ExecuteAsync<EventSourcedRoot>(new TestCommand2 
                                              { 
                AggregateId = TestId,
                Amount = 100,
            });
        }
        
        public static async void EditClient2()
        {
            InitSample();
            await Client2.ExecuteAsync<EventSourcedRoot>(new TestCommand1 
                                              { 
                AggregateId = TestId,
                Name = "Client 2 Edit " + DateTime.Now.Second.ToString(),
            });

            await Client2.ExecuteAsync<EventSourcedRoot>(new TestCommand2 
                                              { 
                AggregateId = TestId,
                Amount = 50,
            });
        }
        
        public static async void Client1SyncWithRemote()
        {
            InitSample();

            // test http remote
            //Client1.SyncSomething<EventSourcedRoot>(new HttpEventStore(Remote.EventSerializer), TestId);
            //return;

            // ordinarily our remote event store wouldn't need a scope around this code
            using (var scope = Remote.BeginUnitOfWork())
            {
                await Client1.SyncSomethingAsync<EventSourcedRoot>(scope.GetRegisteredObject<IEventStore>(), TestId);

                await scope.CommitAsync();
            }
        }
        
        public static async void Client2SyncWithRemote()
        {
            InitSample();

            // test http remote
            //Client2.SyncSomething<EventSourcedRoot>(new HttpEventStore(Remote.EventSerializer), TestId);
            //return;

            // ordinarily our remote event store wouldn't need a scope around this code
            using (var scope = Remote.BeginUnitOfWork())
            {
                await Client2.SyncSomethingAsync<EventSourcedRoot>(scope.GetRegisteredObject<IEventStore>(), TestId);

                await scope.CommitAsync();
            }
        }
    }
}
