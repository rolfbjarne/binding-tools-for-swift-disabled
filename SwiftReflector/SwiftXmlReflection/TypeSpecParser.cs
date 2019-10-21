// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Collections.Generic;
using SwiftReflector.Exceptions;
using ObjCRuntime;

namespace SwiftReflector.SwiftXmlReflection {
	public class TypeSpecParser {
		TextReader reader;
		TypeSpecTokenizer tokenizer;

		TypeSpecParser (TextReader reader)
		{
			this.reader = reader;
			tokenizer = new TypeSpecTokenizer (reader);
		}

		TypeSpec Parse ()
		{
			TypeSpecToken token = tokenizer.Peek ();
			TypeSpec type = null;
			List<TypeSpecAttribute> attrs = null;
			var inout = false;
			string typeLabel = null;

			// Prefix

			// parse any attributes
			if (token.Kind == TypeTokenKind.At) {
				attrs = ParseAttributes ();
				token = tokenizer.Peek ();
			}

			// looks like it's inout
			if (token.Kind == TypeTokenKind.TypeName && token.Value == "inout") {
				inout = true;
				tokenizer.Next ();
				token = tokenizer.Peek ();
			}

			if (token.Kind == TypeTokenKind.TypeLabel) {
				typeLabel = token.Value;
				tokenizer.Next ();
				token = tokenizer.Peek ();
			}


			// meat


			if (token.Kind == TypeTokenKind.LeftParenthesis) { // tuple
				tokenizer.Next ();
				TupleTypeSpec tuple = ParseTuple ();
				type = tuple.Elements.Count == 1 ? tuple.Elements [0] : tuple;
				typeLabel = type.TypeLabel;
			} else if (token.Kind == TypeTokenKind.TypeName) { // name
				tokenizer.Next ();
				var tokenValue = token.Value.StartsWith ("ObjectiveC.", StringComparison.Ordinal) ?
						      "Foundation" + token.Value.Substring ("ObjectiveC".Length) : token.Value;
				type = new NamedTypeSpec (tokenValue);
			} else if (token.Kind == TypeTokenKind.LeftBracket) { // array
				tokenizer.Next ();
				type = ParseArray ();
			} else { // illegal
				throw ErrorHelper.CreateError (ReflectorError.kTypeParseBase + 0, $"Unexpected token {token.Value}.");
			}

			// look-ahead for closure
			if (tokenizer.Peek ().Kind == TypeTokenKind.TypeName && tokenizer.Peek ().Value == "throws") {
				tokenizer.Next ();
				if (tokenizer.Peek ().Kind != TypeTokenKind.Arrow)
					throw ErrorHelper.CreateError (ReflectorError.kTypeParseBase + 1, $"Unexpected token {tokenizer.Peek ().Value} after a 'throws' in a closure.");
				tokenizer.Next ();
				type = ParseClosure (type, true);
			}

			if (tokenizer.Peek ().Kind == TypeTokenKind.Arrow) {
				tokenizer.Next ();
				type = ParseClosure (type, false);
			} else if (tokenizer.Peek ().Kind == TypeTokenKind.LeftAngle) {
				tokenizer.Next ();
				type = Genericize (type);
			}

			if (tokenizer.Peek().Kind == TypeTokenKind.Period) {
				tokenizer.Next ();
				var currType = type as NamedTypeSpec;
				if (currType == null)
					throw ErrorHelper.CreateError (ReflectorError.kTypeParseBase + 2, $"In parsing an inner type (type.type), first element is a {type.Kind} instead of a NamedTypeSpec.");
				var nextType = Parse () as NamedTypeSpec;
				if (nextType == null)
					throw ErrorHelper.CreateError (ReflectorError.kTypeParseBase + 3, $"In parsing an inner type (type.type), the second element is a {nextType.Kind} instead of a NamedTypeSpec");
				currType.InnerType = nextType;
			}

			// Postfix

			if (tokenizer.Peek ().Kind == TypeTokenKind.Ampersand) {
				type = ParseProtocolList (type as NamedTypeSpec);
			}

			while (tokenizer.Peek ().Kind == TypeTokenKind.QuestionMark) {
				tokenizer.Next ();
				type = WrapAsBoundGeneric (type, "Swift.Optional");
			}

			if (tokenizer.Peek ().Kind == TypeTokenKind.ExclamationPoint) {
				tokenizer.Next ();
				type = WrapAsBoundGeneric (type, "Swift.ImplicitlyUnwrappedOptional");
			}

			type.IsInOut = inout;
			type.TypeLabel = typeLabel;

			if (type != null && attrs != null) {
				type.Attributes.AddRange (attrs);
			}

			return type;
		}

		List<TypeSpecAttribute> ParseAttributes ()
		{
			// An attribute is
			// @name
			// or
			// @name [ parameters ]
			// The spec says that it could be ( parameters ), [ parameters ], or { parameters }
			// but the reflection code should make certain that it's [ parameters ].
			List<TypeSpecAttribute> attrs = new List<TypeSpecAttribute> ();
			while (true) {
				if (tokenizer.Peek ().Kind != TypeTokenKind.At) {
					return attrs;
				}
				tokenizer.Next ();
				if (tokenizer.Peek ().Kind != TypeTokenKind.TypeName) {
					throw ErrorHelper.CreateError (ReflectorError.kTypeParseBase + 5, $"Unexpected token {tokenizer.Peek ().Value}, expected a name while parsing an attribute.");
				}
				string name = tokenizer.Next ().Value;
				TypeSpecAttribute attr = new TypeSpecAttribute (name);
				if (tokenizer.Peek ().Kind == TypeTokenKind.LeftBracket) {
					tokenizer.Next ();
					ParseAttributeParameters (attr.Parameters);
				}
				attrs.Add (attr);
			}
		}

		void ParseAttributeParameters (List<string> parameters)
		{
			// Attribute parameters are funny
			// The contents between the brackets vary.
			// They may be comma separated. They may not.
			// Therefore this code is likely to break, but since I'm responsible for
			// generating the text of the attributes parsed here, I can try to ensure
			// that it will always fit the pattern.
			while (true) {
				if (tokenizer.Peek ().Kind == TypeTokenKind.RightBracket) {
					tokenizer.Next ();
					return;
				}
				TypeSpecToken value = tokenizer.Next ();
				if (value.Kind != TypeTokenKind.TypeName) {
					throw ErrorHelper.CreateError (ReflectorError.kTypeParseBase + 6, $"Unexpected token {value.Value} while parsing attribute parameter.");
				}
				parameters.Add (value.Value);
				if (tokenizer.Peek ().Kind == TypeTokenKind.Comma) {
					tokenizer.Next ();
				}
			}
		}

		TypeSpec ParseProtocolList (NamedTypeSpec first)
		{
			Ex.ThrowOnNull (first, nameof (first));
			var protocols = new List<NamedTypeSpec> ();
			protocols.Add (first);
			while (true) {
				if (tokenizer.Peek ().Kind != TypeTokenKind.Ampersand)
					break;
				tokenizer.Next ();
				var nextName = tokenizer.Next ();
				if (nextName.Kind != TypeTokenKind.TypeName)
					throw ErrorHelper.CreateError (ReflectorError.kTypeParseBase + 13, $"Unexpected token '{nextName.Value}' with kind {nextName.Kind} while parsing a protocol list");
				protocols.Add (new NamedTypeSpec (nextName.Value));
			}
			return new ProtocolListTypeSpec (protocols);
		}

		void ConsumeList (List<TypeSpec> elements, TypeTokenKind terminator, string typeImParsing)
		{
			while (true) {
				if (tokenizer.Peek ().Kind == terminator) {
					tokenizer.Next ();
					return;
				}
				TypeSpec next = Parse ();
				if (next == null)
					throw ErrorHelper.CreateError (ReflectorError.kTypeParseBase + 8, $"Unexpected end while parsing a {typeImParsing}");
				elements.Add (next);
				if (tokenizer.Peek ().Kind == TypeTokenKind.Comma) {
					tokenizer.Next ();
				}
			}
		}

		TypeSpec Genericize (TypeSpec type)
		{
			ConsumeList (type.GenericParameters, TypeTokenKind.RightAngle, "generic parameter list");
			return type;
		}

		TypeSpec WrapAsBoundGeneric(TypeSpec type, string name)
		{
			var result = new NamedTypeSpec (name);
			result.GenericParameters.Add (type);
			return result;
		}

		TupleTypeSpec ParseTuple ()
		{
			TupleTypeSpec tuple = new TupleTypeSpec ();
			ConsumeList (tuple.Elements, TypeTokenKind.RightParenthesis, "tuple");
			return tuple;
		}

		NamedTypeSpec ParseArray()
		{
			var keyType = Parse ();
			TypeSpec valueType = null;
			if (keyType == null)
				throw ErrorHelper.CreateError (ReflectorError.kTypeParseBase + 9, "Unexpected end while parsing an array or dictionary.");
			if (tokenizer.Peek ().Kind == TypeTokenKind.Colon) {
				tokenizer.Next ();
				valueType = Parse ();
				if (valueType == null)
					throw ErrorHelper.CreateError (ReflectorError.kTypeParseBase + 10, "Unexpected end while parsing a dictionary value type.");
			} else if (tokenizer.Peek ().Kind != TypeTokenKind.RightBracket)
				throw ErrorHelper.CreateError (ReflectorError.kTypeParseBase + 11, "Expected a right bracket after an array or dictionary.");

			tokenizer.Next ();

			if (valueType == null) {
				var array = new NamedTypeSpec ("Swift.Array");
				array.GenericParameters.Add (keyType);
				return array;
			} else {
				var dictionary = new NamedTypeSpec ("Swift.Dictionary");
				dictionary.GenericParameters.Add (keyType);
				dictionary.GenericParameters.Add (valueType);
				return dictionary;
			}
		}

		ClosureTypeSpec ParseClosure (TypeSpec arg, bool throws)
		{
			TypeSpec returnType = Parse ();
			if (returnType == null)
				throw ErrorHelper.CreateError (ReflectorError.kTypeParseBase + 12, "Unexpected end while parsing a closure.");
			ClosureTypeSpec closure = new ClosureTypeSpec ();
			closure.Arguments = arg;
			closure.ReturnType = returnType;
			closure.Throws = throws;
			return closure;
		}

		public static TypeSpec Parse (string typeName)
		{
			TypeSpecParser parser = new TypeSpecParser (new StringReader (typeName));
			return parser.Parse ();
		}
	}
}

