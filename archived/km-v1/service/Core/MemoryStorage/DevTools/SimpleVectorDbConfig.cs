// Copyright (c) Microsoft.All rights reserved.

using Microsoft.KernelMemory.FileSystem.DevTools;

namespace Microsoft.KernelMemory.MemoryStorage.DevTools;

public class SimpleVectorDbConfig
{
    public static SimpleVectorDbConfig Volatile => new() { StorageType = FileSystemTypes.Volatile };

    public static SimpleVectorDbConfig Persistent => new() { StorageType = FileSystemTypes.Disk };

    /// <summary>
    /// The type of storage to use. Defaults to volatile (in RAM).
    /// </summary>
    public FileSystemTypes StorageType { get; set; } = FileSystemTypes.Volatile;

    /// <summary>
    /// Directory of the text file storage.
    /// </summary>
    public string Directory { get; set; } = "tmp-memory-vectors";
}
