// Copyright (c) Microsoft.All rights reserved.

using Xunit.Abstractions;

namespace Microsoft.KM.TestHelpers;

public abstract class BaseUnitTestCase : IDisposable
{
    private readonly RedirectConsole _output;


    protected BaseUnitTestCase(ITestOutputHelper output)
    {
        _output = new RedirectConsole(output);
        Console.SetOut(_output);
    }


    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }


    protected void Log(string text)
    {
        _output.WriteLine(text);
    }


    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _output.Dispose();
        }
    }
}
