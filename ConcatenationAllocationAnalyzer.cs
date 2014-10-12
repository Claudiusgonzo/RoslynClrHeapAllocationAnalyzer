﻿namespace ClrHeapAllocationAnalyzer
{
    using System;
    using System.Collections.Immutable;
    using System.Linq;
    using System.Threading;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Diagnostics;

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ConcatenationAllocationAnalyzer : ISyntaxNodeAnalyzer<SyntaxKind>
    {
        internal static DiagnosticDescriptor StringConcatenationAllocationRule = new DiagnosticDescriptor("Implicit string concatenation allocation", string.Empty, "Considering using StringBuilder", "Usage", DiagnosticSeverity.Warning, true, string.Empty, "http://msdn.microsoft.com/en-us/library/2839d5h5(v=vs.110).aspx");

        internal static DiagnosticDescriptor ValueTypeToReferenceTypeInAStringConcatenationRule = new DiagnosticDescriptor("Value type to reference type conversion allocation for string concatenation", string.Empty, "Value type ({0}) is being boxed to a reference type for a string concatenation.", "Performance", DiagnosticSeverity.Warning, true, string.Empty, "http://msdn.microsoft.com/en-us/library/yz2be5wk.aspx");

        public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get
            {
                return ImmutableArray.Create(StringConcatenationAllocationRule, ValueTypeToReferenceTypeInAStringConcatenationRule);
            }
        }

        public ImmutableArray<SyntaxKind> SyntaxKindsOfInterest
        {
            get
            {
                return ImmutableArray.Create(SyntaxKind.AddExpression, SyntaxKind.AddAssignmentExpression);
            }
        }

        public void AnalyzeNode(SyntaxNode node, SemanticModel semanticModel, Action<Diagnostic> addDiagnostic, AnalyzerOptions options, CancellationToken cancellationToken)
        {
            string filePath = node.SyntaxTree.FilePath;
            var binaryExpressions = node.DescendantNodesAndSelf().OfType<BinaryExpressionSyntax>().Reverse(); // need inner most expressions

            foreach (var binaryExpression in binaryExpressions)
            {
                if (binaryExpression.Left != null && binaryExpression.Right != null)
                {
                    var left = semanticModel.GetTypeInfo(binaryExpression.Left);
                    CheckForTypeConversion(binaryExpression.Left, left, addDiagnostic, filePath);

                    var right = semanticModel.GetTypeInfo(binaryExpression.Right);
                    CheckForTypeConversion(binaryExpression.Right, right, addDiagnostic, filePath);

                    // regular string allocation
                    if (left.Type != null && left.Type.SpecialType == SpecialType.System_String || right.Type != null && right.Type.SpecialType == SpecialType.System_String)
                    {
                        addDiagnostic(Diagnostic.Create(StringConcatenationAllocationRule, binaryExpression.OperatorToken.GetLocation()));
                        HeapAllocationAnalyzerEventSource.Logger.StringConcatenationAllocation(filePath);
                    }
                }
            }
        }

        private static void CheckForTypeConversion(ExpressionSyntax expression, TypeInfo typeInfo, Action<Diagnostic> addDiagnostic, string filePath)
        {
            if (typeInfo.Type != null && typeInfo.Type.IsValueType && typeInfo.ConvertedType != null && !typeInfo.ConvertedType.IsValueType)
            {
                addDiagnostic(Diagnostic.Create(ValueTypeToReferenceTypeInAStringConcatenationRule, expression.GetLocation(), typeInfo.Type.ToDisplayString()));
                HeapAllocationAnalyzerEventSource.Logger.BoxingAllocationInStringConcatenation(filePath);
            }
        }
    }
}