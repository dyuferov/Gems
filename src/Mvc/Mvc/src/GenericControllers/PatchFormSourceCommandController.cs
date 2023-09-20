// Licensed to the Hoff Tech under one or more agreements.
// The Hoff Tech licenses this file to you under the MIT license.

using System.Threading;
using System.Threading.Tasks;

using MediatR;

using Microsoft.AspNetCore.Mvc;

namespace Gems.Mvc.GenericControllers
{
    public class PatchFormSourceCommandController<T> where T : class, IRequest
    {
        private readonly IMediator mediator;

        public PatchFormSourceCommandController(IMediator mediator)
        {
            this.mediator = mediator;
        }

        [HttpPatch]
        public Task Patch([FromForm] T request, CancellationToken cancellationToken)
        {
            return this.mediator.Send(request, cancellationToken);
        }
    }
}
