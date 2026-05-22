using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;

using Acuminator.Utilities.Common;
using Acuminator.Utilities.Roslyn.Constants;

using Microsoft.CodeAnalysis;

namespace Acuminator.Utilities.Roslyn.Semantic.Dac;

public static class DacSymbolsHierarchyUtils
{
	/// <summary>
	/// Gets the base types of a given <paramref name="dacType"/> that may store DAC properties.<br/>
	/// This includes base types that do not implement IBqlTable interface because they still can declare shared properties.<br/>
	/// The base types are taken up to the <see cref="System.Object"/> or up to the <c>PX.Data.PXBqlTable</c> DAC base type introduced in Acumatica 2024r1.
	/// </summary>
	/// <param name="dacType">The DAC type to act on.</param>
	/// <param name="pxContext">Acumatica context.</param>
	/// <returns>
	/// A collection of <paramref name="dacType"/> base types that may store DAC properties.
	/// </returns>
	/// <remarks>
	/// This helper MUST be called only on DAC types. The behavior on non DAC types is undefined.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static IEnumerable<ITypeSymbol> GetDacBaseTypesThatMayStoreDacProperties(this ITypeSymbol dacType, PXContext pxContext) =>
		GetDacBaseTypesThatMayStoreDacProperties(dacType, pxContext, includeDacType: false);

	/// <summary>
	/// Gets the DAC type <paramref name="dacType"/> with its base types that may store DAC properties.<br/>
	/// This includes base types that do not implement IBqlTable interface because they still can declare shared properties.<br/>
	/// The base types are taken up to the <see cref="System.Object"/> or up to the <c>PX.Data.PXBqlTable</c> DAC base type introduced in Acumatica 2024r1.
	/// </summary>
	/// <param name="dacType">The DAC type to act on.</param>
	/// <param name="pxContext">Acumatica context.</param>
	/// <returns>
	/// A collection containing <paramref name="dacType"/> and its base types that may store DAC properties.
	/// </returns>
	/// <remarks>
	/// This helper MUST be called only on DAC types. The behavior on non DAC types is undefined.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static IEnumerable<ITypeSymbol> GetDacWithBaseTypesThatMayStoreDacProperties(this ITypeSymbol dacType, PXContext pxContext) =>
		GetDacBaseTypesThatMayStoreDacProperties(dacType, pxContext, includeDacType: true);

	private static IEnumerable<ITypeSymbol> GetDacBaseTypesThatMayStoreDacProperties(ITypeSymbol dacType, PXContext pxContext, bool includeDacType)
	{
		dacType.ThrowOnNull();
		var pxBqlTable = pxContext.CheckIfNull().PXBqlTable;

		// Optimization for hot path - most DACs have trivial type hierarchy
		if (dacType.BaseType == null || dacType.BaseType.SpecialType == SpecialType.System_Object ||
			(pxBqlTable != null && dacType.BaseType.Equals(pxBqlTable, SymbolEqualityComparer.Default)))
		{
			return includeDacType ? [dacType] : [];
		}

		IEnumerable<ITypeSymbol> dacHierarchy = dacType.GetBaseTypes();

		// This filter takes all DAC types with a check for the base PXBqlTable type of System.Object type 
		// instead of checking if the type implements IBqlTable interface. This is done to include a useful part of type hierarchy in a scenario 
		// where the base non DAC type which declares some shared fields, for instance, PX.Objects.TX.TaxDetail class.
		if (pxBqlTable != null)
		{
			dacHierarchy = dacHierarchy.TakeWhile(type => !type.Equals(pxBqlTable, SymbolEqualityComparer.Default) &&
						   type.SpecialType != SpecialType.System_Object);
		}
		else
			dacHierarchy = dacHierarchy.TakeWhile(type => type.SpecialType != SpecialType.System_Object);

		if (includeDacType)
			dacHierarchy = dacHierarchy.PrependItem(dacType);

		return dacHierarchy;
	}

	/// <summary>
	/// Gets the DAC extension type with its base types up to first met <c>PX.Data.PXCacheExtension</c>.
	/// </summary>
	/// <param name="extensionType">The DAC extension type to act on.</param>
	/// <returns/>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static IEnumerable<ITypeSymbol> GetDacExtensionWithBaseTypes(this ITypeSymbol extensionType) =>
		extensionType.GetBaseTypesAndThis()
					 .TakeWhile(type => !type.IsDacExtensionBaseType());

	/// <summary>
	/// Gets the DAC extension base types up to first met <c>PX.Data.PXCacheExtension</c>.
	/// </summary>
	/// <param name="extensionType">The DAC extension type to act on.</param>
	/// <returns/>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static IEnumerable<ITypeSymbol> GetDacExtensionBaseTypes(this ITypeSymbol extensionType) =>
		extensionType.GetBaseTypes()
					 .TakeWhile(type => !type.IsDacExtensionBaseType());


	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsDacExtensionBaseType([NotNullWhen(returnValue: true)] this ITypeSymbol? type) =>
		type?.Name == TypeNames.PXCacheExtension;

	/// <summary>
	/// Get chained DAC extension types from the PXCacheExtension base type's type arguments.
	/// </summary>
	/// <param name="pxDacExtensionBaseType">The PXCacheExtension base type to act on.</param>
	/// <param name="pxContext">Acumatica context.</param>
	/// <returns>
	/// Chained DAC extension types from the PXCacheExtension base type's type arguments.<br/>
	/// If there is a problem in any of the type arguments (e.g. not a PXCacheExtension), <see langword="null"/> is returned.
	/// </returns>
	/// <remarks>
	/// For performance reasons this method is unsafe and does not perform validation of the input <paramref name="pxDacExtensionBaseType"/> type.
	/// </remarks>
	internal static IReadOnlyList<ITypeSymbol>? GetChainedExtensionTypesFromPxCacheExtensionTypeArgsUnsafe(ITypeSymbol pxDacExtensionBaseType,
																										   PXContext pxContext)
	{
		if (pxDacExtensionBaseType is not INamedTypeSymbol namedDacExtensionBaseType)
			return [];

		var typeArguments = namedDacExtensionBaseType.TypeArguments;

		if (typeArguments.IsDefault || typeArguments.Length <= 1)
			return [];

		//Excluding DAC type: dacIndex is typeArguments.Length - 1;
		var extensions = new ITypeSymbol[typeArguments.Length - 1];

		for (int i = 0; i < extensions.Length; i++)
		{
			var chainedExtension = typeArguments[i];

			if (!chainedExtension.IsDacExtension(pxContext))
				return null;

			extensions[i] = chainedExtension;
		}

		return extensions;
	}
}
