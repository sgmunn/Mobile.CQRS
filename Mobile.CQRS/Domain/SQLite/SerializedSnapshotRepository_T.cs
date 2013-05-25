// --------------------------------------------------------------------------------------------------------------------
// <copyright file="SerializedSnapshotRepository_T.cs" company="sgmunn">
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

namespace Mobile.CQRS.Domain.SQLite
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Mobile.CQRS.Data;
    using Mobile.CQRS.Data.SQLite;
    using Mobile.CQRS.Serialization;

    public class SerializedSnapshotRepository<T> : ISnapshotRepository 
        where T : class, ISnapshot, new()
    {
        private readonly SerializingRepository<T, AggregateSnapshot> repository;
        
        private readonly IAggregateManifestRepository manifest;

        public SerializedSnapshotRepository(SQLiteConnection connection, ISerializer<T> serializer) 
        {
            var repo = new SqlRepository<AggregateSnapshot>(connection);
            this.repository = new SerializingRepository<T, AggregateSnapshot>(repo, serializer);
            this.manifest = new AggregateManifestRepository(connection);
            connection.CreateTable<AggregateSnapshot>();
        }

        public ISnapshot New()
        {
            return this.repository.New();
        }

        public ISnapshot GetById(Guid id)
        {
            return this.repository.GetById(id);
        }

        public IList<ISnapshot> GetAll()
        {
            return this.repository.GetAll().Cast<ISnapshot>().ToList();
        }

        public SaveResult Save(ISnapshot instance)
        {
            return this.repository.Save((T)instance);
        }
        
        public SaveResult Save(ISnapshot instance, int expectedVersion)
        {
            this.manifest.UpdateManifest(instance.Identity, expectedVersion, instance.Version);
            return this.Save(instance);
        }

        public void Delete(ISnapshot instance)
        {
            this.repository.Delete((T)instance);
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