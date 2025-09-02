// Copyright (c) Microsoft.All rights reserved.

using System;

namespace Microsoft.KernelMemory.Pipeline;

public class OrchestrationException : KernelMemoryException
{
    /// <inheritdoc />
    public OrchestrationException(bool? isTransient = null)
    {
        IsTransient = isTransient;
    }


    /// <inheritdoc />
    public OrchestrationException(string message, bool? isTransient = null) : base(message)
    {
        IsTransient = isTransient;
    }


    /// <inheritdoc />
    public OrchestrationException(string message, Exception? innerException, bool? isTransient = null) : base(message, innerException)
    {
        IsTransient = isTransient;
    }
}
