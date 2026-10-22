namespace Qyl.Telemetry.SemanticConventions.Analyzers;

/// <summary>
/// QYL0013: Detects telemetry attributes whose statically known value type contradicts
/// the type in the complete pinned semantic-convention registry.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class Qyl0013IncorrectAttributeTypeAnalyzer : AlAnalyzer
{
    private const string DiagnosticId = "QYL0013";

    private static readonly DiagnosticDescriptor Rule = CreateRule(
        DiagnosticId,
        DiagnosticCategories.OpenTelemetry,
        DiagnosticSeverity.Warning);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <inheritdoc />
    protected override void InitializeCore(AnalysisContext context) =>
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (!TagSetterDetection.IsTagSetterInvocation(invocation)
            || !TagSetterDetection.TryGetTagSetterKeyArgument(invocation, out var keyArgument)
            || !TagSetterDetection.TryGetTagSetterValueArgument(invocation, out var valueArgument)
            || !TagSetterDetection.TryGetNonEmptyStringConstant(keyArgument.Value, out var attributeName)
            || !SemconvRegistryFacts.TryGetAttributeType(attributeName, out var expectedKind)
            || expectedKind is RegistryAttributeValueKind.Any or RegistryAttributeValueKind.Unknown)
        {
            return;
        }

        var value = valueArgument.Value.UnwrapAllConversions();
        if (value.Type is null || IsTypeMatch(value.Type.GetUnderlyingNullableTypeOrSelf(), expectedKind))
        {
            return;
        }

        context.ReportDiagnostic(
            Rule,
            valueArgument.Syntax.GetLocation(),
            attributeName,
            GetTypeName(expectedKind),
            value.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
    }

    private static bool IsTypeMatch(ITypeSymbol actualType, RegistryAttributeValueKind expectedKind) =>
        expectedKind switch
        {
            RegistryAttributeValueKind.String => actualType.IsString(),
            RegistryAttributeValueKind.Integer => IsIntegerType(actualType),
            RegistryAttributeValueKind.Double => IsFloatingPointType(actualType),
            RegistryAttributeValueKind.Boolean => actualType.IsBoolean(),
            RegistryAttributeValueKind.StringArray => HasSequenceElementType(actualType, TypeSymbolExtensions.IsString),
            RegistryAttributeValueKind.IntegerArray => HasSequenceElementType(actualType, IsIntegerType),
            RegistryAttributeValueKind.DoubleArray => HasSequenceElementType(actualType, IsFloatingPointType),
            RegistryAttributeValueKind.BooleanArray => HasSequenceElementType(actualType, TypeSymbolExtensions.IsBoolean),
            _ => true,
        };

    private static bool IsIntegerType(ITypeSymbol type) =>
        type.SpecialType is
            SpecialType.System_SByte or
            SpecialType.System_Byte or
            SpecialType.System_Int16 or
            SpecialType.System_UInt16 or
            SpecialType.System_Int32 or
            SpecialType.System_UInt32 or
            SpecialType.System_Int64 or
            SpecialType.System_UInt64;

    private static bool IsFloatingPointType(ITypeSymbol type) =>
        type.SpecialType is
            SpecialType.System_Single or
            SpecialType.System_Double or
            SpecialType.System_Decimal;

    private static bool HasSequenceElementType(ITypeSymbol type, Func<ITypeSymbol, bool> predicate)
    {
        if (type is IArrayTypeSymbol array)
        {
            return predicate(array.ElementType);
        }

        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        if (TryGetEnumerableElement(named, out var directElement) && predicate(directElement))
        {
            return true;
        }

        foreach (var contract in named.AllInterfaces)
        {
            if (TryGetEnumerableElement(contract, out var element) && predicate(element))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetEnumerableElement(
        INamedTypeSymbol type,
        [NotNullWhen(true)] out ITypeSymbol? element)
    {
        if (type is
            {
                Name: "IEnumerable",
                Arity: 1,
                ContainingNamespace.Name: "Generic",
                ContainingNamespace.ContainingNamespace.Name: "Collections",
                ContainingNamespace.ContainingNamespace.ContainingNamespace.Name: "System",
            })
        {
            element = type.TypeArguments[0];
            return true;
        }

        element = null;
        return false;
    }

    private static string GetTypeName(RegistryAttributeValueKind expectedKind) =>
        expectedKind switch
        {
            RegistryAttributeValueKind.String => "string",
            RegistryAttributeValueKind.Integer => "integer",
            RegistryAttributeValueKind.Double => "double",
            RegistryAttributeValueKind.Boolean => "boolean",
            RegistryAttributeValueKind.StringArray => "string[]",
            RegistryAttributeValueKind.IntegerArray => "integer[]",
            RegistryAttributeValueKind.DoubleArray => "double[]",
            RegistryAttributeValueKind.BooleanArray => "boolean[]",
            _ => "unknown",
        };
}
