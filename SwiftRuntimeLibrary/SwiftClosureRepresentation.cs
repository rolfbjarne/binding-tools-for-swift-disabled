// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.InteropServices;
using SwiftRuntimeLibrary.SwiftMarshal;
using Xamarin.iOS;

namespace SwiftRuntimeLibrary {
	public struct SwiftClosureRepresentation {
		public SwiftClosureRepresentation (Delegate function, IntPtr data)
		{
			Function = function;
			Data = data;
#if DEBUG
			//Console.WriteLine ($"Constructed SwiftClosureRepresentation with data {data.ToString ("X8")}");
#endif
		}
		[MarshalAs (UnmanagedType.FunctionPtr)]
		public Delegate Function;
		public IntPtr Data;


		static IntPtr LocateRefPtrFromPartialApplicationForwarder(IntPtr p)
		{
			p = Marshal.ReadIntPtr (p + IntPtr.Size);
			return Marshal.ReadIntPtr (p + 3 * IntPtr.Size);
		}

		[MonoPInvokeCallback (typeof (Action<IntPtr, IntPtr>))]
		public static void FuncCallbackVoid (IntPtr retValPtr, IntPtr refPtr)
		{
			if (refPtr == IntPtr.Zero)
				throw new ArgumentNullException (nameof (refPtr), "Inside a closure callback, the closure data pointer was null.");
			refPtr = LocateRefPtrFromPartialApplicationForwarder(refPtr);

			var capsule = SwiftObjectRegistry.Registry.ExistingCSObjectForSwiftObject<SwiftDotNetCapsule> (refPtr);
			var delInfo = SwiftObjectRegistry.Registry.ClosureForCapsule (capsule);
			var retval = delInfo.Item1.DynamicInvoke (null);
			StructMarshal.Marshaler.ToSwift (delInfo.Item3, retval, retValPtr);
			StructMarshal.ReleaseSwiftObject (capsule);
		}


		[MonoPInvokeCallback (typeof (Action<IntPtr, IntPtr, IntPtr>))]
		public static void FuncCallback (IntPtr retValPtr, IntPtr args, IntPtr refPtr)
		{
			if (refPtr == IntPtr.Zero)
				throw new ArgumentNullException(nameof(refPtr), "Inside a closure callback, the closure data pointer was null.");
#if DEBUG
			//Console.WriteLine ($"FuncCallback: refPtr initially {refPtr.ToString ("X8")}");
			//Console.WriteLine ("dereferencing refPtr ");
#endif
			refPtr = LocateRefPtrFromPartialApplicationForwarder (refPtr);
#if DEBUG
			//Console.WriteLine($"FuncCallback: retValPtr {retValPtr.ToString("X8")} args {args.ToString("X8")} refPtr {refPtr.ToString("X8")}");
			//Console.WriteLine ("retValPtr: ");
			//Memory.Dump (retValPtr, 128);
			//Console.WriteLine ("args: ");
			//Memory.Dump (args, 128);
			//Console.WriteLine ("refPtr: ");
			//Memory.DumpPtrs (refPtr, 8);
#endif
			var capsule = SwiftObjectRegistry.Registry.ExistingCSObjectForSwiftObject<SwiftDotNetCapsule> (refPtr);
			var delInfo = SwiftObjectRegistry.Registry.ClosureForCapsule (capsule);
#if DEBUG
			//if (delInfo == null) {
			//	Console.WriteLine ("delInfo is null.");
			//}
#endif
			var argumentValues = StructMarshal.Marshaler.MarshalSwiftTupleMemoryToNet (args, delInfo.Item2);
			var retval = delInfo.Item1.DynamicInvoke (argumentValues);
			StructMarshal.Marshaler.ToSwift (delInfo.Item3, retval, retValPtr);
			StructMarshal.ReleaseSwiftObject (capsule);
		}

		[MonoPInvokeCallback (typeof (Action<IntPtr>))]
		public static void ActionCallbackVoidVoid (IntPtr refPtr)
		{
			if (refPtr == IntPtr.Zero)
				throw new ArgumentNullException (nameof (refPtr), "Inside a closure callback, the closure data pointer was null.");
			refPtr = LocateRefPtrFromPartialApplicationForwarder (refPtr);
			#if DEBUG
						//Console.WriteLine($"refPtr {refPtr.ToString("X8")}");
			#endif
			var capsule = SwiftObjectRegistry.Registry.ExistingCSObjectForSwiftObject<SwiftDotNetCapsule> (refPtr);
			var delInfo = SwiftObjectRegistry.Registry.ClosureForCapsule (capsule);
			#if DEBUG
						//if (delInfo == null)
						//{
						//	Console.WriteLine("delInfo is null.");
						//}
			#endif
			delInfo.Item1.DynamicInvoke (null);
			StructMarshal.ReleaseSwiftObject (capsule);
		}


		[MonoPInvokeCallback (typeof (Action<IntPtr, IntPtr>))]
		public static void ActionCallback (IntPtr args, IntPtr refPtr)
		{
			if (refPtr == IntPtr.Zero)
				throw new ArgumentNullException (nameof (refPtr), "Inside a closure callback, the closure data pointer was null.");
			refPtr = LocateRefPtrFromPartialApplicationForwarder (refPtr);

			#if DEBUG
						//Console.WriteLine($"args {args.ToString("X8")} refPtr {refPtr.ToString("X8")}");
			#endif
			var capsule = SwiftObjectRegistry.Registry.ExistingCSObjectForSwiftObject<SwiftDotNetCapsule> (refPtr);
			var delInfo = SwiftObjectRegistry.Registry.ClosureForCapsule (capsule);
			var argumentValues = StructMarshal.Marshaler.MarshalSwiftTupleMemoryToNet (args, delInfo.Item2);
			delInfo.Item1.DynamicInvoke (argumentValues);
			StructMarshal.ReleaseSwiftObject (capsule);
		}
	}

}
