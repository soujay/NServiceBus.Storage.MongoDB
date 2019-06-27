﻿using System;
using MongoDB.Bson.Serialization;

namespace NServiceBus.Storage.MongoDB
{
    static class TypeExtensions
    {
        public static string GetElementName(this Type type, string propertyName)
        {
            var classMap = BsonClassMap.LookupClassMap(type);

            foreach (var memberMap in classMap.AllMemberMaps)
            {
                if (memberMap.MemberName == propertyName)
                {
                    return memberMap.ElementName;
                }
            }

            throw new InvalidOperationException($"Property '{propertyName}' not found in '{type}' class map.");
        }
    }
}
