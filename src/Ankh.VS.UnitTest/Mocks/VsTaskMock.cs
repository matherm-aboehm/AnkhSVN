using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Task = System.Threading.Tasks.Task;
using System.Collections.Concurrent;
using System.Windows.Threading;
using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace AnkhSvn_UnitTestProject.Mocks
{
    internal class VsTaskMock : IVsTask, IVsTask2, IVsTaskJoinableTask, IVsTaskEvents
    {
        internal static Func<IVsTask, IVsTaskBody, IVsTaskBody> adapterFunctionDelegate;
        private JoinableTask joinableTask;
        private JoinableTask dependencyJoinableTask;
        private List<VsTaskMock> attachedTasks;
        private AsyncAutoResetEvent newDependencyWaiter;
        internal enum VsTaskDependency
        {
            Continuation = 1,
            AttachedTask,
            WaitForExecution
        }

        public IVsTask[] DependentTasks { get; private set; }
        internal Task<object> InternalTask { get; private set; }
        protected bool IsCancelable { get; private set; }
        protected CancellationTokenSource TaskCancellationTokenSource { get; private set; }
        protected CancellationToken TaskCancellationToken => IsCancelable ? TaskCancellationTokenSource.Token : CancellationToken.None;
        public VsTaskRunContext TaskContext { get; private set; }
        private TaskScheduler AssignedScheduler => GetSchedulerFromContext(TaskContext);

        internal VsTaskMock(TaskCompletionSource<object> tcs)
            : this(null, VsTaskRunContext.BackgroundThread, tcs.Task.AsyncState, true, false, false)
        {
            InternalTask = tcs.Task;
        }

        private VsTaskMock(IVsTask[] dependentTasks, VsTaskRunContext context, object asyncState, bool isCancelable, bool isCanceledWithParent, bool isIndependentlyCanceled)
        {
            DependentTasks = dependentTasks;
            //DependentTasksCount = ((dependentTasks != null) ? dependentTasks.Length : 0);
            TaskContext = CalculateTaskRunContext(context);
            //TaskState = VsTaskState.Created;
            AsyncState = asyncState;
            //CreationTime = DateTime.UtcNow;
            IsCancelable = isCancelable;
            if (!IsCancelable)
            {
                return;
            }
            if (!isIndependentlyCanceled && DependentTasks != null && DependentTasks.Length == 1 &&
                DependentTasks[0] is VsTaskMock dependentTask && dependentTask.IsCancelable)
            {
                TaskCancellationTokenSource = dependentTask.TaskCancellationTokenSource;
            }
            else
            {
                TaskCancellationTokenSource = new CancellationTokenSource();
            }
            if (isCanceledWithParent)
            {
                GetCurrentTask()?.TaskCancellationToken.Register(delegate
                {
                    Cancel();
                });
            }
        }

        #region VsRunningTasksManager
        [ThreadStatic]
        private static Stack<VsTaskMock> runningTasks;

        internal static VsTaskMock GetCurrentTask()
        {
            if (runningTasks == null || runningTasks.Count == 0)
                return null;
            return runningTasks.Peek();
        }
        internal static void PushCurrentTask(VsTaskMock task)
        {
            if (runningTasks == null)
                runningTasks = new Stack<VsTaskMock>();
            runningTasks.Push(task);
        }
        internal static void PopCurrentTask()
        {
            if (runningTasks != null && runningTasks.Count != 0)
                runningTasks.Pop();
        }
        #endregion

        internal static TaskCreationOptions GetTPLOptions(VsTaskCreationOptions options)
        {
            return (TaskCreationOptions)(options & (VsTaskCreationOptions)0x5FFFFFFF);
        }

        internal static TaskContinuationOptions GetTPLOptions(VsTaskContinuationOptions options)
        {
            TaskContinuationOptions taskContinuationOptions = (TaskContinuationOptions)(options & (VsTaskContinuationOptions)0x1FFFFFFF);
            return taskContinuationOptions & ~TaskContinuationOptions.ExecuteSynchronously;
        }

        internal static VsTaskRunContext CalculateTaskRunContext(VsTaskRunContext inputContext)
        {
            VsTaskRunContext result = inputContext;
            if (inputContext == VsTaskRunContext.CurrentContext)
            {
                bool isUIThread = ThreadHelper.CheckAccess();
                VsUIThreadBlockableTaskScheduler vsTaskScheduler = TaskScheduler.Current as VsUIThreadBlockableTaskScheduler;
                result = ((vsTaskScheduler == null || isUIThread != vsTaskScheduler.IsUIThreadScheduler) ? (isUIThread ? VsTaskRunContext.UIThreadNormalPriority : VsTaskRunContext.BackgroundThread) : vsTaskScheduler.SchedulerContext);
                //result = TaskScheduler.Current == DispatcherTaskScheduler || isUIThread ? VsTaskRunContext.UIThreadNormalPriority : VsTaskRunContext.BackgroundThread;
            }
            return result;
        }

        //private static readonly TaskScheduler DispatcherTaskScheduler = SynchronizationContext.Current != null ? TaskScheduler.FromCurrentSynchronizationContext() : null;
        private static readonly VsUIThreadBlockableTaskScheduler UIContextBackgroundPriorityScheduler =
            new VsUIThreadBlockableTaskScheduler(VsTaskRunContext.UIThreadBackgroundPriority);
        private static readonly VsUIThreadBlockableTaskScheduler UIContextScheduler =
            new VsUIThreadBlockableTaskScheduler(VsTaskRunContext.UIThreadSend);
        private static readonly VsUIThreadBlockableTaskScheduler UIContextIdleTimeScheduler =
            new VsUIThreadBlockableTaskScheduler(VsTaskRunContext.UIThreadIdlePriority);
        private static readonly VsUIThreadBlockableTaskScheduler UIContextNormalPriorityScheduler =
            new VsUIThreadBlockableTaskScheduler(VsTaskRunContext.UIThreadNormalPriority);
        private static readonly TaskScheduler BackgroundThreadLowIOPriorityScheduler = TaskScheduler.Default;
        //internal static void EnsureDispatcherScheduler()
        //{
        //    Assert.IsNotNull(DispatcherTaskScheduler, "current thread needs a SynchronizationContext");
        //}

        internal static IEnumerable<VsUIThreadBlockableTaskScheduler> GetAllUIThreadSchedulers()
        {
            yield return UIContextBackgroundPriorityScheduler;
            yield return UIContextIdleTimeScheduler;
            yield return UIContextNormalPriorityScheduler;
        }

        internal static TaskScheduler GetSchedulerFromContext(VsTaskRunContext context)
        {
            switch (context)
            {
                case VsTaskRunContext.BackgroundThread:
                    return TaskScheduler.Default;
                case VsTaskRunContext.BackgroundThreadLowIOPriority:
                    return BackgroundThreadLowIOPriorityScheduler;
                case VsTaskRunContext.UIThreadSend:
                    return UIContextScheduler;
                case VsTaskRunContext.UIThreadBackgroundPriority:
                    return UIContextBackgroundPriorityScheduler;
                case VsTaskRunContext.UIThreadIdlePriority:
                    return UIContextIdleTimeScheduler;
                case VsTaskRunContext.UIThreadNormalPriority:
                    return UIContextNormalPriorityScheduler;
                case VsTaskRunContext.CurrentContext:
                    if (ThreadHelper.CheckAccess())
                    {
                        VsTaskRunContext vsTaskRunContext = CalculateTaskRunContext(context);
                        if (vsTaskRunContext != VsTaskRunContext.CurrentContext)
                        {
                            return GetSchedulerFromContext(vsTaskRunContext);
                        }
                        return UIContextNormalPriorityScheduler;
                    }
                    if (SynchronizationContext.Current != null)
                        return TaskScheduler.FromCurrentSynchronizationContext();
                    return TaskScheduler.Default;
                default:
                    throw new ArgumentException("Unknown task run context", nameof(context));
            }
        }

        internal IEnumerable<VsTaskMock> GetAllDependentVsTasks(bool ignoreCanceledTasks = true)
        {
            bool isActive(IVsTask t) => !t.IsCompleted && (!ignoreCanceledTasks || !t.IsCanceled);
            HashSet<VsTaskMock> seenSet = new HashSet<VsTaskMock>();
            Stack<VsTaskMock> remaining = new Stack<VsTaskMock>();
            remaining.Push(this);
            seenSet.Add(this);
            while (remaining.Count > 0)
            {
                VsTaskMock current = remaining.Pop();
                yield return current;
                IVsTask[] dependentTasks = current.DependentTasks;
                List<VsTaskMock> list = current.attachedTasks;
                if (list == null)
                {
                    if (dependentTasks == null)
                        continue;
                    foreach (IVsTask t in dependentTasks.Where(isActive))
                    {
                        if (t is VsTaskMock vsTask && seenSet.Add(vsTask))
                            remaining.Push(vsTask);
                    }
                }
                else
                {
                    lock (list)
                    {
                        IEnumerable<IVsTask> enumerable = dependentTasks;
                        foreach (IVsTask t in list.Union(enumerable ?? Enumerable.Empty<IVsTask>()).Where(isActive))
                        {
                            if (t is VsTaskMock vsTask && seenSet.Add(vsTask))
                                remaining.Push(vsTask);
                        }
                    }
                }
            }
        }

        internal IEnumerable<Task> GetAllDependentInternalTasks()
        {
            foreach (VsTaskMock allDependentVsTask in GetAllDependentVsTasks())
            {
                yield return allDependentVsTask.InternalTask;
            }
        }

        internal static void JoinAntecedentJoinableTasks(IVsTask task)
        {
            VsTaskMock vsTask = task as VsTaskMock;
            if (vsTask == null || !ThreadHelper.JoinableTaskContext.IsWithinJoinableTask)
            {
                return;
            }
            foreach (VsTaskMock dependentTask in vsTask.GetAllDependentVsTasks(ignoreCanceledTasks: false))
            {
                if (dependentTask.joinableTask != null)
                {
                    dependentTask.joinableTask.JoinAsync().Forget();
                    continue;
                }
                if (dependentTask.dependencyJoinableTask != null)
                {
                    dependentTask.dependencyJoinableTask.JoinAsync().Forget();
                    continue;
                }
                dependentTask.dependencyJoinableTask = ThreadHelper.JoinableTaskContext.Factory.RunAsync(async delegate
                {
                    if (dependentTask.AssignedScheduler is VsUIThreadBlockableTaskScheduler blockableTaskScheduler)
                        blockableTaskScheduler.TryExecuteTaskAsync(dependentTask.InternalTask).Forget();
                    int lastNotifiedAttachedTaskIndex = 0;
                    IEnumerable<VsTaskMock> newDependencies;
                    while ((newDependencies = await dependentTask.WaitForNewDependenciesAsync(lastNotifiedAttachedTaskIndex).ConfigureAwait(continueOnCapturedContext: false)) != null)
                    {
                        foreach (VsTaskMock item in newDependencies)
                        {
                            lastNotifiedAttachedTaskIndex++;
                            JoinAntecedentJoinableTasks(item);
                        }
                    }
                });
            }
        }
        private List<VsTaskMock> GetNewDependenciesSinceLastCheck(int lastIndexChecked)
        {
            List<VsTaskMock> result = null;
            if (attachedTasks != null)
            {
                lock (attachedTasks)
                {
                    while (lastIndexChecked < attachedTasks.Count)
                    {
                        if (result == null)
                            result = new List<VsTaskMock>();

                        result.Add(attachedTasks[lastIndexChecked]);
                        lastIndexChecked++;
                    }
                    return result;
                }
            }
            return result;
        }

        private async Task<IEnumerable<VsTaskMock>> WaitForNewDependenciesAsync(int lastNotifiedAttachedTaskIndex)
        {
            if (!IsCompleted && !IsCanceled)
            {
                AsyncAutoResetEvent asyncAutoResetEvent = LazyInitializer.EnsureInitialized(ref newDependencyWaiter, () => new AsyncAutoResetEvent(allowInliningAwaiters: true));
                if (lastNotifiedAttachedTaskIndex == 0)
                {
                    List<VsTaskMock> newDependenciesSinceLastCheck = GetNewDependenciesSinceLastCheck(lastNotifiedAttachedTaskIndex);
                    if (newDependenciesSinceLastCheck != null)
                        return newDependenciesSinceLastCheck;
                }
                Task resetEventTask = asyncAutoResetEvent.WaitAsync();
                if (await Task.WhenAny(InternalTask, resetEventTask).ConfigureAwait(continueOnCapturedContext: false) == resetEventTask)
                {
                    return GetNewDependenciesSinceLastCheck(lastNotifiedAttachedTaskIndex) ?? Enumerable.Empty<VsTaskMock>();
                }
            }
            return null;
        }

        private static bool EnsureTaskIsNotBlocking(VsTaskMock taskToUnblock, VsTaskDependency dependencyType = VsTaskDependency.AttachedTask)
        {
            VsTaskMock currentTask = GetCurrentTask();
            if (currentTask != null)
            {
                bool checkForCycle = dependencyType == VsTaskDependency.WaitForExecution;
                if (!currentTask.TryAddDependentTask(taskToUnblock, checkForCycle))
                {
                    return false;
                }
                //VsTaskSchedulerService.RaiseDependencyTaskEvent(currentTask, taskToUnblock, dependencyType);
            }
            return true;
        }

        internal static Func<Task<object>, object> GetCallbackForSingleParent(IVsTaskBody taskBody, VsTaskMock task, VsTaskRunContext context)
        {
            if (task.DependentTasks == null || task.DependentTasks.Length != 1)
                throw new ArgumentException("Invalid number of parent tasks", "task");

            return (Task<object> internalTask) => GetCallbackForMultipleParent(taskBody, task, context)(new Task<object>[1] { internalTask });
        }

        internal static Func<Task<object>[], object> GetCallbackForMultipleParent(IVsTaskBody taskBody, VsTaskMock task, VsTaskRunContext context)
        {
            return delegate
            {
                PushCurrentTask(task);
                //task.TaskState = VsTaskState.Running;
                object pResult = null;
                try
                {
                    if (TryAdaptTaskBody(task, taskBody, out var adaptedBody))
                    {
                        taskBody = adaptedBody;
                    }
                    taskBody.DoWork(task, (uint)(task.DependentTasks?.Length ?? 0), task.DependentTasks, out pResult);
                    //task.TaskState = VsTaskState.Completed;
                    return pResult;
                }
                catch (Exception)
                {
                    //task.TaskState = VsTaskState.Faulted;
                    throw;
                }
                finally
                {
                    if (Marshal.IsComObject(taskBody))
                        Marshal.ReleaseComObject(taskBody);

                    task.DependentTasks = null;
                    PopCurrentTask();
                }
            };
        }

        private static bool TryAdaptTaskBody(IVsTask task, IVsTaskBody existingBody, out IVsTaskBody adaptedBody)
        {
            adaptedBody = null;
            if (adapterFunctionDelegate == null)
                return false;
            adaptedBody = adapterFunctionDelegate(task, existingBody);
            return true;
        }

        internal bool TryAddDependentTask(IVsTask task, bool checkForCycle)
        {
            Assert.IsNotNull(task, "task");
            VsTaskMock vsTask = task as VsTaskMock;
            if (vsTask != null)
            {
                if (checkForCycle && vsTask.GetAllDependentVsTasks().Contains(this))
                    return false;

                lock (LazyInitializer.EnsureInitialized(ref attachedTasks))
                {
                    attachedTasks.Add(vsTask);
                }
                newDependencyWaiter?.Set();
                if (VsUIThreadBlockableTaskScheduler.IsBlockingTask(InternalTask))
                {
                    foreach (VsTaskMock item in vsTask.GetAllDependentVsTasks().Distinct())
                        item.RaiseOnMarkedAsBlockingEvent(VsUIThreadBlockableTaskScheduler.GetCurrentTaskWaitedOnUIThread() ?? this);

                    VsUIThreadBlockableTaskScheduler.PromoteTaskIfBlocking(vsTask, this);
                }
            }
            return true;
        }

        private static bool OptionsHasFlag(VsTaskContinuationOptions options, VsTaskContinuationOptions flag)
        {
            return (options & flag) != 0;
        }

        private static Exception RethrowException(AggregateException e)
        {
            AggregateException ex = e.Flatten();
            Exception source = ((ex.InnerExceptions.Count == 1) ? ex.InnerExceptions[0] : e);
            ExceptionDispatchInfo.Capture(source).Throw();
            throw new InvalidOperationException("Unreachable");
        }
        private static void IgnoreObjectDisposedException(Action action)
        {
            try
            {
                action();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private T InvokeWithWaitDialog<T>(Func<T> function)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            VsUIThreadBlockableTaskScheduler.BeginTaskWaitOnUIThread(this);
            try
            {
                IVsThreadedWaitDialogFactory vsThreadedWaitDialogFactory = Package.GetGlobalService(typeof(SVsThreadedWaitDialogFactory)) as IVsThreadedWaitDialogFactory;
                IVsThreadedWaitDialog4 vsThreadedWaitDialog;
                if (vsThreadedWaitDialogFactory != null)
                {
                    vsThreadedWaitDialogFactory.CreateInstance(out var ppIVsThreadedWaitDialog);
                    vsThreadedWaitDialog = ppIVsThreadedWaitDialog as IVsThreadedWaitDialog4;
                }
                else
                {
                    vsThreadedWaitDialog = null;
                }
                bool dialogStarted = false;
                if (vsThreadedWaitDialog != null)
                {
                    string text = WaitMessage;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        dialogStarted = vsThreadedWaitDialog.StartWaitDialogEx(null, text, null, null,
                            "Waiting for a requited operation to complete...", 2, fIsCancelable: false, fShowMarqueeProgress: true);
                    }
                }
                try
                {
                    RaiseOnBlockingWaitBeginEvent();
                    foreach (VsTaskMock dependentTask in GetAllDependentVsTasks().Distinct())
                        dependentTask.RaiseOnMarkedAsBlockingEvent(this);

                    return function();
                }
                finally
                {
                    RaiseOnBlockingWaitEndEvent();
                    if (dialogStarted)
                        vsThreadedWaitDialog.EndWaitDialog(out var _);
                }
            }
            finally
            {
                VsUIThreadBlockableTaskScheduler.EndTaskWaitOnUIThread();
            }
        }

        private CancellationTokenSource uiThreadWaitAbortToken;
        private CancellationTokenSource InitializeUIThreadWaitAbortToken()
        {
            return uiThreadWaitAbortToken = new CancellationTokenSource();
        }
        private void ClearUIThreadWaitAbortToken()
        {
            uiThreadWaitAbortToken = null;
        }

        internal object InternalGetResult(bool ignoreUIThreadCheck)
        {
            if (InternalTask.IsFaulted)
                RethrowException(InternalTask.Exception);

            if (InternalTask.IsCanceled)
                throw new OperationCanceledException();

            if (!EnsureTaskIsNotBlocking(this, VsTaskDependency.WaitForExecution))
                throw new CircularTaskDependencyException();

            //VsTaskSchedulerService.RaiseTaskWaitEvent(this);
            object taskResult = null;
            try
            {
                bool isUiThreadWithoutCompletion = ThreadHelper.CheckAccess() && !IsCompleted;
                if (!ignoreUIThreadCheck && isUiThreadWithoutCompletion)
                {
                    taskResult = InvokeWithWaitDialog(delegate
                    {
                        try
                        {
                            CancellationTokenSource abortTokenSource = InitializeUIThreadWaitAbortToken();
                            try
                            {
                                IgnoreObjectDisposedException(delegate
                                {
                                    UIThreadReentrancyScope.WaitOnTaskComplete(InternalTask, abortTokenSource.Token, -1);
                                });
                            }
                            catch (OperationCanceledException)
                            {
                                bool waitAbortRequested = false;
                                IgnoreObjectDisposedException(delegate
                                {
                                    waitAbortRequested = abortTokenSource.IsCancellationRequested;
                                });
                                if (waitAbortRequested)
                                    throw new TaskSchedulingException();
                            }
#pragma warning disable VSTHRD002 // Problematische synchrone Wartevorgänge vermeiden
                            return InternalTask.Result;
#pragma warning restore VSTHRD002 // Problematische synchrone Wartevorgänge vermeiden
                        }
                        finally
                        {
                            ClearUIThreadWaitAbortToken();
                        }
                    });
                }
                else
                {
                    if (isUiThreadWithoutCompletion)
                        UIThreadReentrancyScope.WaitOnTaskComplete(InternalTask, CancellationToken.None, -1);

#pragma warning disable VSTHRD002 // Problematische synchrone Wartevorgänge vermeiden
                    taskResult = InternalTask.Result;
#pragma warning restore VSTHRD002 // Problematische synchrone Wartevorgänge vermeiden
                }
            }
            catch (AggregateException e)
            {
                RethrowException(e);
            }
            return taskResult;
        }

        #region IVsTaskEvents members
        public event EventHandler OnBlockingWaitBegin;
        public event EventHandler OnBlockingWaitEnd;
        public event EventHandler<BlockingTaskEventArgs> OnMarkedAsBlocking;

        private void RaiseOnBlockingWaitBeginEvent()
        {
            EventHandler handler = this.OnBlockingWaitBegin;
            if (handler != null)
            {
                try
                {
                    handler(this, EventArgs.Empty);
                }
                catch (Exception) { }
            }
        }
        private void RaiseOnBlockingWaitEndEvent()
        {
            EventHandler handler = this.OnBlockingWaitEnd;
            if (handler != null)
            {
                try
                {
                    handler(this, EventArgs.Empty);
                }
                catch (Exception) { }
            }
        }
        private void RaiseOnMarkedAsBlockingEvent(IVsTask blockedTask)
        {
            EventHandler<BlockingTaskEventArgs> handler = this.OnMarkedAsBlocking;
            if (handler != null)
            {
                try
                {
                    handler(this, new BlockingTaskEventArgs(this, blockedTask));
                }
                catch (Exception) { }
            }
        }
        #endregion

        #region IVsTask members

        public IVsTask ContinueWith(uint context, IVsTaskBody pTaskBody) => ContinueWithEx(context, (uint)VsTaskContinuationOptions.NotOnCanceled, pTaskBody, null);

        public IVsTask ContinueWithEx(uint context, uint options, IVsTaskBody pTaskBody, object pAsyncState)
        {
            if (OptionsHasFlag((VsTaskContinuationOptions)options, VsTaskContinuationOptions.ExecuteSynchronously))
                Trace.WriteLine("ExecuteSynchronously task continuation option has been passed but it is not supported by VS Task Library hence it is ignored.");

            bool isCancelable = !OptionsHasFlag((VsTaskContinuationOptions)options, VsTaskContinuationOptions.NotCancelable);
            bool isCanceledWithParent = OptionsHasFlag((VsTaskContinuationOptions)options, VsTaskContinuationOptions.CancelWithParent);
            bool isIndependentlyCanceled = OptionsHasFlag((VsTaskContinuationOptions)options, VsTaskContinuationOptions.IndependentlyCanceled);
            VsTaskMock vsTask = new VsTaskMock(new IVsTask[1] { this }, (VsTaskRunContext)context, pAsyncState, isCancelable, isCanceledWithParent, isIndependentlyCanceled);
            vsTask.InternalTask = InternalTask.ContinueWith(GetCallbackForSingleParent(pTaskBody, vsTask, vsTask.TaskContext), vsTask.TaskCancellationToken, GetTPLOptions((VsTaskContinuationOptions)options), vsTask.AssignedScheduler);
            //vsTask.TaskState = VsTaskState.Scheduled;
            //VsTaskSchedulerService.Instance.RaiseOnTaskCreatedEvent(vsTask);
            //VsTaskSchedulerService.RaiseDependencyTaskEvent(this, vsTask, VsTaskDependency.Continuation);
            if (OptionsHasFlag((VsTaskContinuationOptions)options, VsTaskContinuationOptions.AttachedToParent))
            {
                EnsureTaskIsNotBlocking(vsTask);
            }
            JoinAntecedentJoinableTasks(vsTask);
            return vsTask;
        }

        public void Start()
        {
            throw new NotImplementedException();
        }

        public void Cancel()
        {
            if (!IsCancelable)
                throw new InvalidOperationException();
            TaskCancellationTokenSource.Cancel();
            //TaskState = VsTaskState.Canceled;
        }

        public object GetResult() => InternalGetResult(ignoreUIThreadCheck: false);

        public void AbortIfCanceled()
        {
            throw new NotImplementedException();
        }

        public void Wait()
        {
            throw new NotImplementedException();
        }

        public bool WaitEx(int millisecondsTimeout, uint options)
        {
            throw new NotImplementedException();
        }

        public bool IsFaulted => InternalTask?.IsFaulted ?? false;

        public bool IsCompleted => (InternalTask?.IsCompleted ?? false) || IsFaulted;

        public bool IsCanceled => InternalTask?.Status == TaskStatus.Canceled || (IsCancelable && TaskCancellationTokenSource.IsCancellationRequested);

        public object AsyncState { get; private set; }

        public string Description { get; set; }
        #endregion

        #region IVsTask2 members
        public string WaitMessage { get; set; }
        #endregion

        #region IVsTaskJoinableTask members
        void IVsTaskJoinableTask.AssociateJoinableTask(object joinableTask)
        {
            Assert.IsNotNull(joinableTask, "joinableTask is null");
            Assert.IsNull(this.joinableTask, "this.joinableTask is not null. can only join once.");
            if (this.joinableTask != null)
                throw new InvalidOperationException();

            this.joinableTask = (JoinableTask)joinableTask;
        }

        CancellationToken IVsTaskJoinableTask.CancellationToken => TaskCancellationToken;
        #endregion
    }

    internal class VsTaskCompletionSourceMock : IVsTaskCompletionSource
    {
        internal TaskCompletionSource<object> InternalCompletionSource { get; private set; }
        internal VsTaskMock InternalTask { get; private set; }
        public object AsyncState { get; private set; }

        public VsTaskCompletionSourceMock(object asyncState, VsTaskCreationOptions options)
        {
            InternalCompletionSource = new TaskCompletionSource<object>(asyncState, VsTaskMock.GetTPLOptions(options));
            InternalTask = new VsTaskMock(InternalCompletionSource);
            AsyncState = asyncState;
        }

        #region IVsTaskCompletionSource members
        void IVsTaskCompletionSource.SetResult(object result)
        {
            InternalCompletionSource.SetResult(result);
        }

        void IVsTaskCompletionSource.SetCanceled()
        {
            InternalCompletionSource.SetCanceled();
            InternalTask.Cancel();
        }

        void IVsTaskCompletionSource.SetFaulted(int hr)
        {
            Exception exceptionForHR = Marshal.GetExceptionForHR(hr);
            InternalCompletionSource.SetException(exceptionForHR);
        }

        void IVsTaskCompletionSource.AddDependentTask(IVsTask pTask)
        {
            InternalTask.TryAddDependentTask(pTask, checkForCycle: false);
            VsTaskMock.JoinAntecedentJoinableTasks(pTask);
        }

        IVsTask IVsTaskCompletionSource.Task => InternalTask;
        #endregion
    }
    internal class VsUIThreadBlockableTaskScheduler : TaskScheduler /*, IVsUIThreadBlockableTaskScheduler, IVsTaskScheduler*/
    {
        private class TaskSchedulerJoinableTaskFactory : JoinableTaskFactory
        {
            private readonly TaskScheduler taskScheduler;

            internal TaskSchedulerJoinableTaskFactory(TaskScheduler taskScheduler)
                : base(ThreadHelper.JoinableTaskContext)
            {
                Assert.IsNotNull(taskScheduler, "taskScheduler");
                this.taskScheduler = taskScheduler;
            }

            protected override void PostToUnderlyingSynchronizationContext(SendOrPostCallback callback, object state)
            {
                Task.Factory.StartNew(callback.Invoke, state, CancellationToken.None, TaskCreationOptions.None, taskScheduler).Forget();
            }
        }

        private readonly ConcurrentQueue<Task> taskQueue = new ConcurrentQueue<Task>();

        private readonly Dictionary<Task, TaskCompletionSource<object>> taskActivationWaiters = new Dictionary<Task, TaskCompletionSource<object>>();

        private readonly object syncObject = new object();

        private readonly Lazy<JoinableTaskFactory> matchingPriorityJoinableTaskFactory;

        public override int MaximumConcurrencyLevel => 1;

        public VsTaskRunContext SchedulerContext { get; private set; }

        public bool IsUIThreadScheduler => true;

        public VsUIThreadBlockableTaskScheduler(VsTaskRunContext schedulerContext)
        {
            matchingPriorityJoinableTaskFactory = new Lazy<JoinableTaskFactory>(() => new TaskSchedulerJoinableTaskFactory(this));
            SchedulerContext = schedulerContext;
        }

        protected override IEnumerable<Task> GetScheduledTasks()
        {
            return taskQueue;
        }

        #region VsRunningTasksManager
        private static HashSet<Task> blockingTasks = new HashSet<Task>();
        private static int waitOnUIThreadCount = 0;
        private static Stack<VsTaskMock> tasksBeingWaited = new Stack<VsTaskMock>();

        internal static VsTaskMock GetCurrentTaskWaitedOnUIThread()
        {
            lock (tasksBeingWaited)
            {
                if (tasksBeingWaited.Count == 0)
                    return null;
                return tasksBeingWaited.Peek();
            }
        }
        private static void PushCurrentTaskWaitedOnUIThread(VsTaskMock task)
        {
            lock (tasksBeingWaited)
            {
                tasksBeingWaited.Push(task);
            }
        }
        private static void PopCurrentTaskWaitedOnUIThread()
        {
            lock (tasksBeingWaited)
            {
                if (tasksBeingWaited.Count != 0)
                    tasksBeingWaited.Pop();
            }
        }
        public static void BeginTaskWaitOnUIThread(VsTaskMock task)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            PushCurrentTaskWaitedOnUIThread(task);
            lock (blockingTasks)
            {
                waitOnUIThreadCount++;
                blockingTasks.UnionWith(task.GetAllDependentInternalTasks());
            }
            foreach (VsUIThreadBlockableTaskScheduler allUIThreadScheduler in VsTaskMock.GetAllUIThreadSchedulers())
            {
                allUIThreadScheduler.EnsureTasksUnblocked();
            }
        }
        public static void EndTaskWaitOnUIThread()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            PopCurrentTaskWaitedOnUIThread();
            lock (blockingTasks)
            {
                waitOnUIThreadCount--;
                if (waitOnUIThreadCount == 0)
                    blockingTasks.Clear();
            }
        }
        public static bool IsBlockingTask(Task task)
        {
            lock (blockingTasks)
            {
                return waitOnUIThreadCount != 0 && blockingTasks.Contains(task);
            }
        }
        public static void PromoteTaskIfBlocking(VsTaskMock blockingTask, VsTaskMock taskToUnblock)
        {
            Assert.IsNotNull(blockingTask, "blockingTask is null");
            Assert.IsNotNull(taskToUnblock, "taskToUnblock is null");
            bool flag = false;
            lock (blockingTasks)
            {
                if (waitOnUIThreadCount == 0)
                    return;

                if (IsBlockingTask(taskToUnblock.InternalTask))
                {
                    flag = true;
                    blockingTasks.UnionWith(blockingTask.GetAllDependentInternalTasks());
                }
            }
            if (!flag)
                return;

            foreach (VsUIThreadBlockableTaskScheduler allUIThreadScheduler in VsTaskMock.GetAllUIThreadSchedulers())
                allUIThreadScheduler.EnsureTasksUnblocked();
        }
        #endregion

        protected override void QueueTask(Task task)
        {
            TaskCompletionSource<object> tcs;
            lock (syncObject)
            {
                if (taskActivationWaiters.TryGetValue(task, out tcs))
                    taskActivationWaiters.Remove(task);

                if (/*VsRunningTasksManager.*/IsBlockingTask(task))
                    PromoteTaskExecution(task);

                taskQueue.Enqueue(task);
                OnTaskQueued(task);
            }
            if (tcs != null)
            {
                Task.Factory.StartNew((object state) => ((TaskCompletionSource<object>)state).TrySetResult(null),
                    tcs, CancellationToken.None, TaskCreationOptions.None, Default).Forget();
            }
        }

        internal async Task<bool> TryExecuteTaskAsync(Task task)
        {
            if (task.IsCompleted)
                return false;

            await Default.SwitchTo(alwaysYield: true);
            if (task.IsCompleted)
                return false;

#pragma warning disable VSTHRD003 // Vermeiden Sie das Warten auf fremde Aufgaben
            await WaitForTaskToBeScheduledAsync(task);
#pragma warning restore VSTHRD003 // Vermeiden Sie das Warten auf fremde Aufgaben
            await matchingPriorityJoinableTaskFactory.Value.SwitchToMainThreadAsync();
            return TryExecuteTask(task);
        }

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            if (taskWasPreviouslyQueued && ThreadHelper.CheckAccess())
            {
                return TryExecuteTask(task);
            }
            return false;
        }

        protected bool DoOneTask(out int dequeuedTaskCount)
        {
            dequeuedTaskCount = 0;
            bool flag = false;
            while (!flag)
            {
                if (!taskQueue.TryDequeue(out var result))
                    return false;
                dequeuedTaskCount++;
                flag = TryExecuteTask(result);
            }
            return true;
        }

        private static DispatcherPriority CalculateDispatcherPriority(VsTaskRunContext runContext)
        {
            switch (runContext)
            {
                case VsTaskRunContext.UIThreadSend:
                    return DispatcherPriority.Send;
                case VsTaskRunContext.UIThreadBackgroundPriority:
                    return DispatcherPriority.Render;
                case VsTaskRunContext.UIThreadIdlePriority:
                    return DispatcherPriority.ApplicationIdle;
                case VsTaskRunContext.UIThreadNormalPriority:
                    return DispatcherPriority.Normal;
                case VsTaskRunContext.CurrentContext:
                case VsTaskRunContext.BackgroundThread:
                case VsTaskRunContext.BackgroundThreadLowIOPriority:
                default:
                    Assert.Fail("wrong run context");
                    throw new InvalidOperationException();
            }
        }

        private void OnTaskQueued(Task task)
        {
#pragma warning disable VSTHRD001 // Legacy-APIs zum Wechseln von Threads vermeiden
            ThreadHelper.Generic.BeginInvoke(
#pragma warning restore VSTHRD001 // Legacy-APIs zum Wechseln von Threads vermeiden
                CalculateDispatcherPriority(SchedulerContext),
                delegate
                {
                    DoOneTask(out _);
                });
        }

        public void EnsureTasksUnblocked()
        {
            lock (syncObject)
            {
                List<Task> queuedTasks = new List<Task>();
                while (!taskQueue.IsEmpty)
                {
                    if (taskQueue.TryDequeue(out var result))
                    {
                        if (/*VsRunningTasksManager.*/IsBlockingTask(result))
                            PromoteTaskExecution(result);

                        queuedTasks.Add(result);
                    }
                }
                queuedTasks.Reverse();
                foreach (Task task in queuedTasks)
                {
                    taskQueue.Enqueue(task);
                }
            }
        }

        private async Task WaitForTaskToBeScheduledAsync(Task task)
        {
            Assert.IsNotNull(task, "task");
            if (task.Status >= TaskStatus.WaitingToRun)
                return;

            TaskCompletionSource<object> tcs;
            lock (syncObject)
            {
                if (task.Status >= TaskStatus.WaitingToRun)
                    return;

                if (!taskActivationWaiters.TryGetValue(task, out tcs))
                {
                    tcs = new TaskCompletionSource<object>();
                    taskActivationWaiters.Add(task, tcs);
                }
            }
            if (task.Status >= TaskStatus.WaitingToRun)
                tcs.TrySetResult(null);

            task.ApplyResultTo(tcs);
            try
            {
                await tcs.Task;
            }
            catch
            {
            }
            finally
            {
                lock (syncObject)
                {
                    taskActivationWaiters.Remove(task);
                }
            }
        }

        protected void PromoteTaskExecution(Task task)
        {
            UIThreadReentrancyScope.EnqueueActionAsync(delegate
            {
                TryExecuteTask(task);
            }).Forget();
        }
    }
}
