using System.Diagnostics;

namespace Qyl.Telemetry.SemanticConventions.Pipeline.Tests;
// Inside Qyl.Telemetry.SemanticConventions.* the simple name Spans is the definitions namespace.
using Spans = global::Qyl.Spans;

/// <summary>
///   <c>RecordFailure</c> bounds the status description to <see cref="Spans.MaxStatusDescriptionLength"/>
///   UTF-16 code units without cutting a character in half.
/// </summary>
[TestClass]
public sealed class RecordFailureTests
{
    [TestMethod]
    public void RecordFailure_SurrogatePairAtLengthBound_KeepsDescriptionWellFormed()
    {
        using var activity = new Activity("record-failure");
        var message = new string('a', Spans.MaxStatusDescriptionLength - 1) + "\U0001F600 and more";

        activity.RecordFailure(new InvalidOperationException(message));

        var description = activity.StatusDescription!;
        Assert.IsLessThanOrEqualTo(Spans.MaxStatusDescriptionLength, description.Length);
        Assert.IsFalse(char.IsHighSurrogate(description[^1]), "the description ends with a lone high surrogate");
    }
}
