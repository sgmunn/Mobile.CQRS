//  --------------------------------------------------------------------------------------------------------------------
//  <copyright file="AggregateManifestRepository.cs" company="sgmunn">
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

namespace Mobile.CQRS.Domain.SQLite
{
    using System;
    using Mobile.CQRS.Data.SQLite;

    public class AggregateManifestRepository : IAggregateManifestRepository
    {
        ////private const string UpdateSql = "update AggregateManifest set Version = ? where Identity = ? and Version = ?";
        private const string UpdateSql = "update AggregateManifest set Version = {0} where Identity = '{1}' and Version = {2}";

        private readonly SQLiteConnection connection;

        public AggregateManifestRepository(SQLiteConnection connection)
        {
            this.connection = connection;
        }

        public void UpdateManifest(string aggregateType, Guid aggregateId, int currentVersion, int newVersion)
        {
            bool updated = false;

            try
            {
                lock (this.connection)
                {
                    updated = this.DoUpdate(aggregateType, aggregateId, currentVersion, newVersion);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("** Unable to update Aggregate Manifest **", ex);
            }

            if (!updated)
            {
                Console.WriteLine("AggregateManifest FAILED");
                throw new ConcurrencyException(aggregateId, newVersion, currentVersion);
            }
        }

        private bool DoUpdate(string aggregateType, Guid aggregateId, int currentVersion, int newVersion)
        {
            if (currentVersion == 0)
            {
                this.connection.Insert(new AggregateManifest { Identity = aggregateId, AggregateType = aggregateType, Version = newVersion, });
            }
            else
            {
                ////var rows = this.connection.Execute(UpdateSql, newVersion, aggregateId, currentVersion);
                var rows = this.connection.Execute(string.Format(UpdateSql, newVersion, aggregateId, currentVersion));
                return rows == 1;
            }

            return true;
        }
    }
}