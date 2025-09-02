// Copyright (c) Microsoft.All rights reserved.

namespace Microsoft.KernelMemory;

public sealed class KernelMemoryBuilderBuildOptions
{
    public static readonly KernelMemoryBuilderBuildOptions Default = new();

    public static readonly KernelMemoryBuilderBuildOptions WithVolatileAndPersistentData = new()
    {
        AllowMixingVolatileAndPersistentData = true
    };

    public bool AllowMixingVolatileAndPersistentData { get; set; } = false;
}
