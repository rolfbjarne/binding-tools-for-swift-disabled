// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using SwiftReflector.SwiftXmlReflection;
using SwiftReflector.Inventory;
using System.Collections.Generic;
using System.Linq;
using SwiftReflector.Demangling;

namespace SwiftReflector {
	public class XmlToTLFunctionMapper {
		public static TLFunction ToProtocolFactory (string swiftProxyFactoryName, ModuleDeclaration modDecl, ModuleContents contents)
		{
			var swiftProxyFunctionDecl = modDecl.TopLevelFunctions.Where (fn => fn.Name == swiftProxyFactoryName).FirstOrDefault ();
			if (swiftProxyFunctionDecl == null)
				return null;
			return ToTLFunction (swiftProxyFunctionDecl, contents);
		}

		public static TLFunction ToTLFunction (FunctionDeclaration decl, ModuleContents contents)
		{
			string nameToSearch = GetNameToSearchOn (decl);
			var funcs = FuncsToSearch (contents, decl, nameToSearch);
			return MatchFunctionDecl (decl, funcs);
		}

		public static TLFunction ToTLFunction (FunctionDeclaration decl, ModuleInventory mi)
		{
			var scn = ToSwiftClassName (decl);
			var contents = mi.Values.FirstOrDefault (mc => mc.Name.Equals (scn.Module));
			if (contents == null)
				return null;
			return ToTLFunction (decl, contents);
		}

		public static TLFunction ToTLFunction (FunctionDeclaration decl, ClassContents classContents)
		{
			string name = decl.IsProperty ? decl.PropertyName : decl.Name;
			var funcsToSearch = FuncsToSearch (classContents, decl, name);//decl.ParameterLists.Count == 2 ? classContents.Methods.MethodsWithName (decl.Name)
										      //: classContents.StaticFunctions.MethodsWithName (decl.Name);
			return MatchFunctionDecl (decl, funcsToSearch);
		}

		static List<TLFunction> FuncsToSearch (ModuleContents contents, FunctionDeclaration decl, string name)
		{

			List<TLFunction> funcs = null;
			if (decl.Parent == null) { // top level
				if (decl.IsProperty) {
					funcs = contents.Functions.MethodsWithName (name).Where (tlf => tlf.Signature is SwiftPropertyType).ToList ();
				} else {
					funcs = contents.Functions.MethodsWithName (name).Where (tlf => !(tlf.Signature is SwiftPropertyType)).ToList ();
				}
			} else {
				var cn = ToSwiftClassName (decl);
				var theClass = LocateClassContents (contents, cn);
				funcs = FuncsToSearch (theClass, decl, name);
			}
			return funcs;
		}

		static List<TLFunction> FuncsToSearch (ClassContents theClass, FunctionDeclaration decl, string name)
		{
			List<TLFunction> funcs = null;

			if (theClass == null) {
				funcs = new List<TLFunction> ();
			} else {
				if (decl.IsProperty) {
					funcs = new List<TLFunction> ();
					if (decl.IsSubscript) {
						funcs.AddRange (theClass.Subscripts);
					} else {
						foreach (var pc in theClass.AllPropertiesWithName(name)) {
							var propType = pc.TLFGetter.Signature as SwiftPropertyType;
							if (propType == null) // should never happen, but...
								continue;
							if (propType.IsStatic != decl.IsStatic)
								continue;
							if (decl.IsGetter)
								funcs.Add (pc.TLFGetter);
							else if (decl.IsSetter && pc.TLFSetter != null)
								funcs.Add (pc.TLFSetter);
							else if (decl.IsMaterializer && pc.TLFMaterializer != null)
								funcs.Add (pc.TLFMaterializer);
						}
					}
				} else if (decl.IsConstructor) {
					funcs = theClass.Constructors.AllocatingConstructors ();
				} else if (decl.IsDestructor) {
					funcs = theClass.Destructors.DeallocatingDestructors ();
				} else {
					if (decl.IsStatic)
						funcs = theClass.StaticFunctions.MethodsWithName (name);
					else
						funcs = theClass.Methods.MethodsWithName (name);
				}
			}
			return funcs;
		}

		static string GetNameToSearchOn (FunctionDeclaration decl)
		{
			if (!decl.IsProperty)
				return decl.Name;
			if (decl.IsGetter)
				return decl.Name.Substring (FunctionDeclaration.kPropertyGetterPrefix.Length);
			else if (decl.IsSetter)
				return decl.Name.Substring (FunctionDeclaration.kPropertySetterPrefix.Length);
			else if (decl.IsMaterializer)
				return decl.Name.Substring (FunctionDeclaration.kPropertyMaterializerPrefix.Length);
			else
				throw new ArgumentOutOfRangeException ("decl", "Expected getter or setter but got something else?");
		}

		public static ClassContents LocateClassContents (ModuleContents contents, SwiftClassName cn)
		{
			return contents.Classes.Values.FirstOrDefault (cc => cc.Name.Equals (cn));
		}

		public static ClassContents LocateClassContents (ModuleInventory modInventory, SwiftClassName className)
		{
			var contents = modInventory.Values.FirstOrDefault (mod => mod.Name.Equals (className.Module));
			if (contents == null)
				return null;
			return LocateClassContents (contents, className);
		}

		public static ProtocolContents LocateProtocolContents (ModuleContents contents, SwiftClassName cn)
		{
			return contents.Protocols.Values.FirstOrDefault (cc => cc.Name.Equals (cn));
		}

		public static ProtocolContents LocateProtocolContents (ModuleInventory modInventory, SwiftClassName className)
		{
			var contents = modInventory.Values.FirstOrDefault (mod => mod.Name.Equals (className.Module));
			if (contents == null)
				return null;
			return LocateProtocolContents (contents, className);
		}



		public static SwiftClassName ToSwiftClassName (BaseDeclaration bdl, string suffix = null)
		{
			var nesting = new List<MemberNesting> ();
			var names = new List<SwiftName> ();
			var walker = bdl;
			do {
				if (IsNestable (walker)) {
					nesting.Insert (0, ToMemberNesting (walker));
					names.Insert (0, new SwiftName (walker.Name + (walker == bdl && suffix != null ? suffix : ""), false));
				}
				walker = walker.Parent;
			} while (walker != null);
			return new SwiftClassName (new SwiftName (bdl.Module.Name, false), nesting, names);
		}

		static bool IsNestable (BaseDeclaration bdl)
		{
			return bdl is ClassDeclaration || bdl is StructDeclaration
				|| bdl is EnumDeclaration
				;
		}

		static MemberNesting ToMemberNesting (BaseDeclaration decl)
		{
			// Don't reorder - a ProtocolDeclaration is a ClassDeclaration (for now)
			if (decl is ProtocolDeclaration) {
				return MemberNesting.Protocol;
			}
			if (decl is ClassDeclaration) {
				return MemberNesting.Class;
			}
			if (decl is StructDeclaration) {
				return MemberNesting.Struct;
			}
			if (decl is EnumDeclaration) {
				return MemberNesting.Enum;
			}
			throw new ArgumentOutOfRangeException ("decl", String.Format ("unknown class entity type {0}", decl.GetType ().Name));
		}

		static TLFunction MatchFunctionDecl (FunctionDeclaration decl, List<TLFunction> funcs)
		{
			if (decl.Parent == null)
				funcs = funcs.Where (fn => fn.IsTopLevelFunction).ToList ();
			else
				funcs = funcs.Where (fn => !fn.IsTopLevelFunction).ToList ();

			foreach (var func in funcs) {
				if (SignaturesMatch (decl, func))
					return func;
			}
			return null;
		}


		static bool SignaturesMatch (FunctionDeclaration decl, TLFunction func)
		{
			if (decl.IsConstructor && !(func.Signature is SwiftConstructorType) ||
				(!decl.IsConstructor && func.Signature is SwiftConstructorType))
				return false;
			List<ParameterItem> significantParameterList = decl.ParameterLists.Last ();
			if (decl.ParameterLists.Count > 1 && !decl.IsStatic) {
				var ucf = func.Signature as SwiftUncurriedFunctionType;
				if (ucf == null)
					return false;

				var uncurriedParameter = ucf.UncurriedParameter;
				var uncurriedTuple = uncurriedParameter as SwiftTupleType;

				// the !decl.IsConstructor is because the uncurried parameter in a constructor comes out
				// "funny" in XmlReflection and won't match
				if ((decl.Parent == null || !(decl.Parent is StructDeclaration)) && !decl.IsConstructor) {
					if (decl.ParameterLists [0].Count == 1) {
						if (!TypeMatches (decl, decl.ParameterLists [0] [0], uncurriedParameter, false))
							return false;
					} else if (decl.ParameterLists [0].Count == 0) {
						if (uncurriedTuple == null || !uncurriedTuple.IsEmpty)
							return false;
					} else {
						if (uncurriedTuple == null || !TypeMatches (decl, decl.ParameterLists [0], uncurriedTuple))
							return false;
					}
				}
			}

			// return is implied in constructor - we're good on that, thanks
			bool dontMatchReturn = decl.IsConstructor;

			if (func.Signature.ParameterCount == significantParameterList.Count) {
				if (func.Signature.ParameterCount == 0)
					return dontMatchReturn || TypeMatches (decl, decl.ReturnTypeSpec, func.Signature.ReturnType);
				if (func.Signature.ParameterCount == 1) {
					var tuple = func.Signature.Parameters as SwiftTupleType;
					var argsMatch = TypeMatches (decl, significantParameterList [0], tuple != null ? tuple.Contents [0] : func.Signature.Parameters, decl.IsSetter);
					var returnMatches = (dontMatchReturn || TypeMatches (decl, decl.ReturnTypeSpec, func.Signature.ReturnType));
					return argsMatch && returnMatches;
				} else {
					var argsMatch = TypeMatches (decl, significantParameterList, func.Signature.Parameters as SwiftTupleType);
					var returnMatches = dontMatchReturn || TypeMatches (decl, decl.ReturnTypeSpec, func.Signature.ReturnType);
					return argsMatch && returnMatches;
				}
			} else {
				// oh, hooray. Swift does a reduction in the tuple-ness of arguments.
				// if I declare a function, func a(a:(Bool, Bool)) { }
				// This should get turned into:
				// __TF6Module1aFTTSbSb__T_
				// Instead, swift turns it into:
				// __TF6Module1aFTSbSb_T_
				// In other words, if the only argument to the function is a tuple, unwrap it.
				if (significantParameterList.Count == 1 && significantParameterList [0].TypeSpec is TupleTypeSpec) {
					return TypeMatches (decl, significantParameterList [0], func.Signature.Parameters, false)
						&& (dontMatchReturn || TypeMatches (decl, decl.ReturnTypeSpec, func.Signature.ReturnType));
				}
			}
			return false;
		}


		static bool TypeMatches (FunctionDeclaration decl, List<ParameterItem> parms, SwiftTupleType tuple)
		{
			if (tuple == null || parms.Count != tuple.Contents.Count)
				return false;
			for (int i = 0; i < parms.Count; i++) {
				if (!TypeMatches (decl, parms [i], tuple.Contents [i], decl.IsSubscript))
					return false;
			}
			return true;
		}

		static bool TypeMatches (FunctionDeclaration decl, ParameterItem pi, SwiftType st, bool ignoreName)
		{
			// some SwiftType parameters have no names, such as operators
			if (!ignoreName && pi.PublicName != "self" && st.Name != null && pi.PublicName != st.Name.Name)
				return false;
			if (pi.IsVariadic != st.IsVariadic)
				return false;
			return TypeMatches (decl, pi.TypeSpec, st);
		}

		static bool TypeMatches (FunctionDeclaration decl, TypeSpec ts, SwiftType st)
		{
			switch (ts.Kind) {
			case TypeSpecKind.Named:
				return TypeMatches (decl, ts as NamedTypeSpec, st);
			case TypeSpecKind.Closure:
				return TypeMatches (decl, ts as ClosureTypeSpec, st);
			case TypeSpecKind.Tuple:
				return TypeMatches (decl, ts as TupleTypeSpec, st);
			case TypeSpecKind.ProtocolList:
				return TypeMatches (decl, ts as ProtocolListTypeSpec, st);
			default:
				throw new ArgumentOutOfRangeException (nameof (ts));
			}
		}

		static bool TypeMatches (FunctionDeclaration decl, ClosureTypeSpec cs, SwiftType st)
		{
			SwiftBaseFunctionType bft = st as SwiftBaseFunctionType;
			return TypeMatches (decl, cs.Arguments, bft.Parameters) &&
				TypeMatches (decl, cs.ReturnType, bft.ReturnType);
		}

		static bool TypeMatches (FunctionDeclaration decl, TupleTypeSpec ts, SwiftType st)
		{
			var tuple = st as SwiftTupleType;
			if (tuple == null || tuple.Contents.Count != ts.Elements.Count)
				return false;
			for (int i = 0; i < ts.Elements.Count; i++) {
				if (!TypeMatches (decl, ts.Elements [i], tuple.Contents [i]))
					return false;
			}
			return true;
		}

		static bool TypeMatches (FunctionDeclaration decl, ProtocolListTypeSpec ps, SwiftType st)
		{
			var protoList = st as SwiftProtocolListType;
			if (protoList == null || protoList.Protocols.Count != ps.Protocols.Count)
				return false;
			for (int i=0; i < ps.Protocols.Count; i++) {
				if (!TypeMatches (decl, ps.Protocols.Keys [i], protoList.Protocols [i]))
					return false;
			}
			return true;
		}


		static bool TypeMatches (FunctionDeclaration decl, NamedTypeSpec ts, SwiftType st)
		{
			switch (st.Type) {
			case CoreCompoundType.Scalar:
				return TypeMatches (decl, ts, st as SwiftBuiltInType);
			case CoreCompoundType.Class:
				return TypeMatches (decl, ts, st as SwiftClassType);
			case CoreCompoundType.MetaClass:
				return TypeMatches (decl, ts, st as SwiftMetaClassType);
			case CoreCompoundType.BoundGeneric:
				return TypeMatches (decl, ts, st as SwiftBoundGenericType);
			case CoreCompoundType.ProtocolList:
				return TypeMatches (decl, ts, st as SwiftProtocolListType);
			case CoreCompoundType.GenericReference:
				return TypeMatches (decl, ts, st as SwiftGenericArgReferenceType);
			case CoreCompoundType.Struct:
			default:
				return false;
			}
		}

		static bool TypeMatches (FunctionDeclaration decl, NamedTypeSpec ts, SwiftGenericArgReferenceType genArg)
		{
			if (!decl.IsTypeSpecGeneric (ts))
				return false;
			var depthAndIndex = decl.GetGenericDepthAndIndex (ts.Name);
			return genArg.Depth == depthAndIndex.Item1 && genArg.Index == depthAndIndex.Item2;
		}

		static bool TypeMatches (FunctionDeclaration decl, NamedTypeSpec ts, SwiftProtocolListType protList)
		{
			if (protList == null)
				return false;
			if (protList.Protocols.Count == 1 && !ts.IsProtocolList) {
				return TypeMatches (decl, ts, protList.Protocols [0]);
			}

			if (protList.Protocols.Count != ts.GenericParameters.Count || !ts.IsProtocolList)
				return false;
			for (int i = 0; i < ts.GenericParameters.Count; i++) {
				if (!TypeMatches (decl, ts.GenericParameters [i], protList.Protocols [i]))
					return false;
			}
			return true;
		}

		static bool TypeMatches (FunctionDeclaration decl, NamedTypeSpec ts, SwiftClassType ct)
		{
			if (ct == null)
				return false;
			return ts.Name == ct.ClassName.ToFullyQualifiedName (true) ||
				ts.NameWithoutModule == ct.ClassName.ToFullyQualifiedName (false);
		}

		static bool TypeMatches (FunctionDeclaration decl, NamedTypeSpec ts, SwiftBuiltInType st)
		{
			if (st == null)
				return false;
			if (ts.IsInOut != st.IsReference)
				return false;
			switch (st.BuiltInType) {
			case CoreBuiltInType.Bool:
				return ts.Name == "Swift.Bool";
			case CoreBuiltInType.Double:
				return ts.Name == "Swift.Double";
			case CoreBuiltInType.Float:
				return ts.Name == "Swift.Float";
			case CoreBuiltInType.Int:
				return ts.Name == "Swift.Int";
			case CoreBuiltInType.UInt:
				return ts.Name == "Swift.UInt";
			default:
				throw new ArgumentOutOfRangeException ("st");
			}
		}

		static bool TypeMatches (FunctionDeclaration decl, NamedTypeSpec ts, SwiftMetaClassType st)
		{
			if (st == null)
				return false;
			return ts.Name == st.Class.ClassName.ToFullyQualifiedName (true);
		}

		static bool TypeMatches (FunctionDeclaration decl, NamedTypeSpec ts, SwiftBoundGenericType st)
		{
			if (st == null)
				return false;
			if (!ts.ContainsGenericParameters)
				return false;
			return TypeMatches (decl, ts.GenericParameters, st.BoundTypes);
		}

		static bool TypeMatches (FunctionDeclaration decl, List<TypeSpec> ts, List<SwiftType> st)
		{
			if (ts == null || st == null)
				return false;
			if (ts.Count != st.Count)
				return false;
			if (ts.Count == 1) {
				// Thanks swift, you're the best...
				if (IsVoid (ts [0]) && st [0].IsEmptyTuple)
					return true;
			}
			for (int i = 0; i < ts.Count; i++) {
				if (!TypeMatches (decl, ts [i], st [i]))
					return false;
			}
			return true;
		}

		static bool IsVoid (TypeSpec ts)
		{
			var ns = ts as NamedTypeSpec;
			return ns != null && (ns.Name == "Void" || ns.Name == "Swift.Void");
		}
	}
}

