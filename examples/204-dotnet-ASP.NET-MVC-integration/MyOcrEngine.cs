// Copyright (c) Microsoft. All rights reserved.

using Microsoft.KernelMemory.DataFormats;

namespace _204_dotnet_ASP.NET_MVC_integration;

public class MyOcrEngine : IOcrEngine
{
    public Task<string> ExtractTextFromImageAsync(Stream imageContent, CancellationToken cancellationToken = default)
    {
        return Task.FromResult("test");
    }
}
