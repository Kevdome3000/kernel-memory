// Copyright (c) Microsoft.All rights reserved.

using Microsoft.KernelMemory.InteractiveSetup.UI;
using Newtonsoft.Json.Linq;

namespace Microsoft.KernelMemory.InteractiveSetup.Service;

internal static class Logger
{
    public static void Setup()
    {
        string logLevel = "Debug";
        SetupUI.AskQuestionWithOptions(new QuestionWithOptions
        {
            Title = "Log level?",
            Options =
            [
                new Answer("Trace", false, () => { logLevel = "Trace"; }),
                new Answer("Debug", false, () => { logLevel = "Debug"; }),
                new Answer("Information", false, () => { logLevel = "Information"; }),
                new Answer("Warning", true, () => { logLevel = "Warning"; }),
                new Answer("Error", false, () => { logLevel = "Error"; }),
                new Answer("Critical", false, () => { logLevel = "Critical"; }),
                new Answer("-exit-", false, SetupUI.Exit)
            ]
        });

        AppSettings.GlobalChange(data =>
        {
            if (data["Logging"] == null) { data["Logging"] = new JObject(); }

            if (data["Logging"]!["LogLevel"] == null)
            {
                data["Logging"]!["LogLevel"] = new JObject { ["Microsoft.AspNetCore"] = "Warning" };
            }

            data["Logging"]!["LogLevel"]!["Default"] = logLevel;
        });
    }
}
