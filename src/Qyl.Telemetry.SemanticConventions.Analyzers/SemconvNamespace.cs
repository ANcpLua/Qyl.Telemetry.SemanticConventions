// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

namespace Qyl.Telemetry.SemanticConventions.Analyzers;

/// <summary>Shared namespace-shape detection for SemConv attribute classes.</summary>
internal static class SemconvNamespace
{
    private const string Root = "OpenTelemetry.SemanticConventions";
    private const string QylRoot = "Qyl.Telemetry.SemanticConventions";

    /// <summary>
    /// Returns true if the type's suffix matches a recognised SemConv tier AND it lives
    /// in or under the upstream or qyl semantic-convention namespace, including nested layouts.
    /// When <paramref name="allowNonAttributesTiers"/> is <c>true</c>, the suffix check
    /// additionally accepts the three non-Attributes tiers a semantic-convention
    /// package pre-generates — <c>*Metrics</c>, <c>*Meters</c>, <c>*Activities</c> — gated behind
    /// <c>build_property.OtelSemConvNonAttributesTiers</c> so consumers explicitly
    /// opt into wider coverage.
    /// </summary>
    public static bool IsAttributesType(INamedTypeSymbol type, bool allowNonAttributesTiers = false)
    {
        if (!HasRecognisedTierSuffix(type.Name, allowNonAttributesTiers))
        {
            return false;
        }

        return IsInSemconvNamespace(type.ContainingNamespace);
    }

    private static bool HasRecognisedTierSuffix(string typeName, bool allowNonAttributesTiers)
    {
        if (typeName.EndsWith("Attributes", StringComparison.Ordinal))
        {
            return true;
        }

        if (!allowNonAttributesTiers)
        {
            return false;
        }

        return typeName.EndsWith("Metrics", StringComparison.Ordinal)
            || typeName.EndsWith("Meters", StringComparison.Ordinal)
            || typeName.EndsWith("Activities", StringComparison.Ordinal);
    }

    /// <summary>
    /// Lazily enumerates every type in the compilation that satisfies
    /// <see cref="IsAttributesType"/>. Lazy so callers that stop early
    /// (e.g. a first-match search) do not pay for a full walk.
    /// </summary>
    public static IEnumerable<INamedTypeSymbol> EnumerateAttributesTypes(
        Compilation compilation,
        bool allowNonAttributesTiers = false) =>
        compilation.GlobalNamespace.GetAllTypes()
            .Where(type => IsAttributesType(type, allowNonAttributesTiers));

    /// <summary>
    /// Every public, non-empty <c>const string</c> attribute key of every type
    /// <see cref="EnumerateAttributesTypes"/> yields, with its <c>[Obsolete]</c> attribute when it has
    /// one. The deprecation analyzers and the code fix read the referenced package through this one
    /// walk, so they agree on what it says.
    /// </summary>
    public static IEnumerable<(INamedTypeSymbol Type, IFieldSymbol Field, string Value, AttributeData? Obsolete)> EnumerateAttributeKeyConstants(
        Compilation compilation,
        bool allowNonAttributesTiers = false)
    {
        foreach (var type in EnumerateAttributesTypes(compilation, allowNonAttributesTiers))
        {
            foreach (var member in type.GetMembers())
            {
                if (member is IFieldSymbol
                    {
                        IsConst: true,
                        DeclaredAccessibility: Accessibility.Public,
                        Type.SpecialType: SpecialType.System_String,
                        ConstantValue: string value,
                    } field
                    && value.Length > 0)
                {
                    yield return (type, field, value, field.GetAttribute("System.ObsoleteAttribute"));
                }
            }
        }
    }

    /// <summary>
    /// Every <c>const string</c> value of every type <see cref="EnumerateAttributesTypes"/> yields,
    /// with the attribute it belongs to and its <c>[Obsolete]</c> attribute when it has one.
    /// </summary>
    public static IEnumerable<(string AttributeName, IFieldSymbol ValueField, string Value, AttributeData? Obsolete)> EnumerateAttributeValueConstants(
        Compilation compilation) =>
        EnumerateAttributesTypes(compilation).SelectMany(EnumerateAttributeValueConstants);

    /// <summary>
    /// Pairs every nested <c>*Values</c> type of <paramref name="attributesType"/> with
    /// the sibling const that names its attribute (<c>AttributeX</c> upstream, <c>X</c> in the qyl
    /// packages), and yields each
    /// constant string field of the paired type together with that attribute name.
    /// </summary>
    private static IEnumerable<(string AttributeName, IFieldSymbol ValueField, string Value, AttributeData? Obsolete)> EnumerateAttributeValueConstants(
        INamedTypeSymbol attributesType)
    {
        Dictionary<string, string>? attrConstNameToValue = null;
        foreach (var member in attributesType.GetMembers())
        {
            if (member is IFieldSymbol { IsConst: true, Type.SpecialType: SpecialType.System_String, ConstantValue: string attrName } field
                && !string.IsNullOrEmpty(attrName))
            {
                attrConstNameToValue ??= new Dictionary<string, string>(StringComparer.Ordinal);
                attrConstNameToValue[field.Name] = attrName;
            }
        }

        if (attrConstNameToValue is null)
        {
            yield break;
        }

        foreach (var nested in attributesType.GetTypeMembers())
        {
            if (!nested.Name.EndsWith("Values", StringComparison.Ordinal))
            {
                continue;
            }

            var prefix = nested.Name.Substring(0, nested.Name.Length - "Values".Length);
            if (!attrConstNameToValue.TryGetValue("Attribute" + prefix, out var attributeName)
                && !attrConstNameToValue.TryGetValue(prefix, out attributeName))
            {
                continue;
            }

            foreach (var nestedMember in nested.GetMembers())
            {
                if (nestedMember is IFieldSymbol { IsConst: true, Type.SpecialType: SpecialType.System_String, ConstantValue: string value } valueField)
                {
                    yield return (attributeName, valueField, value, valueField.GetAttribute("System.ObsoleteAttribute"));
                }
            }
        }
    }

    private static bool IsInSemconvNamespace(INamespaceSymbol? ns)
    {
        var s = ns?.ToDisplayString();
        if (s is null)
        {
            return false;
        }

        return IsWithinRoot(s, Root) || IsWithinRoot(s, QylRoot);
    }

    private static bool IsWithinRoot(string value, string root) =>
        value == root || value.StartsWith(root + ".", StringComparison.Ordinal)
        || value.ContainsOrdinal("." + root + ".")
        || value.EndsWith("." + root, StringComparison.Ordinal);

    /// <summary>
    /// Returns true if the namespace's display string contains an <c>.Incubating</c>
    /// segment AND descends from a SemanticConventions root. Mirrors the AL0136
    /// detection: any namespace ending in or containing <c>.Incubating</c> under
    /// <c>OpenTelemetry.SemanticConventions</c> or <c>OpenTelemetry.SemConv</c>.
    /// </summary>
    public static bool IsIncubatingNamespace(INamespaceSymbol? ns)
    {
        var s = ns?.ToDisplayString();
        if (s is null || !s.Contains(".Incubating"))
        {
            return false;
        }

        return s.Contains("SemanticConventions") || s.Contains("SemConv");
    }
}
