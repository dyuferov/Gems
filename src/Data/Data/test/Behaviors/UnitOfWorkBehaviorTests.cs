﻿// Licensed to the Hoff Tech under one or more agreements.
// The Hoff Tech licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Gems.Context;
using Gems.Data.Behaviors;
using Gems.Data.Tests.Behaviors.Fixtures;
using Gems.Data.Tests.SeedWork;
using Gems.Data.UnitOfWork;
using Gems.Metrics;
using Gems.Metrics.Data;
using Gems.Mvc;

using MediatR;
using MediatR.Registration;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Moq;

using NUnit.Framework;

namespace Gems.Data.Tests.Behaviors;

[Parallelizable(ParallelScope.All)]
public class UnitOfWorkBehaviorTests
{
    [Test]
    public async Task SimpleCommand_ShouldCreatingOneUnitOfWork()
    {
        // Arrange
        var logs = new ConcurrentBag<string>();
        var serviceProvider = BuildServiceProvider(logs);
        var mediator = serviceProvider.GetRequiredService<IMediator>();

        // Act
        await mediator.Send(new SimpleCommand(), CancellationToken.None);
        var distinctLogs = logs.Distinct().ToList();
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWorkMap:")), 1);
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWork:")), 1);
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWorkUsing:")), 2);
        var unitOfWork = distinctLogs.First(x => x.StartsWith("UnitOfWork:")).Split(",")[0].Split(":")[1].Trim();
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"UnitOfWorkUsing: {unitOfWork}")), 2);

        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("Context:")), 1);
        var context = distinctLogs.First(x => x.StartsWith("Context:")).Split(":")[1].Trim();
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"ContextCreated: {context}")), 1);
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"ContextDisposed: {context}")), 1);
    }

    [Test]
    public async Task SimpleWithInnersCommand_ShouldCreatingOneUnitOfWork()
    {
        // Arrange
        var logs = new ConcurrentBag<string>();
        var serviceProvider = BuildServiceProvider(logs);
        var mediator = serviceProvider.GetRequiredService<IMediator>();

        // Act
        await mediator.Send(new SimpleWithInnersCommand(), CancellationToken.None);
        var distinctLogs = logs.Distinct().ToList();
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWorkMap:")), 1);
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWork:")), 1);
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWorkUsing:")), 4);
        var unitOfWork = distinctLogs.First(x => x.StartsWith("UnitOfWork:")).Split(",")[0].Split(":")[1].Trim();
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"UnitOfWorkUsing: {unitOfWork}")), 4);

        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("Context:")), 1);
        var context = distinctLogs.First(x => x.StartsWith("Context:")).Split(":")[1].Trim();
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"ContextCreated: {context}")), 1);
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"ContextDisposed: {context}")), 1);
    }

    [Test]
    public async Task SimpleWithInnersInsideAsyncAwaiterCommand_ShouldCreatingOneUnitOfWork()
    {
        // Arrange
        var logs = new ConcurrentBag<string>();
        var serviceProvider = BuildServiceProvider(logs);
        var mediator = serviceProvider.GetRequiredService<IMediator>();

        // Act
        await mediator.Send(new SimpleWithInnersInsideAsyncAwaiterCommand(), CancellationToken.None);
        var distinctLogs = logs.Distinct().ToList();
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWorkMap:")), 1);
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWork:")), 1);
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWorkUsing:")), 4);
        var unitOfWork = distinctLogs.First(x => x.StartsWith("UnitOfWork:")).Split(",")[0].Split(":")[1].Trim();
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"UnitOfWorkUsing: {unitOfWork}")), 4);

        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("Context:")), 1);
        var context = distinctLogs.First(x => x.StartsWith("Context:")).Split(":")[1].Trim();
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"ContextCreated: {context}")), 1);
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"ContextDisposed: {context}")), 1);
    }

    [Test]
    public async Task SimpleWithInnersAndDifferentTokensCommand_ShouldCreatingThreeUnitOfWork()
    {
        // Arrange
        var logs = new ConcurrentBag<string>();
        var serviceProvider = BuildServiceProvider(logs);
        var mediator = serviceProvider.GetRequiredService<IMediator>();

        // Act
        await mediator.Send(new SimpleWithInnersAndDifferentTokensCommand(), CancellationToken.None);
        var distinctLogs = logs.Distinct().ToList();
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWorkMap:")), 1);
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWork:")), 3);
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWorkUsing:")), 6);
        foreach (var unitOfWork in distinctLogs.Where(x => x.StartsWith("UnitOfWork:")).Select(x => x.Split(",")[0].Split(":")[1].Trim()))
        {
            Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"UnitOfWorkUsing: {unitOfWork}")), 2);
        }

        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("Context:")), 1);
        var context = distinctLogs.First(x => x.StartsWith("Context:")).Split(":")[1].Trim();
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"ContextCreated: {context}")), 1);
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"ContextDisposed: {context}")), 1);
    }

    [Test]
    public async Task SimpleWithInnersInsideAsyncAwaiterAndDifferentTokensCommand_ShouldCreatingThreeUnitOfWork()
    {
        // Arrange
        var logs = new ConcurrentBag<string>();
        var serviceProvider = BuildServiceProvider(logs);
        var mediator = serviceProvider.GetRequiredService<IMediator>();

        // Act
        await mediator.Send(new SimpleWithInnersInsideAsyncAwaiterAndDifferentTokensCommand(), CancellationToken.None);
        var distinctLogs = logs.Distinct().ToList();
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWorkMap:")), 1);
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWork:")), 3);
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWorkUsing:")), 6);
        foreach (var unitOfWork in distinctLogs.Where(x => x.StartsWith("UnitOfWork:")).Select(x => x.Split(",")[0].Split(":")[1].Trim()))
        {
            Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"UnitOfWorkUsing: {unitOfWork}")), 2);
        }

        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("Context:")), 1);
        var context = distinctLogs.First(x => x.StartsWith("Context:")).Split(":")[1].Trim();
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"ContextCreated: {context}")), 1);
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"ContextDisposed: {context}")), 1);
    }

    [Test]
    public async Task SimpleWithInnerUnitOfWorksCommand_ShouldCreatingThreeUnitOfWorks()
    {
        // Arrange
        var logs = new ConcurrentBag<string>();
        var serviceProvider = BuildServiceProvider(logs);
        var mediator = serviceProvider.GetRequiredService<IMediator>();

        // Act
        await mediator.Send(new SimpleWithInnerUnitOfWorksCommand(), CancellationToken.None);
        var distinctLogs = logs.Distinct().ToList();
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWorkMap:")), 3);
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWork:")), 3);
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWorkUsing:")), 12);
        foreach (var unitOfWork in distinctLogs.Where(x => x.StartsWith("UnitOfWork:")).Select(x => x.Split(",")[0].Split(":")[1].Trim()))
        {
            var expected = distinctLogs.Count(x =>
                x.StartsWith($"UnitOfWorkUsing: {unitOfWork}") &&
                x.Contains("SimpleWithUnitOfWorkCommand")) == 0
                ? 2
                : 5;
            Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"UnitOfWorkUsing: {unitOfWork}")), expected);
        }

        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("Context:")), 3);
        foreach (var context in distinctLogs.Where(x => x.StartsWith("Context:")).Select(x => x.Split(":")[1].Trim()))
        {
            Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"ContextCreated: {context}")), 1);
            Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"ContextDisposed: {context}")), 1);
        }
    }

    [Test]
    public async Task SimpleWithInnerUnitOfWorksInsideAsyncAwaiterCommand_ShouldCreatingThreeUnitOfWorks()
    {
        // Arrange
        var logs = new ConcurrentBag<string>();
        var serviceProvider = BuildServiceProvider(logs);
        var mediator = serviceProvider.GetRequiredService<IMediator>();

        // Act
        await mediator.Send(new SimpleWithInnerUnitOfWorksInsideAsyncAwaiterCommand(), CancellationToken.None);
        var distinctLogs = logs.Distinct().ToList();
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWorkMap:")), 3);
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWork:")), 3);
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWorkUsing:")), 12);
        foreach (var unitOfWork in distinctLogs.Where(x => x.StartsWith("UnitOfWork:")).Select(x => x.Split(",")[0].Split(":")[1].Trim()))
        {
            var expected = distinctLogs.Count(x =>
                x.StartsWith($"UnitOfWorkUsing: {unitOfWork}") &&
                x.Contains("SimpleWithUnitOfWorkCommand")) == 0
                ? 2
                : 5;
            Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"UnitOfWorkUsing: {unitOfWork}")), expected);
        }

        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("Context:")), 3);
        foreach (var context in distinctLogs.Where(x => x.StartsWith("Context:")).Select(x => x.Split(":")[1].Trim()))
        {
            Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"ContextCreated: {context}")), 1);
            Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"ContextDisposed: {context}")), 1);
        }
    }

    [Test]
    public async Task SimpleWithUnitOfWorkCommand_ShouldCreatingOneUnitOfWork()
    {
        // Arrange
        var logs = new ConcurrentBag<string>();
        var serviceProvider = BuildServiceProvider(logs);
        var mediator = serviceProvider.GetRequiredService<IMediator>();

        // Act
        await mediator.Send(new SimpleWithUnitOfWorkCommand(), CancellationToken.None);
        var distinctLogs = logs.Distinct().ToList();
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWorkMap:")), 1);
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWork:")), 1);
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWorkUsing:")), 5);
        var unitOfWork = distinctLogs.First(x => x.StartsWith("UnitOfWork:")).Split(",")[0].Split(":")[1].Trim();
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"UnitOfWorkUsing: {unitOfWork}")), 5);

        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("Context:")), 1);
        var context = distinctLogs.First(x => x.StartsWith("Context:")).Split(":")[1].Trim();
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"ContextCreated: {context}")), 1);
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"ContextDisposed: {context}")), 1);
    }

    [Test]
    public async Task SimpleWithUnitOfWorkAndInnersCommand_ShouldCreatingOneUnitOfWork()
    {
        // Arrange
        var logs = new ConcurrentBag<string>();
        var serviceProvider = BuildServiceProvider(logs);
        var mediator = serviceProvider.GetRequiredService<IMediator>();

        // Act
        await mediator.Send(new SimpleWithUnitOfWorkAndInnersCommand(), CancellationToken.None);
        var distinctLogs = logs.Distinct().ToList();
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWorkMap:")), 1);
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWork:")), 1);
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWorkUsing:")), 7);
        var unitOfWork = distinctLogs.First(x => x.StartsWith("UnitOfWork:")).Split(",")[0].Split(":")[1].Trim();
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"UnitOfWorkUsing: {unitOfWork}")), 7);

        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("Context:")), 1);
        var context = distinctLogs.First(x => x.StartsWith("Context:")).Split(":")[1].Trim();
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"ContextCreated: {context}")), 1);
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"ContextDisposed: {context}")), 1);
    }

    [Test]
    public async Task SimpleWithUnitOfWorkAndInnersInsideAsyncAwaiterCommand_ShouldCreatingOneUnitOfWork()
    {
        // Arrange
        var logs = new ConcurrentBag<string>();
        var serviceProvider = BuildServiceProvider(logs);
        var mediator = serviceProvider.GetRequiredService<IMediator>();

        // Act
        await mediator.Send(new SimpleWithUnitOfWorkAndInnersInsideAsyncAwaiterCommand(), CancellationToken.None);
        var distinctLogs = logs.Distinct().ToList();
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWorkMap:")), 1);
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWork:")), 1);
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWorkUsing:")), 7);
        var unitOfWork = distinctLogs.First(x => x.StartsWith("UnitOfWork:")).Split(",")[0].Split(":")[1].Trim();
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"UnitOfWorkUsing: {unitOfWork}")), 7);

        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("Context:")), 1);
        var context = distinctLogs.First(x => x.StartsWith("Context:")).Split(":")[1].Trim();
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"ContextCreated: {context}")), 1);
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"ContextDisposed: {context}")), 1);
    }

    [Test]
    public async Task SimpleWithUnitOfWorkAndInnerUnitOfWorksCommand_ShouldCreatingThreeUnitOfWorks()
    {
        // Arrange
        var logs = new ConcurrentBag<string>();
        var serviceProvider = BuildServiceProvider(logs);
        var mediator = serviceProvider.GetRequiredService<IMediator>();

        // Act
        await mediator.Send(new SimpleWithUnitOfWorkAndInnerUnitOfWorksCommand(), CancellationToken.None);
        var distinctLogs = logs.Distinct().ToList();
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWorkMap:")), 3);
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWork:")), 3);
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWorkUsing:")), 15);
        foreach (var unitOfWork in distinctLogs.Where(x => x.StartsWith("UnitOfWork:")).Select(x => x.Split(",")[0].Split(":")[1].Trim()))
        {
            Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"UnitOfWorkUsing: {unitOfWork}")), 5);
        }

        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("Context:")), 3);
        foreach (var context in distinctLogs.Where(x => x.StartsWith("Context:")).Select(x => x.Split(":")[1].Trim()))
        {
            Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"ContextCreated: {context}")), 1);
            Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"ContextDisposed: {context}")), 1);
        }
    }

    [Test]
    public async Task SimpleWithUnitOfWorkAndInnerUnitOfWorksInsideAsyncAwaiterCommand_ShouldCreatingThreeUnitOfWorks()
    {
        // Arrange
        var logs = new ConcurrentBag<string>();
        var serviceProvider = BuildServiceProvider(logs);
        var mediator = serviceProvider.GetRequiredService<IMediator>();

        // Act
        await mediator.Send(new SimpleWithUnitOfWorkAndInnerUnitOfWorksInsideAsyncAwaiterCommand(), CancellationToken.None);
        var distinctLogs = logs.Distinct().ToList();
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWorkMap:")), 3);
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWork:")), 3);
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWorkUsing:")), 15);
        foreach (var unitOfWork in distinctLogs.Where(x => x.StartsWith("UnitOfWork:")).Select(x => x.Split(",")[0].Split(":")[1].Trim()))
        {
            Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"UnitOfWorkUsing: {unitOfWork}")), 5);
        }

        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("Context:")), 3);
        foreach (var context in distinctLogs.Where(x => x.StartsWith("Context:")).Select(x => x.Split(":")[1].Trim()))
        {
            Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"ContextCreated: {context}")), 1);
            Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"ContextDisposed: {context}")), 1);
        }
    }

    [Test]
    public async Task SimpleWithUnitOfWorkAndInnerUnitOfWorksAndDifferentTokensCommand_ShouldCreatingThreeUnitOfWorks()
    {
        // Arrange
        var logs = new ConcurrentBag<string>();
        var serviceProvider = BuildServiceProvider(logs);
        var mediator = serviceProvider.GetRequiredService<IMediator>();

        // Act
        await mediator.Send(new SimpleWithUnitOfWorkAndInnerUnitOfWorksAndDifferentTokensCommand(), CancellationToken.None);
        var distinctLogs = logs.Distinct().ToList();
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWorkMap:")), 3);
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWork:")), 3);
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWorkUsing:")), 15);
        foreach (var unitOfWork in distinctLogs.Where(x => x.StartsWith("UnitOfWork:")).Select(x => x.Split(",")[0].Split(":")[1].Trim()))
        {
            Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"UnitOfWorkUsing: {unitOfWork}")), 5);
        }

        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("Context:")), 3);
        foreach (var context in distinctLogs.Where(x => x.StartsWith("Context:")).Select(x => x.Split(":")[1].Trim()))
        {
            Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"ContextCreated: {context}")), 1);
            Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"ContextDisposed: {context}")), 1);
        }
    }

    [Test]
    public async Task SimpleWithUnitOfWorkAndInnerUnitOfWorksInsideAsyncAwaiterAndDifferentTokensCommand_ShouldCreatingThreeUnitOfWorks()
    {
        // Arrange
        var logs = new ConcurrentBag<string>();
        var serviceProvider = BuildServiceProvider(logs);
        var mediator = serviceProvider.GetRequiredService<IMediator>();

        // Act
        await mediator.Send(new SimpleWithUnitOfWorkAndInnerUnitOfWorksInsideAsyncAwaiterAndDifferentTokensCommand(), CancellationToken.None);
        var distinctLogs = logs.Distinct().ToList();
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWorkMap:")), 3);
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWork:")), 3);
        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("UnitOfWorkUsing:")), 15);
        foreach (var unitOfWork in distinctLogs.Where(x => x.StartsWith("UnitOfWork:")).Select(x => x.Split(",")[0].Split(":")[1].Trim()))
        {
            Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"UnitOfWorkUsing: {unitOfWork}")), 5);
        }

        Assert.AreEqual(distinctLogs.Count(x => x.StartsWith("Context:")), 3);
        foreach (var context in distinctLogs.Where(x => x.StartsWith("Context:")).Select(x => x.Split(":")[1].Trim()))
        {
            Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"ContextCreated: {context}")), 1);
            Assert.AreEqual(distinctLogs.Count(x => x.StartsWith($"ContextDisposed: {context}")), 1);
        }
    }

    private static IServiceProvider BuildServiceProvider(ConcurrentBag<string> logs)
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration();
        services.AddSingleton(configuration);

        var serviceConfig = new MediatRServiceConfiguration();
        ServiceRegistrar.AddRequiredServices(services, serviceConfig);
        services.AddPipeline(typeof(UnitOfWorkBehavior<,>));

        AddUnitOfWork(services);

        services.AddSingleton<IRequestHandler<SimpleCommand>, SimpleCommandHandler>();
        services.AddSingleton<IRequestHandler<SimpleWithInnersCommand>, SimpleWithInnersCommandHandler>();
        services.AddSingleton<IRequestHandler<SimpleWithInnersInsideAsyncAwaiterCommand>, SimpleWithInnersInsideAsyncAwaiterCommandHandler>();
        services.AddSingleton<IRequestHandler<SimpleWithInnersAndDifferentTokensCommand>, SimpleWithInnersAndDifferentTokensCommandHandler>();
        services.AddSingleton<IRequestHandler<SimpleWithInnersInsideAsyncAwaiterAndDifferentTokensCommand>, SimpleWithInnersInsideAsyncAwaiterAndDifferentTokensCommandHandler>();
        services.AddSingleton<IRequestHandler<SimpleWithInnerUnitOfWorksCommand>, SimpleWithInnerUnitOfWorksCommandHandler>();
        services.AddSingleton<IRequestHandler<SimpleWithInnerUnitOfWorksInsideAsyncAwaiterCommand>, SimpleWithInnerUnitOfWorksInsideAsyncAwaiterCommandHandler>();
        services.AddSingleton<IRequestHandler<SimpleWithUnitOfWorkCommand>, SimpleWithUnitOfWorkCommandHandler>();
        services.AddSingleton<IRequestHandler<SimpleWithUnitOfWorkAndInnersCommand>, SimpleWithUnitOfWorkAndInnersCommandHandler>();
        services.AddSingleton<IRequestHandler<SimpleWithUnitOfWorkAndInnersInsideAsyncAwaiterCommand>, SimpleWithUnitOfWorkAndInnersInsideAsyncAwaiterCommandHandler>();
        services.AddSingleton<IRequestHandler<SimpleWithUnitOfWorkAndInnerUnitOfWorksCommand>, SimpleWithUnitOfWorkAndInnerUnitOfWorksCommandHandler>();
        services.AddSingleton<IRequestHandler<SimpleWithUnitOfWorkAndInnerUnitOfWorksAndDifferentTokensCommand>, SimpleWithUnitOfWorkAndInnerUnitOfWorksAndDifferentTokensCommandHandler>();
        services.AddSingleton<IRequestHandler<SimpleWithUnitOfWorkAndInnerUnitOfWorksInsideAsyncAwaiterCommand>, SimpleWithUnitOfWorkAndInnerUnitOfWorksInsideAsyncAwaiterCommandHandler>();
        services.AddSingleton<IRequestHandler<SimpleWithUnitOfWorkAndInnerUnitOfWorksInsideAsyncAwaiterAndDifferentTokensCommand>, SimpleWithUnitOfWorkAndInnerUnitOfWorksInsideAsyncAwaiterAndDifferentTokensCommandHandler>();
        services.AddLogging();

        services.AddSingleton<IMetricsService>(Mock.Of<IMetricsService>());

        var serviceProvider = services
            .AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.AddProvider(new DebugLoggerProvider(logs));
                builder.SetMinimumLevel(LogLevel.Trace);
            })
            .BuildServiceProvider();

        return serviceProvider;
    }

    private static void AddUnitOfWork(IServiceCollection services)
    {
        var options = new UnitOfWorkOptions();
        options.Key = "default";
        options.Factory = (_, needTransaction, _, logger, cancellationToken) => new Fixtures.UnitOfWork(needTransaction, logger, cancellationToken);
        services.AddSingleton(options);

        services.AddSingleton<IUnitOfWorkProvider, UnitOfWorkProvider>();
        services.AddSingleton<ITimeMetricProviderFactory, TimeMetricProviderFactory>();
        services.AddContext<UnitOfWorkContextFactory>();
    }

    private static IConfiguration BuildConfiguration()
    {
        var configurationBuilder = new ConfigurationBuilder();
        configurationBuilder.AddInMemoryCollection(new Dictionary<string, string>
        {
            ["UnitOfWorkProviderOptions:UseContext"] = "true"
        });
        return configurationBuilder.Build();
    }
}
