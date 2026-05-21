using System.Collections.Generic;
using System.Collections.Immutable;

using Acuminator.Analyzers.StaticAnalysis.EventHandlers;
using Acuminator.Utilities;
using Acuminator.Utilities.Common;
using Acuminator.Utilities.Roslyn;
using Acuminator.Utilities.Roslyn.Semantic;
using Acuminator.Utilities.Roslyn.Semantic.AcumaticaEvents;
using Acuminator.Utilities.Roslyn.Syntax;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Acuminator.Analyzers.StaticAnalysis.RaiseExceptionHandling
{
	public class RaiseExceptionHandlingInEventHandlersAnalyzer : LooseEventHandlerAggregatedAnalyzerBase
	{
		private static readonly List<EventType> _analyzedEventTypesNonIsvMode =
		[
			EventType.FieldSelecting,
			EventType.FieldUpdating,
		];

		private static readonly List<EventType> _analyzedEventTypesIsvMode =
		[
			EventType.FieldDefaulting,
			EventType.FieldSelecting,
			EventType.FieldUpdating,
			EventType.RowSelecting,
			EventType.RowPersisted
		];

		public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
			ImmutableArray.Create(
				Descriptors.PX1075_RaiseExceptionHandlingInEventHandlers,
				Descriptors.PX1075_RaiseExceptionHandlingInEventHandlers_NonISV
			);

		public override bool ShouldAnalyze(PXContext pxContext, EventHandlerLooseInfo eventHandlerInfo)
		{
			if (!base.ShouldAnalyze(pxContext, eventHandlerInfo))
				return false;

			var supportedEventTypes = pxContext.CodeAnalysisSettings.IsvSpecificAnalyzersEnabled
				? _analyzedEventTypesIsvMode
				: _analyzedEventTypesNonIsvMode;

			return supportedEventTypes.Contains(eventHandlerInfo.Type);
		}

		public override void Analyze(SymbolAnalysisContext context, PXContext pxContext, EventHandlerLooseInfo eventHandlerInfo)
		{
			context.CancellationToken.ThrowIfCancellationRequested();

			if (context.Symbol is not IMethodSymbol methodSymbol ||
				methodSymbol.GetSyntax(context.CancellationToken) is not CSharpSyntaxNode methodSyntax)
			{
				return;
			}

			var walker = new Walker(context, pxContext, eventHandlerInfo.Type);
			methodSyntax.Accept(walker);
		}


		private class Walker : NestedInvocationWalker
		{
			private readonly SymbolAnalysisContext _context;
			private readonly string _eventTypeName;
			private readonly DiagnosticDescriptor _px1075DiagnosticDescriptor;

			public Walker(SymbolAnalysisContext context, PXContext pxContext, EventType eventType)
				: base(pxContext, context.CancellationToken)
			{
				_context = context;
				_eventTypeName = eventType.ToString();
				_px1075DiagnosticDescriptor = pxContext.CodeAnalysisSettings.IsvSpecificAnalyzersEnabled
					? Descriptors.PX1075_RaiseExceptionHandlingInEventHandlers
					: Descriptors.PX1075_RaiseExceptionHandlingInEventHandlers_NonISV;
			}

			public override void VisitInvocationExpression(InvocationExpressionSyntax node)
			{
				ThrowIfCancellationRequested();

				var methodSymbol = GetSymbol<IMethodSymbol>(node);
				methodSymbol = methodSymbol?.OriginalDefinition?.OverriddenMethod ?? methodSymbol?.OriginalDefinition;

				if (methodSymbol == null || !PxContext.PXCache.RaiseExceptionHandling.Contains(methodSymbol, SymbolEqualityComparer.Default))
				{
					base.VisitInvocationExpression(node, methodSymbol);
					return;
				}

				ReportDiagnostic(_context.ReportDiagnostic, _px1075DiagnosticDescriptor, node, _eventTypeName);
			}
		}
	}
}