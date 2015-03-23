// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes.Suppression;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Editor.UnitTests.Workspaces;
using Microsoft.CodeAnalysis.UnitTests.Diagnostics;
using Roslyn.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.Editor.UnitTests.Diagnostics
{
    public abstract class AbstractSuppressionAllCodeTests : IEqualityComparer<Diagnostic>
    {
        protected abstract TestWorkspace CreateWorkspaceFromFile(string definition, ParseOptions parseOptions);
        internal abstract Tuple<Analyzer, ISuppressionFixProvider> CreateDiagnosticProviderAndFixer(Workspace workspace);

        protected void TestPragma(string code, ParseOptions options, Func<string, bool> verifier)
        {
            var set = new HashSet<ValueTuple<SyntaxToken, SyntaxToken>>();
            TestPragmaOrLocal(code, options, pragma: true, digInto: n => true, verifier: verifier, fixChecker: c =>
            {
                var fix = (AbstractSuppressionCodeFixProvider.PragmaWarningCodeAction)c;
                var tuple = ValueTuple.Create(fix.StartToken_TestOnly, fix.EndToken_TestOnly);
                if (set.Contains(tuple))
                {
                    return true;
                }

                set.Add(tuple);
                return false;
            });
        }

        protected void TestLocalSuppression(string code, ParseOptions options, Func<SyntaxNode, bool> digInto, Func<string, bool> verifier)
        {
            var set = new HashSet<SyntaxNode>();
            TestPragmaOrLocal(code, options, pragma: false, digInto: digInto, verifier: verifier, fixChecker: c =>
            {
                var fix = (AbstractSuppressionCodeFixProvider.LocalSuppressMessageCodeAction)c;
                if (set.Contains(fix.TargetNode_TestOnly))
                {
                    return true;
                }

                set.Add(fix.TargetNode_TestOnly);
                return false;
            });
        }

        protected void TestPragmaOrLocal(
            string code, ParseOptions options, bool pragma, Func<SyntaxNode, bool> digInto, Func<string, bool> verifier, Func<CodeAction, bool> fixChecker)
        {
            using (var workspace = CreateWorkspaceFromFile(code, options))
            {
                var document = workspace.CurrentSolution.Projects.Single().Documents.Single();
                var root = document.GetSyntaxRootAsync().GetAwaiter().GetResult();
                var existingDiagnostics = root.GetDiagnostics().ToArray();

                var analyzerAndFixer = CreateDiagnosticProviderAndFixer(workspace);
                var analyzer = analyzerAndFixer.Item1;
                var fixer = analyzerAndFixer.Item2;

                foreach (var node in root.DescendantNodesAndSelf(digInto))
                {
                    analyzer.Node = node;

                    var span = node.Span;
                    var diagnostics = DiagnosticProviderTestUtilities.GetAllDiagnostics(analyzer, document, span);

                    foreach (var diagnostic in diagnostics)
                    {
                        if (!fixer.CanBeSuppressed(diagnostic))
                        {
                            continue;
                        }

                        var fixes = fixer.GetSuppressionsAsync(document, diagnostic.Location.SourceSpan, SpecializedCollections.SingletonEnumerable(diagnostic), CancellationToken.None).GetAwaiter().GetResult();
                        if (fixes == null || fixes.Count() <= 0)
                        {
                            continue;
                        }

                        var fix = GetFix(fixes.Select(f => f.Action), pragma);
                        if (fix == null)
                        {
                            continue;
                        }

                        // already same fix has been tested
                        if (fixChecker(fix))
                        {
                            continue;
                        }

                        var operations = fix.GetOperationsAsync(CancellationToken.None).GetAwaiter().GetResult();

                        var applyChangesOperation = operations.OfType<ApplyChangesOperation>().Single();
                        var newDocument = applyChangesOperation.ChangedSolution.Projects.Single().Documents.Single();
                        var newTree = newDocument.GetSyntaxTreeAsync().GetAwaiter().GetResult();

                        var newText = newTree.GetText().ToString();
                        Assert.True(verifier(newText));

                        var newDiagnostics = newTree.GetDiagnostics();
                        Assert.Equal(0, existingDiagnostics.Except(newDiagnostics, this).Count());
                    }
                }
            }
        }

        private CodeAction GetFix(IEnumerable<CodeAction> fixes, bool pragma)
        {
            if (pragma)
            {
                return fixes.FirstOrDefault(f => f is AbstractSuppressionCodeFixProvider.PragmaWarningCodeAction);
            }

            return fixes.FirstOrDefault(f => f is AbstractSuppressionCodeFixProvider.LocalSuppressMessageCodeAction);
        }

        public bool Equals(Diagnostic x, Diagnostic y)
        {
            return x.Id == y.Id && x.Descriptor.Category == y.Descriptor.Category;
        }

        public int GetHashCode(Diagnostic obj)
        {
            return Hash.Combine(obj.Id, obj.Descriptor.Category.GetHashCode());
        }

        internal class Analyzer : DiagnosticAnalyzer
        {
            private readonly DiagnosticDescriptor _descriptor =
                    new DiagnosticDescriptor("TestId", "Test", "Test", "Test", DiagnosticSeverity.Warning, isEnabledByDefault: true);

            public SyntaxNode Node { get; set; }

            public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            {
                get
                {
                    return ImmutableArray.Create(_descriptor);
                }
            }

            public override void Initialize(AnalysisContext analysisContext)
            {
                analysisContext.RegisterSyntaxTreeAction(
                    (context) =>
                    {
                        context.ReportDiagnostic(Diagnostic.Create(_descriptor, Node.GetLocation()));
                    });
            }
        }
    }
}