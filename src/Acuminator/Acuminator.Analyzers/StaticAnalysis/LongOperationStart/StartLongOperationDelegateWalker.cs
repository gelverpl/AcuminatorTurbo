using System.Collections.Generic;
using System.Threading;
using Acuminator.Utilities.Roslyn.Constants;
using Acuminator.Utilities.Roslyn.Semantic;
using Acuminator.Utilities.Roslyn.Walkers;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Acuminator.Analyzers.StaticAnalysis.LongOperationStart
{
	public class StartLongOperationDelegateWalker : DelegatesWalkerBase
	{
		private readonly Dictionary<SyntaxNode, (ISymbol? DelegateSymbol, SyntaxNode? DelegateNode)> _delegateInfos = [];

		public IReadOnlyDictionary<SyntaxNode, (ISymbol? DelegateSymbol, SyntaxNode? DelegateNode)> DelegateInfosByNode => _delegateInfos;

		public IReadOnlyCollection<(ISymbol? DelegateSymbol, SyntaxNode? DelegateNode)> DelegateInfos => _delegateInfos.Values;

		public StartLongOperationDelegateWalker(PXContext pxContext, CancellationToken cancellation)
			: base(pxContext, cancellation)
		{
		}

		public override void VisitInvocationExpression(InvocationExpressionSyntax node)
		{
			ThrowIfCancellationRequested();

			if (true)
			{
				string? methodName = node.Expression switch
				{
					MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
					GenericNameSyntax generic => generic.Identifier.ValueText,
					IdentifierNameSyntax identifier => identifier.Identifier.ValueText, // we need this if for static using
					_ => null
				};

				if (methodName is null || !PxContext.AsyncOperations.AllMethodsStartingLongRunNames.Contains(methodName))
				{
					base.VisitInvocationExpression(node);
					return;
				}
			}

			IMethodSymbol? methodSymbol = GetSymbol<IMethodSymbol>(node);

			if (methodSymbol == null || node.ArgumentList?.Arguments.Count is null or 0 ||
				!PxContext.AsyncOperations.AllMethodsStartingLongRun.Contains(methodSymbol.OriginalDefinition))
			{
				base.VisitInvocationExpression(node);
				return;
			}

			var delegateArgument = GetLongRunDelegateArgument(methodSymbol, node);

			if (delegateArgument != null)
			{
				var delegateSymbolAndBody = GetDelegateSymbolAndNode(delegateArgument);

				if (delegateSymbolAndBody.DelegateNode != null && !_delegateInfos.ContainsKey(delegateSymbolAndBody.DelegateNode))
				{
					_delegateInfos.Add(delegateSymbolAndBody.DelegateNode, delegateSymbolAndBody);
				}
			}

			base.VisitInvocationExpression(node);
		}

		private ExpressionSyntax? GetLongRunDelegateArgument(IMethodSymbol methodSymbol, InvocationExpressionSyntax methodNode)
		{
			var arguments = methodNode.ArgumentList.Arguments;

			switch (arguments.Count)
			{
				case 0:
					return null;

				case 1:
					if (methodSymbol.Name == DelegateNames.Async.Await ||
						methodSymbol.IsDeclaredInType(PxContext.AsyncOperations.IGraphLongOperationManager))
					{
						return arguments[0].Expression;
					}
					else
						return null;

				default:
				{
					var firstArgument = methodSymbol.Name == DelegateNames.Async.Await
											? arguments[0].Expression
											: arguments[1].Expression;
					return firstArgument;
				}
			}
		}
	}
}