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

using System;
using Microsoft.VsSDK.UnitTestLibrary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System.Collections.Generic;

namespace AnkhSvn_UnitTestProject.Helpers
{
    partial class ServiceProviderHelper : IDisposable
    {
        internal static OleServiceProvider serviceProvider;
        static ServiceProviderHelper()
        {
            serviceProvider = OleServiceProvider.CreateOleServiceProviderWithBasicServices();
            //AddService(typeof(Ankh.UI.IAnkhPackage), AnkhSvn_UnitTestProject.Mocks.PackageMock.EmptyContext(serviceProvider));
        }

        Type type;
        readonly object _instance;
        private ServiceProviderHelper(Type t, object instance)
        {
            type = t;
            _instance = instance;
            serviceProvider.AddService(t, instance, false);
        }

        public void Dispose()
        {
            IDisposable i = _instance as IDisposable;
            if (i != null)
                i.Dispose();

            if (type != null && serviceProvider.GetService(type) != null)
                serviceProvider.RemoveService(type);
        }

        public static IDisposable AddService(Type t, object instance)
        {
            _types.Add(t);
            return new ServiceProviderHelper(t, instance);
        }

        static readonly List<Type> _types = new List<Type>();

        private class SitedPackage : IDisposable
        {
            readonly IVsPackage package;

            public SitedPackage(IVsPackage package)
            {
                this.package = package;
            }

            public void Dispose()
            {
                // Unsite the package
                SetSiteOnUIThread(package, null);
            }
        }

        public static IDisposable SetSite(IVsPackage package)
        {
            SetSiteOnUIThread(package, serviceProvider);
            return new SitedPackage(package);
        }

        internal static void DisposeServices()
        {
            foreach (Type t in _types)
            {
                object service = null;
                if ((service = serviceProvider.GetService(t)) != null)
                {
                    (service as IDisposable)?.Dispose();
                    serviceProvider.RemoveService(t);
                }
            }

            _types.Clear();
        }
    }
}
