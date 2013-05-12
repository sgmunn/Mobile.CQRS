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

namespace Mobile.CQRS.Data.SQLite
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Mobile.CQRS.Reactive;

    public class SqlRepository<T> : IRepository<T>, IRepositoryConnection, IObservableRepository 
        where T: IUniqueId, new()
    {
        private readonly SQLiteConnection connection;

        private readonly Subject<IModelNotification> changes;
        
        public SqlRepository(SQLiteConnection connection)
        {
            this.connection = connection;
            this.changes = new Subject<IModelNotification>();
        }

        object IRepositoryConnection.Connection
        {
            get
            {
                return this.Connection;
            }
        }

        public IObservable<IModelNotification> Changes
        {
            get
            {
                return this.changes;
            }
        }

        public SQLiteConnection Connection
        {
            get
            {
                return this.connection;
            }
        }

        public void Dispose()
        {
        }

        public virtual T New()
        {
            return new T();
        }

        public virtual T GetById(Guid id)
        {
            try
            {
                lock (this.Connection)
                {
                    return this.Connection.Get<T>(id);
                }
            }
            catch
            {
                return default(T);
            }
        }

        public virtual IList<T> GetAll()
        {
            lock (this.Connection)
            {
                return this.Connection.Table<T>().ToList();
            }
        }

        public virtual SaveResult Save(T instance)
        {
            var result = SaveResult.Updated;

            lock (this.Connection)
            {
                if (this.Connection.Update(instance) == 0)
                {
                    this.Connection.Insert(instance);
                    result = SaveResult.Added;
                }
            }

            IModelNotification modelChange = null;
            switch (result)
            {
                case SaveResult.Added: 
                    modelChange = Notifications.CreateModelNotification(instance.Identity, instance, ModelChangeKind.Added);
                    break;
                case SaveResult.Updated:
                    modelChange = Notifications.CreateModelNotification(instance.Identity, instance, ModelChangeKind.Changed);
                    break;
            }

            this.changes.OnNext(modelChange);

            return result;
        }

        public virtual void Delete(T instance)
        {
            lock (this.Connection)
            {
                this.Connection.Delete(instance);  
            }

            var modelChange = Notifications.CreateModelNotification(instance.Identity, null, ModelChangeKind.Deleted);
            this.changes.OnNext(modelChange);
        }

        public virtual void DeleteId(Guid id)
        {
            lock (this.Connection)
            {
                var map = this.Connection.GetMapping(typeof(T));
                var pk = map.PK;
                
                if (pk == null) 
                {
                    throw new NotSupportedException ("Cannot delete " + map.TableName + ": it has no PK");
                }
                
                var q = string.Format ("delete from \"{0}\" where \"{1}\" = ?", map.TableName, pk.Name);
                this.Connection.Execute (q, id);
            }

            var modelChange = Notifications.CreateModelNotification(id, null, ModelChangeKind.Deleted);
            this.changes.OnNext(modelChange);
        }
    }    
}