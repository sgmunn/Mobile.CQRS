// --------------------------------------------------------------------------------------------------------------------
// <copyright file="SyncState.cs" company="sgmunn">
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
using System.Collections.Generic;
using System.Linq;

namespace Mobile.CQRS.SQLite.Domain
{
    using System;
    using Mobile.CQRS.Domain;

    public sealed class SyncState : ISyncState, IUniqueId
    {
        [PrimaryKey]
        public Guid Identity { get; set; }
        
        [Indexed]
        public string AggregateType { get; set; }

        public int LastSyncedVersion { get; set; }
    }

    
    public class SyncStateRepository : IRepository<ISyncState> 
    {
        private readonly SqlRepository<SyncState> repository;

        public SyncStateRepository(SQLiteConnection connection)
        {
            this.repository = new SqlRepository<SyncState>(connection);
            connection.CreateTable<SyncState>();
        }

        public ISyncState New()
        {
            return new SyncState();
        }

        public ISyncState GetById(Guid id)
        {
            return this.repository.GetById(id);
        }

        public IList<ISyncState> GetAll()
        {
            return this.repository.GetAll().Cast<ISyncState>().ToList();
        }

        public SaveResult Save(ISyncState instance)
        {
            return this.repository.Save((SyncState)instance);
        }

        public void Delete(ISyncState instance)
        {
            this.repository.Delete((SyncState)instance);
        }

        public void DeleteId(Guid id)
        {
            this.repository.DeleteId(id);
        }

        public void Dispose()
        {
            this.repository.Dispose();
        }
    }
}