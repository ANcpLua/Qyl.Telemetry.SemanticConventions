using System.Diagnostics;

namespace Qyl.Telemetry.SemanticConventions;

/// <summary>
/// A finished span as a trace reader received it: identity, name, kind, timing, status, scope,
/// attributes, resource, events and links. The generated span views (namespaces <c>…Views</c>)
/// read it, and <c>Qyl.Telemetry.Testing</c>'s receiver produces it.
/// </summary>
/// <remarks>
/// Attribute values are what OTLP carries: <see cref="string"/>, <see cref="long"/>,
/// <see cref="double"/>, <see cref="bool"/>, bytes, recursive lists/maps, or null. The typed getters convert
/// between the integer and floating representations and return <see langword="null"/> for an
/// absent key or a value of another type.
/// </remarks>
[JetBrains.Annotations.PublicAPI]
public sealed class SpanRecord
{
    /// <summary>Initializes a span record.</summary>
    /// <param name="traceId">The trace id, 32 lowercase hex characters.</param>
    /// <param name="spanId">The span id, 16 lowercase hex characters.</param>
    /// <param name="name">The span name.</param>
    public SpanRecord(string traceId, string spanId, string name)
    {
        TraceId = traceId ?? throw new ArgumentNullException(nameof(traceId));
        SpanId = spanId ?? throw new ArgumentNullException(nameof(spanId));
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    /// <summary>The trace id, 32 lowercase hex characters.</summary>
    public string TraceId { get; }

    /// <summary>The span id, 16 lowercase hex characters.</summary>
    public string SpanId { get; }

    /// <summary>The parent span id, or <see langword="null"/> for a root span.</summary>
    public string? ParentSpanId { get; init; }

    /// <summary>The W3C trace state supplied by the producer.</summary>
    public string? TraceState { get; init; }

    /// <summary>The complete OTLP flags field, including remote-context and unknown bits.</summary>
    public uint Flags { get; init; }

    /// <summary>The span name.</summary>
    public string Name { get; }

    /// <summary>The span kind.</summary>
    public SpanKind Kind { get; init; }

    /// <summary>The exact start timestamp in nanoseconds since the Unix epoch.</summary>
    public ulong StartTimeUnixNano { get; init; }

    /// <summary>The exact end timestamp in nanoseconds since the Unix epoch.</summary>
    public ulong EndTimeUnixNano { get; init; }

    /// <summary>When the span started, projected to .NET's 100-nanosecond tick resolution.</summary>
    public DateTimeOffset StartTime => TelemetryTimestamp.ToDateTimeOffset(StartTimeUnixNano);

    /// <summary>When the span ended, projected to .NET's 100-nanosecond tick resolution.</summary>
    public DateTimeOffset EndTime => TelemetryTimestamp.ToDateTimeOffset(EndTimeUnixNano);

    /// <summary>The elapsed nanoseconds projected to .NET ticks, without subtracting rounded timestamps.</summary>
    public TimeSpan Duration => EndTimeUnixNano >= StartTimeUnixNano
        ? TimeSpan.FromTicks((long)((EndTimeUnixNano - StartTimeUnixNano) / 100))
        : -TimeSpan.FromTicks((long)((StartTimeUnixNano - EndTimeUnixNano) / 100));

    /// <summary>The status code.</summary>
    public ActivityStatusCode Status { get; init; }

    /// <summary>The status description, or <see langword="null"/>.</summary>
    public string? StatusDescription { get; init; }

    /// <summary>The instrumentation scope, independent of the resource schema.</summary>
    public InstrumentationScopeRecord? Scope { get; init; }

    /// <summary>The span attributes.</summary>
    public IReadOnlyDictionary<string, object?> Attributes { get; init; } = new Dictionary<string, object?>();

    /// <summary>The attributes dropped by the producer before export.</summary>
    public uint DroppedAttributesCount { get; init; }

    /// <summary>The resource attributes and their own schema and loss metadata.</summary>
    public ResourceRecord Resource { get; init; } = new(new Dictionary<string, object?>());

    /// <summary>The span events.</summary>
    public IReadOnlyList<SpanEventRecord> Events { get; init; } = Array.Empty<SpanEventRecord>();

    /// <summary>The events dropped by the producer before export.</summary>
    public uint DroppedEventsCount { get; init; }

    /// <summary>The span links.</summary>
    public IReadOnlyList<SpanLinkRecord> Links { get; init; } = Array.Empty<SpanLinkRecord>();

    /// <summary>The links dropped by the producer before export.</summary>
    public uint DroppedLinksCount { get; init; }

    /// <summary>The resource's <c>service.name</c>, or <see langword="null"/>.</summary>
    public string? Service => Resource.Attributes.TryGetValue("service.name", out var value) ? value as string : null;

    /// <summary>The attribute as a string, or <see langword="null"/> when absent or not a string.</summary>
    /// <param name="key">The attribute key.</param>
    /// <returns>The value.</returns>
    public string? GetString(string key) => Attributes.TryGetValue(key, out var value) ? value as string : null;

    /// <summary>The attribute as a 64-bit integer, or <see langword="null"/> when absent or not an integer.</summary>
    /// <param name="key">The attribute key.</param>
    /// <returns>The value.</returns>
    public long? GetInt64(string key) =>
        Attributes.TryGetValue(key, out var value) ? ReadInt64(value) : null;

    /// <summary>The attribute as a double, or <see langword="null"/> when absent or not a number.</summary>
    /// <param name="key">The attribute key.</param>
    /// <returns>The value.</returns>
    public double? GetDouble(string key) =>
        Attributes.TryGetValue(key, out var value) ? ReadDouble(value) : null;

    private static long? ReadInt64(object? value) => value switch
    {
        long number => number,
        int number => number,
        short number => number,
        byte number => number,
        _ => null
    };

    private static double? ReadDouble(object? value) => value switch
    {
        double number => number,
        float number => number,
        _ => ReadInt64(value)
    };

    /// <summary>The attribute as integers, or null if absent or any element has an unsupported type.</summary>
    public IReadOnlyList<long>? GetInt64s(string key) => GetValues(key, ReadInt64);

    /// <summary>The attribute as numbers, or null if absent or any element has an unsupported type.</summary>
    public IReadOnlyList<double>? GetDoubles(string key) => GetValues(key, ReadDouble);

    /// <summary>The attribute as booleans, or null if absent or any element is not a boolean.</summary>
    public IReadOnlyList<bool>? GetBooleans(string key) => GetValues<bool>(key, static value => value is bool boolean ? boolean : null);

    private List<T>? GetValues<T>(string key, Func<object?, T?> read) where T : struct
    {
        if (!Attributes.TryGetValue(key, out var value) || value is string || value is not System.Collections.IEnumerable items)
            return null;

        var values = new List<T>();
        foreach (var item in items)
        {
            if (read(item) is not { } typed) return null;
            values.Add(typed);
        }
        return values;
    }

    /// <summary>The attribute as a boolean, or <see langword="null"/> when absent or not a boolean.</summary>
    /// <param name="key">The attribute key.</param>
    /// <returns>The value.</returns>
    public bool? GetBoolean(string key) => Attributes.TryGetValue(key, out var value) && value is bool b ? b : null;

    /// <summary>The attribute as a list of strings, or <see langword="null"/> when absent or not a list of strings.</summary>
    /// <param name="key">The attribute key.</param>
    /// <returns>The value.</returns>
    public IReadOnlyList<string>? GetStrings(string key)
    {
        if (!Attributes.TryGetValue(key, out var value) || value is string || value is not System.Collections.IEnumerable items)
        {
            return null;
        }

        var list = new List<string>();
        foreach (var item in items)
        {
            if (item is not string text)
            {
                return null;
            }

            list.Add(text);
        }

        return list;
    }

    /// <inheritdoc/>
    public override string ToString() => Name;


}

/// <summary>An event of a <see cref="SpanRecord"/>.</summary>
[JetBrains.Annotations.PublicAPI]
public sealed class SpanEventRecord
{
    /// <summary>Initializes a span event record.</summary>
    /// <param name="name">The event name (<c>exception</c> for a recorded exception).</param>
    /// <param name="timeUnixNano">The exact timestamp in nanoseconds since the Unix epoch.</param>
    /// <param name="attributes">The event attributes.</param>
    public SpanEventRecord(string name, ulong timeUnixNano, IReadOnlyDictionary<string, object?> attributes)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        TimeUnixNano = timeUnixNano;
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    /// <summary>The event name.</summary>
    public string Name { get; }

    /// <summary>When the event happened.</summary>
    public DateTimeOffset Timestamp => TelemetryTimestamp.ToDateTimeOffset(TimeUnixNano);

    /// <summary>The exact event timestamp, without rounding to .NET ticks.</summary>
    public ulong TimeUnixNano { get; }

    /// <summary>The event attributes dropped by the producer.</summary>
    public uint DroppedAttributesCount { get; init; }

    /// <summary>The event attributes.</summary>
    public IReadOnlyDictionary<string, object?> Attributes { get; }
}

/// <summary>A link of a <see cref="SpanRecord"/>.</summary>
[JetBrains.Annotations.PublicAPI]
public sealed class SpanLinkRecord
{
    /// <summary>Initializes a span link record.</summary>
    /// <param name="traceId">The linked trace id.</param>
    /// <param name="spanId">The linked span id.</param>
    /// <param name="attributes">The link attributes.</param>
    public SpanLinkRecord(string traceId, string spanId, IReadOnlyDictionary<string, object?> attributes)
    {
        TraceId = traceId ?? throw new ArgumentNullException(nameof(traceId));
        SpanId = spanId ?? throw new ArgumentNullException(nameof(spanId));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    /// <summary>The linked trace id.</summary>
    public string TraceId { get; }

    /// <summary>The linked span id.</summary>
    public string SpanId { get; }

    /// <summary>The linked span's W3C trace state.</summary>
    public string? TraceState { get; init; }

    /// <summary>The complete OTLP flags field of the linked context.</summary>
    public uint Flags { get; init; }

    /// <summary>The link attributes dropped by the producer.</summary>
    public uint DroppedAttributesCount { get; init; }

    /// <summary>The link attributes.</summary>
    public IReadOnlyDictionary<string, object?> Attributes { get; }
}
