// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

namespace Qyl.Telemetry.SemanticConventions.Analyzers;

/// <summary>
/// Matches attribute keys against word patterns on segment boundaries rather than as raw
/// substrings. A key splits on <c>.</c>, <c>_</c>, <c>-</c> and camelCase transitions, so
/// <c>api_key</c>, <c>api.key</c>, <c>apiKey</c> and <c>x-api-key</c> all read as
/// <c>[api, key]</c>. A pattern matches when its segments appear as a contiguous run in
/// the key's segments: <c>token</c> matches <c>auth.token</c> but not
/// <c>gen_ai.usage.input_tokens</c>, and <c>pin</c> matches <c>card.pin</c> but not
/// <c>cicd.pipeline.run.id</c>. Against the 1031 attribute keys of the pinned registry,
/// substring matching of the QYL0601 list hit 33 of them; segment matching hits 8.
/// </summary>
internal sealed class AttributeKeyPatternSet
{
    private readonly ImmutableArray<ImmutableArray<string>> _patterns;

    public AttributeKeyPatternSet(params string[] patterns)
    {
        var builder = ImmutableArray.CreateBuilder<ImmutableArray<string>>(patterns.Length);
        foreach (var pattern in patterns)
        {
            var segments = Segment(pattern);
            if (!segments.IsEmpty)
            {
                builder.Add(segments);
            }
        }

        _patterns = builder.ToImmutable();
    }

    public bool Matches(string key)
    {
        var segments = Segment(key);
        if (segments.IsEmpty)
        {
            return false;
        }

        foreach (var pattern in _patterns)
        {
            if (ContainsRun(segments, pattern))
            {
                return true;
            }
        }

        return false;
    }

    internal static ImmutableArray<string> Segment(string key)
    {
        var segments = ImmutableArray.CreateBuilder<string>();
        var current = new StringBuilder();

        for (var i = 0; i < key.Length; i++)
        {
            var c = key[i];
            if (IsSeparator(c))
            {
                Flush(segments, current);
                continue;
            }

            if (char.IsUpper(c) && i > 0 && IsCamelBoundary(key, i))
            {
                Flush(segments, current);
            }

            current.Append(char.ToLowerInvariant(c));
        }

        Flush(segments, current);
        return segments.ToImmutable();
    }

    private static bool ContainsRun(ImmutableArray<string> segments, ImmutableArray<string> pattern) =>
        segments.AsSpan().IndexOf(pattern.AsSpan()) >= 0;

    /// <summary>
    /// <c>apiKey</c> breaks before <c>K</c> (lower to upper); <c>HTTPToken</c> breaks before
    /// <c>T</c> of <c>Token</c> (an upper followed by a lower, after a run of uppers).
    /// </summary>
    private static bool IsCamelBoundary(string key, int index)
    {
        var previous = key[index - 1];
        if (char.IsLower(previous) || char.IsDigit(previous))
        {
            return true;
        }

        return char.IsUpper(previous)
            && index + 1 < key.Length
            && char.IsLower(key[index + 1]);
    }

    private static bool IsSeparator(char c) =>
        c is '.' or '_' or '-' or ':' or '/' or ' ';

    private static void Flush(ImmutableArray<string>.Builder segments, StringBuilder current)
    {
        if (current.Length > 0)
        {
            segments.Add(current.ToString());
            current.Clear();
        }
    }
}
