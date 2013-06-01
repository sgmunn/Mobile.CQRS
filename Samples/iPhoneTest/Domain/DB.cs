//  --------------------------------------------------------------------------------------------------------------------
//  <copyright file="DB.cs" company="sgmunn">
//    (c) sgmunn 2012  
//
//    Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated 
//    documentation files (the "Software"), to deal in the Software without restriction, including without limitation 
//    the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and 
//    to permit persons to whom the Software is furnished to do so, subject to the following conditions:
//
//    The above copyright notice and this permission notice shall be included in all copies or substantial portions of 
//    the Software.
//
//    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO 
//    THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
//    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF 
//    CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS 
//    IN THE SOFTWARE.
//  </copyright>
//  --------------------------------------------------------------------------------------------------------------------
//

namespace Sample.Domain
{
    using System;
    using System.IO;
    using Mobile.CQRS.SQLite;
    using Mobile.CQRS.SQLite.Domain;

    public class EventSourcedDB : SQLiteConnection
    {
        private static EventSourcedDB MainDatabase = new EventSourcedDB();

        public static string SampleDatabasePath()
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "EventSample.db");
            return path;
        }

        public static EventSourcedDB Main
        {
            get
            {
                return MainDatabase;
            }
        }

        protected EventSourcedDB() : base(SampleDatabasePath())
        {
            this.CreateTable<AggregateIndex>();
            this.CreateTable<AggregateEvent>();
            this.CreateTable<AggregateSnapshot>();
        }
    }
    
    public class ReadModelDB1 : SQLiteConnection
    {
        private static ReadModelDB1 MainDatabase = new ReadModelDB1();

        public static string SampleDatabasePath()
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "ReadModelSample1.db");
            return path;
        }

        public static ReadModelDB1 Main
        {
            get
            {
                return MainDatabase;
            }
        }

        protected ReadModelDB1() : base(SampleDatabasePath())
        {
            this.CreateTable<TransactionDataContract>();
        }
    }
    
    public class ReadModelDB2 : SQLiteConnection
    {
        private static ReadModelDB2 MainDatabase = new ReadModelDB2();

        public static string SampleDatabasePath()
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "ReadModelSample2.db");
            return path;
        }

        public static ReadModelDB2 Main
        {
            get
            {
                return MainDatabase;
            }
        }

        protected ReadModelDB2() : base(SampleDatabasePath())
        {
            this.CreateTable<TransactionDataContract>();
        }
    }

    public class SnapshotSourcedDB : SQLiteConnection
    {
        private static SnapshotSourcedDB MainDatabase = new SnapshotSourcedDB();

        public static string SampleDatabasePath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "SnapshotSample.db");
        }

        public static SnapshotSourcedDB Main
        {
            get
            {
                return MainDatabase;
            }
        }

        protected SnapshotSourcedDB() : base(SampleDatabasePath())
        {
            this.CreateTable<AggregateIndex>();
            this.CreateTable<AggregateEvent>();
            this.CreateTable<AggregateSnapshot>();
            this.CreateTable<TestSnapshot>();
            this.CreateTable<TransactionDataContract>();
        }
    }
}

