// --------------------------------------------------------------------------------------------------------------------
// <copyright file="SqlRepository_T.cs" company="sgmunn">
//   (c) sgmunn 2012  
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

namespace Mobile.CQRS.SQLite
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    public class SqlRepository<T> : IRepository<T>, IScopedRepository 
        where T: IUniqueId, new()
    {
        private readonly SQLiteConnection connection;
        
        public SqlRepository(SQLiteConnection connection, string scopeFieldName = "")
        {
            this.connection = connection;
            this.ScopeFieldName = scopeFieldName;
            this.TableName = typeof(T).Name;
        }
        
        public SqlRepository(IUnitOfWorkScope scope, string scopeFieldName = "")
        {
            this.connection = scope.GetRegisteredObject<SQLiteConnection>();
            this.ScopeFieldName = scopeFieldName;
            this.TableName = typeof(T).Name;
        }

        public SQLiteConnection Connection
        {
            get
            {
                return this.connection;
            }
        }
        
        protected string TableName { get; set; }

        protected string ScopeFieldName { get; set; }

        public void Dispose()
        {
        }

        public virtual T New()
        {
            return new T();
        }

        public async virtual Task<T> GetByIdAsync(Guid id)
        {
            try
            {
                return await Task.Factory.StartNew<T>(() => this.Connection.Get<T>(id)).ConfigureAwait(false);
            }
            catch
            {
                return default(T);
            }
        }

        public virtual Task<IList<T>> GetAllAsync()
        {
            return Task.Factory.StartNew<IList<T>>(() => this.Connection.Table<T>().ToList());
        }

        public async virtual Task<SaveResult> SaveAsync(T instance)
        {
            var result = SaveResult.Updated;

            await Task.Factory.StartNew(() => {
                if (this.Connection.Update(instance) == 0)
                {
                    this.Connection.Insert(instance);
                    result = SaveResult.Added;
                }
            }).ConfigureAwait(false);

            return result;
        }

        public virtual Task DeleteAsync(T instance)
        {
            return Task.Factory.StartNew(() => this.Connection.Delete(instance));
        }

        public virtual Task DeleteIdAsync(Guid id)
        {
            var map = this.Connection.GetMapping(typeof(T));
            var pk = map.PK;
            
            if (pk == null) 
            {
                throw new NotSupportedException ("Cannot delete " + map.TableName + ": it has no PK");
            }
            
            var q = string.Format ("delete from \"{0}\" where \"{1}\" = ?", map.TableName, pk.Name);
            return Task.Factory.StartNew(() => this.Connection.Execute (q, id));
        }

        public virtual Task DeleteAllInScopeAsync(Guid scopeId)
        {
            if (string.IsNullOrWhiteSpace(this.ScopeFieldName) || string.IsNullOrEmpty(this.TableName))
            {
                throw new NotSupportedException();
            }

            return Task.Factory.StartNew(() => this.Connection.Execute(string.Format("delete from {0} where {1} = ?", this.TableName, this.ScopeFieldName), scopeId));
        }
    }    
}