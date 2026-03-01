// Copyright (c) Microsoft. All rights reserved.

using Microsoft.AspNetCore.Mvc;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.DataFormats;

namespace _204_dotnet_ASP.NET_MVC_integration.Controllers;

[ApiController]
[Route("[controller]")]
public class MemoryController : Controller
{
    private readonly IKernelMemory _memory;
    private readonly IOcrEngine _ocr;


    public MemoryController(
        IKernelMemory memory,
        IOcrEngine ocr)
    {
        _memory = memory;
        _ocr = ocr;
    }


    // GET http://localhost:5000/Memory
    [HttpGet]
    public async Task<string> GetAsync()
    {
        // Return data from MyOcrEngine
        var ocrResult = await _ocr.ExtractTextFromImageAsync(new MemoryStream());
        return ocrResult;
    }
}
