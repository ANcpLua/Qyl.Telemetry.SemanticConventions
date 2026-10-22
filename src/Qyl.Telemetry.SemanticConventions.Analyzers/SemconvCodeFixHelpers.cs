// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

namespace Qyl.Telemetry.SemanticConventions.Analyzers;

internal static class SemconvCodeFixHelpers
{
    public const string ReplacementValueProperty = "ReplacementValue";

    /// <summary>Fallback when an <c>[Obsolete]</c> attribute carries no usable message.</summary>
    private const string MissingObsoleteMessage = "no replacement message provided";

    /// <summary>The <c>[Obsolete]</c> message, or <see cref="MissingObsoleteMessage"/> when absent or empty.</summary>
    public static string GetObsoleteMessage(AttributeData obsolete) =>
        obsolete.TryGetConstructorArgument<string>(0, out var message) && !string.IsNullOrEmpty(message)
            ? message
            : MissingObsoleteMessage;

    public static bool TryExtractExactReplacement(
        string message,
        [NotNullWhen(true)] out string? replacement)
    {
        replacement = null;
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        if (TryExtractCodeTagReplacement(message, out replacement))
        {
            return true;
        }

        if (TryExtractQuotedUseReplacement(message, out replacement))
        {
            return true;
        }

        const string prefix = "Replaced by ";
        var trimmed = message.Trim();
        if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        replacement = NormalizeCandidate(trimmed.Substring(prefix.Length));
        return IsExactReplacementCandidate(replacement);
    }

    private static bool TryExtractQuotedUseReplacement(string guidance, [NotNullWhen(true)] out string? replacement) =>
        TryExtractDelimited(guidance, '\'', out replacement) || TryExtractDelimited(guidance, '`', out replacement);

    /// <summary>The text between <paramref name="delimiter"/>s right after <c>Use </c>, as in <c>Use 'x'</c>.</summary>
    private static bool TryExtractDelimited(string guidance, char delimiter, [NotNullWhen(true)] out string? replacement)
    {
        var prefix = "Use " + delimiter;
        if (guidance.StartsWith(prefix, StringComparison.Ordinal))
        {
            var start = prefix.Length;
            var end = guidance.IndexOf(delimiter, start);
            if (end > start)
            {
                replacement = guidance.Substring(start, end - start);
                return true;
            }
        }

        replacement = null;
        return false;
    }

    /// <summary>The string literal <paramref name="diagnostic"/> points at, or <see langword="null"/> when it points at anything else.</summary>
    public static LiteralExpressionSyntax? FindStringLiteral(SyntaxNode root, Diagnostic diagnostic) =>
        root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true)
            .FirstAncestorOrSelf<LiteralExpressionSyntax>() is { } literal
        && literal.IsKind(SyntaxKind.StringLiteralExpression)
            ? literal
            : null;

    /// <summary>Replaces <paramref name="literal"/> in <paramref name="document"/> with a string literal of <paramref name="replacement"/>.</summary>
    public static async Task<Document> ReplaceLiteralAsync(
        Document document,
        LiteralExpressionSyntax literal,
        string replacement,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        return document.WithSyntaxRoot(root.ReplaceNode(literal, CreateReplacementLiteral(literal, replacement)));
    }

    public static LiteralExpressionSyntax CreateReplacementLiteral(
        LiteralExpressionSyntax original,
        string replacement)
    {
        var token = original.Token;
        var text = token.Text;
        var literalText = text.StartsWith("@\"", StringComparison.Ordinal)
            ? "@\"" + replacement.Replace("\"", "\"\"") + "\""
            : "\"" + replacement.EscapeCSharpString() + "\"";

        return SyntaxFactory.LiteralExpression(
                SyntaxKind.StringLiteralExpression,
                SyntaxFactory.Literal(token.LeadingTrivia, literalText, replacement, token.TrailingTrivia))
            .WithTriviaFrom(original);
    }

    private static bool TryExtractCodeTagReplacement(
        string message,
        [NotNullWhen(true)] out string? replacement)
    {
        replacement = null;
        var start = message.IndexOf("<c>", StringComparison.Ordinal);
        if (start < 0)
        {
            return false;
        }

        start += "<c>".Length;
        var end = message.IndexOf("</c>", start, StringComparison.Ordinal);
        if (end <= start)
        {
            return false;
        }

        replacement = NormalizeCandidate(message.Substring(start, end - start));
        return IsExactReplacementCandidate(replacement);
    }

    private static string NormalizeCandidate(string value)
    {
        var candidate = value.Trim().TrimEnd('.').Trim();
        candidate = candidate.Trim('`', '"', '\'');
        return candidate;
    }

    private static bool IsExactReplacementCandidate([NotNullWhen(true)] string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value!;
        foreach (var ch in candidate)
        {
            if (char.IsWhiteSpace(ch)
                || ch == ','
                || ch == ';'
                || ch == '('
                || ch == ')')
            {
                return false;
            }
        }

        return true;
    }
}
