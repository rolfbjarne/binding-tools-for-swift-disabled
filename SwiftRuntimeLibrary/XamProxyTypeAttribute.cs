﻿using System;

namespace SwiftRuntimeLibrary {
	[AttributeUsage (AttributeTargets.Field, AllowMultiple = false)]
	public class XamProxyTypeAttribute : Attribute {
		public XamProxyTypeAttribute (Type proxyType)
		{
			ProxyType = proxyType;
		}
		public Type ProxyType { get; private set; }
	}
}

