// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
namespace Dynamo.SwiftLang {
	public class SLLineableOperator : SLBaseExpr, ISLLineable {
		public SLLineableOperator (SLUnaryExpr unaryExpr)
		{
			OperatorExpr = Exceptions.ThrowOnNull (unaryExpr, nameof (unaryExpr));
		}

		public SLLineableOperator (SLBinaryExpr binaryExpr)
		{
			OperatorExpr = Exceptions.ThrowOnNull (binaryExpr, nameof (binaryExpr));
		}

		protected override void LLWrite (ICodeWriter writer, object o)
		{
			OperatorExpr.WriteAll (writer);
		}

		public SLBaseExpr OperatorExpr { get; private set; }
	}
}
