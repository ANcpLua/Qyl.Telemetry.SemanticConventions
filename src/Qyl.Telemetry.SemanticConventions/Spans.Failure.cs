using System.Diagnostics;
using Qyl.Telemetry.SemanticConventions.Activities;
using Qyl.Telemetry.SemanticConventions.Attributes.Error;

namespace Qyl;

[JetBrains.Annotations.PublicAPI]
public static partial class Spans
{
    /// <summary>The longest status description <c>RecordFailure</c> writes; a longer one is cut.</summary>
    public const int MaxStatusDescriptionLength = 256;

    /// <summary>
    /// Records a failure the way qyl reads it (ADR D4): status <see cref="ActivityStatusCode.Error"/>
    /// with the exception's message as the bounded description, <c>error.type</c> the exception's
    /// full type name, and the exception event.
    /// </summary>
    /// <remarks>
    /// The Collector sanitizes exception text before persistence. The bounded status description
    /// and exception event both retain useful failure context. Call this in each rethrowing
    /// <c>catch</c> inside a starter scope (QYL0110).
    /// </remarks>
    /// <param name="activity">The failed span.</param>
    /// <param name="exception">The exception.</param>
    /// <returns>The same span.</returns>
    public static Activity RecordFailure(this Activity activity, Exception exception)
    {
        ThrowIfNull(activity, nameof(activity));
        ThrowIfNull(exception, nameof(exception));

        activity.SetStatus(ActivityStatusCode.Error, Bound(exception.Message));
        activity.SetTag(ErrorAttributes.Type, exception.GetType().FullName);
        activity.AddException(exception);
        return activity;
    }

    /// <summary>
    /// Records a failure that no exception carries: status <see cref="ActivityStatusCode.Error"/>
    /// with <paramref name="description"/>, bounded, and <c>error.type</c> <paramref name="type"/>.
    /// </summary>
    /// <param name="activity">The failed span.</param>
    /// <param name="type">The failure class, such as <see cref="ErrorType.Other"/> or <c>ErrorType.Custom("rate_limit_exceeded")</c>.</param>
    /// <param name="description">What failed, in a sentence.</param>
    /// <returns>The same span.</returns>
    public static Activity RecordFailure(this Activity activity, ErrorType type, string description)
    {
        ThrowIfNull(activity, nameof(activity));
        ThrowIfNull(description, nameof(description));

        activity.SetStatus(ActivityStatusCode.Error, Bound(description));
        activity.SetTag(ErrorAttributes.Type, type.Value);
        return activity;
    }

    private static void ThrowIfNull(object? argument, string name)
    {
#if NET
        ArgumentNullException.ThrowIfNull(argument, name);
#else
        if (argument is null) throw new ArgumentNullException(name);
#endif
    }

    private static string Bound(string description) =>
        description.Length <= MaxStatusDescriptionLength
            ? description
            : description.Substring(0, char.IsHighSurrogate(description[MaxStatusDescriptionLength - 1])
                ? MaxStatusDescriptionLength - 1
                : MaxStatusDescriptionLength);
}
