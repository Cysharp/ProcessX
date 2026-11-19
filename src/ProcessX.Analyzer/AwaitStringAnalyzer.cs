using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace ProcessX.Analyzer;

/// <summary>
/// <c>await "..."</c> passes the text to the shell verbatim, and C# gives a library no way to restrict
/// an awaitable string to constants. This analyzer does: anything that is not a compile-time constant is an error.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AwaitStringAnalyzer : DiagnosticAnalyzer
{
    const string AwaitableExtensionsTypeName = "ProcessX.AwaitableExtensions";
    const string ZxTypeName = "ProcessX.Zx";

    public static readonly DiagnosticDescriptor NonConstantScript = new(
        id: "PX001",
        title: "await string must be a compile-time constant",
        messageFormat: "'await' on a string runs it through the shell without escaping, so it must be a compile-time constant. Use run($\"...\") or sh($\"...\") to pass values.",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NonConstantShellScript = new(
        id: "PX003",
        title: "A string passed to sh must be a compile-time constant",
        messageFormat: "A string passed to sh(...) is script text and goes to the shell without escaping, so it must be a compile-time constant. Use sh($\"...{value}...\") to pass values (they are quoted for the shell), or {value:raw} to insert text as it is.",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ChangeDirectoryHasNoEffect = new(
        id: "PX002",
        title: "cd in a shell command does not change the directory of the script",
        messageFormat: "'{0}' only changes the directory of the shell process that runs it. Use cd(\"...\") to change the directory of the script.",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    // a script that is nothing but `cd path`; `cd path && command` is fine
    static readonly Regex ChangeDirectoryOnly = new(@"^\s*(cd|chdir)(\s+[^&|;]*)?$", RegexOptions.CultureInvariant);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(NonConstantScript, ChangeDirectoryHasNoEffect, NonConstantShellScript);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeAwait, OperationKind.Await);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    // sh(script): an interpolated string binds to the handler directly and its holes are quoted.
    // A string gets there through the implicit conversion and is taken as script text, so it has to be a constant.
    static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        if (method.Name != "sh" || invocation.Arguments.Length != 1) return;
        if (method.ContainingType.ToDisplayString() != ZxTypeName) return;

        var argument = invocation.Arguments[0].Value;
        while (argument is IParenthesizedOperation parenthesized)
        {
            argument = parenthesized.Operand;
        }

        if (argument is IConversionOperation { OperatorMethod: not null } conversion)
        {
            AnalyzeScript(context, Unwrap(conversion.Operand), NonConstantShellScript);
        }
    }

    static void AnalyzeAwait(OperationAnalysisContext context)
    {
        var operation = (IAwaitOperation)context.Operation;
        if (operation.Syntax is not AwaitExpressionSyntax syntax) return;

        var semanticModel = operation.SemanticModel;
        if (semanticModel == null) return;

        var method = semanticModel.GetAwaitExpressionInfo(syntax).GetAwaiterMethod;
        if (method == null) return;

        method = method.ReducedFrom ?? method;
        if (!method.IsExtensionMethod || method.Parameters.Length == 0) return;
        if (method.ContainingType.ToDisplayString() != AwaitableExtensionsTypeName) return;

        var parameterType = method.Parameters[0].Type;
        var operand = Unwrap(operation.Operation);

        if (parameterType.SpecialType == SpecialType.System_String)
        {
            AnalyzeScript(context, operand);
        }
        else if (parameterType is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_String })
        {
            if (operand is IArrayCreationOperation { Initializer: { } initializer })
            {
                foreach (var element in initializer.ElementValues)
                {
                    AnalyzeScript(context, Unwrap(element));
                }
            }
            else
            {
                context.ReportDiagnostic(Diagnostic.Create(NonConstantScript, operand.Syntax.GetLocation()));
            }
        }
    }

    static void AnalyzeScript(OperationAnalysisContext context, IOperation script)
    {
        AnalyzeScript(context, script, NonConstantScript);
    }

    static void AnalyzeScript(OperationAnalysisContext context, IOperation script, DiagnosticDescriptor nonConstant)
    {
        var constant = script.ConstantValue;
        if (!constant.HasValue)
        {
            context.ReportDiagnostic(Diagnostic.Create(nonConstant, script.Syntax.GetLocation()));
            return;
        }

        if (constant.Value is string text && ChangeDirectoryOnly.IsMatch(text))
        {
            context.ReportDiagnostic(Diagnostic.Create(ChangeDirectoryHasNoEffect, script.Syntax.GetLocation(), text.Trim()));
        }
    }

    static IOperation Unwrap(IOperation operation)
    {
        while (true)
        {
            switch (operation)
            {
                case IConversionOperation { IsImplicit: true } conversion:
                    operation = conversion.Operand;
                    break;
                case IParenthesizedOperation parenthesized:
                    operation = parenthesized.Operand;
                    break;
                default:
                    return operation;
            }
        }
    }
}
