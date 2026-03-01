// Copyright (c) Microsoft.All rights reserved.

using System;

namespace Microsoft.KernelMemory.AI.AzureOpenAI;

public class AzureOpenAIException : KernelMemoryException
{
    /// <inheritdoc />
    public AzureOpenAIException(bool? isTransient = null)
    {
        IsTransient = isTransient;
    }


    /// <inheritdoc />
    public AzureOpenAIException(string message, bool? isTransient = null) : base(message)
    {
        IsTransient = isTransient;
    }


    /// <inheritdoc />
    public AzureOpenAIException(string message, Exception? innerException, bool? isTransient = null) : base(message, innerException)
    {
        IsTransient = isTransient;
    }
}
