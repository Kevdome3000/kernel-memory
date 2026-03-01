// Copyright (c) Microsoft. All rights reserved.
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace KernelMemory.Core.Storage.Entities;

/// <summary>
/// Entity representing an operation in the Operations table.
/// Used for queue-based processing with distributed locking.
/// </summary>
public class OperationRecord
{
    public string Id { get; set; } = string.Empty;
    public bool Complete { get; set; }
    public bool Cancelled { get; set; }
    public string ContentId { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public string LastFailureReason { get; set; } = string.Empty;

    /// <summary>
    /// When last attempt was made (nullable). Used for distributed locking.
    /// If NOT NULL and Complete=false: operation is locked (executing or crashed).
    /// </summary>
    public DateTimeOffset? LastAttemptTimestamp { get; set; }

    // JSON-backed array fields
    public string PlannedStepsJson { get; set; } = "[]";

    /// <summary>
    /// Gets or sets the planned steps array. Not mapped to database - uses PlannedStepsJson for persistence.
    /// </summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays")]
    public string[] PlannedSteps
    {
        get => string.IsNullOrWhiteSpace(PlannedStepsJson)
            ? []
            : JsonSerializer.Deserialize<string[]>(PlannedStepsJson) ?? [];
        set => PlannedStepsJson = JsonSerializer.Serialize(value);
    }

    public string CompletedStepsJson { get; set; } = "[]";

    /// <summary>
    /// Gets or sets the completed steps array. Not mapped to database - uses CompletedStepsJson for persistence.
    /// </summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays")]
    public string[] CompletedSteps
    {
        get => string.IsNullOrWhiteSpace(CompletedStepsJson)
            ? []
            : JsonSerializer.Deserialize<string[]>(CompletedStepsJson) ?? [];
        set => CompletedStepsJson = JsonSerializer.Serialize(value);
    }

    public string RemainingStepsJson { get; set; } = "[]";

    /// <summary>
    /// Gets or sets the remaining steps array. Not mapped to database - uses RemainingStepsJson for persistence.
    /// </summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays")]
    public string[] RemainingSteps
    {
        get => string.IsNullOrWhiteSpace(RemainingStepsJson)
            ? []
            : JsonSerializer.Deserialize<string[]>(RemainingStepsJson) ?? [];
        set => RemainingStepsJson = JsonSerializer.Serialize(value);
    }

    // Payload stored as JSON
    public string PayloadJson { get; set; } = "{}";

    /// <summary>
    /// Gets or sets the payload object. Not mapped to database - uses PayloadJson for persistence.
    /// </summary>
    public object? Payload
    {
        get => string.IsNullOrWhiteSpace(PayloadJson)
            ? null
            : JsonSerializer.Deserialize<object>(PayloadJson);
        set => PayloadJson = value == null
            ? "{}"
            : JsonSerializer.Serialize(value);
    }
}
