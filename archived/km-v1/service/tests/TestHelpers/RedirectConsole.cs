// Copyright (c) Microsoft.All rights reserved.

using System.Globalization;
using System.Text;
using Xunit.Abstractions;

namespace Microsoft.KM.TestHelpers;

internal sealed class RedirectConsole : TextWriter
{
    private readonly ITestOutputHelper _output;

    public override IFormatProvider FormatProvider => CultureInfo.CurrentCulture;

    public override Encoding Encoding { get; } = Encoding.Default;


    public RedirectConsole(ITestOutputHelper output)
    {
        _output = output;
    }


    public override void Write(string? value)
    {
        Text(value);
    }


    public override void WriteLine(string? value)
    {
        Line(value);
    }


    public override void Write(char value)
    {
        Text($"{value}");
    }


    public override void WriteLine(char value)
    {
        Line($"{value}");
    }


    public override void Write(char[]? buffer)
    {
        if (buffer == null || buffer.Length == 0) { return; }

        var s = new StringBuilder();

        foreach (var c in buffer) { s.Append(c); }

        Text(s.ToString());
    }


    public override void WriteLine(char[]? buffer)
    {
        if (buffer == null)
        {
            Line();
            return;
        }

        var s = new StringBuilder();

        foreach (var c in buffer) { s.Append(c); }

        Line(s.ToString());
    }


    public override void Write(char[] buffer, int index, int count)
    {
        if (buffer.Length == 0 || count <= 0 || index < 0 || buffer.Length - index < count)
        {
            return;
        }

        var s = new StringBuilder();

        for (int i = 0; i < count; i++)
        {
            s.Append(buffer[index + i]);
        }

        Text(s.ToString());
    }


    public override void WriteLine(char[] buffer, int index, int count)
    {
        if (count <= 0 || index < 0 || buffer.Length - index < count)
        {
            Line();
            return;
        }

        var s = new StringBuilder();

        for (int i = 0; i < count; i++)
        {
            s.Append(buffer[index + i]);
        }

        Line(s.ToString());
    }


    public override void Write(ReadOnlySpan<char> buffer)
    {
        if (buffer == null || buffer.Length == 0) { return; }

        var s = new StringBuilder();

        foreach (var c in buffer) { s.Append(c); }

        Text(s.ToString());
    }


    public override void WriteLine(ReadOnlySpan<char> buffer)
    {
        if (buffer == null)
        {
            Line();
            return;
        }

        var s = new StringBuilder();

        foreach (var c in buffer) { s.Append(c); }

        Line(s.ToString());
    }


    public override void Write(StringBuilder? buffer)
    {
        if (buffer == null || buffer.Length == 0) { return; }

        Text(buffer.ToString());
    }


    public override void WriteLine(StringBuilder? buffer)
    {
        if (buffer == null)
        {
            Line();
            return;
        }

        Line(buffer.ToString());
    }


    public override void Write(bool value)
    {
        Text(value
            ? "True"
            : "False");
    }


    public override void WriteLine(bool value)
    {
        Line(value
            ? "True"
            : "False");
    }


    public override void Write(int value)
    {
        Text(value.ToString(FormatProvider));
    }


    public override void WriteLine(int value)
    {
        Line(value.ToString(FormatProvider));
    }


    public override void Write(uint value)
    {
        Text(value.ToString(FormatProvider));
    }


    public override void WriteLine(uint value)
    {
        Line(value.ToString(FormatProvider));
    }


    public override void Write(long value)
    {
        Text(value.ToString(FormatProvider));
    }


    public override void WriteLine(long value)
    {
        Line(value.ToString(FormatProvider));
    }


    public override void Write(ulong value)
    {
        Text(value.ToString(FormatProvider));
    }


    public override void WriteLine(ulong value)
    {
        Line(value.ToString(FormatProvider));
    }


    public override void Write(float value)
    {
        Text(value.ToString(FormatProvider));
    }


    public override void WriteLine(float value)
    {
        Line(value.ToString(FormatProvider));
    }


    public override void Write(double value)
    {
        Text(value.ToString(FormatProvider));
    }


    public override void WriteLine(double value)
    {
        Line(value.ToString(FormatProvider));
    }


    public override void Write(decimal value)
    {
        Text(value.ToString(FormatProvider));
    }


    public override void WriteLine(decimal value)
    {
        Line(value.ToString(FormatProvider));
    }


    public override void Write(object? value)
    {
        if (value != null)
        {
            if (value is IFormattable f)
            {
                Text(f.ToString(null, FormatProvider));
            }
            else
            {
                Text(value.ToString());
            }
        }
    }


    public override void WriteLine(object? value)
    {
        if (value != null)
        {
            if (value is IFormattable f)
            {
                Line(f.ToString(null, FormatProvider));
            }
            else
            {
                Line(value.ToString());
            }
        }
        else
        {
            Line();
        }
    }


    // public override void Write([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0)
    // {
    //     this.Text(string.Format(this.FormatProvider, format, arg0));
    // }
    //
    // public override void WriteLine([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0)
    // {
    //     this.Line(string.Format(this.FormatProvider, format, arg0));
    // }
    //
    // public override void Write([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0, object? arg1)
    // {
    //     this.Text(string.Format(this.FormatProvider, format, arg0, arg1));
    // }
    //
    // public override void WriteLine([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0, object? arg1)
    // {
    //     this.Line(string.Format(this.FormatProvider, format, arg0, arg1));
    // }
    //
    // public override void Write([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0, object? arg1, object? arg2)
    // {
    //     this.Text(string.Format(this.FormatProvider, format, arg0, arg1, arg2));
    // }
    //
    // public override void WriteLine([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0, object? arg1, object? arg2)
    // {
    //     this.Line(string.Format(this.FormatProvider, format, arg0, arg1, arg2));
    // }
    //
    // public override void Write([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, params object?[] arg)
    // {
    //     this.Text(string.Format(this.FormatProvider, format, arg));
    // }
    //
    // public override void WriteLine([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, params object?[] arg)
    // {
    //     this.Line(string.Format(this.FormatProvider, format, arg));
    // }


    private void Text(string? s)
    {
        if (string.IsNullOrEmpty(s)) { return; }

        try
        {
            _output.WriteLine(s);
        }
        catch (InvalidOperationException e) when (e.Message.Contains("no currently active test", StringComparison.OrdinalIgnoreCase))
        {
            // NOOP: Xunit thread out of scope
        }
    }


    private void Line(string? s = null)
    {
        try
        {
            _output.WriteLine(s ?? string.Empty);
        }
        catch (InvalidOperationException e) when (e.Message.Contains("no currently active test", StringComparison.OrdinalIgnoreCase))
        {
            // NOOP: Xunit thread out of scope
        }
    }
}
