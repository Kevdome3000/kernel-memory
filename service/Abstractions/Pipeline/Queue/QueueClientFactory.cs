// Copyright (c) Microsoft.All rights reserved.

using System;

namespace Microsoft.KernelMemory.Pipeline.Queue;

public class QueueClientFactory
{
    private readonly Func<IQueue> _queueBuilder;


    public QueueClientFactory(Func<IQueue> queueBuilder)
    {
        _queueBuilder = queueBuilder;
    }


    /// <summary>
    /// Connect to a new queue
    /// </summary>
    /// <returns>Queue instance</returns>
    public IQueue Build()
    {
        return _queueBuilder.Invoke();
    }
}
