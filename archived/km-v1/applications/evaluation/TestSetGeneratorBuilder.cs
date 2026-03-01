// Copyright (c) Microsoft.All rights reserved.

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.MemoryStorage;
using Microsoft.SemanticKernel;

namespace Microsoft.KernelMemory.Evaluation;

public sealed class TestSetGeneratorBuilder
{
    // Services required to build the testset generator class
    private readonly IServiceCollection _serviceCollection;


    public TestSetGeneratorBuilder(IServiceCollection? hostServiceCollection = null)
    {
        _serviceCollection = new ServiceCollection();

        CopyServiceCollection(hostServiceCollection, _serviceCollection);
    }


    public TestSetGeneratorBuilder AddIngestionMemoryDb(IMemoryDb service)
    {
        _serviceCollection.AddSingleton<IMemoryDb>(service);

        return this;
    }


    public TestSetGeneratorBuilder AddEvaluatorKernel(Kernel kernel)
    {
        _serviceCollection.AddKeyedSingleton<Kernel>("evaluation", kernel);

        return this;
    }


    public TestSetGeneratorBuilder AddTranslatorKernel(Kernel kernel)
    {
        _serviceCollection.AddKeyedSingleton<Kernel>("translation", kernel);

        return this;
    }


    public TestSetGenerator Build()
    {
        if (!_serviceCollection.HasService<IMemoryDb>())
        {
            throw new InvalidOperationException("MemoryDb service is required to build the TestSetGenerator");
        }

        _serviceCollection.AddScoped<TestSetGenerator>(sp =>
        {
            return new TestSetGenerator(
                sp.GetRequiredKeyedService<Kernel>("evaluation"),
                sp.GetKeyedService<Kernel>("translation"),
                sp.GetRequiredService<IMemoryDb>());
        });

        return _serviceCollection.BuildServiceProvider()
            .GetRequiredService<TestSetGenerator>();
    }


    private static void CopyServiceCollection(
        IServiceCollection? source,
        IServiceCollection destination)
    {
        if (source == null) { return; }

        foreach (ServiceDescriptor d in source)
        {
            destination.Add(d);
        }
    }
}
