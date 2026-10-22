// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

namespace Qyl.Telemetry.SemanticConventions.Analyzers;

/// <summary>The OTLP exporter configuration names the OTLP rules (QYL0301, QYL0600, QYL0700, QYL0701) recognise.</summary>
internal static class OtlpConfigurationNames
{
    /// <summary>The methods that add or configure an OTLP exporter.</summary>
    public static ImmutableHashSet<string> ExporterMethods { get; } =
    [
        "AddOtlpExporter",
        "UseOtlpExporter",
        "WithOtlpExporter",
    ];

    /// <summary>The properties that carry an OTLP collector endpoint.</summary>
    public static ImmutableArray<string> EndpointProperties { get; } =
    [
        "Endpoint",
        "CollectorEndpoint",
        "OtlpEndpoint",
        "ExporterEndpoint",
    ];
}
