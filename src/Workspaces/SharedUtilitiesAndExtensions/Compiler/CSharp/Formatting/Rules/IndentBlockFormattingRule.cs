﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Formatting.Rules;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Shared.Utilities;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.CodeAnalysis.CSharp.Formatting
{
    internal class IndentBlockFormattingRule : BaseFormattingRule
    {
        internal const string Name = "CSharp IndentBlock Formatting Rule";

        public override void AddIndentBlockOperations(List<IndentBlockOperation> list, SyntaxNode node, AnalyzerConfigOptions options, in NextIndentBlockOperationAction nextOperation)
        {
            nextOperation.Invoke();

            AddAlignmentBlockOperation(list, node);

            AddBlockIndentationOperation(list, node, options);

            AddLabelIndentationOperation(list, node, options);

            AddSwitchIndentationOperation(list, node, options);

            AddEmbeddedStatementsIndentationOperation(list, node);

            AddTypeParameterConstraintClauseOperation(list, node);
        }

        private void AddTypeParameterConstraintClauseOperation(List<IndentBlockOperation> list, SyntaxNode node)
        {
            if (node is TypeParameterConstraintClauseSyntax typeParameterConstraintClause)
            {
                var declaringNode = typeParameterConstraintClause.Parent;
                var baseToken = declaringNode.GetFirstToken();
                AddIndentBlockOperation(list, baseToken, node.GetFirstToken(), node.GetLastToken());
            }
        }

        private void AddSwitchIndentationOperation(List<IndentBlockOperation> list, SyntaxNode node, AnalyzerConfigOptions options)
        {
            if (!(node is SwitchSectionSyntax section))
            {
                return;
            }

            // can this ever happen?
            if (section.Labels.Count == 0 &&
                section.Statements.Count == 0)
            {
                return;
            }

            var indentSwitchCase = options.GetOption(CSharpFormattingOptions.IndentSwitchCaseSection);
            var indentSwitchCaseWhenBlock = options.GetOption(CSharpFormattingOptions.IndentSwitchCaseSectionWhenBlock);
            if (!indentSwitchCase && !indentSwitchCaseWhenBlock)
            {
                // Never indent
                return;
            }

            var alwaysIndent = indentSwitchCase && indentSwitchCaseWhenBlock;
            if (!alwaysIndent)
            {
                // Only one of these values can be true at this point.
                Debug.Assert(indentSwitchCase != indentSwitchCaseWhenBlock);

                var firstStatementIsBlock =
                    section.Statements.Count > 0 &&
                    section.Statements[0].IsKind(SyntaxKind.Block);

                if (indentSwitchCaseWhenBlock != firstStatementIsBlock)
                {
                    return;
                }
            }

            // see whether we are the last statement
            var switchStatement = node.Parent as SwitchStatementSyntax;
            var lastSection = switchStatement.Sections.Last() == node;

            if (section.Statements.Count == 0)
            {
                // even if there is no statement under section, we still want indent operation
                var lastTokenOfLabel = section.Labels.Last().GetLastToken(includeZeroWidth: true);
                var nextToken = lastTokenOfLabel.GetNextToken(includeZeroWidth: true);

                AddIndentBlockOperation(list, lastTokenOfLabel, lastTokenOfLabel,
                    lastSection ?
                        TextSpan.FromBounds(lastTokenOfLabel.FullSpan.End, nextToken.SpanStart) : TextSpan.FromBounds(lastTokenOfLabel.FullSpan.End, lastTokenOfLabel.FullSpan.End));
                return;
            }

            var startToken = section.Statements.First().GetFirstToken(includeZeroWidth: true);
            var endToken = section.Statements.Last().GetLastToken(includeZeroWidth: true);

            // see whether we are the last statement
            var span = CommonFormattingHelpers.GetSpanIncludingTrailingAndLeadingTriviaOfAdjacentTokens(startToken, endToken);
            span = lastSection ? span : TextSpan.FromBounds(span.Start, endToken.FullSpan.End);

            AddIndentBlockOperation(list, startToken, endToken, span);
        }

        private void AddLabelIndentationOperation(List<IndentBlockOperation> list, SyntaxNode node, AnalyzerConfigOptions options)
        {
            // label statement
            if (node is LabeledStatementSyntax labeledStatement)
            {
                var labelPositioningOption = options.GetOption(CSharpFormattingOptions.LabelPositioning);

                if (labelPositioningOption == LabelPositionOptions.OneLess)
                {
                    AddUnindentBlockOperation(list, labeledStatement.Identifier, labeledStatement.ColonToken);
                }
                else if (labelPositioningOption == LabelPositionOptions.LeftMost)
                {
                    AddAbsoluteZeroIndentBlockOperation(list, labeledStatement.Identifier, labeledStatement.ColonToken);
                }
            }
        }

        private void AddAlignmentBlockOperation(List<IndentBlockOperation> list, SyntaxNode node)
        {
            switch (node)
            {
                case SimpleLambdaExpressionSyntax simpleLambda:
                    SetAlignmentBlockOperation(list, simpleLambda, simpleLambda.Body);
                    return;
                case ParenthesizedLambdaExpressionSyntax parenthesizedLambda:
                    SetAlignmentBlockOperation(list, parenthesizedLambda, parenthesizedLambda.Body);
                    return;
                case AnonymousMethodExpressionSyntax anonymousMethod:
                    SetAlignmentBlockOperation(list, anonymousMethod, anonymousMethod.Block);
                    return;
                case ObjectCreationExpressionSyntax objectCreation when objectCreation.Initializer != null:
                    SetAlignmentBlockOperation(list, objectCreation, objectCreation.Initializer);
                    return;
                case AnonymousObjectCreationExpressionSyntax anonymousObjectCreation:
                    SetAlignmentBlockOperation(list, anonymousObjectCreation.NewKeyword, anonymousObjectCreation.OpenBraceToken, anonymousObjectCreation.CloseBraceToken, IndentBlockOption.RelativeToFirstTokenOnBaseTokenLine);
                    return;
                case ArrayCreationExpressionSyntax arrayCreation when arrayCreation.Initializer != null:
                    SetAlignmentBlockOperation(list, arrayCreation.NewKeyword, arrayCreation.Initializer.OpenBraceToken, arrayCreation.Initializer.CloseBraceToken, IndentBlockOption.RelativeToFirstTokenOnBaseTokenLine);
                    return;
                case ImplicitArrayCreationExpressionSyntax implicitArrayCreation when implicitArrayCreation.Initializer != null:
                    SetAlignmentBlockOperation(list, implicitArrayCreation.NewKeyword, implicitArrayCreation.Initializer.OpenBraceToken, implicitArrayCreation.Initializer.CloseBraceToken, IndentBlockOption.RelativeToFirstTokenOnBaseTokenLine);
                    return;
                case CSharpSyntaxNode syntaxNode when syntaxNode.IsKind(SyntaxKind.SwitchExpression):
                    SetAlignmentBlockOperation(
                        list,
                        syntaxNode.GetFirstToken(),
                        syntaxNode.ChildNodesAndTokens().First(child => child.IsKind(SyntaxKind.OpenBraceToken)).AsToken(),
                        syntaxNode.ChildNodesAndTokens().Last(child => child.IsKind(SyntaxKind.CloseBraceToken)).AsToken(),
                        IndentBlockOption.RelativeToFirstTokenOnBaseTokenLine);
                    return;
            }
        }

        private void SetAlignmentBlockOperation(List<IndentBlockOperation> list, SyntaxNode baseNode, SyntaxNode body)
        {
            var option = IndentBlockOption.RelativeToFirstTokenOnBaseTokenLine;

            var baseToken = baseNode.GetFirstToken(includeZeroWidth: true);
            var firstToken = body.GetFirstToken(includeZeroWidth: true);
            var lastToken = body.GetLastToken(includeZeroWidth: true);

            SetAlignmentBlockOperation(list, baseToken, firstToken, lastToken, option);
        }

        private void AddBlockIndentationOperation(List<IndentBlockOperation> list, SyntaxNode node, AnalyzerConfigOptions options)
        {
            var bracePair = node.GetBracePair();

            // don't put block indentation operation if the block only contains label statement
            if (!bracePair.IsValidBracePair())
            {
                return;
            }

            // for lambda, set alignment around braces so that users can put brace wherever they want
            if (node.IsLambdaBodyBlock() || node.IsAnonymousMethodBlock() || node.IsKind(SyntaxKind.PropertyPatternClause) || node.IsKind(SyntaxKind.SwitchExpression))
            {
                AddAlignmentBlockOperationRelativeToFirstTokenOnBaseTokenLine(list, bracePair);
            }

            // For ArrayInitializationExpression, set indent to relative to the open brace so the content is properly indented
            if (node.IsKind(SyntaxKind.ArrayInitializerExpression) && node.Parent != null && node.Parent.IsKind(SyntaxKind.ArrayCreationExpression))
            {
                AddAlignmentBlockOperationRelativeToFirstTokenOnBaseTokenLine(list, bracePair);
            }

            if (node is BlockSyntax && !options.GetOption(CSharpFormattingOptions.IndentBlock))
            {
                // do not add indent operation for block
                return;
            }

            if (node is SwitchStatementSyntax && !options.GetOption(CSharpFormattingOptions.IndentSwitchSection))
            {
                // do not add indent operation for switch statement
                return;
            }

            AddIndentBlockOperation(list, bracePair.Item1.GetNextToken(includeZeroWidth: true), bracePair.Item2.GetPreviousToken(includeZeroWidth: true));
        }

        private void AddAlignmentBlockOperationRelativeToFirstTokenOnBaseTokenLine(List<IndentBlockOperation> list, ValueTuple<SyntaxToken, SyntaxToken> bracePair)
        {
            var option = IndentBlockOption.RelativeToFirstTokenOnBaseTokenLine;
            SetAlignmentBlockOperation(list, bracePair.Item1, bracePair.Item1.GetNextToken(includeZeroWidth: true), bracePair.Item2, option);
        }

        private void AddEmbeddedStatementsIndentationOperation(List<IndentBlockOperation> list, SyntaxNode node)
        {
            // increase indentation - embedded statement cases
            if (node is IfStatementSyntax ifStatement && ifStatement.Statement != null && !(ifStatement.Statement is BlockSyntax))
            {
                AddEmbeddedStatementsIndentationOperation(list, ifStatement.Statement);
                return;
            }

            if (node is ElseClauseSyntax elseClause && elseClause.Statement != null)
            {
                if (!(elseClause.Statement is BlockSyntax || elseClause.Statement is IfStatementSyntax))
                {
                    AddEmbeddedStatementsIndentationOperation(list, elseClause.Statement);
                }

                return;
            }

            if (node is WhileStatementSyntax whileStatement && whileStatement.Statement != null && !(whileStatement.Statement is BlockSyntax))
            {
                AddEmbeddedStatementsIndentationOperation(list, whileStatement.Statement);
                return;
            }

            if (node is ForStatementSyntax forStatement && forStatement.Statement != null && !(forStatement.Statement is BlockSyntax))
            {
                AddEmbeddedStatementsIndentationOperation(list, forStatement.Statement);
                return;
            }

            if (node is CommonForEachStatementSyntax foreachStatement && foreachStatement.Statement != null && !(foreachStatement.Statement is BlockSyntax))
            {
                AddEmbeddedStatementsIndentationOperation(list, foreachStatement.Statement);
                return;
            }

            if (node is UsingStatementSyntax usingStatement && usingStatement.Statement != null && !(usingStatement.Statement is BlockSyntax || usingStatement.Statement is UsingStatementSyntax))
            {
                AddEmbeddedStatementsIndentationOperation(list, usingStatement.Statement);
                return;
            }

            if (node is FixedStatementSyntax fixedStatement && fixedStatement.Statement != null && !(fixedStatement.Statement is BlockSyntax || fixedStatement.Statement is FixedStatementSyntax))
            {
                AddEmbeddedStatementsIndentationOperation(list, fixedStatement.Statement);
                return;
            }

            if (node is DoStatementSyntax doStatement && doStatement.Statement != null && !(doStatement.Statement is BlockSyntax))
            {
                AddEmbeddedStatementsIndentationOperation(list, doStatement.Statement);
                return;
            }

            if (node is LockStatementSyntax lockStatement && lockStatement.Statement != null && !(lockStatement.Statement is BlockSyntax))
            {
                AddEmbeddedStatementsIndentationOperation(list, lockStatement.Statement);
                return;
            }
        }

        private void AddEmbeddedStatementsIndentationOperation(List<IndentBlockOperation> list, StatementSyntax statement)
        {
            var firstToken = statement.GetFirstToken(includeZeroWidth: true);
            var lastToken = statement.GetLastToken(includeZeroWidth: true);

            if (lastToken.IsMissing)
            {
                // embedded statement is not done, consider following as part of embedded statement
                AddIndentBlockOperation(list, firstToken, lastToken);
            }
            else
            {
                // embedded statement is done
                AddIndentBlockOperation(list, firstToken, lastToken, TextSpan.FromBounds(firstToken.FullSpan.Start, lastToken.FullSpan.End));
            }
        }
    }
}
