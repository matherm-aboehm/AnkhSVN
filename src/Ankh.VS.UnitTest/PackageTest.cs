// Copyright 2008-2009 The AnkhSVN Project
//
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.

/***************************************************************************

Copyright (c) Microsoft Corporation. All rights reserved.
This code is licensed under the Visual Studio SDK license terms.
THIS CODE IS PROVIDED *AS IS* WITHOUT WARRANTY OF
ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING ANY
IMPLIED WARRANTIES OF FITNESS FOR A PARTICULAR
PURPOSE, MERCHANTABILITY, OR NON-INFRINGEMENT.

***************************************************************************/

using System;
using System.Collections;
using System.Text;
using System.Reflection;
using Microsoft.VisualStudio.Shell.Interop;
using Ankh.VSPackage;
using EnvDTE;
using AnkhSvn_UnitTestProject.Mocks;
using AnkhSvn_UnitTestProject.Helpers;
using Ankh;
using Ankh.Scc;
using NUnit.Framework;
using Moq;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.OLE.Interop;

namespace UnitTestProject
{
    [TestFixture]
    public class PackageTest
    {
        [Test]
        public void CreateInstance()
        {
            AnkhSvnPackage package = new AnkhSvnPackage();
        }

        [Test]
        public void IsIVsPackage()
        {
            AnkhSvnPackage package = new AnkhSvnPackage();
            Assert.IsNotNull(package as IVsPackage, "The object does not implement IVsPackage");
        }

        [Test]
        public void SetSite()
        {
            ServiceProviderHelper.InitAsGlobalServiceProvider();

            // Create the package
            IVsPackage package = new AnkhSvnPackage() as IVsPackage;
            Assert.IsNotNull(package, "The object does not implement IVsPackage");

            var statusCache = new Mock<ISvnStatusCache>();
            var regEditors = new Mock<SVsRegisterEditors>().As<IVsRegisterEditors>();

            var vsShell = new Mock<SVsShell>().As<IVsShell>();
            object r = @"SOFTWARE\Microsoft\VisualStudio\14.0";
            vsShell.Setup(x => x.GetProperty((int)__VSSPROPID.VSSPROPID_VirtualRegistryRoot, out r)).Returns(VSErr.S_OK);
            object falseBox = false;
            vsShell.Setup(x => x.GetProperty((int)__VSSPROPID.VSSPROPID_IsInCommandLineMode, out falseBox)).Returns(VSErr.S_OK);
            const int VSSPROPID_ReleaseVersion = -9068; // VS 12+
            object version = "14.0";
            vsShell.Setup(x => x.GetProperty(VSSPROPID_ReleaseVersion, out version)).Returns(VSErr.S_OK);

            var vsUIShell = new Mock<SVsUIShell>().As<IVsUIShell>();

            var vsTextMgr = new Mock<SVsTextManager>().As<IVsTextManager>();

            var monitorSelection = new Mock<IVsMonitorSelection>();

            var olMgr = new Mock<SOleComponentManager>().As<IOleComponentManager>();

            var outputWindow = new Mock<SVsOutputWindow>().As<IVsOutputWindow>();

            var dte = new Mock<SDTE>().As<_DTE>();
            dte.SetupGet(x => x.Version).Returns((string)null);

            try
            {
                using (ServiceProviderHelper.AddService(typeof(SVsOutputWindow), outputWindow.Object))
                using (ServiceProviderHelper.AddService(typeof(SOleComponentManager), olMgr.Object))
                using (ServiceProviderHelper.AddService(typeof(IVsMonitorSelection), monitorSelection.Object))
                using (ServiceProviderHelper.AddService(typeof(SVsTextManager), vsTextMgr.Object))
                using (ServiceProviderHelper.AddService(typeof(SVsShell), vsShell.Object))
                using (ServiceProviderHelper.AddService(typeof(SVsUIShell), vsUIShell.Object))
                using (ServiceProviderHelper.AddService(typeof(SVsRegisterEditors), regEditors.Object))
                using (ServiceProviderHelper.AddService(typeof(ISvnStatusCache), statusCache.Object))
                using (ServiceProviderHelper.AddService(typeof(SDTE), dte.Object))
                using (ServiceProviderHelper.SetSite(package))
                { }
            }
            finally
            {
                ServiceProviderHelper.DisposeServices();
                ServiceProviderHelper.RemoveAsGlobalServiceProvider();
            }
        }
    }
}
