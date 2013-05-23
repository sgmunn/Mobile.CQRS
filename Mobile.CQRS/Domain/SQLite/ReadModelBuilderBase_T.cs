// --------------------------------------------------------------------------------------------------------------------
// <copyright file="ReadModelBuilderBase_T.cs" company="sgmunn">
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
    using Mobile.CQRS.Data.SQLite;

    public abstract class ReadModelBuilderBase<T> : ReadModelBuilder<T>
        where T : class, IUniqueId, new()
    {
        protected ReadModelBuilderBase(SQLiteConnection connection) : base(new SqlRepository<T>(connection))
        {
            this.Connection = connection;
            this.TableName = typeof(T).Name;
        }

        public SQLiteConnection Connection { get; private set; }

        protected string TableName { get; set; }

        protected string AggregateFieldName { get; set; }

        public override void DeleteForAggregate(Guid aggregateId)
        {
            // TODO: a better way of declaring how to perform the delete would be good
            if (!string.IsNullOrWhiteSpace(this.AggregateFieldName))
            {
                this.Connection.Execute(string.Format("delete from {0} where {1} = ?", this.TableName, this.AggregateFieldName), aggregateId);
            }
        }
    }
}