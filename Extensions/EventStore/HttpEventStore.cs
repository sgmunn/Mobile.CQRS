// --------------------------------------------------------------------------------------------------------------------
// <copyright file="HttpEventStore.cs" company="sgmunn">
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
using System.Net.Http;
using Newtonsoft.Json;
using Mobile.CQRS.Serialization;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Xml;

namespace Mobile.CQRS.EventStore
{
    using System;
    using System.Collections.Generic;
    using Mobile.CQRS.Domain;
//
//    public sealed class AggregateEvent : IUniqueId
//    {
//        public Guid Identity { get; set; }
//
//        public Guid AggregateId { get; set; }
//
//        public int Version { get; set; }
//
//        public string AggregateType { get; set; }
//
//        public string EventData { get; set; }
//    }


    public class RemoteEvent
    {
        public int Version { get; set; }

        public string Data { get; set; }
    }

    public class RemoteEventStream
    {
        public RemoteEventStream()
        {
            this.Events = new List<RemoteEvent>();
        }

        public string Id { get; set; }

        public int Version { get; set; }

        public List<RemoteEvent> Events { get; set; }
    }

    public class RemoteEventSaveRequest
    {
        public RemoteEventSaveRequest()
        {
            this.Events = new List<RemoteEvent>();
        }

        public string Id { get; set; }

        public int ExpectedVersion { get; set; }

        public List<RemoteEvent> Events { get; set; }
    }

    public class HttpEventStore : IEventStore
    {
        private readonly ISerializer<IAggregateEvent> serializer;

        public HttpEventStore(ISerializer<IAggregateEvent> serializer)
        {
            this.serializer = serializer;
        }

        public void SaveEvents(Guid aggregateId, IList<IAggregateEvent> events, int expectedVersion)
        {
            if (events.Count == 0)
            {
                return;
            }

            Console.WriteLine(aggregateId);

            var serializedEvents = events.Select(evt => new RemoteEvent{
                Version = evt.Version,
                Data = this.serializer.SerializeToString(evt),
            }).ToList();

            //var newVersion = events[events.Count - 1].Version;
            //this.index.UpdateIndex(aggregateId, expectedVersion, newVersion);

            var x = new RemoteEventSaveRequest();
            x.Id = aggregateId.ToString();
            x.ExpectedVersion = expectedVersion;
            x.Events = serializedEvents;

            var client = new HttpClient();
            using (client)
            {
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var dataToSend = JsonConvert.SerializeObject(x);
                using (var content = new StringContent(dataToSend, Encoding.UTF8, "application/json"))
                {
                    var response = client.PostAsync(string.Format("http://helloservicestack.apphb.com/eventsx/{0}", aggregateId), content).Result;

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new HttpRequestException(response.StatusCode.ToString());
                    }
                }
            }
        }

        public int GetCurrentVersion(Guid rootId)
        {
            var client = new HttpClient();
            using (client)
            {
                var response = client.GetStringAsync(string.Format("http://helloservicestack.apphb.com/events/{0}?VersionOnly=1&format=json", rootId)).Result;
                Console.WriteLine(response);
                var stream = JsonConvert.DeserializeObject<RemoteEventStream>(response);

                return stream.Version;
            }
        }

        public IList<IAggregateEvent> GetAllEvents(Guid rootId)
        {
            // yes, really bad code here...
            var client = new HttpClient();
            using (client)
            {
                var response = client.GetStringAsync(string.Format("http://helloservicestack.apphb.com/events/{0}?format=json", rootId)).Result;
                Console.WriteLine(response);
                var stream = JsonConvert.DeserializeObject<RemoteEventStream>(response);

                return stream.Events.Select(evt => this.serializer.DeserializeFromString(evt.Data)).ToList();
            }
        }

        public IList<IAggregateEvent> GetEventsAfterVersion(Guid rootId, int version)
        {
            var client = new HttpClient();
            using (client)
            {
                var response = client.GetStringAsync(string.Format("http://helloservicestack.apphb.com/events/{0}?Skip={1}&format=json", rootId, version)).Result;
                Console.WriteLine(response);
                var stream = JsonConvert.DeserializeObject<RemoteEventStream>(response);

                return stream.Events.Select(evt => this.serializer.DeserializeFromString(evt.Data)).ToList();
            }
        }

        public IList<IAggregateEvent> GetEventsUpToVersion(Guid rootId, int version)
        {
            var client = new HttpClient();
            using (client)
            {
                var response = client.GetStringAsync(string.Format("http://helloservicestack.apphb.com/events/{0}?Skip=0&Take={1}&format=json", rootId, version)).Result;
                Console.WriteLine(response);
                var stream = JsonConvert.DeserializeObject<RemoteEventStream>(response);

                return stream.Events.Select(evt => this.serializer.DeserializeFromString(evt.Data)).ToList();
            }
        }

        public void Dispose()
        {
        }
    }
}

