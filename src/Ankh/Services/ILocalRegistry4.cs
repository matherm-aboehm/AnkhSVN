using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Ankh.Configuration
{
    [Guid("5C45B909-E820-4ACC-B894-0A013C6DA212"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [ComImport]
    public interface ILocalRegistry4
    {
        int RegisterClassObject([ComAliasName("Microsoft.VisualStudio.OLE.Interop.REFCLSID")] [In] ref Guid rclsid, [ComAliasName("Microsoft.VisualStudio.OLE.Interop.DWORD")] out uint pdwCookie);
        int RevokeClassObject([ComAliasName("Microsoft.VisualStudio.OLE.Interop.DWORD")] uint dwCookie);
        int RegisterInterface([ComAliasName("Microsoft.VisualStudio.OLE.Interop.REFIID")] [In] ref Guid riid);
        int GetLocalRegistryRootEx([ComAliasName("Microsoft.VisualStudio.Shell.Interop.VSLOCALREGISTRYTYPE")] [In] uint dwRegType, [ComAliasName("Microsoft.VisualStudio.Shell.Interop.VSLOCALREGISTRYROOTHANDLE")] out uint pdwRegRootHandle, [MarshalAs(19)] out string pbstrRoot);
    }
}
