// Licensed to the Hoff Tech under one or more agreements.
// The Hoff Tech licenses this file to you under the MIT license.

using System.Text.Json.Serialization;

using MediatR;

using Microsoft.AspNetCore.Mvc;

namespace Gems.Jobs.Quartz.Handlers.RemoveJob
{
    public class RemoveJobCommand : IRequest
    {
        [FromRoute]
        [JsonIgnore]
        public string JobName { get; set; }

        [FromQuery]
        public string JobGroup { get; set; }
    }
}
