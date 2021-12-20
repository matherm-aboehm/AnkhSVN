using System;
using Microsoft.VisualStudio.Shell;
using System.Threading;

namespace Ankh.VSPackage
{
    // Either 'AnkhSvnPackage.LegacyPackage.cs' or 'AnkhSvnPackage.AsyncPackage.cs' is compiled,
    // make sure these files stay in sync

    // This attribute tells the registration utility (regpkg.exe) that this class needs
    // to be registered as package.
    [PackageRegistration(UseManagedResourcesOnly = true)]
    // A Visual Studio component can be registered under different regitry roots; for instance
    // when you debug your package you want to register it in the experimental hive. This
    // attribute specifies the registry root to use if no one is provided to regpkg.exe with
    // the /root switch.
    [DefaultRegistryRoot("Software\\Microsoft\\VisualStudio\\9.0")]

    // In order be loaded inside Visual Studio in a machine that has not the VS SDK installed, 
    // package needs to have a valid load key (it can be requested at 
    // http://msdn.microsoft.com/vstudio/extend/). This attributes tells the shell that this 
    // package has a load key embedded in its resources.
    [ProvideLoadKey("Standard", AnkhId.PlkVersion, AnkhId.PlkProduct, AnkhId.PlkCompany, 1)]
    [ProvideAutoLoad(AnkhId.SccProviderId)] // Load on 'Scc active' for Subversion
    public partial class AnkhSvnPackage : Package
    {
        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        protected override void Initialize()
        {
            base.Initialize();

            if (InCommandLineMode)
                return; // Do nothing; speed up devenv /setup by not loading all our modules!

            InitializeRuntime(); // Moved to function of their own to speed up devenv /setup
            RegisterAsOleComponent();
        }
    }
}
