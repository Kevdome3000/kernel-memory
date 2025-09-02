// Copyright (c) Microsoft.All rights reserved.

using Microsoft.KernelMemory.FileSystem.DevTools;

namespace Microsoft.KernelMemory.DocumentStorage.DevTools;

public class SimpleFileStorageConfig
{
    public static SimpleFileStorageConfig Volatile => new() { StorageType = FileSystemTypes.Volatile };

    public static SimpleFileStorageConfig Persistent => new() { StorageType = FileSystemTypes.Disk };

    /// <summary>
    /// The type of storage to use. Defaults to volatile (in RAM).
    /// </summary>
    public FileSystemTypes StorageType { get; set; } = FileSystemTypes.Volatile;

    public string Directory { get; set; } = "tmp-memory-files";
}
