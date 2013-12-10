// --------------------------------------------------------------------------------------------------------------------
// <copyright file="PendingCommandRepository.cs" company="sgmunn">
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
    using Mobile.CQRS.Serialization;
    using System.Threading.Tasks;
    using Mobile.CQRS.Domain;

    public sealed class PendingCommandRepository : SqlRepository<PendingCommand>, IPendingCommandRepository
    {
        private readonly ISerializer<IAggregateCommand> serializer;

        public PendingCommandRepository(SQLiteConnection connection, ISerializer<IAggregateCommand> serializer) : base(connection)
        {
            if (serializer == null)
                throw new ArgumentNullException("serializer");

            this.serializer = serializer;

            connection.CreateTable<PendingCommand>();
            this.ScopeFieldName = "AggregateId";
        }

        public Task StorePendingCommandAsync(IAggregateCommand command)
        {
            var cmd = new PendingCommand {
                AggregateId = command.AggregateId,
                CommandId = command.Identity,
                Identity = Guid.NewGuid(),
                CommandData = this.serializer.SerializeToString(command),
            };

            return this.SaveAsync(cmd);
        }

        public async Task<IList<IAggregateCommand>> PendingCommandsForAggregateAsync(Guid id)
        {
             var commands = this.Connection.Table<PendingCommand>()
                    .Where(x => x.AggregateId == id)
                        .OrderByDescending(x => x.Key)
                        .ToList();

            return commands.Select(cmd => this.serializer.DeserializeFromString(cmd.CommandData)).ToList();
        }

        public Task RemovePendingCommandsAsync(Guid id)
        {
            return this.DeleteAllInScopeAsync(id);
        }
    }
}