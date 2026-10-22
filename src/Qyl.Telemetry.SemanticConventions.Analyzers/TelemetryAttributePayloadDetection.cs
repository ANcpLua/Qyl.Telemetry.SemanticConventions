// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

namespace Qyl.Telemetry.SemanticConventions.Analyzers;

/// <summary>The telemetry surface a detected attribute payload reaches.</summary>
internal enum TelemetryPayloadSink
{
    /// <summary>A key/value pair or dictionary entry that never provably reaches telemetry.</summary>
    None,

    /// <summary>
    /// <c>SetTag</c>/<c>AddTag</c>/<c>SetAttribute</c>/<c>AddAttribute</c>, or a
    /// <c>TagList</c> / <c>ActivityTagsCollection</c> entry that does not flow anywhere more specific.
    /// </summary>
    TagSetter,

    /// <summary><c>SetBaggage</c>/<c>AddBaggage</c>.</summary>
    Baggage,

    /// <summary><c>Counter.Add</c>, <c>Histogram.Record</c>, <c>UpDownCounter.Add</c> tags, or a <c>Measurement</c>.</summary>
    MetricMeasurement,

    /// <summary><c>ActivitySource.StartActivity(tags:)</c>.</summary>
    ActivityTags,

    /// <summary><c>ILogger.BeginScope</c> state or <c>ILogger.Log</c> state.</summary>
    LoggerState,

    /// <summary><c>ResourceBuilder.AddAttributes</c>.</summary>
    ResourceAttributes,

    /// <summary><c>ActivityEvent</c> tags.</summary>
    ActivityEvent,

    /// <summary><c>ActivityLink</c> tags.</summary>
    ActivityLink,
}

/// <summary>
/// The locals of one operation block that are passed to a known telemetry sink, computed once
/// per block on first use. Every payload literal in the block then answers "does this dictionary
/// or tag collection reach telemetry, and where?" with a lookup instead of a walk over the whole
/// block, which is what the previous per-literal <c>DescendantsAndSelf</c> scan cost.
/// </summary>
internal sealed class TelemetryPayloadFlowScope(ImmutableArray<IOperation> operationBlocks)
{
    private readonly Lazy<ImmutableDictionary<ILocalSymbol, TelemetryPayloadSink>> _sinkByLocal = new(
        () => Build(operationBlocks),
        LazyThreadSafetyMode.ExecutionAndPublication);

    public bool TryGetSink(ILocalSymbol local, out TelemetryPayloadSink sink) =>
        _sinkByLocal.Value.TryGetValue(local, out sink);

    private static ImmutableDictionary<ILocalSymbol, TelemetryPayloadSink> Build(
        ImmutableArray<IOperation> operationBlocks)
    {
        var builder = ImmutableDictionary.CreateBuilder<ILocalSymbol, TelemetryPayloadSink>(
            SymbolEqualityComparer.Default);

        foreach (var block in operationBlocks)
        {
            foreach (var descendant in block.DescendantsAndSelf())
            {
                if (descendant is IArgumentOperation argument
                    && TelemetryAttributePayloadDetection.TryGetLocalReference(argument.Value, out var local)
                    && TelemetryAttributePayloadDetection.TryGetSinkForArgument(argument, out var sink)
                    && !builder.ContainsKey(local))
                {
                    builder.Add(local, sink);
                }
            }
        }

        return builder.ToImmutable();
    }
}

internal static class TelemetryAttributePayloadDetection
{
    private static readonly ImmutableHashSet<string> BaggageMethodNames =
    [
        "SetBaggage",
        "AddBaggage",
    ];

    /// <summary>
    /// Registers the four payload-producing operation actions (invocation, object creation,
    /// local declarator, indexer assignment) per operation block and routes every detected
    /// literal payload to <paramref name="onPayload"/>. The block-level registration is what
    /// lets the local-flow set be built once per block.
    /// </summary>
    public static void RegisterPayloadAnalysis(
        CompilationStartAnalysisContext context,
        Action<OperationAnalysisContext, TelemetryAttributePayloadLiteral> onPayload)
    {
        context.RegisterOperationBlockStartAction(blockContext =>
        {
            var scope = new TelemetryPayloadFlowScope(blockContext.OperationBlocks);
            blockContext.RegisterOperationAction(
                ctx => AnalyzeInvocation((IInvocationOperation)ctx.Operation, scope, payload => onPayload(ctx, payload)),
                OperationKind.Invocation);
            blockContext.RegisterOperationAction(
                ctx => AnalyzeObjectCreation((IObjectCreationOperation)ctx.Operation, scope, payload => onPayload(ctx, payload)),
                OperationKind.ObjectCreation);
            blockContext.RegisterOperationAction(
                ctx => AnalyzeVariableDeclarator((IVariableDeclaratorOperation)ctx.Operation, scope, payload => onPayload(ctx, payload)),
                OperationKind.VariableDeclarator);
            blockContext.RegisterOperationAction(
                ctx => AnalyzeAssignment((ISimpleAssignmentOperation)ctx.Operation, scope, payload => onPayload(ctx, payload)),
                OperationKind.SimpleAssignment);
        });
    }

    public static void AnalyzeInvocation(
        IInvocationOperation invocation,
        TelemetryPayloadFlowScope scope,
        Action<TelemetryAttributePayloadLiteral> report)
    {
        if (TagSetterDetection.IsTagSetterInvocation(invocation))
        {
            AnalyzeKeyValueArguments(invocation.Arguments, invocation.TargetMethod.IsExtensionMethod, WithSink(report, TelemetryPayloadSink.TagSetter));
        }
        else if (BaggageMethodNames.Contains(invocation.TargetMethod.Name))
        {
            AnalyzeKeyValueArguments(invocation.Arguments, invocation.TargetMethod.IsExtensionMethod, WithSink(report, TelemetryPayloadSink.Baggage));
        }
        else if (invocation.TargetMethod.Name == "Add"
            && IsTelemetryTagCollection(ReceiverType(invocation))
            && !IsInsideKnownTelemetryAttributePayload(invocation, scope))
        {
            // A TagList built up by Add calls and then handed to a metric or a span reports as
            // that sink; one that never leaves the method is still a tag setter.
            var sink = ResolveCollectionSink(invocation.Instance, scope, TelemetryPayloadSink.TagSetter);
            AnalyzeKeyValueArguments(invocation.Arguments, invocation.TargetMethod.IsExtensionMethod, WithSink(report, sink));
        }

        if (IsMetricMeasurementInvocation(invocation))
        {
            AnalyzeArgumentsAfterFirst(
                invocation.Arguments,
                invocation.TargetMethod.IsExtensionMethod,
                WithSink(report, TelemetryPayloadSink.MetricMeasurement));
        }

        if (IsActivitySourceStartActivity(invocation)
            && TryGetArgumentByNameOrOrdinal(invocation.Arguments, "tags", 3, out var startActivityTagsArgument))
        {
            AnalyzePayload(startActivityTagsArgument.Value, WithSink(report, TelemetryPayloadSink.ActivityTags));
            return;
        }

        if (IsLoggerBeginScope(invocation)
            && TryGetArgumentByOrdinal(invocation.Arguments, invocation.TargetMethod.IsExtensionMethod, 0, out var scopeStateArgument))
        {
            AnalyzePayload(scopeStateArgument.Value, WithSink(report, TelemetryPayloadSink.LoggerState));
            return;
        }

        if (IsLoggerLog(invocation)
            && TryGetArgumentByNameOrOrdinal(invocation.Arguments, "state", 2, out var logStateArgument))
        {
            AnalyzePayload(logStateArgument.Value, WithSink(report, TelemetryPayloadSink.LoggerState));
            return;
        }

        if (IsResourceBuilderAddAttributes(invocation)
            && TryGetArgumentByOrdinal(invocation.Arguments, invocation.TargetMethod.IsExtensionMethod, 0, out var attributesArgument))
        {
            AnalyzePayload(attributesArgument.Value, WithSink(report, TelemetryPayloadSink.ResourceAttributes));
            return;
        }

        if (invocation.TargetMethod.Name == "Add"
            && IsStringKeyDictionary(ReceiverType(invocation))
            && !IsInsideKnownTelemetryAttributePayload(invocation, scope))
        {
            var sink = ResolveCollectionSink(invocation.Instance, scope, TelemetryPayloadSink.None);
            AnalyzeKeyValueArguments(invocation.Arguments, invocation.TargetMethod.IsExtensionMethod, WithSink(report, sink));
        }
    }

    public static void AnalyzeObjectCreation(
        IObjectCreationOperation objectCreation,
        TelemetryPayloadFlowScope scope,
        Action<TelemetryAttributePayloadLiteral> report)
    {
        if (IsKeyValuePairStringObject(objectCreation.Type)
            && !IsInsideKnownTelemetryAttributePayload(objectCreation, scope))
        {
            AnalyzeKeyValuePairCreation(objectCreation, report);
        }

        if (IsMetricMeasurementCreation(objectCreation.Type))
        {
            AnalyzeArgumentsAfterFirst(
                objectCreation.Arguments,
                extensionMethod: false,
                WithSink(report, TelemetryPayloadSink.MetricMeasurement));
        }

        if (IsActivityEventCreation(objectCreation.Type)
            && TryGetArgumentByNameOrOrdinal(objectCreation.Arguments, "tags", 2, out var tagsArgument))
        {
            AnalyzePayload(tagsArgument.Value, WithSink(report, TelemetryPayloadSink.ActivityEvent));
        }

        if (IsActivityLinkCreation(objectCreation.Type)
            && TryGetArgumentByNameOrOrdinal(objectCreation.Arguments, "tags", 1, out var linkTagsArgument))
        {
            AnalyzePayload(linkTagsArgument.Value, WithSink(report, TelemetryPayloadSink.ActivityLink));
        }
    }

    /// <summary>
    /// A local whose initializer is a dictionary, array, collection expression or tag
    /// collection, and which later reaches a telemetry sink, is analyzed as that sink's
    /// payload. The per-element actions skip anything under such an initializer so no
    /// literal reports twice.
    /// </summary>
    public static void AnalyzeVariableDeclarator(
        IVariableDeclaratorOperation declarator,
        TelemetryPayloadFlowScope scope,
        Action<TelemetryAttributePayloadLiteral> report)
    {
        if (declarator.Initializer?.Value is { } initializerValue
            && scope.TryGetSink(declarator.Symbol, out var sink))
        {
            AnalyzePayload(initializerValue, WithSink(report, sink));
        }
    }

    public static void AnalyzeAssignment(
        ISimpleAssignmentOperation assignment,
        TelemetryPayloadFlowScope scope,
        Action<TelemetryAttributePayloadLiteral> report)
    {
        if (assignment.Target.UnwrapImplicitConversions() is not IPropertyReferenceOperation propertyReference)
        {
            return;
        }

        var receiverType = propertyReference.Instance?.Type ?? propertyReference.Property.ContainingType;
        if (IsTelemetryTagCollection(receiverType))
        {
            if (!IsInsideKnownTelemetryAttributePayload(assignment, scope))
            {
                var sink = ResolveCollectionSink(propertyReference.Instance, scope, TelemetryPayloadSink.TagSetter);
                AnalyzeIndexerAssignment(assignment, WithSink(report, sink));
            }

            return;
        }

        if (IsStringKeyDictionary(receiverType)
            && TryGetLocalSink(propertyReference.Instance, scope, out var dictionarySink))
        {
            AnalyzeIndexerAssignment(assignment, WithSink(report, dictionarySink));
        }
    }

    /// <summary>
    /// Classifies an argument by the sink its parent call feeds: the <c>tags</c> of
    /// <c>StartActivity</c>, the state of a logger call, the attributes of a resource builder,
    /// the tags after a metric measurement's value, or the tags of an event or link.
    /// </summary>
    internal static bool TryGetSinkForArgument(IArgumentOperation argument, out TelemetryPayloadSink sink)
    {
        switch (argument.Parent)
        {
            case IInvocationOperation invocation:
                if (IsResourceBuilderAddAttributes(invocation)
                    && IsLogicalArgument(argument, invocation.TargetMethod.IsExtensionMethod, 0))
                {
                    sink = TelemetryPayloadSink.ResourceAttributes;
                    return true;
                }

                if (IsMetricMeasurementInvocation(invocation)
                    && IsAfterFirstLogicalArgument(argument, invocation.TargetMethod.IsExtensionMethod))
                {
                    sink = TelemetryPayloadSink.MetricMeasurement;
                    return true;
                }

                if (IsActivitySourceTagsArgument(invocation, argument))
                {
                    sink = TelemetryPayloadSink.ActivityTags;
                    return true;
                }

                if (IsLoggerPayloadArgument(invocation, argument))
                {
                    sink = TelemetryPayloadSink.LoggerState;
                    return true;
                }

                break;

            case IObjectCreationOperation objectCreation:
                if (IsMetricMeasurementCreation(objectCreation.Type)
                    && IsAfterFirstLogicalArgument(argument, extensionMethod: false))
                {
                    sink = TelemetryPayloadSink.MetricMeasurement;
                    return true;
                }

                if (IsActivityEventTagsArgument(objectCreation, argument))
                {
                    sink = TelemetryPayloadSink.ActivityEvent;
                    return true;
                }

                if (IsActivityLinkTagsArgument(objectCreation, argument))
                {
                    sink = TelemetryPayloadSink.ActivityLink;
                    return true;
                }

                break;
        }

        sink = TelemetryPayloadSink.None;
        return false;
    }

    private static void AnalyzePayload(
        IOperation operation,
        Action<TelemetryAttributePayloadLiteral> report)
    {
        var unwrapped = operation.UnwrapImplicitConversions();

        if (unwrapped is IArrayCreationOperation arrayCreation)
        {
            if (arrayCreation.Initializer is not null)
            {
                AnalyzeArrayInitializer(arrayCreation.Initializer, report);
            }

            return;
        }

        if (unwrapped is IArrayInitializerOperation arrayInitializer)
        {
            AnalyzeArrayInitializer(arrayInitializer, report);
            return;
        }

        if (unwrapped is ICollectionExpressionOperation collectionExpression)
        {
            AnalyzeCollectionExpressionElements(collectionExpression, report);
            return;
        }

        if (unwrapped is IObjectCreationOperation objectCreation)
        {
            if (IsKeyValuePairStringObject(objectCreation.Type))
            {
                AnalyzeKeyValuePairCreation(objectCreation, report);
            }

            if (IsTelemetryTagCollection(objectCreation.Type))
            {
                foreach (var argument in objectCreation.Arguments)
                {
                    AnalyzePayload(argument.Value, report);
                }
            }

            if (objectCreation.Initializer is not null)
            {
                AnalyzeObjectInitializer(objectCreation.Initializer, report);
            }

            return;
        }

        if (unwrapped is IInvocationOperation { TargetMethod.Name: "Add" } invocation)
        {
            AnalyzeKeyValueArguments(invocation.Arguments, invocation.TargetMethod.IsExtensionMethod, report);
            return;
        }

        if (unwrapped is ISimpleAssignmentOperation assignment)
        {
            AnalyzeIndexerAssignment(assignment, report);
        }
    }

    private static void AnalyzeArrayInitializer(
        IArrayInitializerOperation initializer,
        Action<TelemetryAttributePayloadLiteral> report)
    {
        foreach (var element in initializer.ElementValues)
        {
            AnalyzePayload(element, report);
        }
    }

    private static void AnalyzeObjectInitializer(
        IObjectOrCollectionInitializerOperation initializer,
        Action<TelemetryAttributePayloadLiteral> report)
    {
        foreach (var initializerOperation in initializer.Initializers)
        {
            AnalyzePayload(initializerOperation, report);
        }
    }

    private static void AnalyzeCollectionExpressionElements(
        ICollectionExpressionOperation collectionExpression,
        Action<TelemetryAttributePayloadLiteral> report)
    {
        foreach (var element in collectionExpression.Elements)
        {
            if (element is ISpreadOperation spread)
            {
                AnalyzePayload(spread.Operand, report);
                continue;
            }

            AnalyzePayload(element, report);
        }
    }

    private static void AnalyzeKeyValuePairCreation(
        IObjectCreationOperation objectCreation,
        Action<TelemetryAttributePayloadLiteral> report)
    {
        if (!TryGetArgumentByOrdinal(objectCreation.Arguments, extensionMethod: false, 0, out var keyArgument)
            || !TryGetArgumentByOrdinal(objectCreation.Arguments, extensionMethod: false, 1, out var valueArgument)
            || !TryGetKey(keyArgument.Value, out var key, out var keySyntax, out var keyIsBareLiteral))
        {
            return;
        }

        ReadValue(valueArgument.Value, out var value, out var valueSyntax, out var valueIsBareLiteral);
        report(new TelemetryAttributePayloadLiteral(
            key,
            keySyntax,
            keyIsBareLiteral,
            value,
            valueSyntax,
            valueIsBareLiteral));
    }

    /// <summary>
    /// Reads a (key, value) argument pair: <c>SetTag(key, value)</c>, <c>TagList.Add(key, value)</c>,
    /// <c>Dictionary.Add(key, value)</c> and the collection-initializer form of the last two.
    /// </summary>
    private static void AnalyzeKeyValueArguments(
        ImmutableArray<IArgumentOperation> arguments,
        bool extensionMethod,
        Action<TelemetryAttributePayloadLiteral> report)
    {
        if (!TryGetArgumentByOrdinal(arguments, extensionMethod, 0, out var keyArgument)
            || !TryGetKey(keyArgument.Value, out var key, out var keySyntax, out var keyIsBareLiteral))
        {
            return;
        }

        string? value = null;
        SyntaxNode? valueSyntax = null;
        var valueIsBareLiteral = false;
        if (TryGetArgumentByOrdinal(arguments, extensionMethod, 1, out var valueArgument))
        {
            ReadValue(valueArgument.Value, out value, out valueSyntax, out valueIsBareLiteral);
        }

        report(new TelemetryAttributePayloadLiteral(
            key,
            keySyntax,
            keyIsBareLiteral,
            value,
            valueSyntax,
            valueIsBareLiteral));
    }

    private static void AnalyzeIndexerAssignment(
        ISimpleAssignmentOperation assignment,
        Action<TelemetryAttributePayloadLiteral> report)
    {
        if (!TryGetIndexerKey(assignment.Target, out var key, out var keySyntax, out var keyIsBareLiteral))
        {
            return;
        }

        ReadValue(assignment.Value, out var value, out var valueSyntax, out var valueIsBareLiteral);
        report(new TelemetryAttributePayloadLiteral(
            key,
            keySyntax,
            keyIsBareLiteral,
            value,
            valueSyntax,
            valueIsBareLiteral));
    }

    private static void AnalyzeArgumentsAfterFirst(
        ImmutableArray<IArgumentOperation> arguments,
        bool extensionMethod,
        Action<TelemetryAttributePayloadLiteral> report)
    {
        for (var i = 0; i < arguments.Length; i++)
        {
            var argument = arguments[i];
            var logicalOrdinal = argument.Parameter is null
                ? i
                : argument.Parameter.Ordinal - (extensionMethod ? 1 : 0);
            if (logicalOrdinal <= 0 && i == 0)
            {
                continue;
            }

            AnalyzePayload(argument.Value, report);
        }
    }

    private static bool TryGetKey(
        IOperation operation,
        [NotNullWhen(true)] out string? key,
        [NotNullWhen(true)] out SyntaxNode? syntax,
        out bool isBareLiteral)
    {
        var unwrapped = operation.UnwrapImplicitConversions();
        isBareLiteral = unwrapped.Syntax is LiteralExpressionSyntax;
        if (TagSetterDetection.TryGetNonEmptyStringConstant(unwrapped, out key))
        {
            syntax = unwrapped.Syntax;
            return true;
        }

        syntax = null;
        return false;
    }

    private static void ReadValue(
        IOperation operation,
        out string? value,
        out SyntaxNode? syntax,
        out bool isBareLiteral)
    {
        var unwrapped = operation.UnwrapImplicitConversions();
        isBareLiteral = unwrapped.Syntax is LiteralExpressionSyntax;
        syntax = TagSetterDetection.TryGetStringConstant(unwrapped, out value) ? unwrapped.Syntax : null;
    }

    private static Action<TelemetryAttributePayloadLiteral> WithSink(
        Action<TelemetryAttributePayloadLiteral> report,
        TelemetryPayloadSink sink) =>
        payload => report(payload with { Sink = sink });

    private static bool TryGetIndexerKey(
        IOperation operation,
        [NotNullWhen(true)] out string? key,
        [NotNullWhen(true)] out SyntaxNode? syntax,
        out bool isBareLiteral)
    {
        var unwrapped = operation.UnwrapImplicitConversions();
        if (unwrapped is IPropertyReferenceOperation propertyReference)
        {
            foreach (var argument in propertyReference.Arguments)
            {
                if (IsLogicalArgument(argument, extensionMethod: false, 0)
                    && TryGetKey(argument.Value, out key, out syntax, out isBareLiteral))
                {
                    return true;
                }
            }
        }

        key = null;
        syntax = null;
        isBareLiteral = false;
        return false;
    }

    internal static bool TryGetArgumentByOrdinal(
        ImmutableArray<IArgumentOperation> arguments,
        bool extensionMethod,
        int logicalParameterOrdinal,
        [NotNullWhen(true)] out IArgumentOperation? argument)
    {
        // Extension methods expose the receiver as parameter 0. Prefer Roslyn's
        // parameter binding so named/reordered arguments still resolve correctly.
        var parameterOrdinal = extensionMethod ? logicalParameterOrdinal + 1 : logicalParameterOrdinal;
        foreach (var candidate in arguments)
        {
            if (candidate.Parameter?.Ordinal == parameterOrdinal)
            {
                argument = candidate;
                return true;
            }
        }

        var fallbackIndex = arguments.Length > parameterOrdinal ? parameterOrdinal : logicalParameterOrdinal;
        if (arguments.Length > fallbackIndex)
        {
            argument = arguments[fallbackIndex];
            return true;
        }

        argument = null;
        return false;
    }

    internal static bool TryGetBareStringLiteral(
        IOperation operation,
        [NotNullWhen(true)] out string? value,
        [NotNullWhen(true)] out LiteralExpressionSyntax? syntax)
    {
        var unwrapped = operation.UnwrapImplicitConversions();
        if (unwrapped.Syntax is LiteralExpressionSyntax literal
            && TagSetterDetection.TryGetNonEmptyStringConstant(unwrapped, out value))
        {
            syntax = literal;
            return true;
        }

        value = null;
        syntax = null;
        return false;
    }

    private static bool TryGetArgumentByNameOrOrdinal(
        ImmutableArray<IArgumentOperation> arguments,
        string parameterName,
        int parameterOrdinal,
        [NotNullWhen(true)] out IArgumentOperation? argument)
    {
        foreach (var candidate in arguments)
        {
            if (string.Equals(candidate.Parameter?.Name, parameterName, StringComparison.Ordinal))
            {
                argument = candidate;
                return true;
            }
        }

        return TryGetArgumentByOrdinal(arguments, extensionMethod: false, parameterOrdinal, out argument);
    }

    private static bool IsLogicalArgument(
        IArgumentOperation argument,
        bool extensionMethod,
        int logicalParameterOrdinal)
    {
        var parameterOrdinal = extensionMethod ? logicalParameterOrdinal + 1 : logicalParameterOrdinal;
        return argument.Parameter?.Ordinal == parameterOrdinal;
    }

    private static bool IsAfterFirstLogicalArgument(
        IArgumentOperation argument,
        bool extensionMethod)
    {
        if (argument.Parameter is null)
        {
            return false;
        }

        return argument.Parameter.Ordinal - (extensionMethod ? 1 : 0) > 0;
    }

    private static bool IsResourceBuilderAddAttributes(IInvocationOperation invocation)
    {
        if (invocation.TargetMethod.Name != "AddAttributes")
        {
            return false;
        }

        if (invocation.TargetMethod.ContainingType.ToDisplayString() == "OpenTelemetry.Resources.ResourceBuilder")
        {
            return true;
        }

        return invocation.TargetMethod is { IsExtensionMethod: true, Parameters.Length: > 0 }
               && invocation.TargetMethod.Parameters[0].Type.ToDisplayString() == "OpenTelemetry.Resources.ResourceBuilder";
    }

    private static bool IsMetricMeasurementInvocation(IInvocationOperation invocation) =>
        invocation.TargetMethod.Name is "Add" or "Record"
        && IsMetricInstrument(invocation.TargetMethod.ContainingType);

    /// <summary>
    /// True when the operation sits under a sink argument or under the initializer of a local
    /// that reaches a sink. Those payloads are reported from the sink side, so the per-element
    /// actions must not report them again.
    /// </summary>
    private static bool IsInsideKnownTelemetryAttributePayload(IOperation operation, TelemetryPayloadFlowScope scope)
    {
        if (IsInitializerOfSinkLocal(operation, scope))
        {
            return true;
        }

        for (var current = operation.Parent; current is not null; current = current.Parent)
        {
            if (current is IArgumentOperation argument && TryGetSinkForArgument(argument, out _))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInitializerOfSinkLocal(
        IOperation operation,
        TelemetryPayloadFlowScope scope)
    {
        for (var current = operation.Parent; current is not null; current = current.Parent)
        {
            if (current is IVariableInitializerOperation
                && current.Parent is IVariableDeclaratorOperation { Symbol: { } local })
            {
                return scope.TryGetSink(local, out _);
            }
        }

        return false;
    }

    private static TelemetryPayloadSink ResolveCollectionSink(
        IOperation? instance,
        TelemetryPayloadFlowScope scope,
        TelemetryPayloadSink fallback) =>
        TryGetLocalSink(instance, scope, out var sink) ? sink : fallback;

    private static bool TryGetLocalSink(
        IOperation? instance,
        TelemetryPayloadFlowScope scope,
        out TelemetryPayloadSink sink)
    {
        if (TryGetLocalReference(instance, out var local))
        {
            return scope.TryGetSink(local, out sink);
        }

        sink = TelemetryPayloadSink.None;
        return false;
    }

    internal static bool TryGetLocalReference(
        IOperation? operation,
        [NotNullWhen(true)] out ILocalSymbol? local)
    {
        if (operation is not null
            && operation.UnwrapImplicitConversions() is ILocalReferenceOperation localReference)
        {
            local = localReference.Local;
            return true;
        }

        local = null;
        return false;
    }

    private static ITypeSymbol? ReceiverType(IInvocationOperation invocation) =>
        invocation.Instance?.Type ?? invocation.TargetMethod.ContainingType;

    private static bool IsActivityEventTagsArgument(
        IObjectCreationOperation objectCreation,
        IArgumentOperation argument) =>
        IsActivityEventCreation(objectCreation.Type)
        && (string.Equals(argument.Parameter?.Name, "tags", StringComparison.Ordinal)
            || argument.Parameter?.Ordinal == 2);

    private static bool IsActivityLinkTagsArgument(
        IObjectCreationOperation objectCreation,
        IArgumentOperation argument) =>
        IsActivityLinkCreation(objectCreation.Type)
        && (string.Equals(argument.Parameter?.Name, "tags", StringComparison.Ordinal)
            || argument.Parameter?.Ordinal == 1);

    private static bool IsActivitySourceTagsArgument(
        IInvocationOperation invocation,
        IArgumentOperation argument) =>
        IsActivitySourceStartActivity(invocation)
        && (string.Equals(argument.Parameter?.Name, "tags", StringComparison.Ordinal)
            || argument.Parameter?.Ordinal == 3);

    private static bool IsLoggerPayloadArgument(
        IInvocationOperation invocation,
        IArgumentOperation argument)
    {
        if (IsLoggerBeginScope(invocation))
        {
            return IsLogicalArgument(argument, invocation.TargetMethod.IsExtensionMethod, 0);
        }

        return IsLoggerLog(invocation)
            && (string.Equals(argument.Parameter?.Name, "state", StringComparison.Ordinal)
                || IsLogicalArgument(argument, invocation.TargetMethod.IsExtensionMethod, 2));
    }

    private static bool IsKeyValuePairStringObject(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol { Name: "KeyValuePair", TypeArguments.Length: 2 } named)
        {
            return false;
        }

        return named.TypeArguments[0].SpecialType == SpecialType.System_String;
    }

    private static bool IsStringKeyDictionary(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol named || named.TypeArguments.Length < 2)
        {
            return false;
        }

        return named.Name is "Dictionary" or "IDictionary" or "IReadOnlyDictionary"
            && named.TypeArguments[0].SpecialType == SpecialType.System_String;
    }

    private static bool IsTelemetryTagCollection(ITypeSymbol? type) =>
        type?.Name is "TagList" or "ActivityTagsCollection";

    private static bool IsActivityEventCreation(ITypeSymbol? type) =>
        type?.Name is "ActivityEvent";

    private static bool IsActivityLinkCreation(ITypeSymbol? type) =>
        type?.Name is "ActivityLink";

    private static bool IsActivitySourceStartActivity(IInvocationOperation invocation) =>
        invocation.TargetMethod.Name == "StartActivity"
        && invocation.TargetMethod.ContainingType.Name == "ActivitySource";

    private static bool IsLoggerBeginScope(IInvocationOperation invocation) =>
        invocation.TargetMethod.Name == "BeginScope"
        && IsLoggerLike(invocation.TargetMethod.ContainingType);

    private static bool IsLoggerLog(IInvocationOperation invocation) =>
        invocation.TargetMethod.Name == "Log"
        && IsLoggerLike(invocation.TargetMethod.ContainingType);

    private static bool IsMetricInstrument(ITypeSymbol? type) =>
        type is INamedTypeSymbol { Arity: 1, Name: "Counter" or "Gauge" or "Histogram" or "UpDownCounter" }
        && type.ContainingNamespace.ToDisplayString() == "System.Diagnostics.Metrics";

    private static bool IsMetricMeasurementCreation(ITypeSymbol? type) =>
        type is INamedTypeSymbol { Arity: 1, Name: "Measurement" }
        && type.ContainingNamespace.ToDisplayString() == "System.Diagnostics.Metrics";

    private static bool IsLoggerLike(ITypeSymbol? type) =>
        type?.Name is "ILogger";
}

/// <summary>A key/value payload literal and the telemetry surface it reaches.</summary>
/// <param name="Key">The constant attribute key.</param>
/// <param name="KeySyntax">The syntax of the key.</param>
/// <param name="KeyIsBareLiteral">Whether the key is written as a string literal.</param>
/// <param name="Value">The constant string value, when the payload has one.</param>
/// <param name="ValueSyntax">The syntax of the constant value, when the payload has one.</param>
/// <param name="ValueIsBareLiteral">Whether the value is written as a literal.</param>
/// <param name="Sink">Where the payload ends up; <see cref="TelemetryPayloadSink.None"/> when it provably goes nowhere.</param>
internal readonly record struct TelemetryAttributePayloadLiteral(
    string Key,
    SyntaxNode KeySyntax,
    bool KeyIsBareLiteral,
    string? Value,
    SyntaxNode? ValueSyntax,
    bool ValueIsBareLiteral,
    TelemetryPayloadSink Sink = TelemetryPayloadSink.None)
{
    /// <summary>True when the payload reaches a telemetry sink rather than an unattached collection.</summary>
    public bool IsProductionEmission => Sink != TelemetryPayloadSink.None;
}
