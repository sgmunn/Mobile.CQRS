// --------------------------------------------------------------------------------------------------------------------
// <copyright file="SqlQuery_T.cs" company="sgmunn">
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

namespace Mobile.CQRS.SQLite
{
    using System;
    using System.Collections.Generic;
    using Mobile.CQRS.Data;

    /// <summary>
    /// Represents a query that returns instances of TState.
    /// </summary>
    public class SqlQuery<TState> : IQuery<TState>
        where TState : IUniqueId
    {
        private readonly SQLiteConnection connection;

        private Func<SQLiteConnection, int, int, IList<TState>> onExecute;

        public SqlQuery(SQLiteConnection connection)
        {
            this.connection = connection;
        }

        public SqlQuery(SQLiteConnection connection, Func<SQLiteConnection, int, int, IList<TState>> onExecute)
        {
            this.connection = connection;
            this.onExecute = onExecute;
        }

        public virtual IList<TState> Execute(int page, int pageSize)
        {
            if (this.onExecute != null)
            {
                return this.onExecute(this.connection, page, pageSize);
            }

            return null;
        }
        
        public IList<TState> Execute()
        {
            return this.Execute(0, 0);
        }
    }
}