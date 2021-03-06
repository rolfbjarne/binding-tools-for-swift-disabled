﻿using System;

namespace Dynamo {
	public class WriteEventArgs : EventArgs {
		public WriteEventArgs (ICodeWriter writer)
		{
			if (writer == null)
				throw new ArgumentNullException (nameof(writer));
			Writer = writer;
		}

		public ICodeWriter Writer { get; private set; }
	}
}

