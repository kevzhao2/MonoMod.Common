﻿using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Reflection.Emit;
using MonoMod.Utils;
using System.Linq;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.IO;

namespace MonoMod.RuntimeDetour.Platforms {
#if !MONOMOD_INTERNAL
    public
#endif
    class DetourRuntimeNETCorePlatform : DetourRuntimeNETPlatform {

#if MONOMOD_RUNTIMEDETOUR
        // All of this stuff is for JIT hooking in RuntimeDetour so we can update hooks when a method is re-jitted
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate IntPtr d_getJit();
        private static d_getJit getJit;

        protected static IntPtr GetJitObject() {
            if (getJit == null) {
                // To make sure we get the right clrjit, we enumerate the process's modules and find the one 
                //   with the name we care aboutm, then use its full path to gat a handle and load symbols.
                Process currentProc = Process.GetCurrentProcess();
                ProcessModule clrjitModule = currentProc.Modules.Cast<ProcessModule>()
                    .FirstOrDefault(m => Path.GetFileNameWithoutExtension(m.FileName).EndsWith("clrjit"));
                if (clrjitModule == null)
                    throw new PlatformNotSupportedException();

                if (!DynDll.TryOpenLibrary(clrjitModule.FileName, out IntPtr clrjitPtr) || clrjitPtr == IntPtr.Zero)
                    throw new PlatformNotSupportedException();

                try {
                    getJit = clrjitPtr.GetFunction(nameof(getJit)).AsDelegate<d_getJit>();
                } catch {
                    DynDll.CloseLibrary(clrjitPtr);
                    throw;
                }
            }

            return getJit();
        }

        protected static Guid GetJitGuid(IntPtr jit) {
            d_getVersionIdentifier getVersionIdentifier = ReadObjectVTable(jit, vtableIndex_ICorJitCompiler_getVersionIdentifier)
                .AsDelegate<d_getVersionIdentifier>();
            getVersionIdentifier(jit, out Guid guid);
            return guid;
        }

        protected enum CorJitResult { 
            CORJIT_OK = 0,
            // There are more, but I don't particularly care about them
        }

        protected const int vtableIndex_ICorJitCompiler_compileMethod = 0;
        private   const int vtableIndex_ICorJitCompiler_getVersionIdentifier = 4;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        protected unsafe delegate CorJitResult d_compileMethod(
            IntPtr thisPtr, // ICorJitCompiler
            IntPtr corJitInfo, // ICorJitInfo*
            IntPtr methodInfo, // CORINFO_METHOD_INFO*
            uint flags,
            out byte* nativeEntry,
            out ulong nativeSizeOfCode
            );

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void d_getVersionIdentifier(
            IntPtr thisPtr, // ICorJitCompiler
            out Guid versionIdentifier
            );

        protected static unsafe IntPtr* GetVTableEntry(IntPtr @object, int index) 
            => (*(IntPtr**)@object) + index;
        protected static unsafe IntPtr ReadObjectVTable(IntPtr @object, int index)
            => *GetVTableEntry(@object, index);
#endif

        protected override unsafe void DisableInlining(MethodBase method, RuntimeMethodHandle handle) {
            // https://github.com/dotnet/runtime/blob/89965be3ad2be404dc82bd9e688d5dd2a04bcb5f/src/coreclr/src/vm/method.hpp#L178
            // mdcNotInline = 0x2000
            // References to RuntimeMethodHandle (CORINFO_METHOD_HANDLE) pointing to MethodDesc
            // can be traced as far back as https://ntcore.com/files/netint_injection.htm

            // FIXME: Take a very educated guess regarding the offset to m_wFlags in MethodDesc.

            const int offset =
                2 // UINT16 m_wFlags3AndTokenRemainder
              + 1 // BYTE m_chunkIndex
              + 1 // BYTE m_chunkIndex
              + 2 // WORD m_wSlotNumber
              ;
            ushort* m_wFlags = (ushort*)(((byte*) handle.Value) + offset);
            *m_wFlags |= 0x2000;
        }

        public static DetourRuntimeNETCorePlatform Create() {
            // This only needs to have a specialized method when working in RuntimeDetour, because
            //   it needs the JIT hooks that the specializations look for and install.
#if MONOMOD_RUNTIMEDETOUR
            IntPtr jit = GetJitObject();
            Guid jitGuid = GetJitGuid(jit);

            throw new NotImplementedException();
#else
            return new DetourRuntimeNETCorePlatform();
#endif
        }
    }
}
