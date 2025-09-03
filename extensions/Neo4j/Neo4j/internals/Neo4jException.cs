// Copyright (c) Microsoft.All rights reserved.

// ReSharper disable once CheckNamespace
namespace Microsoft.KernelMemory;

// ReSharper disable once InconsistentNaming
public class Neo4jException : KernelMemoryException
{
    /// <inheritdoc />
    public Neo4jException() { }


    /// <inheritdoc />
    public Neo4jException(string message) : base(message) { }


    /// <inheritdoc />
    public Neo4jException(string message, Exception? innerException) : base(message, innerException) { }
}
