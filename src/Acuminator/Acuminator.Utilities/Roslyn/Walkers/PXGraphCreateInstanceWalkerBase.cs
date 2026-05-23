using Acuminator.Utilities.Common;
using Acuminator.Utilities.Roslyn.Semantic;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Acuminator.Utilities.Roslyn.Walkers
{
	/// <summary>
	/// A base class for recursive walkers that report graph creation.
	/// </summary>
	public abstract class PXGraphCreateInstanceWalkerBase : NestedInvocationWalker
	{
		protected SymbolAnalysisContext Context { get; }

		protected abstract DiagnosticDescriptor Descriptor { get; }

		public PXGraphCreateInstanceWalkerBase(SymbolAnalysisContext context, PXContext pxContext) : base(pxContext, context.CancellationToken)
		{
			Context = context;
		}

		public override void VisitInvocationExpression(InvocationExpressionSyntax node)
		{
			ThrowIfCancellationRequested();

			IMethodSymbol? methodSymbol = GetSymbol<IMethodSymbol>(node);

			if (methodSymbol != null &&
				PxContext.PXGraph.CreateInstance.Contains<IMethodSymbol>(methodSymbol.ConstructedFrom, SymbolEqualityComparer.Default))
			{
				ReportDiagnostic(Context.ReportDiagnostic, Descriptor, node);
			}

			base.VisitInvocationExpression(node, methodSymbol);
		}

		public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
		{
			ThrowIfCancellationRequested();

			if (node.Parent is InvocationExpressionSyntax invocation && invocation.Expression == node)
			{
				base.VisitMemberAccessExpression(node);
				return;
			}

			IMethodSymbol? methodSymbol = GetSymbol<IMethodSymbol>(node);

			if (methodSymbol != null &&
				PxContext.PXGraph.CreateInstance.Contains<IMethodSymbol>(methodSymbol.ConstructedFrom, SymbolEqualityComparer.Default))
			{
				ReportDiagnostic(Context.ReportDiagnostic, Descriptor, node);
				return;
			}

			base.VisitMemberAccessExpression(node);
		}

		/// <summary>
		/// Called when the visitor visits a <see cref="ObjectCreationExpressionSyntax"/> node which represents a constructor call via "<c><see langword="new"/> MyGraph()</c>".<br/>
		/// </summary>
		/// <param name="node">The node.</param>
		public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
		{
			ThrowIfCancellationRequested();

			ITypeSymbol? createdObjectType = GetSymbol<ITypeSymbol>(node.Type);

			if (createdObjectType != null && createdObjectType.IsPXGraph(PxContext))
			{
				ReportDiagnostic(Context.ReportDiagnostic, Descriptor, node);
			}
			else
			{
				base.VisitObjectCreationExpression(node);
			}
		}

		/// <summary>
		/// Called when the visitor visits a <see cref="ImplicitObjectCreationExpressionSyntax"/> node which represents a constructor call via "<c><see langword="new"/>()</c>".<br/>
		/// </summary>
		/// <param name="node">The node.</param>
		public override void VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)
		{
			ThrowIfCancellationRequested();

			var constructor = GetSymbol<IMethodSymbol>(node);

			if (constructor == null || constructor.MethodKind != MethodKind.Constructor)
			{
				base.VisitImplicitObjectCreationExpression(node);
				return;
			}

			if (constructor?.ContainingType != null && constructor.ContainingType.IsPXGraph(PxContext))
			{
				ReportDiagnostic(Context.ReportDiagnostic, Descriptor, node);
			}
			else
			{
				base.VisitImplicitObjectCreationExpression(node);
			}
		}
	}
}
