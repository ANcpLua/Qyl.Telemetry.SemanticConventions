// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

namespace Qyl.Telemetry.SemanticConventions.Analyzers;

/// <summary>
/// Applies the replacement carried in the diagnostic's properties for the live
/// registry-metadata rules: swaps a deprecated typed constant for its successor
/// field (QYL0003) or rewrites a deprecated string literal to the renamed
/// attribute key (QYL0005, QYL0007).
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(LiveSemconvMetadataCodeFixProvider))]
public sealed class LiveSemconvMetadataCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
    [
        "QYL0003",
        "QYL0005",
        "QYL0007",
    ];

    /// <inheritdoc/>
    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (var diagnostic in context.Diagnostics)
        {
            if (!diagnostic.Properties.TryGetValue(
                    SemconvCodeFixHelpers.ReplacementValueProperty,
                    out var replacement)
                || string.IsNullOrWhiteSpace(replacement))
            {
                continue;
            }

            if (diagnostic.Id == "QYL0003")
            {
                await RegisterTypedConstantFixAsync(context, diagnostic, replacement!).ConfigureAwait(false);
                continue;
            }

            await RegisterLiteralFixAsync(context, diagnostic, replacement!).ConfigureAwait(false);
        }
    }

    private static async Task RegisterTypedConstantFixAsync(
        CodeFixContext context,
        Diagnostic diagnostic,
        string replacement)
    {
        var document = context.Document;
        var root = await document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        var semanticModel = await document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel is null)
        {
            return;
        }

        var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
        var name = node.FirstAncestorOrSelf<SimpleNameSyntax>();
        if (name is null)
        {
            return;
        }

        var symbol = semanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol as IFieldSymbol;
        if (symbol is null)
        {
            return;
        }

        var replacementField = FindReplacementField(semanticModel.Compilation, replacement);
        if (replacementField is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                $"Replace with {replacementField.ContainingType.Name}.{replacementField.Name}",
                ct => ReplaceTypedConstantAsync(document, name, symbol, replacementField, ct),
                nameof(LiveSemconvMetadataCodeFixProvider) + ".TypedConstant"),
            diagnostic);
    }

    private static async Task<Document> ReplaceTypedConstantAsync(
        Document document,
        SimpleNameSyntax originalName,
        IFieldSymbol originalField,
        IFieldSymbol replacementField,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        if (originalName.Parent is MemberAccessExpressionSyntax memberAccess
            && memberAccess.Name == originalName
            && SymbolEqualityComparer.Default.Equals(originalField.ContainingType, replacementField.ContainingType))
        {
            var replacementName = SyntaxFactory.IdentifierName(replacementField.Name)
                .WithTriviaFrom(originalName);
            return document.WithSyntaxRoot(root.ReplaceNode(originalName, replacementName));
        }

        var qualified = CreateQualifiedFieldExpression(replacementField)
            .WithTriviaFrom(originalName.Parent is MemberAccessExpressionSyntax containingMemberAccess
                && containingMemberAccess.Name == originalName
                    ? containingMemberAccess
                    : originalName);

        SyntaxNode nodeToReplace = originalName.Parent is MemberAccessExpressionSyntax parentMemberAccess
            && parentMemberAccess.Name == originalName
                ? parentMemberAccess
                : originalName;
        return document.WithSyntaxRoot(root.ReplaceNode(nodeToReplace, qualified));
    }

    private static async Task RegisterLiteralFixAsync(
        CodeFixContext context,
        Diagnostic diagnostic,
        string replacement)
    {
        var document = context.Document;
        var root = await document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        if (SemconvCodeFixHelpers.FindStringLiteral(root, diagnostic) is not { } literal)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                $"Replace with \"{replacement}\"",
                ct => SemconvCodeFixHelpers.ReplaceLiteralAsync(document, literal, replacement, ct),
                nameof(LiveSemconvMetadataCodeFixProvider) + ".Literal"),
            diagnostic);
    }

    private static IFieldSymbol? FindReplacementField(
        Compilation compilation,
        string replacement)
    {
        foreach (var (_, field, value, obsolete) in SemconvNamespace.EnumerateAttributeKeyConstants(compilation))
        {
            if (obsolete is null && string.Equals(value, replacement, StringComparison.Ordinal))
            {
                return field;
            }
        }

        return null;
    }

    private static ExpressionSyntax CreateQualifiedFieldExpression(IFieldSymbol field)
    {
        var qualifiedName = field.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            + "."
            + field.Name;
        return SyntaxFactory.ParseExpression(qualifiedName);
    }
}
