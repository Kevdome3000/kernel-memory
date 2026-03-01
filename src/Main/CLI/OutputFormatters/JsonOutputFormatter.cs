// Copyright (c) Microsoft. All rights reserved.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KernelMemory.Main.CLI.OutputFormatters;

/// <summary>
/// Formats output as JSON for machine-readable consumption.
/// </summary>
public class JsonOutputFormatter : IOutputFormatter
{
    private readonly JsonSerializerOptions _jsonOptions;

    public string Verbosity { get; }


    public JsonOutputFormatter(string verbosity)
    {
        Verbosity = verbosity;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }


    public void Format(object data)
    {
        if (Verbosity.Equals("silent", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var json = JsonSerializer.Serialize(data, _jsonOptions);
        Console.WriteLine(json);
    }


    public void FormatError(string errorMessage)
    {
        var error = new { error = errorMessage };
        var json = JsonSerializer.Serialize(error, _jsonOptions);
        Console.Error.WriteLine(json);
    }


    public void FormatList<T>(
        IEnumerable<T> items,
        long totalCount,
        int skip,
        int take)
    {
        if (Verbosity.Equals("silent", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var result = new
        {
            items,
            pagination = new
            {
                totalCount,
                skip,
                take,
                returned = items.Count()
            }
        };

        var json = JsonSerializer.Serialize(result, _jsonOptions);
        Console.WriteLine(json);
    }
}
