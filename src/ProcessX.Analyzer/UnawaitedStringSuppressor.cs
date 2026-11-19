using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;

namespace ProcessX.Analyzer;

/// <summary>
/// <c>using ProcessX;</c> makes string and string[] awaitable, so the compiler reports CS4014 ("call is not awaited")
/// for every discarded call that returns one, such as <c>Console.ReadLine();</c>. Those are never forgotten awaits.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnawaitedStringSuppressor : DiagnosticSuppressor
{
    public static readonly SuppressionDescriptor UnawaitedString = new(
        id: "PXSPR001",
        suppressedDiagnosticId: "CS4014",
        justification: "string is awaitable only to enable `await \"...\"`; a discarded string is not a forgotten await.");

    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions { get; } = ImmutableArray.Create(UnawaitedString);

    public override void ReportSuppressions(SuppressionAnalysisContext context)
    {
        foreach (var diagnostic in context.ReportedDiagnostics)
        {
            var tree = diagnostic.Location.SourceTree;
            if (tree == null) continue;

            var node = tree.GetRoot(context.CancellationToken).FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            if (node is ExpressionStatementSyntax statement)
            {
                node = statement.Expression;
            }

            var type = context.GetSemanticModel(tree).GetTypeInfo(node, context.CancellationToken).Type;
            if (IsStringOrStringArray(type))
            {
                context.ReportSuppression(Suppression.Create(UnawaitedString, diagnostic));
            }
        }
    }

    static bool IsStringOrStringArray(ITypeSymbol? type)
    {
        return type switch
        {
            { SpecialType: SpecialType.System_String } => true,
            IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_String } => true,
            _ => false,
        };
    }
}
