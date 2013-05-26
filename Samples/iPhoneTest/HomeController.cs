// //  --------------------------------------------------------------------------------------------------------------------
// //  <copyright file="HomeController.cs" company="sgmunn">
// //    (c) sgmunn 2013  
// //
// //    Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated 
// //    documentation files (the "Software"), to deal in the Software without restriction, including without limitation 
// //    the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and 
// //    to permit persons to whom the Software is furnished to do so, subject to the following conditions:
// //
// //    The above copyright notice and this permission notice shall be included in all copies or substantial portions of 
// //    the Software.
// //
// //    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO 
// //    THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// //    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF 
// //    CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS 
// //    IN THE SOFTWARE.
// //  </copyright>
// //  --------------------------------------------------------------------------------------------------------------------

namespace Sample.Domain
{
    using System;
    using MonoTouch.Dialog;

    public class HomeController : DialogViewController
    {
        public HomeController() : base(new RootElement("Home")) 
        {
            this.Root.Add
                (
                    new Section("Sync Test") 
                    {
                    new StringElement("Reset", SyncSample.ResetSample),  
                    new StringElement("Create (client) 1", SyncSample.CreateRootClient1),  
                    new StringElement("Client 1 sync with remote", SyncSample.Client1SyncWithRemote),  
                    new StringElement("Client 2 sync with remote", SyncSample.Client2SyncWithRemote),  
                    new StringElement("Edit on client 1", SyncSample.EditClient1),  
                    new StringElement("Edit on client 2", SyncSample.EditClient2),  
                });
            
            this.Root.Add
                (
                    new Section("Event Sourced") 
                    {
                    new StringElement("Test 1", EventSourceSamples.DoTest1),  
                    new StringElement("Test 2", EventSourceSamples.DoTest2),  
                    new StringElement("Delete Test", EventSourceSamples.DoDeleteTest),  
                });

            this.Root.Add
                (
                    new Section("Snapshot Sourced") 
                    {
                    new StringElement("Test 1", SnapshotSamples.DoTest1),  
                    new StringElement("Test 2", SnapshotSamples.DoTest2),  
                }
                );
        }
    }
}