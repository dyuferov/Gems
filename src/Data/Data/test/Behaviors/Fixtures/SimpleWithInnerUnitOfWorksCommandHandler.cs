﻿// Licensed to the Hoff Tech under one or more agreements.
// The Hoff Tech licenses this file to you under the MIT license.

using System;
using System.Threading;
using System.Threading.Tasks;

using Gems.Data.UnitOfWork;

using MediatR;

namespace Gems.Data.Tests.Behaviors.Fixtures;

public class SimpleWithInnerUnitOfWorksCommandHandler : IRequestHandler<SimpleWithInnerUnitOfWorksCommand>
{
    private readonly IMediator mediator;
    private readonly IUnitOfWorkProvider unitOfWorkProvider;

    public SimpleWithInnerUnitOfWorksCommandHandler(IMediator mediator, IUnitOfWorkProvider unitOfWorkProvider)
    {
        this.mediator = mediator;
        this.unitOfWorkProvider = unitOfWorkProvider;
    }

    public async Task Handle(SimpleWithInnerUnitOfWorksCommand request, CancellationToken cancellationToken)
    {
        await this.unitOfWorkProvider.GetUnitOfWork(cancellationToken).CallStoredProcedureAsync($"SimpleWithInnerUnitOfWorksCommand: {Guid.NewGuid()}")
            .ConfigureAwait(false);
        await this.mediator.Send(new SimpleWithUnitOfWorkCommand(), cancellationToken).ConfigureAwait(false);
        await this.mediator.Send(new SimpleWithUnitOfWorkCommand(), cancellationToken).ConfigureAwait(false);
    }
}
