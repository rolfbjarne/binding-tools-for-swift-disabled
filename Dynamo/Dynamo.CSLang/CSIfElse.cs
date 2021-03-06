﻿using System;
using System.Collections.Generic;

namespace Dynamo.CSLang {
	public class CSIfElse : CodeElementCollection<ICodeElement>, ICSStatement {
		class CSIfElement : DelegatedSimpleElement, ICSStatement {
			public CSIfElement (CSBaseExpression condition)
				: base ()
			{
				Condition = condition;
			}

			protected override void LLWrite (ICodeWriter writer, object o)
			{
				writer.BeginNewLine (true);
				writer.Write ("if (", false);
				Condition.WriteAll (writer);
				writer.Write (")", false);
				writer.EndLine ();
			}

			public CSBaseExpression Condition { get; private set; }
		}

		public CSIfElse (CSBaseExpression condition, CSCodeBlock ifClause, CSCodeBlock elseClause = null)
			: base ()
		{
			Condition = new CSIfElement (Exceptions.ThrowOnNull (condition, "condition"));
			IfClause = Exceptions.ThrowOnNull (ifClause, "ifClause");
			ElseClause = elseClause;

			Add (Condition);
			Add (IfClause);
			if (ElseClause != null && ElseClause.Count > 0) {
				Add (new SimpleLineElement ("else", false, true, false));
				Add (ElseClause);
			}
		}

		public CSIfElse (CSBaseExpression expr, IEnumerable<ICodeElement> ifClause, IEnumerable<ICodeElement> elseClause)
			: this (expr, new CSCodeBlock (ifClause),
				elseClause != null ? new CSCodeBlock (elseClause) : null)

		{

		}

		public DelegatedSimpleElement Condition { get; private set; }
		public CSCodeBlock IfClause { get; private set; }
		public CSCodeBlock ElseClause { get; private set; }


	}
}

