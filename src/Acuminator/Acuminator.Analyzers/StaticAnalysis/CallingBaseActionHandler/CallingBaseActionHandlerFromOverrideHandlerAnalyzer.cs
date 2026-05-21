using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Acuminator.Analyzers.StaticAnalysis.PXGraph;
using Acuminator.Utilities.Common;
using Acuminator.Utilities.Roslyn;
using Acuminator.Utilities.Roslyn.Semantic;
using Acuminator.Utilities.Roslyn.Semantic.PXGraph;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Acuminator.Analyzers.StaticAnalysis.CallingBaseActionHandler
{
	public class CallingBaseActionHandlerFromOverrideHandlerAnalyzer : PXGraphAggregatedAnalyzerBase
	{
		public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
			ImmutableArray.Create(Descriptors.PX1091_CausingStackOverflowExceptionInBaseActionHandlerInvocation);

		public override bool ShouldAnalyze(PXContext pxContext, PXGraphEventSemanticModel graph) =>
			base.ShouldAnalyze(pxContext, graph) && graph.GraphType == GraphType.PXGraphExtension;

		public override void Analyze(SymbolAnalysisContext context, PXContext pxContext, PXGraphEventSemanticModel graphExtension)
		{
			context.CancellationToken.ThrowIfCancellationRequested();

			var redeclaredActionNamesHashSet = graphExtension.Actions
				.Where(action => graphExtension.Symbol.Equals(action.Symbol?.ContainingSymbol, SymbolEqualityComparer.Default) && action.Base != null)
				.Select(action => action.Symbol.Name)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);

			var redeclaredDelegatesWithoutActionsList = graphExtension.ActionDelegates
				.Where(actionDelegate => graphExtension.Symbol.Equals(actionDelegate.Symbol?.ContainingSymbol, SymbolEqualityComparer.Default) && 
										 actionDelegate.Base != null && !redeclaredActionNamesHashSet.Contains(actionDelegate.Symbol.Name))
				.ToList();

			var baseDelegatesHashSet = redeclaredDelegatesWithoutActionsList
				.SelectMany(aDelegate => aDelegate.JustOverriddenItems()
												  .Select(baseDelegate => baseDelegate.Symbol))
				.ToHashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

			var baseActionsHashSet = redeclaredDelegatesWithoutActionsList
				.SelectMany(actionDelegate => graphExtension.ActionsByNames[actionDelegate.Symbol.Name]
															.ThisAndOverriddenItems()
															.Select(action => action.Symbol))
				.ToHashSet(SymbolEqualityComparer.Default);

			var walker = new Walker(context, pxContext, baseActionsHashSet, baseDelegatesHashSet);

			foreach (ActionDelegateInfo actionDelegate in redeclaredDelegatesWithoutActionsList)
			{
				context.CancellationToken.ThrowIfCancellationRequested();
				walker.CheckActionDelegate(actionDelegate);
			}
		}

		private class Walker : NestedInvocationWalker
		{
			private readonly SymbolAnalysisContext _context;
			private readonly HashSet<ISymbol> _baseActions;
			private readonly HashSet<IMethodSymbol> _baseDelegates;

			private ActionDelegateInfo? _currentActionDelegate;

			public Walker(SymbolAnalysisContext context, PXContext pxContext, HashSet<ISymbol> baseActions, HashSet<IMethodSymbol> baseDelegates)
				: base(pxContext, context.CancellationToken)
			{
				_baseActions = baseActions.CheckIfNull();
				_baseDelegates = baseDelegates.CheckIfNull();
				_context = context;
			}

			public void CheckActionDelegate(ActionDelegateInfo actionDelegate)
			{
				try
				{
					_currentActionDelegate = actionDelegate;
					Visit(_currentActionDelegate.Node);
				}
				finally
				{
					_currentActionDelegate = null;
				}
			}

			public override void VisitInvocationExpression(InvocationExpressionSyntax invocationNode)
			{
				ThrowIfCancellationRequested();

				var methodSymbol = GetSymbol<IMethodSymbol>(invocationNode);
				if (methodSymbol == null)
				{
					return;
				}

				// Case Base.someActionHandler(adapter)
				if (IsDirectCallToBaseActionDelegate(invocationNode, methodSymbol))
				{
					ReportDiagnostic(_context.ReportDiagnostic,
						Descriptors.PX1091_CausingStackOverflowExceptionInBaseActionHandlerInvocation,
						invocationNode);

					return;
				}

				var originalMethodSymbol = methodSymbol.OriginalDefinition?.OverriddenMethod ?? methodSymbol.OriginalDefinition;

				if (originalMethodSymbol == null)
					return;

				// Case Base.SomeAction.Press(adapter)
				if (PxContext.PXAction.Press.Contains<IMethodSymbol>(originalMethodSymbol, SymbolEqualityComparer.Default) &&
					invocationNode.Expression is MemberAccessExpressionSyntax memberAccess && memberAccess.Expression != null)
				{
					var expressionSymbol = GetSymbol<ISymbol>(memberAccess.Expression);

					if (expressionSymbol != null && _baseActions.Contains(expressionSymbol))
					{
						ReportDiagnostic(_context.ReportDiagnostic,
							Descriptors.PX1091_CausingStackOverflowExceptionInBaseActionHandlerInvocation,
							invocationNode);

						return;
					}
				}

				base.VisitInvocationExpression(invocationNode, methodSymbol);
			}

			/// <summary>
			/// Check if the call is a direct call to the base action delegate that looks like this:<br/>
			/// <c>Base.someActionDelegate(adapter)</c>
			/// </summary>
			/// <param name="invocationNode">The invocation node.</param>
			/// <param name="calledMethod">The called method.</param>
			/// <returns>
			/// True for a direct call to the base action delegate.
			/// </returns>
			private bool IsDirectCallToBaseActionDelegate(InvocationExpressionSyntax invocationNode, IMethodSymbol calledMethod)
			{
				if (!_baseDelegates.Contains(calledMethod))
					return false;

				if (_currentActionDelegate?.Symbol.IsOverride != true)
					return true;

				// For action handler overrides we must check that this is not an access via base keyword like this:
				// base.someActionHandler(adapter);
				return invocationNode.Expression is not MemberAccessExpressionSyntax memberAccessExpressionNode ||
					   memberAccessExpressionNode.Expression is not BaseExpressionSyntax;
			}
		}
	}
}