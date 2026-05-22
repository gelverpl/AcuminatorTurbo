#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

using Acuminator.Utilities.Common;
using Acuminator.Utilities.Roslyn.Constants;
using Acuminator.Utilities.Roslyn.Semantic.PXGraph;
using Acuminator.Utilities.Roslyn.Syntax;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Acuminator.Utilities.Roslyn.Semantic
{
	public static class ITypeSymbolExtensions
	{
		private const char DefaultGenericArgsCountSeparator = '`';
		private const char DefaultNestedTypesSeparator = '+';

		/// <summary>
		/// Gets the base types and this in this collection. The types are returned from the most derived ones to the most base <see cref="Object"/> type
		/// </summary>
		/// <param name="type">The type to act on.</param>
		/// <returns/>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static BaseTypesEnumerable GetBaseTypesAndThis(this ITypeSymbol type) =>
			type.GetBaseTypesImplementation(includeThis: true);

		/// <summary>
		/// Gets the base types and this in this collection. The types are returned from the most derived ones to the most base <see cref="Object"/> type
		/// </summary>
		/// <param name="type">The type to act on.</param>
		/// <returns/>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static BaseTypesEnumerable GetBaseTypes(this ITypeSymbol type) =>
			type.GetBaseTypesImplementation(includeThis: false);

		/// <summary>
		/// Returns a <see cref="BaseTypesEnumerable"/> over the type's base-type chain.
		/// Returns a struct (not <see cref="IEnumerable{T}"/>) so internal callers in this file can
		/// <c>foreach</c> over it without boxing the enumerable or its enumerator.
		/// </summary>
		/// <remarks>
		/// Public callers via <see cref="GetBaseTypesAndThis"/>/<see cref="GetBaseTypes"/> still receive
		/// <see cref="IEnumerable{T}"/> — those wrappers box the struct, preserving the existing public contract.
		/// The rare <see cref="ITypeParameterSymbol"/> branch falls back to the slow allocating path and is not optimized.
		/// </remarks>
		private static BaseTypesEnumerable GetBaseTypesImplementation(this ITypeSymbol type, bool includeThis)
		{
			type.ThrowOnNull();

			if (type is ITypeParameterSymbol typeParameter)
			{
				// Rare path — allocations are acceptable here.
				// For a type parameter, "base types" are its generic constraints (e.g. `where T : SomeClass`).
				IEnumerable<ITypeSymbol> constraintTypes = typeParameter.GetAllConstraintTypes(includeInterfaces: false)
																	    .SelectMany(constraint => constraint.GetBaseTypesIterator(includeThis: true))
																	    .Distinct<ITypeSymbol>(SymbolEqualityComparer.Default);
				IEnumerable<ITypeSymbol> finalSequence = includeThis
					? constraintTypes.PrependItem(typeParameter)
					: constraintTypes;

				return new BaseTypesEnumerable(finalSequence);
			}

			// Hot path — no allocation. The struct walks the .BaseType chain directly.
			return new BaseTypesEnumerable(type, includeThis);
		}

		// Kept for the rare type-parameter branch above, where it is consumed by SelectMany.
		// Not used on the hot path — that lives in BaseTypesEnumerable.Enumerator.
		private static IEnumerable<ITypeSymbol> GetBaseTypesIterator(this ITypeSymbol typeToUse, bool includeThis)
		{
			var current = includeThis ? typeToUse : typeToUse.BaseType;

			while (current != null)
			{
				yield return current;
				current = current.BaseType;
			}
		}

		public static IEnumerable<INamedTypeSymbol> GetFlattenedNestedTypes(this ITypeSymbol type, CancellationToken cancellationToken)
		{
			type.ThrowOnNull();
			cancellationToken.ThrowIfCancellationRequested();
			return type.GetFlattenedNestedTypesImplementation(shouldWalkThroughNestedTypesPredicate: null, cancellationToken);			
		}

		public static IEnumerable<INamedTypeSymbol> GetFlattenedNestedTypes(this ITypeSymbol type, Func<ITypeSymbol, bool>? shouldWalkThroughNestedTypesPredicate, 
																			CancellationToken cancellationToken)
		{
			type.ThrowOnNull();
			cancellationToken.ThrowIfCancellationRequested();
			shouldWalkThroughNestedTypesPredicate.ThrowOnNull();

			return type.GetFlattenedNestedTypesImplementation(shouldWalkThroughNestedTypesPredicate, cancellationToken);
		}

		private static IEnumerable<INamedTypeSymbol> GetFlattenedNestedTypesImplementation(this ITypeSymbol type, 
																						   Func<ITypeSymbol, bool>? shouldWalkThroughNestedTypesPredicate,
																						   CancellationToken cancellationToken)
		{
			var nestedTypes = type.GetTypeMembers();

			if (nestedTypes.IsDefaultOrEmpty)
				yield break;

			var typesQueue = new Queue<INamedTypeSymbol>(nestedTypes);

			while (typesQueue.Count > 0)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var currentType = typesQueue.Dequeue();
				bool shouldWalkThroughChildNestedTypes = shouldWalkThroughNestedTypesPredicate?.Invoke(currentType) ?? true;

				if (shouldWalkThroughChildNestedTypes)
				{
					var declaredNestedTypes = currentType.GetTypeMembers();

					if (!declaredNestedTypes.IsDefaultOrEmpty)
					{
						foreach (var nestedType in declaredNestedTypes)
						{
							typesQueue.Enqueue(nestedType);
						}
					}
				}

				yield return currentType;
			}
		}

		public static IEnumerable<ITypeSymbol> GetContainingTypesAndThis(this ITypeSymbol type)
		{
			var current = type;

			while (current != null)
			{
				yield return current;
				current = current.ContainingType;
			}
		}

		public static IEnumerable<INamedTypeSymbol> GetContainingTypes(this ISymbol symbol)
		{
			var current = symbol.CheckIfNull().ContainingType;

			while (current != null)
			{
				yield return current;
				current = current.ContainingType;
			}
		}

		public static INamedTypeSymbol? TopMostContainingType(this ITypeSymbol type)
		{
			INamedTypeSymbol current = type.CheckIfNull().ContainingType;

			while (current != null)
			{
				if (current.ContainingType == null)
					return current;

				current = current.ContainingType;
			}

			return null;
		}

		public static IEnumerable<INamespaceSymbol> GetContainingNamespaces(this ITypeSymbol? type)
		{
			var currentNamespace = type?.ContainingNamespace;

			while (currentNamespace != null)
			{
				yield return currentNamespace;
				currentNamespace = currentNamespace.ContainingNamespace;
			}
		}

		/// <summary>
		///  Determine if "type" inherits from "baseType", ignoring constructed types and interfaces, dealing only with original types.
		/// </summary>
		/// <param name="type">The type to act on.</param>
		/// <param name="baseType">The base type.</param>
		/// <returns/>    
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool InheritsFromOrEquals(this ITypeSymbol type, ITypeSymbol baseType) =>
			InheritsFromOrEquals(type, baseType, includeInterfaces: false);

		/// <summary>
		/// Determine if "type" inherits from "baseType", ignoring constructed types, optionally including interfaces, dealing only with original types.
		/// </summary>
		/// <param name="type">The type to act on.</param>
		/// <param name="baseType">The base type.</param>
		/// <param name="includeInterfaces">True to include, false to exclude the interfaces.</param>
		/// <returns/>
		public static bool InheritsFromOrEquals(this ITypeSymbol type, ITypeSymbol baseType, bool includeInterfaces)
		{
			type.ThrowOnNull();
			baseType.ThrowOnNull();

			foreach (ITypeSymbol typeSymbol in type.GetBaseTypesAndThis())
			{
				if (typeSymbol.Equals(baseType, SymbolEqualityComparer.Default))
					return true;
			}

			if (includeInterfaces)
			{
				foreach (INamedTypeSymbol namedTypeSymbol in type.AllInterfaces)
				{
					if (namedTypeSymbol.Equals(baseType, SymbolEqualityComparer.Default))
						return true;
				}
			}

			return false;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool InheritsFromOrEqualsGeneric(this ITypeSymbol type, ITypeSymbol baseType) =>
			InheritsFromOrEqualsGeneric(type, baseType, includeInterfaces: false);

		public static bool InheritsFromOrEqualsGeneric(this ITypeSymbol type, ITypeSymbol baseType, bool includeInterfaces)
		{
			type.ThrowOnNull();
			baseType.ThrowOnNull();

			foreach (ITypeSymbol typeSymbol in type.GetBaseTypesAndThis())
			{
				if (typeSymbol.OriginalDefinition.Equals(baseType.OriginalDefinition, SymbolEqualityComparer.Default))
					return true;
			}

			if (includeInterfaces)
			{
				foreach (INamedTypeSymbol namedTypeSymbol in type.AllInterfaces)
				{
					if (namedTypeSymbol.OriginalDefinition.Equals(baseType.OriginalDefinition,
						    SymbolEqualityComparer.Default))
						return true;
				}
			}

			return false;
		}

		public static bool InheritsFrom(this ITypeSymbol type, ITypeSymbol baseType, bool includeInterfaces = false)
		{
			type.ThrowOnNull();
			baseType.ThrowOnNull();

			foreach (ITypeSymbol typeSymbol in type.GetBaseTypes())
			{
				if (typeSymbol.Equals(baseType, SymbolEqualityComparer.Default))
				{
					return true;
				}
			}

			if (includeInterfaces)
			{
				foreach (INamedTypeSymbol namedTypeSymbol in type.AllInterfaces)
				{
					if (namedTypeSymbol.Equals(baseType, SymbolEqualityComparer.Default))
					{
						return true;
					}
				}
			}

			return false;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool ImplementsInterface(this ITypeSymbol type, ITypeSymbol interfaceType)
		{
			type.ThrowOnNull();
			interfaceType.ThrowOnNull();

			if (interfaceType.TypeKind != TypeKind.Interface)
			{
				throw new ArgumentException("Invalid interface type", nameof(interfaceType));
			}

			// Interface types themselves are not included into AllInterfaces set, they do not implement themselves from Roslyn POV.
			// However, for simplicity in Acuminator analysis we can assume equality of type and interfaceType as a special case of type implementing interfaceType interface.
			// Therefore, we need to check type if it's an interface if it equals to the interfaceType
			if (type.TypeKind == TypeKind.Interface && type.Equals(interfaceType, SymbolEqualityComparer.Default))
				return true;

			foreach (INamedTypeSymbol namedTypeSymbol in type.AllInterfaces)
			{
				if (namedTypeSymbol.Equals(interfaceType, SymbolEqualityComparer.Default))
					return true;
			}

			return false;
		}

		/// <summary>
		/// Determine if "type" inherits from "baseType", ignoring constructed types, optionally including interfaces, dealing only with original
		/// types.
		/// </summary>
		/// <param name="type">The type to act on.</param>
		/// <param name="baseTypeName">Name of the base type.</param>
		/// <param name="includeInterfaces">(Optional) True to include, false to exclude the interfaces.</param>
		/// <returns/>
		public static bool InheritsOrImplementsOrEquals(this ITypeSymbol? type, string baseTypeName,
														bool includeInterfaces = true)
		{
			if (type == null)
				return false;

			foreach (ITypeSymbol typeSymbol in type.GetBaseTypesAndThis())
			{
				if (typeSymbol.Name == baseTypeName)
					return true;
			}

			if (includeInterfaces)
			{
				foreach (INamedTypeSymbol namedTypeSymbol in type.AllInterfaces)
				{
					if (namedTypeSymbol.Name == baseTypeName)
						return true;
				}
			}

			return false;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool ImplementsInterface(this ITypeSymbol? type, string interfaceName)
		{
			if (type == null)
				return false;
			else if (type.TypeKind == TypeKind.Interface && type.Name == interfaceName)
				return true;
			else
			{
				foreach (INamedTypeSymbol namedTypeSymbol in type.AllInterfaces)
				{
					if(namedTypeSymbol.Name == interfaceName)
						return true;
				}

				return false;
			}
		}
			

		/// <summary>
		/// Gets <paramref name="typeParameterSymbol"/> and its all constraint types.
		/// </summary>
		/// <param name="typeParameterSymbol">The typeParameterSymbol to act on.</param>
		/// <param name="includeInterfaces">(Optional) True to include, false to exclude the interfaces.</param>
		/// <returns/>
		public static IEnumerable<ITypeSymbol> GetTypeWithAllConstraintTypes(this ITypeParameterSymbol typeParameterSymbol,
																			 bool includeInterfaces = true)
		{
			var constraintTypes = typeParameterSymbol.GetAllConstraintTypes(includeInterfaces);
			return constraintTypes.PrependItem(typeParameterSymbol);
		}

		/// <summary>
		/// Gets all constraint types for the given <paramref name="typeParameterSymbol"/>.
		/// </summary>
		/// <param name="typeParameterSymbol">The typeParameterSymbol to act on.</param>
		/// <param name="includeInterfaces">(Optional) True to include, false to exclude the interfaces.</param>
		/// <returns/>
		public static IEnumerable<ITypeSymbol> GetAllConstraintTypes(this ITypeParameterSymbol typeParameterSymbol, bool includeInterfaces = true)
		{
			typeParameterSymbol.ThrowOnNull();
			
			var constraintTypes = includeInterfaces
				? GetAllConstraintTypesImplementation(typeParameterSymbol)
				: GetAllConstraintTypesImplementation(typeParameterSymbol)
							.Where(type => type.TypeKind != TypeKind.Interface);

			return constraintTypes.Distinct<ITypeSymbol>(SymbolEqualityComparer.Default);

			//---------------------------------Local Functions--------------------------------------------------------
			IEnumerable<ITypeSymbol> GetAllConstraintTypesImplementation(ITypeParameterSymbol typeParameter, int recursionLevel = 0)
			{
				const int maxRecursionLevel = 40;

				if (recursionLevel > maxRecursionLevel || typeParameter.ConstraintTypes.Length == 0)
					yield break;

				foreach (ITypeSymbol constraintType in typeParameter.ConstraintTypes)
				{
					if (constraintType is ITypeParameterSymbol constraintTypeParameter)
					{
						var nextOrderTypeParams = GetAllConstraintTypesImplementation(constraintTypeParameter, recursionLevel + 1);

						foreach (ITypeSymbol type in nextOrderTypeParams)
						{
							yield return type;
						}
					}
					else
					{
						yield return constraintType;
					}
				}
			}
		}

		/// <summary>
		/// Gets the depth of inheritance between <paramref name="type"/> and its <paramref name="baseType"/>.
		/// If <paramref name="baseType"/> is not an ancestor of type returns <c>null</c>.
		/// </summary>
		/// <param name="type">The type to act on.</param>
		/// <param name="baseType">The base type.</param>
		/// <returns>
		/// The inheritance depth.
		/// </returns>
		public static int? GetInheritanceDepth(this ITypeSymbol type, ITypeSymbol baseType)
		{
			type.ThrowOnNull();
			baseType.ThrowOnNull();

			ITypeSymbol? current = type;
			int depth = 0;

			while (current != null && !current.Equals(baseType, SymbolEqualityComparer.Default))
			{
				current = current.BaseType;
				depth++;
			}

			return current != null ? depth : null;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static IEnumerable<ITypeSymbol> GetAllAttributesDefinedOnThisAndBaseTypes(this ITypeSymbol typeSymbol) =>
			typeSymbol.GetAllAttributesApplicationsDefinedOnThisAndBaseTypes()
					  .Select(a => a.AttributeClass)
					  .Where(attrType => attrType != null)!;
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static IEnumerable<AttributeData> GetAllAttributesApplicationsDefinedOnThisAndBaseTypes(this ITypeSymbol typeSymbol)
		{
			typeSymbol.ThrowOnNull();
			return typeSymbol.GetBaseTypesAndThis()
							 .SelectMany(t => t.GetAttributes());
		}

		/// <summary>
		/// Gets all attributes applications defined on this and base types using the already known list of base types.
		/// </summary>
		/// <param name="typeSymbol">The type symbol to act on.</param>
		/// <param name="precalcedBaseTypes">List the pre-calculated base types.</param>
		/// <returns>
		/// All attributes' applications defined on this and base types.
		/// </returns>
		/// <remarks>
		/// This is unsafe method used for optimization.
		/// It does not calculate base types itself, instead it relies on the list of base types provided by the caller (which can be incorrect).<br/>
		/// In addition, the method does not check the <paramref name="typeSymbol"/> and <paramref name="precalcedBaseTypes"/> parameters for nulls and 
		/// does not perform boxing of <see cref="ImmutableArray{T}"/> collections of attributes.
		/// </remarks>
		internal static IReadOnlyCollection<AttributeData> GetAllAttributesApplicationsDefinedOnThisAndBaseTypesUnsafe(this ITypeSymbol typeSymbol,
																										IReadOnlyList<ITypeSymbol> precalcedBaseTypes)
		{
			var attributesOnHierarchy = new List<AttributeData>(capacity: 8);
			var typeAttributes = typeSymbol.GetAttributes();

			if (!typeAttributes.IsDefaultOrEmpty)
			{
				for (int i = 0; i < typeAttributes.Length; i++)
					attributesOnHierarchy.Add(typeAttributes[i]);
			}

			if (precalcedBaseTypes.Count == 0)
				return attributesOnHierarchy;

			for (int i = 0; i < precalcedBaseTypes.Count; i++)
			{
				var baseType		   = precalcedBaseTypes[i];
				var baseTypeAttributes = baseType.GetAttributes();

				if (baseTypeAttributes.IsDefaultOrEmpty)
					continue;

				for (int j = 0; j < baseTypeAttributes.Length; j++)
					attributesOnHierarchy.Add(baseTypeAttributes[j]);
			}

			return attributesOnHierarchy;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static ITypeSymbol? GetUnderlyingTypeFromNullable(this ITypeSymbol? typeSymbol, PXContext pxContext)
		{
			if (!typeSymbol.IsNullable(pxContext) || typeSymbol is not INamedTypeSymbol namedTypeSymbol)
				return null;

			ImmutableArray<ITypeSymbol> typeArgs = namedTypeSymbol.TypeArguments;
			return typeArgs.Length == 1
				? typeArgs[0]
				: null;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool IsNullable(this ITypeSymbol? typeSymbol, PXContext pxContext)
		{
			pxContext.ThrowOnNull();
			return typeSymbol?.OriginalDefinition?.Equals(pxContext.SystemTypes.Nullable, SymbolEqualityComparer.Default) ?? false;
		}

		/// <summary>
		/// A <see cref="String"/> extension method that removes the empty spaces in array type names described by dataTypeName.
		/// </summary>
		/// <exception cref="ArgumentException">Thrown when one or more arguments have unsupported or illegal values.</exception>
		/// <param name="dataTypeName">The dataTypeName to act on.</param>
		/// <returns>
		/// A string.
		/// </returns>
		public static string RemoveEmptySpacesInArrayTypeNames(this string dataTypeName)
		{
			dataTypeName.ThrowOnNullOrWhiteSpace();

			bool isArrayTypeName = dataTypeName[^1] == ']';

			if (!isArrayTypeName)
				return dataTypeName;
			else if (dataTypeName.Length <= 2)
				throw new ArgumentException($"Invalid data type name \"{dataTypeName}\"", nameof(dataTypeName));

			// if there is no empty spaces in array, return the original string
			if (dataTypeName[^2] == '[' && !char.IsWhiteSpace(dataTypeName[^3]))
				return dataTypeName;

			int indexOfOpeningSquareBracket = dataTypeName.LastIndexOf('[');

			if (indexOfOpeningSquareBracket < 0)
				throw new ArgumentException($"Invalid data type name \"{dataTypeName}\"", nameof(dataTypeName));

			var elementTypeName = dataTypeName[..indexOfOpeningSquareBracket].Trim();
			return $"{elementTypeName}[]";
		}

		/// <summary>
		/// An <see cref="INamedTypeSymbol"/> extension method that gets CLR-style full type name from type.
		/// </summary>
		/// <param name="typeSymbol">The typeSymbol to act on.</param>
		/// <returns/>
		public static string GetCLRTypeNameFromType(this ITypeSymbol? typeSymbol)
		{
			if (typeSymbol == null)
				return string.Empty;
			else if (typeSymbol.ContainingType == null)
				return typeSymbol.GetClrStyleTypeFullNameForNotNestedType();

			Stack<ITypeSymbol> containingTypesStack = typeSymbol.GetContainingTypesAndThis().ToStack();
			string notNestedTypeName = containingTypesStack.Pop().GetClrStyleTypeFullNameForNotNestedType();
			StringBuilder nameBuilder = new StringBuilder(notNestedTypeName, capacity: 128);

			while (containingTypesStack.Count > 0)
			{
				ITypeSymbol nestedType = containingTypesStack.Pop();
				nameBuilder.AppendClrStyleNestedTypeShortName(nestedType);
			}

			return nameBuilder.ToString();
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static StringBuilder AppendClrStyleNestedTypeShortName(this StringBuilder builder, ITypeSymbol typeSymbol)
		{
			builder.Append(DefaultNestedTypesSeparator)
				   .Append(typeSymbol.Name);

			if (typeSymbol is not INamedTypeSymbol namedType || !namedType.IsGenericType)
				return builder;

			var typeArgs = namedType.TypeArguments;
			return builder.Append(DefaultGenericArgsCountSeparator)
						  .Append(typeArgs.Length);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static string GetClrStyleTypeFullNameForNotNestedType(this ITypeSymbol notNestedTypeSymbol)
		{
			if (notNestedTypeSymbol is not INamedTypeSymbol namedType || !namedType.IsGenericType)
				return notNestedTypeSymbol.ToDisplayString();

			var typeArgs = namedType.TypeArguments;
			var displayFormat = new SymbolDisplayFormat(typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
														genericsOptions: SymbolDisplayGenericsOptions.None);
			string typeNameWithoutGeneric = namedType.ToDisplayString(displayFormat);
			return typeNameWithoutGeneric + DefaultGenericArgsCountSeparator + typeArgs.Length;
		}

		internal static IReadOnlyCollection<(ConstructorDeclarationSyntax? Node, IMethodSymbol Symbol)> GetDeclaredInstanceConstructors(
																												this ITypeSymbol typeSymbol, 
																												CancellationToken cancellation)
		{
			typeSymbol.ThrowOnNull();

			if (typeSymbol is not INamedTypeSymbol namedTypeSymbol)
				return [];

			var instanceConstructors = namedTypeSymbol.InstanceConstructors;

			if (instanceConstructors.IsDefaultOrEmpty)
				return [];

			var instanceConstructorsWithNodes = new List<(ConstructorDeclarationSyntax?, IMethodSymbol)>(instanceConstructors.Length);

			foreach (IMethodSymbol constructor in instanceConstructors)
			{
				cancellation.ThrowIfCancellationRequested();

				var node = constructor.GetSyntax(cancellation) as ConstructorDeclarationSyntax;
				instanceConstructorsWithNodes.Add((node, constructor));
			}

			return instanceConstructorsWithNodes;
		}

		public static ImmutableArray<StaticConstructorInfo> GetStaticConstructors(this ITypeSymbol typeSymbol, CancellationToken cancellation)
		{
			typeSymbol.ThrowOnNull();

			if (typeSymbol is not INamedTypeSymbol namedTypeSymbol)
				return [];

			var staticConstructors = namedTypeSymbol.StaticConstructors;

			if (staticConstructors.IsDefaultOrEmpty)
				return [];

			int order = 0;
			var builder = ImmutableArray.CreateBuilder<StaticConstructorInfo>(staticConstructors.Length);

			foreach (IMethodSymbol constructor in staticConstructors)
			{
				cancellation.ThrowIfCancellationRequested();

				var node = constructor.GetSyntax(cancellation) as ConstructorDeclarationSyntax;
				var staticConstructorInfo = new StaticConstructorInfo(node, constructor, order);

				builder.Add(staticConstructorInfo);
				order++;
			}

			return builder.ToImmutable();
		}

		/// <summary>
		/// Get all methods of this <paramref name="type"/>. If <paramref name="methodName"/> is specified then all methods with this name are returned.
		/// </summary>
		/// <param name="type">The type to act on.</param>
		/// <param name="methodName">(Optional) Name of the method to look for.</param>
		/// <returns>
		/// Returns a collection containing all the methods of this symbol. Never returns Null.
		/// </returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static IEnumerable<IMethodSymbol> GetMethods(this ITypeSymbol type, string? methodName = null) =>
			GetMembersInternal<IMethodSymbol>(type, methodName);

		/// <summary>
		/// Get all properties of this <paramref name="type"/>. If <paramref name="propertyName"/> is specified then all properties with this name are returned.
		/// </summary>
		/// <param name="type">The type to act on.</param>
		/// <param name="propertyName">(Optional) Name of the property to look for.</param>
		/// <returns>
		/// Returns a collection containing all the properties of this symbol. Never returns Null.
		/// </returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static IEnumerable<IPropertySymbol> GetProperties(this ITypeSymbol type, string? propertyName = null) =>
			GetMembersInternal<IPropertySymbol>(type, propertyName);

		/// <summary>
		/// Get all fields of this <paramref name="type"/>. If <paramref name="fieldName"/> is specified then all fields with this name are returned.
		/// </summary>
		/// <param name="type">The type to act on.</param>
		/// <param name="fieldName">(Optional) Name of the field to look for.</param>
		/// <returns>
		/// Returns a collection containing all the fields of this symbol. Never returns Null.
		/// </returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static IEnumerable<IFieldSymbol> GetFields(this ITypeSymbol type, string? fieldName = null) =>
			GetMembersInternal<IFieldSymbol>(type, fieldName);

		private static IEnumerable<TSymbol> GetMembersInternal<TSymbol>(this ITypeSymbol type, string? memberName = null)
		where TSymbol : ISymbol
		{
			ImmutableArray<ISymbol> members = memberName != null
				? type.GetMembers(memberName)
				: type.GetMembers();

			return members.IsDefaultOrEmpty
				? []
				: members.OfType<TSymbol>();
		}

		/// <summary>
		/// Returns true if a type is declared in Acumatica root namespace
		/// </summary>
		/// <param name="type"></param>
		/// <returns></returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool IsInAcumaticaRootNamespace(this ITypeSymbol type)
		{
			type.ThrowOnNull();

			var typeRootNamespace = type
				.GetContainingNamespaces()
				.Where(n => !string.IsNullOrEmpty(n.Name))
				.Last();

			return NamespaceNames.AcumaticaRootNamespace.Equals(typeRootNamespace.Name, StringComparison.Ordinal);
		}


		/// <summary>
		/// Gets a simplified name for type if it is a primitive type.
		/// For nullable ref types returns the name without the "?" annotation.
		/// </summary>
		/// <param name="type">The type to act on.</param>
		/// <returns/>
		public static string GetSimplifiedName(this ITypeSymbol type)
		{
			switch (type.CheckIfNull().SpecialType)
			{
				case SpecialType.System_Object:
				case SpecialType.System_String:
				case SpecialType.None when type.TypeKind == TypeKind.Dynamic:
				{
					string simpleTypeName = type.ToString();
					return simpleTypeName[^1] == '?'
						? simpleTypeName[..^1]
						: simpleTypeName;
				}

				case SpecialType.None when type.TypeKind == TypeKind.Array:
				case SpecialType.System_Array:
				{
					string simpleTypeName = type.ToString();

					if (type is IArrayTypeSymbol arrayType && arrayType.ElementNullableAnnotation == NullableAnnotation.Annotated &&
						(arrayType.ElementType.SpecialType == SpecialType.System_Nullable_T || arrayType.ElementType.IsValueType))
					{
						return simpleTypeName.Replace("?", string.Empty);
					}
						
					return simpleTypeName[^1] == '?'
						? simpleTypeName[..^1]
						: simpleTypeName;
				}

				case SpecialType.System_Void:
				case SpecialType.System_Boolean:
				case SpecialType.System_Char:
				case SpecialType.System_SByte:
				case SpecialType.System_Byte:
				case SpecialType.System_Int16:
				case SpecialType.System_UInt16:
				case SpecialType.System_Int32:
				case SpecialType.System_UInt32:
				case SpecialType.System_Int64:
				case SpecialType.System_UInt64:
				case SpecialType.System_Decimal:
				case SpecialType.System_Single:
				case SpecialType.System_Double:
				case SpecialType.System_Nullable_T:
					return type.ToString();
				default:
					return type.Name;
			}
		}

		/// <summary>
		/// Check if <paramref name="typeSymbol"/> and all its containing types are <see langword="public"/>.
		/// </summary>
		/// <param name="typeSymbol">The typeSymbol to act on.</param>
		/// <returns>
		/// True if <paramref name="typeSymbol"/> is public with all its containing types, false if not.
		/// </returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool IsPublicWithAllContainingTypes(this ITypeSymbol typeSymbol) =>
			typeSymbol.CheckIfNull(nameof(typeSymbol))
					  .GetContainingTypesAndThis()
					  .All(type => type.DeclaredAccessibility == Accessibility.Public);

		/// <summary>
		/// Check if <paramref name="typeSymbol"/> type is one dimensional string array.
		/// </summary>
		/// <param name="typeSymbol">The type to act on.</param>
		/// <returns>
		/// True if one dimensional string array, false if not.
		/// </returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool IsOneDimensionalStringArray(this ITypeSymbol? typeSymbol) =>
			typeSymbol is IArrayTypeSymbol arrayType && arrayType.Rank == 1 && 
			arrayType.ElementType.SpecialType == SpecialType.System_String;

		public static bool IsGenericType(this ITypeSymbol? typeSymbol)
		{
			if (typeSymbol is INamedTypeSymbol namedTypeSymbol)
				return namedTypeSymbol.IsGenericType;
			else if (typeSymbol is ITypeParameterSymbol typeParameterSymbol && !typeParameterSymbol.ConstraintTypes.IsDefaultOrEmpty)
			{
				// Generic interfaces can be applied to non-generic types, so we don't consider them here
				bool hasGenericConstraint = typeParameterSymbol.GetAllConstraintTypes(includeInterfaces: false)
															   .OfType<INamedTypeSymbol>()
															   .Any(constraintType => constraintType.IsGenericType);
				return hasGenericConstraint;
			}
			else
				return false;
		}

		public static bool IsUnboundGenericType(this ITypeSymbol? typeSymbol)
		{
			if (typeSymbol is INamedTypeSymbol namedTypeSymbol)
				return namedTypeSymbol.IsUnboundGenericType;
			else if (typeSymbol is ITypeParameterSymbol typeParameterSymbol && !typeParameterSymbol.ConstraintTypes.IsDefaultOrEmpty)
			{
				// Generic interfaces can be applied to non-generic types, so we don't consider them here
				bool hasGenericConstraint = typeParameterSymbol.GetAllConstraintTypes(includeInterfaces: false)
															   .OfType<INamedTypeSymbol>()
															   .Any(constraintType => constraintType.IsUnboundGenericType);
				return hasGenericConstraint;
			}
			else
				return false;
		}

		public static ImmutableArray<ITypeSymbol> TypeArguments(this ITypeSymbol? typeSymbol)
		{
			if (typeSymbol is INamedTypeSymbol namedTypeSymbol)
				return namedTypeSymbol.TypeArguments;
			else if (typeSymbol is ITypeParameterSymbol typeParameterSymbol && !typeParameterSymbol.ConstraintTypes.IsDefaultOrEmpty)
			{
				return typeParameterSymbol.GetAllConstraintTypes(includeInterfaces: false)
										  .Where(constraintType => constraintType.TypeKind is TypeKind.Class or TypeKind.Struct)
										  .ToImmutableArray();
			}
			else
				return [];
		}

		public static ImmutableArray<ITypeParameterSymbol> TypeParameters(this ITypeSymbol? typeSymbol)
		{
			if (typeSymbol is INamedTypeSymbol namedTypeSymbol)
				return namedTypeSymbol.TypeParameters;
			else if (typeSymbol is ITypeParameterSymbol typeParameterSymbol && !typeParameterSymbol.ConstraintTypes.IsDefaultOrEmpty)
			{
				return typeParameterSymbol.GetAllConstraintTypes(includeInterfaces: false)
										  .OfType<INamedTypeSymbol>()
										  .SelectMany(constraintType => constraintType.TypeParameters)
										  .ToImmutableArray();
			}
			else
				return [];
		}
	}
	
	public readonly struct BaseTypesEnumerable : IEnumerable<ITypeSymbol>
	{
		// Fast path — the common case. Walks the .BaseType chain starting from _start.
		private readonly ITypeSymbol? _start;
		private readonly bool _includeThis;

		// Slow path — used only by the rare ITypeParameterSymbol branch in GetBaseTypesImplementation.
		// When non-null, iteration is delegated to this sequence (boxing the enumerator).
		private readonly IEnumerable<ITypeSymbol>? _slowFallback;

		public BaseTypesEnumerable(ITypeSymbol start, bool includeThis)
		{
			_start = start;
			_includeThis = includeThis;
			_slowFallback = null;
		}

		/// <summary>
		/// Slow-path constructor. Used only for the rare type-parameter case;
		/// iterating allocates one boxed enumerator from <paramref name="slowFallback"/>.
		/// </summary>
		public BaseTypesEnumerable(IEnumerable<ITypeSymbol> slowFallback)
		{
			_start = null;
			_includeThis = false;
			_slowFallback = slowFallback;
		}

		public Enumerator GetEnumerator() => _slowFallback != null
			? new Enumerator(_slowFallback.GetEnumerator())
			: new Enumerator(_start!, _includeThis);

		IEnumerator<ITypeSymbol> IEnumerable<ITypeSymbol>.GetEnumerator() => GetEnumerator();
		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

		public struct Enumerator : IEnumerator<ITypeSymbol>
		{
			// Fast-path state:
			private readonly ITypeSymbol? _start;
			private readonly bool _includeThis;
			private ITypeSymbol? _current;
			private bool _started;

			// Slow-path state — non-null switches MoveNext to the boxed-enumerator branch.
			private readonly IEnumerator<ITypeSymbol>? _slowEnumerator;

			public Enumerator(ITypeSymbol start, bool includeThis)
			{
				_start = start;
				_includeThis = includeThis;
				_current = null;
				_started = false;
				_slowEnumerator = null;
			}

			public Enumerator(IEnumerator<ITypeSymbol> slowEnumerator)
			{
				_start = null;
				_includeThis = false;
				_current = null;
				_started = false;
				_slowEnumerator = slowEnumerator;
			}

			public ITypeSymbol Current => _slowEnumerator != null ? _slowEnumerator.Current : _current!;
			object IEnumerator.Current => Current;

			public bool MoveNext()
			{
				if (_slowEnumerator != null)
					return _slowEnumerator.MoveNext();

				_current = !_started
					? (_started = true, _includeThis ? _start : _start!.BaseType).Item2
					: _current?.BaseType;
				return _current != null;
			}

			public void Reset() => throw new NotSupportedException();
			public void Dispose() => _slowEnumerator?.Dispose();
		}
	}
}