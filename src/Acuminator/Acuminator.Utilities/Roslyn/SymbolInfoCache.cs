using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Acuminator.Utilities.Roslyn;

/// <summary>
/// Caches Roslyn symbol lookup results for expression syntax nodes visited by a syntax walker.
/// </summary>
public sealed class SymbolInfoCache
{
	private readonly Dictionary<ExpressionSyntax, SymbolInfo?> _map = new();

	/// <summary>
	/// Gets the cached symbol information for the specified expression, or creates and stores it using the provided factory.
	/// Pass caller state via <paramref name="caller"/> and use a <see langword="static"/> lambda to avoid closure allocations.
	/// </summary>
	public SymbolInfo? GetOrCreate<TCaller>(ExpressionSyntax key, TCaller caller, Func<ExpressionSyntax, TCaller, SymbolInfo?> factory)
	{
		if (_map.TryGetValue(key, out SymbolInfo? cached))
		{
			return cached;
		}

		SymbolInfo? potentialValue = factory(key, caller);
		_map[key] = potentialValue;

		return potentialValue;
	}
}

