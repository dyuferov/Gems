﻿// Licensed to the Hoff Tech under one or more agreements.
// The Hoff Tech licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace Gems.Jobs.Quartz.Handlers.GetSchedulerInfo.Dto;

public class SchedulerInfo
{
    public string SchedulerName { get; set; }

    public string SchedulerInstanceId { get; set; }

    public List<JobInfo> EnqueuedJobList { get; set; }
}
