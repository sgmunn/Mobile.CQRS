// --------------------------------------------------------------------------------------------------------------------
// <copyright file="IDomainContext.cs" company="sgmunn">
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

namespace Mobile.CQRS.Domain
{
    using System;
    using System.Collections.Generic;
    using Mobile.CQRS.Data;
    using Mobile.CQRS.Serialization;

    public interface IDomainContext
    {
        IEventStore EventStore { get; }
        
        IDomainNotificationBus EventBus { get; }

        ISerializer<IAggregateEvent> EventSerializer { get; }

        IAggregateManifestRepository Manifest { get; }

      //  ISerializer<T> GetSerializer<T>();
        
//        ISerializer<ISnapshot> SnapshotSerializer { get; }

        ISnapshotRepository GetSnapshotRepository<T>();

        IUnitOfWorkScope BeginUnitOfWork();

        ICommandExecutor<T> NewCommandExecutor<T>() where T : class, IAggregateRoot, new();
        
        IAggregateRepository<T> GetAggregateRepository<T>(IDomainNotificationBus bus) where T : IAggregateRoot, new();

        IList<IReadModelBuilder> GetReadModelBuilders<T>() where T : IAggregateRoot, new();
    }
}