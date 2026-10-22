// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

namespace Qyl.Telemetry.SemanticConventions.Analyzers;

/// <summary>
/// Rewrites deprecated attribute-key string literals to their successors for
/// supplemental-catalog migrations (QYL0009–QYL0010), but only when the
/// migration has an exact replacement.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SupplementalSemconvMigrationCodeFixProvider))]
public sealed class SupplementalSemconvMigrationCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
    [
        "QYL0009",
        "QYL0010",
    ];

    /// <inheritdoc/>
    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var document = context.Document;
        var root = await document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            if (!IsExactReplacement(diagnostic)
                || !diagnostic.Properties.TryGetValue(
                    SupplementalSemconvMigrationAnalyzer.ReplacementNameProperty,
                    out var replacement)
                || string.IsNullOrWhiteSpace(replacement))
            {
                continue;
            }

            if (SemconvCodeFixHelpers.FindStringLiteral(root, diagnostic) is not { } literal)
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Replace with \"{replacement}\"",
                    ct => SemconvCodeFixHelpers.ReplaceLiteralAsync(document, literal, replacement!, ct),
                    nameof(SupplementalSemconvMigrationCodeFixProvider)),
                diagnostic);
        }
    }

    private static bool IsExactReplacement(Diagnostic diagnostic)
    {
        if (!diagnostic.Properties.TryGetValue(
                SupplementalSemconvMigrationAnalyzer.MigrationKindProperty,
                out var migrationKind))
        {
            return false;
        }

        return migrationKind is nameof(SemconvMigrationKind.ExactRename) or nameof(SemconvMigrationKind.ExactValueRename) or nameof(SemconvMigrationKind.DeprecatedButGenerated);
    }
}
