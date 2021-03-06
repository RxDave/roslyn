﻿' Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

Imports System.Collections.Immutable
Imports System.Threading
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax

Namespace Microsoft.CodeAnalysis.VisualBasic
    Friend Class VisualBasicDeclarationComputer
        Inherits DeclarationComputer

        Public Shared Function GetDeclarationsInSpan(model As SemanticModel, span As TextSpan, getSymbol As Boolean, cancellationToken As CancellationToken) As ImmutableArray(Of DeclarationInfo)
            Dim builder = ArrayBuilder(Of DeclarationInfo).GetInstance()
            ComputeDeclarationsCore(model, model.SyntaxTree.GetRoot(),
                                    Function(node, level) Not node.Span.OverlapsWith(span) OrElse InvalidLevel(level),
                                    getSymbol, builder, Nothing, cancellationToken)
            Return builder.ToImmutable()
        End Function

        Public Shared Function GetDeclarationsInNode(model As SemanticModel, node As SyntaxNode, getSymbol As Boolean, cancellationToken As CancellationToken, Optional levelsToCompute As Integer? = Nothing) As ImmutableArray(Of DeclarationInfo)
            Dim builder = ArrayBuilder(Of DeclarationInfo).GetInstance()
            ComputeDeclarationsCore(model, node, Function(n, level) InvalidLevel(level), getSymbol, builder, levelsToCompute, cancellationToken)
            Return builder.ToImmutable()
        End Function

        Private Shared Function InvalidLevel(level As Integer?) As Boolean
            Return level.HasValue AndAlso level.Value <= 0
        End Function


        Private Shared Function DecrementLevel(level As Integer?) As Integer?
            Return If(level.HasValue, level.Value - 1, level)
        End Function

        Private Shared Sub ComputeDeclarationsCore(model As SemanticModel, node As SyntaxNode, shouldSkip As Func(Of SyntaxNode, Integer?, Boolean), getSymbol As Boolean, builder As ArrayBuilder(Of DeclarationInfo), levelsToCompute As Integer?, cancellationToken As CancellationToken)
            If shouldSkip(node, levelsToCompute) Then
                Return
            End If

            Dim newLevel = DecrementLevel(levelsToCompute)

            Select Case node.Kind()
                Case SyntaxKind.NamespaceBlock
                    Dim ns = CType(node, NamespaceBlockSyntax)
                    For Each decl In ns.Members
                        ComputeDeclarationsCore(model, decl, shouldSkip, getSymbol, builder, newLevel, cancellationToken)
                    Next
                    builder.Add(GetDeclarationInfo(model, node, getSymbol, cancellationToken))

                    Dim name = ns.NamespaceStatement.Name
                    While (name.Kind() = SyntaxKind.QualifiedName)
                        name = (CType(name, QualifiedNameSyntax)).Left
                        Dim declaredSymbol = If(getSymbol, model.GetSymbolInfo(name, cancellationToken).Symbol, Nothing)
                        builder.Add(New DeclarationInfo(name, ImmutableArray(Of SyntaxNode).Empty, declaredSymbol))
                    End While

                    Return
                Case SyntaxKind.ClassBlock,
                     SyntaxKind.StructureBlock,
                     SyntaxKind.InterfaceBlock,
                     SyntaxKind.ModuleBlock
                    Dim t = CType(node, TypeBlockSyntax)
                    For Each decl In t.Members
                        ComputeDeclarationsCore(model, decl, shouldSkip, getSymbol, builder, newLevel, cancellationToken)
                    Next
                    builder.Add(GetDeclarationInfo(model, node, getSymbol, cancellationToken))
                    Return
                Case SyntaxKind.EnumBlock
                    Dim t = CType(node, EnumBlockSyntax)
                    For Each decl In t.Members
                        ComputeDeclarationsCore(model, decl, shouldSkip, getSymbol, builder, newLevel, cancellationToken)
                    Next
                    builder.Add(GetDeclarationInfo(model, node, getSymbol, cancellationToken))
                    Return
                Case SyntaxKind.EnumMemberDeclaration
                    Dim t = CType(node, EnumMemberDeclarationSyntax)
                    builder.Add(GetDeclarationInfo(model, node, getSymbol, t.Initializer, cancellationToken))
                    Return
                Case SyntaxKind.DelegateSubStatement, SyntaxKind.DelegateFunctionStatement
                    Dim t = CType(node, DelegateStatementSyntax)
                    Dim paramInitializers As IEnumerable(Of SyntaxNode) = GetParameterInitializers(t.ParameterList)
                    builder.Add(GetDeclarationInfo(model, node, getSymbol, paramInitializers, cancellationToken))
                    Return
                Case SyntaxKind.EventBlock
                    Dim t = CType(node, EventBlockSyntax)
                    For Each decl In t.Accessors
                        ComputeDeclarationsCore(model, decl, shouldSkip, getSymbol, builder, newLevel, cancellationToken)
                    Next
                    Dim eventInitializers = GetParameterInitializers(t.EventStatement.ParameterList)
                    builder.Add(GetDeclarationInfo(model, node, getSymbol, eventInitializers, cancellationToken))
                    Return
                Case SyntaxKind.EventStatement
                    Dim t = CType(node, EventStatementSyntax)
                    Dim paramInitializers = GetParameterInitializers(t.ParameterList)
                    builder.Add(GetDeclarationInfo(model, node, getSymbol, paramInitializers, cancellationToken))
                    Return
                Case SyntaxKind.FieldDeclaration
                    Dim t = CType(node, FieldDeclarationSyntax)
                    For Each decl In t.Declarators
                        For Each identifier In decl.Names
                            builder.Add(GetDeclarationInfo(model, identifier, getSymbol, decl.Initializer, cancellationToken))
                        Next
                    Next
                    Return
                Case SyntaxKind.PropertyBlock
                    Dim t = CType(node, PropertyBlockSyntax)
                    For Each decl In t.Accessors
                        ComputeDeclarationsCore(model, decl, shouldSkip, getSymbol, builder, newLevel, cancellationToken)
                    Next
                    Dim propertyInitializers = GetParameterInitializers(t.PropertyStatement.ParameterList)
                    Dim codeBlocks = propertyInitializers.Concat(t.PropertyStatement.Initializer)
                    builder.Add(GetDeclarationInfo(model, node, getSymbol, codeBlocks, cancellationToken))
                    Return
                Case SyntaxKind.PropertyStatement
                    Dim t = CType(node, PropertyStatementSyntax)
                    Dim propertyInitializers = GetParameterInitializers(t.ParameterList)
                    Dim codeBlocks = propertyInitializers.Concat(t.Initializer)
                    builder.Add(GetDeclarationInfo(model, node, getSymbol, codeBlocks, cancellationToken))
                    Return
                Case SyntaxKind.GetAccessorBlock,
                     SyntaxKind.SetAccessorBlock,
                     SyntaxKind.AddHandlerAccessorBlock,
                     SyntaxKind.RemoveHandlerAccessorBlock,
                     SyntaxKind.RaiseEventAccessorBlock,
                     SyntaxKind.SubBlock,
                     SyntaxKind.FunctionBlock,
                     SyntaxKind.OperatorBlock,
                     SyntaxKind.ConstructorBlock
                    Dim t = CType(node, MethodBlockBaseSyntax)
                    Dim paramInitializers = GetParameterInitializers(t.Begin.ParameterList)
                    Dim codeBlocks = paramInitializers.Concat(t.Statements).Concat(t.End)
                    builder.Add(GetDeclarationInfo(model, node, getSymbol, codeBlocks, cancellationToken))
                    Return
                Case SyntaxKind.DeclareSubStatement, SyntaxKind.DeclareFunctionStatement
                    Dim t = CType(node, MethodBaseSyntax)
                    Dim paramInitializers = GetParameterInitializers(t.ParameterList)
                    builder.Add(GetDeclarationInfo(model, node, getSymbol, paramInitializers, cancellationToken))
                    Return
                Case SyntaxKind.CompilationUnit
                    Dim t = CType(node, CompilationUnitSyntax)
                    For Each decl In t.Members
                        ComputeDeclarationsCore(model, decl, shouldSkip, getSymbol, builder, newLevel, cancellationToken)
                    Next
                    Return
                Case Else
                    Return
            End Select
        End Sub

        Private Shared Function GetParameterInitializers(parameterList As ParameterListSyntax) As IEnumerable(Of SyntaxNode)
            Return If(parameterList IsNot Nothing,
                parameterList.Parameters.Select(Function(p) p.Default),
                SpecializedCollections.EmptyEnumerable(Of SyntaxNode))
        End Function
    End Class
End Namespace
