﻿using NServiceBus.Features;
using NServiceBus.Persistence.MongoDB.Database;

namespace NServiceBus.Persistence.MongoDB.Timeout
{
    public class MongoDbTimeoutStorage : Feature
    {
        internal MongoDbTimeoutStorage()
        {
            DependsOn<TimeoutManager>();
            DependsOn<MongoDbStorage>();
        }

        /// <summary>
        /// Called when the feature should perform its initialization. This call will only happen if the feature is enabled.
        /// </summary>
        protected override void Setup(FeatureConfigurationContext context)
        {
            context.Container.ConfigureComponent<TimeoutPersister>(DependencyLifecycle.InstancePerCall)
                .ConfigureProperty(x => x.EndpointName, context.Settings.EndpointName());
        }
    }
}
