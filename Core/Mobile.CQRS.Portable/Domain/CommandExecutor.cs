// --------------------------------------------------------------------------------------------------------------------
// <copyright file="CommandExecutor.cs" company="sgmunn">
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
using System.Threading.Tasks;

namespace Mobile.CQRS.Domain
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    
    public sealed class CommandExecutor : ICommandExecutor
    {
        private readonly IAggregateRepository repository;

        private readonly Dictionary<Guid, int> versions;

        public CommandExecutor(IAggregateRepository aggregateRepository)
        {
            this.repository = aggregateRepository;
            this.versions = new Dictionary<Guid, int>();
        }

        public async Task ExecuteAsync(IList<IAggregateCommand> commands, int expectedVersion)
        {
            if (!commands.Any())
            {
                return;
            }

            var rootId = commands.First().AggregateId;

            if (commands.Any(x => x.AggregateId != rootId))
            {
                throw new InvalidOperationException("Can only execute commands for a single aggregate at a time");
            }

            var root = await this.repository.GetByIdAsync(rootId).ConfigureAwait(false);
            if (root == null)
            {
                root = this.repository.New();
                root.Identity = rootId;
            }

            if (expectedVersion != 0)
            {
                // if we using this command executor multiple times for the same aggregate, when we invoke it we will often have the 
                // verion of the roor prior to the first invocation. Consequently when we check the expected version the second time, 
                // we have a different version and we get an exception. This is supposed to handle that case by remembering what the 
                // version of the root was when we first saw it.
                var rootVersion = root.Version;
                if (this.versions.ContainsKey(root.Identity))
                {
                    rootVersion = this.versions[root.Identity];
                }

                if (rootVersion != expectedVersion)
                {
                    throw new InvalidOperationException(string.Format("Not Expected Version {0}, {1}", expectedVersion, root.Version));
                }
            }

            var exec = new ExecutingCommandExecutor(root);
            await exec.ExecuteAsync(commands, expectedVersion).ConfigureAwait(false);

            this.repository.Save(root);

            if (expectedVersion != 0 && !this.versions.ContainsKey(root.Identity))
            {
                this.versions[root.Identity] = expectedVersion;
            }
        }
    }
}