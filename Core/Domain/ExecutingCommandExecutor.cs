// --------------------------------------------------------------------------------------------------------------------
// <copyright file="ExecutingCommandExecutor.cs" company="sgmunn">
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

namespace Mobile.CQRS.Domain
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;

    public sealed class ExecutingCommandExecutor : ICommandExecutor
    {
        private readonly IAggregateRoot root;

        public ExecutingCommandExecutor(IAggregateRoot root)
        {
            this.root = root;
        }

        public void Execute(IList<IAggregateCommand> commands, int expectedVersion)
        {
            foreach (var cmd in commands)
            {
                this.Execute(this.root, cmd);
            }
        }

        private void Execute(object aggregate, object command)
        {
            try
            {
                if (!MethodExecutor.ExecuteMethod(aggregate, command))
                {
                    throw new MissingMethodException(string.Format("Aggregate {0} does not support a method that can be called with {1}", aggregate, command));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error executing command\n{0}", ex);
                throw;
            }
        }
    }
}