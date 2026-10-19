// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

namespace Qyl.Telemetry.SemanticConventions.Analyzers;

internal static class DiagnosticDescriptors
{
    private const string Category = "OpenTelemetry.SemanticConventions";

    // Each rule deep-links to its own per-rule page at
    //   docs/rules/QYL00XX_<SymbolicName>.md
    // where SymbolicName is the analyzer class-name suffix after stripping the QYL
    // prefix + "Analyzer" suffix. tools/.../DocsGenerator emits those files; --check
    // verifies the helpLinkUri here matches what reflection on the analyzer class
    // would compute. Drift fails CI.

    public static readonly DiagnosticDescriptor DeprecatedSemconvConstant = new(
        id: "QYL0003",
        title: "Deprecated semantic-convention constant",
        messageFormat: "Semantic-convention constant '{0}' is deprecated: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "References to constants in OpenTelemetry.SemanticConventions.Attributes.* that carry [Obsolete]. Migrate to the replacement attribute named in the deprecation message.",
        helpLinkUri: RuleDocs.HelpLink("QYL0003", "DeprecatedSemconv"));

    public static readonly DiagnosticDescriptor RpcServerHasClientAddressAttribute = new(
        id: "QYL0002",
        title: "RPC server span must not include client.address / client.port",
        messageFormat: "RPC server span sets '{0}'; client.* attributes are invalid on RPC server spans",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "RPC server spans extend the rpc base group directly and exclude client.address and client.port. Use server.address / server.port instead.",
        helpLinkUri: RuleDocs.HelpLink("QYL0002", "RpcServerClientAttribute"));

    public static readonly DiagnosticDescriptor GenAiExecuteToolMissingToolName = new(
        id: "QYL0400",
        title: "Execute-tool span is missing its registry-required attribute",
        messageFormat: "Method sets {0}=\"{1}\" but does not set registry-required attribute '{2}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The pinned GenAI span definition requires an additional tool attribute when the execute-tool operation discriminator is present.",
        helpLinkUri: RuleDocs.HelpLink("QYL0400", "GenAiExecuteToolName"));

    public static readonly DiagnosticDescriptor GraphqlDocumentIsOptIn = new(
        id: "QYL0001",
        title: "graphql.document is opt-in",
        messageFormat: "Setting graphql.document captures user-supplied data; v1.41.0 demoted it from recommended to opt_in — verify explicit enablement and sanitization",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "graphql.document carries user-inputted, potentially sensitive, high-cardinality content. v1.41.0 moved its requirement level from recommended to opt_in. Capture only behind an explicit opt-in flag with sanitization.",
        helpLinkUri: RuleDocs.HelpLink("QYL0001", "GraphqlDocumentOptIn"));

    public static readonly DiagnosticDescriptor PreferSemconvConstant = new(
        id: "QYL0004",
        title: "Prefer typed semantic-convention constant over string literal",
        messageFormat: "String literal \"{0}\" matches the semantic-convention constant '{1}' — use the typed constant",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "When a telemetry attribute key literal matches a known attribute name from the referenced semantic-convention packages, prefer the typed constant for refactor-safety and discoverability.",
        helpLinkUri: RuleDocs.HelpLink("QYL0004", "PreferSemconvConstant"));

    public static readonly DiagnosticDescriptor LiteralMatchesDeprecatedSemconv = new(
        id: "QYL0005",
        title: "String literal matches a deprecated semantic-convention name",
        messageFormat: "Literal \"{0}\" matches a deprecated semantic-convention attribute: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "When a telemetry attribute key literal matches a semantic-convention attribute that is marked [Obsolete] in the consumer's referenced OpenTelemetry.SemanticConventions package, the call site needs migration regardless of whether a typed constant is being used.",
        helpLinkUri: RuleDocs.HelpLink("QYL0005", "LiteralMatchesDeprecatedSemconv"));

    public static readonly DiagnosticDescriptor DeprecatedSemconvValue = new(
        id: "QYL0007",
        title: "Deprecated semantic-convention value",
        messageFormat: "Value \"{0}\" of attribute '{1}' is deprecated: {2}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A constant string used as the value of a known semantic-convention telemetry attribute matches a value member that is marked [Obsolete] in the consumer's referenced *Values enum class.",
        helpLinkUri: RuleDocs.HelpLink("QYL0007", "DeprecatedSemconvValue"));

    public static readonly DiagnosticDescriptor IncubatingSemconvInLibrary = new(
        id: "QYL0008",
        title: "Incubating semantic-convention member used in a library",
        messageFormat: "Member '{0}' from an Incubating namespace forces every consumer onto its exact package version; copy the constant locally in libraries",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Members under any *.SemanticConventions.Incubating namespace may rename or change values across minor package releases. Library projects (non-exe, non-test) baking direct references push that volatility onto every downstream consumer. Copying the value into the library suppresses the rule: a const field, a static readonly field, or a static readonly array of copies, and a method-local const. A library that deliberately version-locks with the incubating tier opts the whole project out with the MSBuild property OtelSemConvInstrumentationLibrary set to true.",
        helpLinkUri: RuleDocs.HelpLink("QYL0008", "IncubatingSemconvInLibrary"));

    public static readonly DiagnosticDescriptor SupplementalExactSemconvMigration = new(
        id: "QYL0009",
        title: "Obsolete semantic convention has an exact replacement",
        messageFormat: "Semantic convention '{0}' is obsolete in production telemetry emission; use '{1}' ({2})",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A hard-coded semantic-convention name or value matches the supplemental OpenTelemetry migration catalog and has a one-to-one replacement. This supplements, but does not replace, [Obsolete] metadata from OpenTelemetry.SemanticConventions.",
        helpLinkUri: RuleDocs.HelpLink("QYL0009", "SupplementalSemconvMigration"));

    public static readonly DiagnosticDescriptor SupplementalManualSemconvMigration = new(
        id: "QYL0010",
        title: "Semantic convention migration needs review",
        messageFormat: "Semantic convention '{0}' needs semantic-convention migration review: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A hard-coded semantic-convention name or value matches the supplemental OpenTelemetry migration catalog, but the migration is context-sensitive or has no safe automatic replacement.",
        helpLinkUri: RuleDocs.HelpLink("QYL0010", "SupplementalSemconvMigration"));

}
