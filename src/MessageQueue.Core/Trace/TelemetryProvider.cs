// -----------------------------------------------------------------------
// <copyright file="TelemetryProvider.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Core.Trace;

/// <summary>
/// Telemetry provider types.
/// </summary>
[Flags]
public enum TelemetryProvider
{
    /// <summary>
    /// No telemetry.
    /// </summary>
    None = 0,

    /// <summary>
    /// Event Tracing for Windows (ETW).
    /// </summary>
    ETW = 1,

    /// <summary>
    /// OpenTelemetry.
    /// </summary>
    OpenTelemetry = 2,

    /// <summary>
    /// Both ETW and OpenTelemetry.
    /// </summary>
    All = ETW | OpenTelemetry
}
