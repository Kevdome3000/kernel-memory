// Copyright (c) Microsoft.All rights reserved.

using System;

namespace Microsoft.KernelMemory.AI.OpenAI;

public class OpenAIException : KernelMemoryException
{
    /// <inheritdoc />
    public OpenAIException(bool? isTransient = null)
    {
        IsTransient = isTransient;
    }


    /// <inheritdoc />
    public OpenAIException(string message, bool? isTransient = null) : base(message)
    {
        IsTransient = isTransient;
    }


    /// <inheritdoc />
    public OpenAIException(string message, Exception? innerException, bool? isTransient = null) : base(message, innerException)
    {
        IsTransient = isTransient;
    }
}
