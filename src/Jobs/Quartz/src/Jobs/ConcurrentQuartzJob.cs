// Licensed to the Hoff Tech under one or more agreements.
// The Hoff Tech licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;

using MediatR;

using Microsoft.Extensions.Logging;

using Quartz;

namespace Gems.Jobs.Quartz.Jobs
{
    public class ConcurrentQuartzJob<T> : IJob where T : class, IRequest, new()
    {
        private readonly IMediator mediator;
        private readonly ILogger<ConcurrentQuartzJob<T>> logger;

        public ConcurrentQuartzJob(IMediator mediator, ILogger<ConcurrentQuartzJob<T>> logger)
        {
            this.mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task Execute(IJobExecutionContext context)
        {
            this.logger.LogInformation($"Concurrent Job {typeof(T).Name} {context.FireInstanceId} executing at {DateTime.UtcNow}");
            try
            {
                await this.mediator.Send(new T(), context.CancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }
            finally
            {
                this.logger.LogInformation($"Concurrent Job {typeof(T).Name} {context.FireInstanceId} executed at {DateTime.UtcNow}");
            }
        }
    }
}
