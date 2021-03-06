' Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

Imports System.Threading
Imports Microsoft.CodeAnalysis.VisualBasic
Imports Microsoft.CodeAnalysis.VisualBasic.Symbols
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax

Namespace Microsoft.CodeAnalysis.Editor.VisualBasic.UnitTests.SymbolId

    Public Class SymbolKeyTestBase
        Inherits BasicTestBase

        <Flags>
        Friend Enum SymbolIdComparison
            None = 0
            CaseSensitive = 1
            CaseInsensitive = 2
            IgnoreAssemblyIds = 4
        End Enum

        <Flags>
        Friend Enum SymbolCategory
            All = 0
            DeclaredNamespace = 1
            DeclaredType = 2
            NonTypeMember = 4
            Parameter = 16
            Local = 32
        End Enum

#Region "Verification"

        Friend Shared Sub ResolveAndVerifySymbolList(newSymbols As IEnumerable(Of ISymbol), newCompilation As Compilation, originalSymbols As IEnumerable(Of ISymbol), originalCompilation As Compilation)

            Dim newlist = newSymbols.OrderBy(Function(s) s.Name).ToList()
            Dim origlist = originalSymbols.OrderBy(Function(s) s.Name).ToList()

            Assert.Equal(origlist.Count, newlist.Count)

            For i = 0 To newlist.Count - 1
                ResolveAndVerifySymbol(newlist(i), newCompilation, origlist(i), originalCompilation)
            Next

        End Sub

        Friend Shared Sub ResolveAndVerifyTypeSymbol(node As ExpressionSyntax, sourceSymbol As ITypeSymbol, model As SemanticModel, sourceComp As Compilation)
            Dim typeinfo = model.GetTypeInfo(node)
            ResolveAndVerifySymbol(If(typeinfo.Type, typeinfo.ConvertedType), model.Compilation, sourceSymbol, sourceComp)
        End Sub

        Friend Shared Sub ResolveAndVerifySymbol(node As ExpressionSyntax, sourceSymbol As ISymbol, model As SemanticModel, sourceComp As Compilation, Optional comparison As SymbolIdComparison = SymbolIdComparison.None)
            Dim syminfo = model.GetSymbolInfo(node)
            Dim symbol = syminfo.Symbol

            If symbol Is Nothing Then
                symbol = syminfo.CandidateSymbols.Single()
            End If

            ResolveAndVerifySymbol(symbol, model.Compilation, sourceSymbol, sourceComp, comparison)
        End Sub

        Friend Shared Sub ResolveAndVerifySymbol(symbol1 As ISymbol, compilation1 As Compilation, symbol2 As ISymbol, compilation2 As Compilation, Optional comparison As SymbolIdComparison = SymbolIdComparison.None)

            AssertSymbolsIdsEqual(symbol1, compilation1, symbol2, compilation2, comparison)

            Dim resolvedSymbol = ResolveSymbol(symbol1, compilation1, compilation2, comparison)
            Assert.NotNull(resolvedSymbol)
            Assert.Equal(symbol2, resolvedSymbol)
            Assert.Equal(symbol2.GetHashCode(), resolvedSymbol.GetHashCode())
        End Sub

        Friend Shared Function ResolveSymbol(originalSymbol As ISymbol, originalCompilation As Compilation, targetCompilation As Compilation, comparision As SymbolIdComparison) As ISymbol
            Dim sid = SymbolKey.Create(originalSymbol, originalCompilation, CancellationToken.None)
            Dim symInfo = sid.Resolve(targetCompilation, (comparision And SymbolIdComparison.IgnoreAssemblyIds) = SymbolIdComparison.IgnoreAssemblyIds)
            Return symInfo.Symbol
        End Function

        Friend Shared Sub AssertSymbolsIdsEqual(symbol1 As ISymbol, compilation1 As Compilation, symbol2 As ISymbol, compilation2 As Compilation, comparision As SymbolIdComparison, Optional expectEqual As Boolean = True)

            Dim sid1 = SymbolKey.Create(symbol1, compilation1, CancellationToken.None)
            Dim sid2 = SymbolKey.Create(symbol2, compilation2, CancellationToken.None)

            Dim isCaseSensitive = (comparision And SymbolIdComparison.CaseSensitive) = SymbolIdComparison.CaseSensitive
            Dim ignoreAssemblyIds = (comparision And SymbolIdComparison.IgnoreAssemblyIds) = SymbolIdComparison.IgnoreAssemblyIds
            Dim message = String.Concat(
                If(isCaseSensitive, "SymbolID CaseSensitive", "SymbolID CaseInsensitive"),
                If(ignoreAssemblyIds, " IgnoreAssemblyIds ", " "),
                "Compare")

            If expectEqual Then
                Assert.[True](CodeAnalysis.SymbolKey.GetComparer(isCaseSensitive, ignoreAssemblyIds).Equals(sid2, sid1), message)
            Else
                Assert.[False](CodeAnalysis.SymbolKey.GetComparer(isCaseSensitive, ignoreAssemblyIds).Equals(sid2, sid1), message)
            End If
        End Sub

#End Region

#Region "Utilities"

        Friend Shared Function GetBindNodes(Of T As VisualBasicSyntaxNode)(comp As VisualBasicCompilation, fileName As String, count As Integer) As IList(Of T)
            Dim list = New List(Of T)()
            ' 1 based - BIND#:
            For i = 1 To count
                Try
                    Dim node = CompilationUtils.FindBindingText(Of T)(comp, fileName, i)
                    list.Add(node)
                Catch ex As Exception
                    Exit For
                End Try
            Next
            Return list
        End Function

        Friend Shared Function GetSourceSymbols(comp As VisualBasicCompilation, category As SymbolCategory) As IEnumerable(Of ISymbol)

            Dim list = GetSourceSymbols(comp, includeLocals:=(category And SymbolCategory.Local) <> 0)

            Dim kinds = New List(Of SymbolKind)()
            If (category And SymbolCategory.DeclaredNamespace) <> 0 Then
                kinds.Add(SymbolKind.Namespace)
            End If

            If (category And SymbolCategory.DeclaredType) <> 0 Then
                kinds.Add(SymbolKind.NamedType)
                kinds.Add(SymbolKind.TypeParameter)
            End If

            If (category And SymbolCategory.NonTypeMember) <> 0 Then
                kinds.Add(SymbolKind.Field)
                kinds.Add(SymbolKind.Event)
                kinds.Add(SymbolKind.Property)
                kinds.Add(SymbolKind.Method)
            End If

            If (category And SymbolCategory.Parameter) <> 0 Then
                kinds.Add(SymbolKind.Parameter)
            End If

            If (category And SymbolCategory.Local) <> 0 Then
                kinds.Add(SymbolKind.Local)
                kinds.Add(SymbolKind.Label)
                kinds.Add(SymbolKind.RangeVariable)
                ' TODO: anonymous type & func
            End If

            Return list.Where(Function(s)
                                  If s.IsImplicitlyDeclared Then
                                      Return False
                                  End If

                                  For Each k In kinds
                                      If s.Kind = k Then
                                          Return True
                                      End If
                                  Next

                                  Return False
                              End Function)
        End Function

        Friend Shared Function GetSourceSymbols(compilation As VisualBasicCompilation, includeLocals As Boolean) As IList(Of ISymbol)

            Dim list = New List(Of ISymbol)()
            Dim localDumper As LocalSymbolDumper = If(includeLocals, New LocalSymbolDumper(compilation), Nothing)
            GetSourceMemberSymbols(compilation.SourceModule.GlobalNamespace, list, localDumper)

            GetSourceAliasSymbols(compilation, list)
            list.Add(compilation.Assembly)
            list.AddRange(compilation.Assembly.Modules)

            Return list
        End Function

        Private Shared Sub GetSourceAliasSymbols(comp As VisualBasicCompilation, list As List(Of ISymbol))
            For Each tree In comp.SyntaxTrees
                Dim aliases = tree.GetRoot().DescendantNodes().OfType(Of ImportAliasClauseSyntax)()
                Dim model = comp.GetSemanticModel(tree)
                For Each a In aliases
                    Dim sym = model.GetDeclaredSymbol(a)
                    If sym IsNot Nothing AndAlso Not list.Contains(sym) Then
                        list.Add(sym)
                    End If
                Next
            Next
        End Sub

        Private Shared Sub GetSourceMemberSymbols(symbol As INamespaceOrTypeSymbol, list As List(Of ISymbol), localDumper As LocalSymbolDumper)
            For Each member In symbol.GetMembers()
                list.Add(member)

                Select Case member.Kind
                    Case SymbolKind.NamedType, SymbolKind.Namespace
                        GetSourceMemberSymbols(DirectCast(member, INamespaceOrTypeSymbol), list, localDumper)
                    Case SymbolKind.Method
                        Dim method = DirectCast(member, IMethodSymbol)
                        For Each parameter In method.Parameters
                            list.Add(parameter)
                        Next
                        If localDumper IsNot Nothing Then
                            localDumper.GetLocalSymbols(method, list)
                        End If
                    Case SymbolKind.Field
                        If localDumper IsNot Nothing Then
                            localDumper.GetLocalSymbols(DirectCast(member, IFieldSymbol), list)
                        End If
                End Select
            Next
        End Sub

#End Region

    End Class

    Friend Class LocalSymbolDumper
        Private _comp As VisualBasicCompilation
        Public Sub New(comp As VisualBasicCompilation)
            Me._comp = comp
        End Sub

        Public Sub GetLocalSymbols(symbol As IFieldSymbol, list As List(Of ISymbol))

            For Each node In symbol.DeclaringSyntaxReferences.Select(Function(d) d.GetSyntax())

                Dim declarator = TryCast(node.Parent, VariableDeclaratorSyntax)
                If declarator IsNot Nothing AndAlso declarator.Initializer IsNot Nothing Then

                    Dim model = _comp.GetSemanticModel(declarator.SyntaxTree)
                    Dim df = model.AnalyzeDataFlow(declarator.Initializer.Value)

                    GetLocalAndType(df, list)
                    GetAnonymousExprSymbols(declarator.Initializer.Value, model, list)

                End If
            Next

        End Sub

        Public Sub GetLocalSymbols(symbol As IMethodSymbol, list As List(Of ISymbol))

            ' Delaration statement is child of Block
            For Each n In symbol.DeclaringSyntaxReferences.Select(Function(d) d.GetSyntax())
                Dim body = TryCast(n.Parent, MethodBlockSyntax)
                ' interface method
                If body IsNot Nothing Then
                    If body.Statements <> Nothing AndAlso body.Statements.Count > 0 Then

                        Dim model = _comp.GetSemanticModel(body.SyntaxTree)
                        Dim df As DataFlowAnalysis = Nothing
                        If body.Statements.Count = 1 Then
                            df = model.AnalyzeDataFlow(body.Statements.First)
                        Else
                            df = model.AnalyzeDataFlow(body.Statements.First, body.Statements.Last)
                        End If

                        GetLocalAndType(df, list)
                        GetLabelSymbols(body, model, list)
                        GetAnonymousTypeAndFuncSymbols(body, model, list)
                    End If
                End If
            Next

        End Sub

        Private Sub GetLocalAndType(df As DataFlowAnalysis, list As List(Of ISymbol))
            ' add local symbols to list
            For Each v As ISymbol In df.VariablesDeclared
                list.Add(v)
                Dim local = TryCast(v, ILocalSymbol)
                If local IsNot Nothing AndAlso local.Type.Kind = SymbolKind.ArrayType Then
                    list.Add(local.Type)
                End If
            Next
        End Sub

        Private Sub GetLabelSymbols(body As MethodBlockSyntax, model As SemanticModel, list As List(Of ISymbol))
            Dim labels = body.DescendantNodes().OfType(Of LabelStatementSyntax)()
            For Each lb As LabelStatementSyntax In labels
                Dim sym = model.GetDeclaredSymbol(lb)
                list.Add(sym)
            Next

            ' VB has not SwitchLabel; it's CaseStatement
        End Sub

        Private Sub GetAnonymousTypeAndFuncSymbols(body As MethodBlockSyntax, model As SemanticModel, list As List(Of ISymbol))

            Dim exprs As IEnumerable(Of ExpressionSyntax), tmp As IEnumerable(Of ExpressionSyntax)
            exprs = body.DescendantNodes().OfType(Of AnonymousObjectCreationExpressionSyntax)()
            tmp = body.DescendantNodes().OfType(Of SingleLineLambdaExpressionSyntax)()
            exprs = exprs.Concat(tmp)
            tmp = body.DescendantNodes().OfType(Of MultiLineLambdaExpressionSyntax)()
            exprs = exprs.Concat(tmp)

            For Each expr As ExpressionSyntax In exprs
                GetAnonymousExprSymbols(expr, model, list)
            Next
        End Sub

        Private Sub GetAnonymousExprSymbols(expr As ExpressionSyntax, model As SemanticModel, list As List(Of ISymbol))

            Dim kind = expr.Kind
            If kind <> SyntaxKind.AnonymousObjectCreationExpression AndAlso
                kind <> SyntaxKind.SingleLineSubLambdaExpression AndAlso
                kind <> SyntaxKind.SingleLineFunctionLambdaExpression AndAlso
                kind <> SyntaxKind.MultiLineSubLambdaExpression AndAlso
                kind <> SyntaxKind.MultiLineFunctionLambdaExpression Then
                Return
            End If

            Dim tinfo = model.GetTypeInfo(expr)
            Dim tconv = model.GetConversion(expr)
            ' lambda has NO type
            ' Bug#13362 - Lambda
            If tconv.IsAnonymousDelegate OrElse tconv.IsLambda Then
                Dim sinfo = model.GetSymbolInfo(expr)
                ' SymbolInfo should NOT be null
                list.Add(sinfo.Symbol)
                ' Error case might be Nothing
            ElseIf tinfo.Type IsNot Nothing Then
                list.Add(tinfo.Type)
                For Each m In tinfo.Type.GetMembers
                    list.Add(m)
                Next
            End If
        End Sub

    End Class

End Namespace
