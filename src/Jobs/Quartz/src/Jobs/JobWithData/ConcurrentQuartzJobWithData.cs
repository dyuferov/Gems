// Licensed to the Hoff Tech under one or more agreements.
// The Hoff Tech licenses this file to you under the MIT license.

using MediatR;

using Microsoft.Extensions.Logging;

using Quartz;

namespace Gems.Jobs.Quartz.Jobs.JobWithData;

[DisallowConcurrentExecution]
public sealed class ConcurrentQuartzJobWithData<TCommand> : QuartzJobWithDataBase<TCommand> where TCommand : class, IRequest, new()
{
    public ConcurrentQuartzJobWithData(
        IMediator mediator,
        ILogger<ConcurrentQuartzJobWithData<TCommand>> logger)
        : base(mediator, logger)
    {
    }
}
