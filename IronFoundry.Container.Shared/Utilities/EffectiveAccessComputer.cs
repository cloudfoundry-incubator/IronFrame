﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using IronFoundry.Container.Win32;

namespace IronFoundry.Container.Utilities
{
    using PDWORD = IntPtr;
    using PACCESS_MASK = IntPtr;

    public interface IEffectiveAccessComputer
    {
        ACCESS_MASK ComputeAccess(RawSecurityDescriptor descriptor, IdentityReference identity);
    }

    public class EffectiveAccessComputer : IEffectiveAccessComputer
    {
        public ACCESS_MASK ComputeAccess(RawSecurityDescriptor descriptor, IdentityReference identity)
        {
            var disposables = new List<IDisposable>();
            var accessGranted = ACCESS_MASK.NONE;

            try
            {
                // Create the Resource Manager
                SafeAuthzRMHandle authzRM;
                if (!NativeMethods.AuthzInitializeResourceManager(
                    NativeMethods.AuthzResourceManagerFlags.NO_AUDIT,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    "EffectiveAccessCheck",
                    out authzRM))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                disposables.Add(authzRM);


                // Create the context for the user
                var securityIdentifier = (SecurityIdentifier) identity.Translate(typeof (SecurityIdentifier));
                var rawSid = new byte[securityIdentifier.BinaryLength];
                securityIdentifier.GetBinaryForm(rawSid, 0);

                SafeAuthzContextHandle userClientCtxt;
                if (!NativeMethods.AuthzInitializeContextFromSid(
                    NativeMethods.AuthzInitFlags.Default,
                    rawSid,
                    authzRM,
                    IntPtr.Zero,
                    NativeMethods.LUID.NullLuid,
                    IntPtr.Zero,
                    out userClientCtxt))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                disposables.Add(userClientCtxt);


                // Prepare the Access Check request
                var request = new NativeMethods.AUTHZ_ACCESS_REQUEST();
                request.DesiredAccess = ACCESS_MASK.MAXIMUM_ALLOWED;
                request.PrincipalSelfSid = null;
                request.ObjectTypeList = IntPtr.Zero;
                request.ObjectTypeListLength = 0;
                request.OptionalArguments = IntPtr.Zero;

                // Prepare the access check reply
                var grantedAccessBuffer = new SafeHGlobal(sizeof (ACCESS_MASK));
                disposables.Add(grantedAccessBuffer);

                var errorBuffer = new SafeHGlobal(sizeof (uint));
                disposables.Add(errorBuffer);

                var reply = new NativeMethods.AUTHZ_ACCESS_REPLY();
                reply.ResultListLength = 1;
                reply.SaclEvaluationResults = IntPtr.Zero;
                reply.GrantedAccessMask = grantedAccessBuffer.DangerousGetHandle();
                reply.Error = errorBuffer.DangerousGetHandle();

                // Do the access check
                var rawSD = new byte[descriptor.BinaryLength];
                descriptor.GetBinaryForm(rawSD, 0);

                if (!NativeMethods.AuthzAccessCheck(
                    NativeMethods.AuthzACFlags.None,
                    userClientCtxt,
                    ref request,
                    IntPtr.Zero,
                    rawSD,
                    null,
                    0,
                    ref reply,
                    IntPtr.Zero))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                accessGranted = (ACCESS_MASK) Marshal.ReadInt32(grantedAccessBuffer.DangerousGetHandle());
            }
            finally
            {
                // Clean up all the unmanaged memory
                foreach (var disposable in disposables)
                {
                    disposable.Dispose();
                }
            }

            return accessGranted;
        }
    }
}