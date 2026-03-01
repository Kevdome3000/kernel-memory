// Copyright (c) Microsoft.All rights reserved.

using System;

namespace Microsoft.KernelMemory.DataFormats.AzureAIDocIntel;

public class AzureAIDocIntelException : KernelMemoryException
{
    /// <inheritdoc />
    public AzureAIDocIntelException(bool? isTransient = null)
    {
        IsTransient = isTransient;
    }


    /// <inheritdoc />
    public AzureAIDocIntelException(string message, bool? isTransient = null) : base(message)
    {
        IsTransient = isTransient;
    }


    /// <inheritdoc />
    public AzureAIDocIntelException(string message, Exception? innerException, bool? isTransient = null) : base(message, innerException)
    {
        IsTransient = isTransient;
    }
}
