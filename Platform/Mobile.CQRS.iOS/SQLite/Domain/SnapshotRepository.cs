// --------------------------------------------------------------------------------------------------------------------
// <copyright file="SnapshotRepository.cs" company="sgmunn">
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
    using Mobile.CQRS.Serialization;
    using Mobile.CQRS.Domain;
    
    public sealed class SnapshotRepository : SqlRepository<AggregateSnapshot>, ISnapshotRepository
    {
        private readonly ISerializer<ISnapshot> serializer;
        
        private readonly IAggregateIndexRepository index;

        public SnapshotRepository(SQLiteConnection connection, ISerializer<ISnapshot> serializer) : base(connection)
        {
            if (serializer == null)
                throw new ArgumentNullException("serializer");

            this.serializer = serializer;
            this.index = new AggregateIndexRepository(connection);
            connection.CreateTable<AggregateSnapshot>();
        }
        
        public SaveResult Save(ISnapshot instance)
        {
            var snapshot = new AggregateSnapshot {
                Identity = instance.Identity,
                Version = instance.Version,
                SnapshotType = AggregateRootBase.GetAggregateTypeDescriptor(instance.GetType()),
                ObjectData = this.serializer.SerializeToString(instance),
            };

            return base.Save(snapshot);
        }
        
        public SaveResult Save(ISnapshot instance, int expectedVersion)
        {
            this.index.UpdateIndex(instance.Identity, expectedVersion, instance.Version);
            return this.Save(instance);
        }

        public new ISnapshot GetById(Guid id)
        {
            var snapshot = base.GetById(id);

            if (snapshot == null)
            {
                return null;
            }

            return this.serializer.DeserializeFromString(snapshot.ObjectData);;
        }
    }
}