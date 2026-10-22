// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

namespace Qyl.Telemetry.SemanticConventions.Analyzers;

/// <summary>
/// Shared detection of attribute writes across the analyzers. A write is a raw
/// <c>SetTag("key", value)</c>-style call, a generated typed setter
/// (<c>activity.SetDbSystemName(DbSystemName.Postgresql)</c>), or an argument of a generated span
/// starter (<c>Spans.DbClient(system, ...)</c>, which also sets its domain and fixed values). The
/// typed forms resolve through the generated <see cref="SemconvTypedSurface"/> tables, so the rules
/// that read attribute writes see typed code.
/// </summary>
internal static class TagSetterDetection
{
    private const string ActivitiesNamespace = "Qyl.Telemetry.SemanticConventions.Activities";
    private const string IncubatingActivitiesNamespace = "Qyl.Telemetry.SemanticConventions.Incubating.Activities";

    /// <summary>
    /// Method names treated as a raw "set this telemetry tag" callsite. Matched by simple
    /// name against <see cref="IInvocationOperation.TargetMethod"/>; the receiver type stays open
    /// because consumers use Activity, Span, TagList, custom builders, and OTel-extension wrappers
    /// interchangeably. Only the framework's own non-telemetry types are excluded.
    /// </summary>
    private static readonly ImmutableHashSet<string> TagSetterMethodNames =
        ["SetTag", "AddTag", "SetAttribute", "AddAttribute"];

    /// <summary>Whether <paramref name="invocation"/> is a raw <c>SetTag</c>-style call.</summary>
    public static bool IsTagSetterInvocation(IInvocationOperation invocation) =>
        TagSetterMethodNames.Contains(invocation.TargetMethod.Name)
        && !IsDeclaredInNonTelemetryFrameworkNamespace(invocation.TargetMethod);

    /// <summary>
    /// The <c>System</c> namespace tree outside <c>System.Diagnostics</c> declares no telemetry tag
    /// setter; its same-named methods write something else, such as
    /// <c>System.Xml.XmlElement.SetAttribute</c> writing an XML attribute.
    /// </summary>
    private static bool IsDeclaredInNonTelemetryFrameworkNamespace(IMethodSymbol method)
    {
        var ns = method.ContainingNamespace.ToDisplayString();
        return (ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal))
            && ns != "System.Diagnostics"
            && !ns.StartsWith("System.Diagnostics.", StringComparison.Ordinal);
    }

    public static bool TryGetTagSetterKeyArgument(
        IInvocationOperation invocation,
        [NotNullWhen(true)] out IArgumentOperation? argument)
    {
        return TelemetryAttributePayloadDetection.TryGetArgumentByOrdinal(
            invocation.Arguments,
            invocation.TargetMethod.IsExtensionMethod,
            logicalParameterOrdinal: 0,
            out argument);
    }

    public static bool TryGetTagSetterValueArgument(
        IInvocationOperation invocation,
        [NotNullWhen(true)] out IArgumentOperation? argument)
    {
        return TelemetryAttributePayloadDetection.TryGetArgumentByOrdinal(
            invocation.Arguments,
            invocation.TargetMethod.IsExtensionMethod,
            logicalParameterOrdinal: 1,
            out argument);
    }

    /// <summary>
    /// The key and value of a single-attribute write: a raw <c>SetTag</c>-style call with a constant
    /// key, or a generated typed setter (a template setter's key needs a constant segment).
    /// </summary>
    public static bool TryGetTagWrite(
        IInvocationOperation invocation,
        [NotNullWhen(true)] out string? key,
        [NotNullWhen(true)] out IArgumentOperation? valueArgument)
    {
        if (IsTagSetterInvocation(invocation))
        {
            if (TryGetTagSetterKeyArgument(invocation, out var keyArgument)
                && TryGetNonEmptyStringConstant(keyArgument.Value, out key)
                && TryGetTagSetterValueArgument(invocation, out valueArgument))
            {
                return true;
            }

            key = null;
            valueArgument = null;
            return false;
        }

        if (TryGetTypedSetter(invocation, out var setter))
        {
            // Extension setters carry the activity as parameter 0: (activity, value) or
            // (activity, segment, value) for a template attribute.
            var ordinal = setter.IsTemplate ? 2 : 1;
            valueArgument = invocation.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == ordinal);
            if (valueArgument is null)
            {
                key = null;
                return false;
            }

            if (!setter.IsTemplate)
            {
                key = setter.Key;
                return true;
            }

            var segment = invocation.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == 1);
            if (segment is not null && TryGetNonEmptyStringConstant(segment.Value, out var segmentValue))
            {
                key = setter.Key + "." + segmentValue;
                return true;
            }
        }

        key = null;
        valueArgument = null;
        return false;
    }

    /// <summary>
    /// The constant a value expression carries: a constant string, a value-set struct member
    /// (<c>DbSystemName.Postgresql</c>), its <c>.Value</c>, or <c>Custom("literal")</c>.
    /// </summary>
    public static bool TryGetValueConstant(IOperation operation, [NotNullWhen(true)] out string? value)
    {
        var unwrapped = operation.UnwrapAllConversions();
        if (TryGetStringConstant(unwrapped, out value))
        {
            return true;
        }

        switch (unwrapped)
        {
            case IPropertyReferenceOperation { Property.Name: "Value", Instance: { } instance }
                when TryGetValueSetKey(instance.Type, out _):
                return TryGetValueConstant(instance, out value);

            case IPropertyReferenceOperation { Property: { IsStatic: true } property }
                when TryGetValueSetKey(property.ContainingType, out var key)
                     && SemconvTypedSurface.ValueSetMembers.TryGetValue(key, out var members)
                     && members.TryGetValue(property.Name, out var member):
                value = member;
                return true;

            case IInvocationOperation { TargetMethod: { Name: "Custom", IsStatic: true } custom } invocation
                when TryGetValueSetKey(custom.ContainingType, out _)
                     && invocation.Arguments.Length == 1
                     && invocation.Arguments[0].Value.UnwrapAllConversions().ConstantValue is { HasValue: true, Value: { } constant }:
                value = Convert.ToString(constant, CultureInfo.InvariantCulture);
                return value is not null;
        }

        value = null;
        return false;
    }

    /// <summary>
    /// The attribute key of a generated value-set struct (its <c>Key</c> constant), when
    /// <paramref name="type"/> is one.
    /// </summary>
    public static bool TryGetValueSetKey(ITypeSymbol? type, [NotNullWhen(true)] out string? key)
    {
        if (type is INamedTypeSymbol { TypeKind: TypeKind.Struct } named
            && IsActivitiesNamespace(named.ContainingNamespace)
            && named.GetMembers("Key").OfType<IFieldSymbol>().FirstOrDefault() is { IsConst: true, ConstantValue: string constantKey }
            && SemconvTypedSurface.ValueSetMembers.ContainsKey(constantKey))
        {
            key = constantKey;
            return true;
        }

        key = null;
        return false;
    }

    /// <summary>The <c>Qyl.Spans</c>/<c>Qyl.IncubatingSpans</c> starter <paramref name="invocation"/> calls, if any.</summary>
    public static bool TryGetStarter(IInvocationOperation invocation, [NotNullWhen(true)] out string? starter)
    {
        var method = invocation.TargetMethod;
        if (method is { IsStatic: true, ContainingType: { ContainingNamespace: { Name: "Qyl" } ns } type } && ns.ContainingNamespace.IsGlobalNamespace)
        {
            var name = "Qyl." + type.Name + "." + method.Name;
            if (SemconvTypedSurface.StarterParameters.ContainsKey(name))
            {
                starter = name;
                return true;
            }
        }

        starter = null;
        return false;
    }

    public static bool TryGetNonEmptyStringConstant(IOperation operation, [NotNullWhen(true)] out string? value)
    {
        if (!TryGetStringConstant(operation, out value) || string.IsNullOrEmpty(value))
        {
            value = null;
            return false;
        }

        return true;
    }

    public static bool TryGetStringConstant(IOperation operation, [NotNullWhen(true)] out string? value)
    {
        if (operation.UnwrapImplicitConversions().TryGetConstantValue<string>(out var constantValue))
        {
            value = constantValue;
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>
    /// Walks an operation tree and yields every attribute write whose key is known: raw calls with a
    /// constant key, typed setters, and starter arguments plus the attributes a starter sets itself.
    /// The value is captured when it is a constant.
    /// </summary>
    public static void CollectTagSetterCalls(
        IOperation root,
        List<TagSetterCall> sink)
    {
        foreach (var descendant in root.DescendantsAndSelf())
        {
            if (descendant is not IInvocationOperation invocation)
            {
                continue;
            }

            if (TryGetTagWrite(invocation, out var key, out var valueArgument))
            {
                var anchor = IsTagSetterInvocation(invocation) && TryGetTagSetterKeyArgument(invocation, out var keyArgument)
                    ? keyArgument.Syntax.GetLocation()
                    : valueArgument.Syntax.GetLocation();
                sink.Add(new TagSetterCall(key, TryGetValueConstant(valueArgument.Value, out var value) ? value : null, anchor));
                continue;
            }

            if (!TryGetStarter(invocation, out var starter))
            {
                continue;
            }

            var parameters = SemconvTypedSurface.StarterParameters[starter];
            foreach (var argument in invocation.Arguments)
            {
                if (argument.Parameter is { } parameter
                    && parameters.TryGetValue(parameter.Name, out var parameterKey)
                    && !argument.Value.UnwrapAllConversions().IsNullLiteral()
                    && !(argument.IsImplicit || argument.ArgumentKind == ArgumentKind.DefaultValue))
                {
                    sink.Add(new TagSetterCall(
                        parameterKey,
                        TryGetValueConstant(argument.Value, out var value) ? value : null,
                        argument.Syntax.GetLocation()));
                }
            }

            foreach (var fixedAttribute in SemconvTypedSurface.StarterFixedAttributes[starter])
            {
                sink.Add(new TagSetterCall(fixedAttribute.Key, fixedAttribute.Value, invocation.Syntax.GetLocation()));
            }
        }
    }

    private static bool TryGetTypedSetter(IInvocationOperation invocation, [NotNullWhen(true)] out SemconvTypedSetter? setter)
    {
        var method = (invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod).OriginalDefinition;
        if (method is { IsExtensionMethod: true, Arity: 0, ContainingType: { } type }
            && IsActivitiesNamespace(type.ContainingNamespace)
            && SemconvTypedSurface.Setters.TryGetValue((
                type.ToDisplayString(),
                method.Name,
                string.Join(",", method.Parameters.Select(static parameter => parameter.Type.ToDisplayString(SetterTypeFormat)))), out setter))
        {
            return true;
        }

        setter = null;
        return false;
    }

    private static readonly SymbolDisplayFormat SetterTypeFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

    private static bool IsActivitiesNamespace(INamespaceSymbol? ns)
    {
        var name = ns?.ToDisplayString();
        return name is ActivitiesNamespace or IncubatingActivitiesNamespace;
    }

    private static bool IsNullLiteral(this IOperation operation) =>
        operation.ConstantValue is { HasValue: true, Value: null };
}

/// <summary>An attribute write with its key, its constant value when it has one, and where it is.</summary>
/// <param name="Key">The attribute key.</param>
/// <param name="Value">The constant value, when the write has one.</param>
/// <param name="KeyLocation">The syntax location of the write (suitable for diagnostic reports).</param>
internal readonly record struct TagSetterCall(string Key, string? Value, Location KeyLocation);
