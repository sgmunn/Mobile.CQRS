// --------------------------------------------------------------------------------------------------------------------
// <copyright file="SyncStateRepository.cs" company="sgmunn">
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
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Mobile.CQRS.Domain;
    
    public sealed class SyncStateRepository : IRepository<ISyncState> 
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

        public async Task<ISyncState> GetByIdAsync(Guid id)
        {
            return await this.repository.GetByIdAsync(id).ConfigureAwait(false);
        }

        public async Task<IList<ISyncState>> GetAllAsync()
        {
            return (await this.repository.GetAllAsync().ConfigureAwait(false)).Cast<ISyncState>().ToList();
        }

        public Task<SaveResult> SaveAsync(ISyncState instance)
        {
            return this.repository.SaveAsync((SyncState)instance);
        }

        public Task DeleteAsync(ISyncState instance)
        {
            return this.repository.DeleteAsync((SyncState)instance);
        }

        public Task DeleteIdAsync(Guid id)
        {
            return this.repository.DeleteIdAsync(id);
        }

        public void Dispose()
        {
            this.repository.Dispose();
        }
    }
}