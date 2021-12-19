using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VsSDK.UnitTestLibrary;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AnkhSvn_UnitTestProject.Helpers
{
    partial class ServiceProviderHelper
    {
        public static void InitAsGlobalServiceProvider()
        {
            LocalRegistryMock localRegistry = new LocalRegistryMock { RegistryRoot = @"SOFTWARE\Microsoft\VisualStudio\14.0" };
            AddService(typeof(SLocalRegistry), localRegistry);

            ServiceProvider.CreateFromSetSite(serviceProvider);
        }

        public static void RemoveAsGlobalServiceProvider()
        {
            ServiceProvider.GlobalProvider.Dispose();
        }

        private static void SetSiteOnUIThread(IVsPackage package, Microsoft.VisualStudio.OLE.Interop.IServiceProvider sp)
        {
            Assert.AreEqual(0, package.SetSite(sp), "SetSite({0}) did not return S_OK", (sp != null ? "sp" : "null"));
        }
    }
}
