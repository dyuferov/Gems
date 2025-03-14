﻿// Licensed to the Hoff Tech under one or more agreements.
// The Hoff Tech licenses this file to you under the MIT license.

using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Gems.Settings.Gitlab
{
    public class GitlabConfigurationUpdater : BackgroundService
    {
        private readonly GitlabConfigurationUpdaterSettings settings;
        private readonly IConfiguration configuration;
        private readonly IServiceProvider serviceProvider;

        public GitlabConfigurationUpdater(
            GitlabConfigurationUpdaterSettings settings,
            IConfiguration configuration,
            IServiceProvider serviceProvider)
        {
            this.configuration = configuration;
            this.settings = settings;
            this.serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
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
                                var gitlabVariables = await GitlabConfigurationReader.ReadAsync(url, token, Convert.ToInt32(projectId), prefix);
                                var variables = GitlabConfigurationParser.Parse(gitlabVariables, prefix);
                                var configurationChanged = false;
                                foreach (var variable in variables)
                                {
                                    var currentValue = this.configuration[variable.Key];
                                    if (currentValue != variable.Value)
                                    {
                                        try
                                        {
                                            this.settings.ValueChanged?.Invoke(this.serviceProvider, variable.Key, variable.Value, currentValue);
                                        }
                                        catch (Exception e)
                                        {
                                            this.settings.HandleError?.Invoke(this.serviceProvider, e);
                                        }

                                        this.configuration[variable.Key] = variable.Value;
                                        configurationChanged = true;
                                    }
                                }

                                if (configurationChanged)
                                {
                                    (this.configuration as IConfigurationRoot).Reload();
                                }

                                this.settings.LastUpdate = DateTime.Now;
                                this.settings.LastUpdateSucceeded = true;
                            }
                        }
                    }

                    await Task.Delay(this.settings.UpdateInterval, ct);
                }
                catch (Exception e)
                {
                    this.settings.LastUpdateSucceeded = false;
                    this.settings.HandleError?.Invoke(this.serviceProvider, e);
                }
            }
        }
    }
}
