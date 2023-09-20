﻿// Licensed to the Hoff Tech under one or more agreements.
// The Hoff Tech licenses this file to you under the MIT license.

using Gems.Patterns.SyncTables.EntitiesViews;

namespace Gems.Patterns.SyncTables.Tests.Infrastructure
{
    public class RealExternalEntity : ExternalEntity
    {
        public string Name { get; set; }

        public string Age { get; set; }
    }
}
