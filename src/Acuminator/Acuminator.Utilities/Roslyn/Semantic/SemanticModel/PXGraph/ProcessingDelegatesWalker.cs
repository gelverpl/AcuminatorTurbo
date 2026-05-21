using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Acuminator.Utilities.Common;
using Acuminator.Utilities.Roslyn.Syntax;
using Acuminator.Utilities.Roslyn.Walkers;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Acuminator.Utilities.Roslyn.Semantic.PXGraph
{
	internal class ProcessingDelegatesWalker : DelegatesWalkerBase
	{
		private int _currentDeclarationOrder;
		private readonly HashSet<ISymbol> _processingViewSymbols;

		public Dictionary<string, List<ProcessingDelegateInfo>> ParametersDelegateListByView { get; } = new();

		public Dictionary<string, List<ProcessingDelegateInfo>> ProcessDelegateListByView { get; } = new();
		
		public Dictionary<string, List<ProcessingDelegateInfo>> FinallyProcessDelegateListByView { get; } = new();

		public ProcessingDelegatesWalker(PXContext pxContext, HashSet<ISymbol> processingViewSymbols, CancellationToken cancellation) : 
									base(pxContext, cancellation)
		{
			_processingViewSymbols = processingViewSymbols.CheckIfNull();
		}

		public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
		{
			ThrowIfCancellationRequested();
			base.VisitMethodDeclaration(node);
		}

		public override void VisitInvocationExpression(InvocationExpressionSyntax node)
		{
			ThrowIfCancellationRequested();

			if (node.ArgumentList == null || node.Expression is not MemberAccessExpressionSyntax memberAccess)
				return;

			var viewSymbol = GetSymbol<ISymbol>(memberAccess.Expression);

			if (viewSymbol == null)
				return;

			var isProcessingView = _processingViewSymbols.Contains(viewSymbol);

			if (!isProcessingView)
				return;

			var viewName = viewSymbol.Name;
			var methodSymbol = GetSymbol<IMethodSymbol>(memberAccess.Name);

			if (methodSymbol == null)
				return;

			bool isSetProcessDelegate =
					PxContext.PXProcessingBase.SetProcessDelegate.Contains<IMethodSymbol>(methodSymbol.OriginalDefinition, 
																							SymbolEqualityComparer.Default);
			bool isSetAsyncProcessDelegate =
					PxContext.PXProcessingBase.SetAsyncProcessDelegate.Contains<IMethodSymbol>(methodSymbol.OriginalDefinition,
																								SymbolEqualityComparer.Default);
			if (isSetProcessDelegate || isSetAsyncProcessDelegate)
			{
				AnalyzeSetProcessDelegate(viewName, node.ArgumentList);
				base.VisitInvocationExpression(node, methodSymbol);
				return;
			}

			bool isSetParametersDelegate =
					PxContext.PXProcessingBase.SetParametersDelegate.Equals(methodSymbol.OriginalDefinition, SymbolEqualityComparer.Default);

			if (isSetParametersDelegate)
			{
				AnalyzeSetParametersDelegate(viewName, node.ArgumentList);
			}

			base.VisitInvocationExpression(node, methodSymbol);
		}

		private void AnalyzeSetParametersDelegate(string viewName, ArgumentListSyntax argumentList)
		{
			ThrowIfCancellationRequested();

			var handlerNode = argumentList.Arguments.FirstOrDefault()?.Expression;
			if (handlerNode == null)
			{
				return;
			}

			var parametersDelegateInfo = GetDelegateInfo(handlerNode);

			if (parametersDelegateInfo == null)
				return;

			if (ParametersDelegateListByView.TryGetValue(viewName, out List<ProcessingDelegateInfo> parameterDelegates))
			{
				parameterDelegates.Add(parametersDelegateInfo);
			}
			else
			{
				parameterDelegates = new List<ProcessingDelegateInfo>(capacity: 1) { parametersDelegateInfo };
				ParametersDelegateListByView.Add(viewName, parameterDelegates);
			}
		}

		private void AnalyzeSetProcessDelegate(string viewName, ArgumentListSyntax argumentList)
		{
			ThrowIfCancellationRequested();

			ExpressionSyntax? handlerNode = argumentList.Arguments.FirstOrDefault()?.Expression;
			if (handlerNode == null)
			{
				return;
			}

			var processDelegateInfo = GetDelegateInfo(handlerNode);

			if (processDelegateInfo != null)
			{
				if (ProcessDelegateListByView.TryGetValue(viewName, out List<ProcessingDelegateInfo> processingDelegates))
					processingDelegates.Add(processDelegateInfo);
				else
				{
					processingDelegates = new List<ProcessingDelegateInfo>(capacity: 1) { processDelegateInfo };
					ProcessDelegateListByView.Add(viewName, processingDelegates);
				}
			}

			if (argumentList!.Arguments.Count <= 1)
				return;

			var finallyHandlerNode = argumentList.Arguments[1].Expression;

			if (finallyHandlerNode == null)
				return;

			var finallyHandlerInfo = GetDelegateInfo(finallyHandlerNode);

			if (finallyHandlerInfo == null)
				return;

			if (FinallyProcessDelegateListByView.TryGetValue(viewName, out List<ProcessingDelegateInfo> finallyDelegates))
				finallyDelegates.Add(finallyHandlerInfo);
			else
			{
				finallyDelegates = new List<ProcessingDelegateInfo>(capacity: 1) { finallyHandlerInfo };
				FinallyProcessDelegateListByView.Add(viewName, finallyDelegates);
			}
		}

		private ProcessingDelegateInfo? GetDelegateInfo(ExpressionSyntax handlerNode)
		{
			ThrowIfCancellationRequested();

			var (delegateSymbol, delegateNode) = GetDelegateSymbolAndNode(handlerNode);

			if (delegateSymbol == null || delegateNode == null)  // Skip analysis for unrecognized arguments
				return null;

			var processingDelegateInfo = new ProcessingDelegateInfo(delegateNode, delegateSymbol, _currentDeclarationOrder);

			_currentDeclarationOrder++;
			return processingDelegateInfo;
		}
	}
}
