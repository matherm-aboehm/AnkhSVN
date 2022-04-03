using System;
using Microsoft.VisualStudio.Shell;
using System.Threading;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

namespace Ankh.VSPackage
{
    // Either 'AnkhSvnPackage.LegacyPackage.cs' or 'AnkhSvnPackage.AsyncPackage.cs' is compiled,
    // make sure these files stay in sync

    // This attribute tells the registration utility (regpkg.exe) that this class needs
    // to be registered as package.
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [ProvideAutoLoad(AnkhId.SccProviderId, PackageAutoLoadFlags.BackgroundLoad)] // Load on 'Scc active' for Subversion
    [ProvideBindingPath]
    public partial class AnkhSvnPackage : AsyncPackage
    {

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        protected async override Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await base.InitializeAsync(cancellationToken, progress);

            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            if (InCommandLineMode)
                return; // Do nothing; speed up devenv /setup by not loading all our modules!

            InitializeRuntime(); // Moved to function of their own to speed up devenv /setup
            RegisterAsOleComponent();
        }

        //HACK: AsyncPackage allows access to GetService only from UI thread
        //TODO: Implement async versions of AnkhService, AnkhServiceContainer, IAnkhServiceProvider, etc. if possible
        private async Task<object> GetServiceFromUIThreadAsync(Type serviceType)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            return base.GetService(serviceType);
        }

        protected override object GetService(Type serviceType)
        {
            if (_staticServices.TryGetValue(serviceType, out var v))
            {
                return v;
            }
            if (serviceType == typeof(IAnkhServiceProvider)
                || serviceType == typeof(IAnkhQueryService))
            {
                return this;
            }

            return ThreadHelper.CheckAccess() ? base.GetService(serviceType) : JoinableTaskFactory.Run(() => GetServiceFromUIThreadAsync(serviceType));
        }
    }
}
