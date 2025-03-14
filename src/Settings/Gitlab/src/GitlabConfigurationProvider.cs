﻿// Licensed to the Hoff Tech under one or more agreements.
// The Hoff Tech licenses this file to you under the MIT license.

using System;

using Microsoft.Extensions.Configuration;

namespace Gems.Settings.Gitlab
{
    internal class GitlabConfigurationProvider : ConfigurationProvider
    {
        private readonly GitlabConfigurationSettings settings;

        public GitlabConfigurationProvider(GitlabConfigurationSettings settings)
        {
            this.settings = settings;
        }

        public override void Load()
        {
            var aspNetEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            if (!string.IsNullOrWhiteSpace(aspNetEnvironment))
            {
                if (this.settings.Prefixes.TryGetValue(aspNetEnvironment, out var prefix))
                {
                    var url = this.settings.GitlabUrl ?? Environment.GetEnvironmentVariable("GITLAB_CONFIGURATION_URL");
                    var token = this.settings.GitlabToken ?? Environment.GetEnvironmentVariable("GITLAB_CONFIGURATION_TOKEN");
                    var projectId = this.settings.GitlabProjectId.HasValue ? this.settings.GitlabProjectId.ToString() : Environment.GetEnvironmentVariable("GITLAB_CONFIGURATION_PROJECTID");
                    if (!(string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(projectId)))
                    {
                        var gitlabVariables = GitlabConfigurationReader.ReadAsync(url, token, Convert.ToInt32(projectId), prefix)
                            .GetAwaiter()
                            .GetResult();

                        this.Data = GitlabConfigurationParser.Parse(gitlabVariables, prefix);
                    }
                }
            }
        }
    }
}
