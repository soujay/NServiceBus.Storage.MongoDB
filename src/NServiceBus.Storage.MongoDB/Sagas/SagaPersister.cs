﻿using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using NServiceBus.Extensibility;
using NServiceBus.Persistence;
using NServiceBus.Sagas;

namespace NServiceBus.Storage.MongoDB
{
    class SagaPersister : ISagaPersister
    {
        public SagaPersister(string versionElementName)
        {
            this.versionElementName = versionElementName;
        }

        public async Task Save(IContainSagaData sagaData, SagaCorrelationProperty correlationProperty, SynchronizedStorageSession session, ContextBag context)
        {
            var storageSession = (StorageSession)session;
            var sagaDataType = sagaData.GetType();
            var collection = storageSession.GetCollection(sagaDataType);

            if (correlationProperty != null && !createdIndexCache.ContainsKey(sagaDataType.Name))
            {
                var propertyElementName = GetElementName(BsonClassMap.LookupClassMap(sagaDataType), correlationProperty.Name);

                var indexModel = new CreateIndexModel<BsonDocument>(new BsonDocumentIndexKeysDefinition<BsonDocument>(new BsonDocument(propertyElementName, 1)), new CreateIndexOptions() { Unique = true });

                await collection.Indexes.CreateOneAsync(indexModel).ConfigureAwait(false);

                createdIndexCache.GetOrAdd(sagaDataType.Name, true);
            }

            var document = sagaData.ToBsonDocument();
            document.Add(versionElementName, 0);

            await collection.InsertOneAsync(document).ConfigureAwait(false);
        }

        public async Task Update(IContainSagaData sagaData, SynchronizedStorageSession session, ContextBag context)
        {
            var storageSession = (StorageSession)session;
            var sagaDataType = sagaData.GetType();
            var collection = storageSession.GetCollection(sagaDataType);

            var version = storageSession.RetrieveVersion(sagaDataType);

            var filterBuilder = Builders<BsonDocument>.Filter;
            var filter = filterBuilder.Eq(idElementName, sagaData.Id) & filterBuilder.Eq(versionElementName, version);

            var document = sagaData.ToBsonDocument();
            var updateBuilder = Builders<BsonDocument>.Update;
            var update = updateBuilder.Inc(versionElementName, 1);

            foreach (var element in document)
            {
                if (element.Name != versionElementName && element.Name != idElementName)
                {
                    update = update.Set(element.Name, element.Value);
                }
            }

            var modifyResult = await collection.FindOneAndUpdateAsync(
                filter,
                update,
                new FindOneAndUpdateOptions<BsonDocument> { IsUpsert = false, ReturnDocument = ReturnDocument.After }).ConfigureAwait(false);

            if (modifyResult == null)
            {
                throw new Exception($"The '{sagaDataType.Name}' saga with id '{sagaData.Id}' was updated by another process or no longer exists.");
            }
        }

        public async Task<TSagaData> Get<TSagaData>(Guid sagaId, SynchronizedStorageSession session, ContextBag context) where TSagaData : class, IContainSagaData
        {
            var storageSession = (StorageSession)session;
            var sagaDataType = typeof(TSagaData);
            var collection = storageSession.GetCollection(sagaDataType);

            var document = await collection.Find(new BsonDocument(idElementName, sagaId)).FirstOrDefaultAsync().ConfigureAwait(false);

            if (document != null)
            {
                var version = document.GetValue(versionElementName);
                document.Remove(versionElementName);
                storageSession.StoreVersion(sagaDataType, version);

                if (!BsonClassMap.IsClassMapRegistered(sagaDataType))
                {
                    BsonClassMap.RegisterClassMap<TSagaData>(cm =>
                    {
                        cm.AutoMap();
                        cm.SetIgnoreExtraElements(true);
                    });
                }

                return BsonSerializer.Deserialize<TSagaData>(document);
            }

            return default;
        }

        public async Task<TSagaData> Get<TSagaData>(string propertyName, object propertyValue, SynchronizedStorageSession session, ContextBag context) where TSagaData : class, IContainSagaData
        {
            var storageSession = (StorageSession)session;
            var sagaDataType = typeof(TSagaData);
            var collection = storageSession.GetCollection(sagaDataType);

            var classMap = BsonClassMap.LookupClassMap(sagaDataType);
            var propertyElementName = GetElementName(classMap, propertyName);

            var document = await collection.Find(new BsonDocument(propertyElementName, BsonValue.Create(propertyValue))).Limit(1).FirstOrDefaultAsync().ConfigureAwait(false);

            if (document != null)
            {
                var version = document.GetValue(versionElementName);
                document.Remove(versionElementName);
                storageSession.StoreVersion(sagaDataType, version);

                if (!BsonClassMap.IsClassMapRegistered(sagaDataType))
                {
                    BsonClassMap.RegisterClassMap<TSagaData>(cm =>
                    {
                        cm.AutoMap();
                        cm.SetIgnoreExtraElements(true);
                    });
                }

                return BsonSerializer.Deserialize<TSagaData>(document);
            }

            return default;
        }

        public Task Complete(IContainSagaData sagaData, SynchronizedStorageSession session, ContextBag context)
        {
            var storageSession = (StorageSession)session;
            var sagaDataType = sagaData.GetType();
            var collection = storageSession.GetCollection(sagaDataType);

            return collection.DeleteOneAsync(new BsonDocument(idElementName, sagaData.Id));
        }

        string GetElementName(BsonClassMap classMap, string property)
        {
            foreach(var memberMap in classMap.AllMemberMaps)
            {
                if (memberMap.MemberName == property)
                {
                    return memberMap.ElementName;
                }
            }

            throw new ArgumentException($"Property '{property}' not found in class member map.", nameof(property));
        }

        const string idElementName = "_id";
        readonly string versionElementName;
        readonly ConcurrentDictionary<string, bool> createdIndexCache = new ConcurrentDictionary<string, bool>();
    }
}