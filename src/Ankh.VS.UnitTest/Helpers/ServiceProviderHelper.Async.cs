using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.Threading;
using Microsoft.VsSDK.UnitTestLibrary;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace AnkhSvn_UnitTestProject.Helpers
{
    partial class ServiceProviderHelper
    {
        private delegate IVsTask QueryServiceAsyncDelegate([In] ref Guid guidService);
        private delegate Task<object> GetServiceAsyncDelegate(Type serviceType);

        private static Thread CreateAndStartSTAThread<T>(Action<T> startAction, T state)
        {
            void worker(object s)
            {
                var (startAction2, state2) = ((Action<T>, T))s;
                startAction2(state2);
            }
            Thread thread = new Thread(worker);
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start((startAction, state));
            return thread;
        }

        private class MainThreadInitializeHelper
        {
            public DispatcherFrame Frame { get; private set; }
            public SynchronizationContext SyncContext { get; private set; }
            public AutoResetEvent InitializedEvent { get; } = new AutoResetEvent(false);
            public Thread MainThread { get; private set; }
            public Exception Error { get; private set; }

            public void StartNew()
            {
                MainThread = CreateAndStartSTAThread(helper =>
                {
                    try
                    {
                        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
#pragma warning disable VSTHRD001 // Legacy-APIs zum Wechseln von Threads vermeiden
                        var forget = dispatcher.BeginInvoke(DispatcherPriority.Send, (DispatcherOperationCallback)delegate (object state)
#pragma warning restore VSTHRD001 // Legacy-APIs zum Wechseln von Threads vermeiden
                        {
                            var helper2 = (MainThreadInitializeHelper)state;
                            helper2.SyncContext = SynchronizationContext.Current;
                            //Mocks.VsTaskMock.EnsureDispatcherScheduler();
                            helper2.InitializedEvent.Set();
                            return null;
                        }, helper);
                        ServiceProvider.CreateFromSetSite(serviceProvider);
                        ThreadHelper.ThrowIfNotOnUIThread();
                        helper.Frame = new DispatcherFrame();
                        Dispatcher.PushFrame(helper.Frame);
                    }
                    catch (Exception ex)
                    {
                        helper.Error = ex;
                        helper.InitializedEvent.Set();
                    }
                }, this);
                InitializedEvent.WaitOne();
                if (Error != null)
                    ExceptionDispatchInfo.Capture(Error).Throw();
                Assert.IsNotNull(SyncContext, "AsyncPackage needs a synchronization context");
            }

            public void ShutDown()
            {
                Frame.Continue = false;
                MainThread.Join();
            }
        }

        private static JoinableTaskContext taskContext;
        private static MainThreadInitializeHelper mainThreadHelper;
        private static Mock<IDisposable> schedulerServiceDisposable;

        public static void InitAsGlobalServiceProvider()
        {
            // Init global service provider
            var asyncProviderMock = new Mock<SAsyncServiceProvider>();
            var asyncProvider = asyncProviderMock.As<Microsoft.VisualStudio.Shell.Interop.IAsyncServiceProvider>();
            var asyncProvider2 = asyncProviderMock.As<Microsoft.VisualStudio.Shell.IAsyncServiceProvider>();
            asyncProvider.Setup(x => x.QueryServiceAsync(ref It.Ref<Guid>.IsAny)).Returns((QueryServiceAsyncDelegate)QueryServiceAsync);
            asyncProvider2.Setup(x => x.GetServiceAsync(It.IsAny<Type>())).Returns((GetServiceAsyncDelegate)GetServiceAsync);
            AddService(typeof(SAsyncServiceProvider), asyncProviderMock.Object);

            var asyncProffer = new Mock<SProfferAsyncService>().As<IProfferAsyncService>();
            asyncProffer.Setup(x => x.ProfferAsyncService(ref It.Ref<Guid>.IsAny, asyncProvider.Object)).Returns(0);
            AddService(typeof(SProfferAsyncService), asyncProffer.Object);

            LocalRegistryMock localRegistry = new LocalRegistryMock { RegistryRoot = @"SOFTWARE\Microsoft\VisualStudio\14.0" };
            AddService(typeof(SLocalRegistry), localRegistry);

            var schedulerServiceMock = new Mock<SVsTaskSchedulerService>(MockBehavior.Strict);
            var schedulerService = schedulerServiceMock.As<IVsTaskSchedulerService>();
            schedulerService.Setup(x => x.CreateTaskCompletionSource()).Returns(() => new Mocks.VsTaskCompletionSourceMock(null, 0));
            schedulerService.Setup(x => x.CreateTaskCompletionSourceEx(It.IsAny<uint>(), It.IsAny<object>())).Returns((uint o, object s) => new Mocks.VsTaskCompletionSourceMock(s, (VsTaskCreationOptions)o));
            var schedulerService2 = schedulerServiceMock.As<IVsTaskSchedulerService2>();
            mainThreadHelper = new MainThreadInitializeHelper();
            mainThreadHelper.StartNew();
            schedulerServiceDisposable = schedulerServiceMock.As<IDisposable>();
            schedulerServiceDisposable.Setup(x => x.Dispose()).Callback(() => mainThreadHelper.ShutDown()).Verifiable();
#pragma warning disable VSSDK005 // Avoid instantiating JoinableTaskContext
            taskContext = new JoinableTaskContext(mainThreadHelper.MainThread, mainThreadHelper.SyncContext);
#pragma warning restore VSSDK005 // Avoid instantiating JoinableTaskContext
            schedulerService2.Setup(x => x.GetAsyncTaskContext()).Returns(() => taskContext);
            uint[] allcontexts = (uint[])Enum.GetValues(typeof(VsTaskRunContext));
            schedulerService2.Setup(x => x.GetTaskScheduler(It.IsIn(allcontexts))).Returns((uint c) => Mocks.VsTaskMock.GetSchedulerFromContext((VsTaskRunContext)c));
            AddService(typeof(SVsTaskSchedulerService), schedulerServiceMock.Object);

            var taskContextFromMock = ThreadHelper.JoinableTaskContext;
            Assert.AreSame(taskContext, taskContextFromMock, "mocking framework returned the wrong object");
        }

        public static void RemoveAsGlobalServiceProvider()
        {
            schedulerServiceDisposable.Verify();
            schedulerServiceDisposable = null;
            ServiceProvider.GlobalProvider.Dispose();
        }

        private static IVsTask QueryServiceAsync([In] ref Guid guidService)
        {
            Guid serviceGuid = guidService;
            return ThreadHelper.JoinableTaskFactory.RunAsyncAsVsTask(VsTaskRunContext.CurrentContext, async token =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);
                return serviceProvider.QueryService(serviceGuid);
            });
        }

        private static async Task<object> GetServiceAsync(Type serviceType)
        {
            Guid serviceGuid = serviceType.GUID;
            return await QueryServiceAsync(ref serviceGuid);
        }

        private static void SetSiteOnUIThread(IVsPackage package, Microsoft.VisualStudio.OLE.Interop.IServiceProvider sp)
        {
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                Assert.AreEqual(0, package.SetSite(sp), "SetSite({0}) did not return S_OK", (sp != null ? "sp" : "null"));
                // Workaround for old Microsoft.VisualStudio.Shell.15.0
                if (package is AsyncPackage asyncPackage && asyncPackage.JoinableTaskFactory == null)
                {
                    Microsoft.VisualStudio.Shell.Interop.IAsyncServiceProvider asyncProvider = ServiceProvider.GlobalProvider.GetService(typeof(SAsyncServiceProvider)) as Microsoft.VisualStudio.Shell.Interop.IAsyncServiceProvider;
                    IProfferAsyncService asyncProffer = ServiceProvider.GlobalProvider.GetService(typeof(SProfferAsyncService)) as IProfferAsyncService;
                    await ((IAsyncLoadablePackageInitialize)asyncPackage).Initialize(asyncProvider, asyncProffer, null);
                }
            });
        }
    }
}
