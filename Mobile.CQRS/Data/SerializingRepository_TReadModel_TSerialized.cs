// --------------------------------------------------------------------------------------------------------------------
// <copyright file="SerializingRepository_TReadModel_TSerialized.cs" company="sgmunn">
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

namespace Mobile.CQRS.Data
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using Mobile.CQRS.Data;
    using Mobile.CQRS.Serialization;

    /// <summary>
    /// Stores a TReadModel serialized as a string in another object
    /// </summary>
    public class SerializingRepository<TReadModel, TSerialized> : IRepository<TReadModel> 
        where TReadModel : IUniqueId, new() where TSerialized : ISerializedReadModel, new() 
    {
        private readonly IRepository<TSerialized> repository;

        private readonly ISerializer<TReadModel> serializer;

        public SerializingRepository(IRepository<TSerialized> repository, ISerializer<TReadModel> serializer)
        {
            this.repository = repository;
            this.serializer = serializer;
        }

        protected IRepository<TSerialized> Repository
        {
            get
            {
                return this.repository;
            }
        }
        
        protected ISerializer<TReadModel> Serializer
        {
            get
            {
                return this.serializer;
            }
        }

        public void Dispose()
        {
            this.Repository.Dispose();
        }

        public TReadModel New()
        {
            return new TReadModel();
        }

        public TReadModel GetById(Guid id)
        {
            var serialized = this.Repository.GetById(id);
            if (serialized == null)
            {
                return default(TReadModel);
            }

            return this.Serializer.DeserializeFromString(serialized.ObjectData);
        }

        public virtual IList<TReadModel> GetAll()
        {
            var result = new List<TReadModel>();
            var serialized = this.Repository.GetAll();

            foreach (var item in serialized)
            {
                result.Add(this.Serializer.DeserializeFromString(item.ObjectData));
            }

            return result;
        }

        public virtual SaveResult Save(TReadModel instance)
        {
            var serialized = this.Repository.New();
            serialized.ObjectData = this.Serializer.SerializeToString(instance);
            serialized.Identity = instance.Identity;

            this.Flatten(instance, serialized);

            return this.Repository.Save(serialized);
        }

        public virtual void Delete(TReadModel instance)
        {
            this.Repository.DeleteId(instance.Identity);
        }

        public virtual void DeleteId(Guid id)
        {
            this.Repository.DeleteId(id);
        }

        protected virtual void Flatten(TReadModel readModel, TSerialized serialized)
        {
            var props = readModel.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var serializedProps = serialized.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in props)
            {
                var serializedProp = serializedProps.FirstOrDefault(sp => sp.Name == prop.Name);
                if (serializedProp != null && prop.Name != "Identity" && prop.Name != "ObjectData" && prop.PropertyType == serializedProp.PropertyType)
                {
                    if (serializedProp.CanWrite)
                    {
                        serializedProp.SetValue(serialized, prop.GetValue(readModel));
                    }
                }
            }
        }
    }
}