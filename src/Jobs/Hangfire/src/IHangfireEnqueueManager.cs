// Licensed to the Hoff Tech under one or more agreements.
// The Hoff Tech licenses this file to you under the MIT license.

namespace Gems.Jobs.Hangfire
{
    public interface IHangfireEnqueueManager
    {
        void Enqueue<T>(string name, T command);
    }
}
