﻿// Licensed to the Hoff Tech under one or more agreements.
// The Hoff Tech licenses this file to you under the MIT license.

using Gems.Patterns.SyncTables.MergeProcessor;
using Gems.Patterns.SyncTables.Options;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gems.Patterns.SyncTables
{
    public static class ServiceCollectionExtensions
    {
        public static void AddTableSyncer(this IServiceCollection services, IConfigurationSection section)
        {
            services.Configure<ChangeTrackingSyncOptions>(section);

            services.AddSingleton<EntitiesUpdater>();
            services.AddSingleton<ExternalEntitiesProvider>();
            services.AddSingleton<ExternalEntitiesProvider>();
            services.AddSingleton<RowVersionProvider>();
            services.AddSingleton<RowVersionUpdater>();
            services.AddSingleton<ChangeTrackingMergeProcessorFactory>();
            services.AddSingleton<MergeProcessorFactory>();
        }
    }
}
