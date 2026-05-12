using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Acuminator.Utilities.Common;
using Acuminator.Utilities.Roslyn.Constants;

using Microsoft.CodeAnalysis;

namespace Acuminator.Utilities.Roslyn.Semantic.Symbols
{
	public class AsyncOperationsSymbols : SymbolsSetBase
	{
		public ImmutableArray<IMethodSymbol> StartOperation_PXLongOperation { get; }

		public ImmutableArray<IMethodSymbol> AsyncOperationsFromILongOperationManager { get; }

		public ImmutableArray<IMethodSymbol> AsyncOperationsFromIGraphLongOperationManager { get; }

		public INamedTypeSymbol PXLongOperation { get; }

		public INamedTypeSymbol? ILongOperationManager { get; }

		public INamedTypeSymbol? IGraphLongOperationManager { get; }

		public INamedTypeSymbol? Task { get; }

		public INamedTypeSymbol? Task_Generic { get; }

		public INamedTypeSymbol? ValueTask { get; }

		public INamedTypeSymbol? ValueTask_Generic { get; }

		/// <summary>
		/// All methods that can start async long-running operations.
		/// </summary>
		public ImmutableHashSet<IMethodSymbol> AllMethodsStartingLongRun { get; }
		
		/// <summary>
		/// Names of all methods that can start async long-running operations.
		/// </summary>
		public ImmutableHashSet<string> AllMethodsStartingLongRunNames { get; }

		internal AsyncOperationsSymbols(Compilation compilation) : base(compilation)
		{
			Task 			  = Compilation.GetTypeByMetadataName(TypeFullNames.DotNetTypes.Task);
			Task_Generic 	  = Compilation.GetTypeByMetadataName(TypeFullNames.DotNetTypes.Task_Generic);
			ValueTask 		  = Compilation.GetTypeByMetadataName(TypeFullNames.DotNetTypes.ValueTask);
			ValueTask_Generic = Compilation.GetTypeByMetadataName(TypeFullNames.DotNetTypes.ValueTask_Generic);

			PXLongOperation		  = Compilation.GetTypeByMetadataName(TypeFullNames.Async.PXLongOperation)!;
			ILongOperationManager = Compilation.GetTypeByMetadataName(TypeFullNames.Async.ILongOperationManager) ??
									Compilation.GetTypeByMetadataName(TypeFullNames.Async.ILongOperationManagerOld);
			IGraphLongOperationManager = Compilation.GetTypeByMetadataName(TypeFullNames.Async.IGraphLongOperationManager) ??
										 Compilation.GetTypeByMetadataName(TypeFullNames.Async.IGraphLongOperationManagerOld);

			var allStartOperationMethods = 
				PXLongOperation.GetMethods(DelegateNames.Async.StartOperation)
							   .Where(method => method.DeclaredAccessibility is not (Accessibility.Private or Accessibility.Protected))
							   .ToList(capacity: 16);
			StartOperation_PXLongOperation = allStartOperationMethods.ToImmutableArray();

			if (ILongOperationManager != null)
			{
				AsyncOperationsFromILongOperationManager = GetMethodsFromLongOperationManagerType(ILongOperationManager)
																.ToImmutableArray();
				allStartOperationMethods.AddRange(AsyncOperationsFromILongOperationManager);
			}
			else
			{
				AsyncOperationsFromILongOperationManager = [];
			}

			if (IGraphLongOperationManager != null)
			{
				AsyncOperationsFromIGraphLongOperationManager = GetMethodsFromLongOperationManagerType(IGraphLongOperationManager)
																	.ToImmutableArray();
				allStartOperationMethods.AddRange(AsyncOperationsFromIGraphLongOperationManager);
			}
			else
			{
				AsyncOperationsFromIGraphLongOperationManager = [];
			}

			AllMethodsStartingLongRun = allStartOperationMethods.ToImmutableHashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
			AllMethodsStartingLongRunNames = AllMethodsStartingLongRun.Select(o => o.OriginalDefinition.Name).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
		}

		private static IEnumerable<IMethodSymbol> GetMethodsFromLongOperationManagerType(INamedTypeSymbol longOperationManagerType)
		{
			var startOperationMethods 	   = longOperationManagerType.GetMethods(DelegateNames.Async.StartOperation);
			var startAsyncOperationMethods = longOperationManagerType.GetMethods(DelegateNames.Async.StartAsyncOperation);
			var awaitMethods 			   = longOperationManagerType.GetMethods(DelegateNames.Async.Await);

			return startOperationMethods.Concat(startAsyncOperationMethods)
										.Concat(awaitMethods)
										.Where(method => method.DeclaredAccessibility is not (Accessibility.Private or Accessibility.Protected));
		}
	}
}
