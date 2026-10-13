using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DexManager.Models;

namespace DexManager.Services
{
    public sealed class DexOrchestrator
    {
        private readonly AdbService _adbService;
        private readonly VirtualDisplayService _virtualDisplayService;
        private readonly ScrcpyService _scrcpyService;
        private readonly ScrcpyLaunchCoordinator _launchCoordinator;
        private readonly SettingsService _settingsService;
        private readonly LogService _logService;
        private readonly AppSettings _settings;
        private readonly DeviceRuntimeSessionRegistry _runtimeSessions;
        private readonly object _operationGate = new object();
        private readonly object _shutdownTaskLock = new object();
        private readonly ManualResetEvent _shutdownSignal =
            new ManualResetEvent(false);
        private int _shutdownRequested;
        private Task _shutdownTask;
        private ManagedDisplaySession _currentSession;
        private readonly Dictionary<string, VirtualDisplayLease>
            _pendingDisplayCleanup =
                new Dictionary<string, VirtualDisplayLease>(
                    StringComparer.OrdinalIgnoreCase);
        private int _naturalExitCleanupScheduled;

        public DexOrchestrator(
            AdbService adbService,
            VirtualDisplayService virtualDisplayService,
            ScrcpyService scrcpyService,
            ScrcpyLaunchCoordinator launchCoordinator,
            SettingsService settingsService,
            LogService logService,
            AppSettings settings,
            DeviceRuntimeSessionRegistry runtimeSessions)
        {
            _adbService = adbService;
            _virtualDisplayService = virtualDisplayService;
            _scrcpyService = scrcpyService;
            _launchCoordinator = launchCoordinator;
            _settingsService = settingsService;
            _logService = logService;
            _settings = settings;
            _runtimeSessions = runtimeSessions ??
                throw new ArgumentNullException("runtimeSessions");
            _scrcpyService.RunningChanged +=
                ScrcpyService_RunningChanged;
        }

        public bool IsRunning
        {
            get { return _scrcpyService.IsRunning; }
        }

        public ManagedDisplaySession CurrentSession
        {
            get { return _currentSession; }
        }

        public string DeviceIdentity { get; set; }

        public bool IsShutdownRequested
        {
            get
            {
                return Interlocked.CompareExchange(
                    ref _shutdownRequested,
                    0,
                    0) != 0;
            }
        }

        public Task StartAsync(string serial)
        {
            return Task.Run(delegate
            {
                lock (_operationGate) StartCore(serial);
            });
        }

        public Task StopAsync()
        {
            return Task.Run(delegate
            {
                lock (_operationGate) StopCore();
            });
        }

        public Task<bool> ResetDesktopAsync(string serial)
        {
            return Task.Run(delegate
            {
                lock (_operationGate)
                {
                    StopCore();
                    return RetryDeferredCleanupCore(serial);
                }
            });
        }

        public Task<bool> RetryDeferredCleanupAsync(string serial)
        {
            return Task.Run(delegate
            {
                lock (_operationGate)
                    return RetryDeferredCleanupCore(serial);
            });
        }

        public void RequestShutdown()
        {
            _scrcpyService.RequestShutdown();
            if (Interlocked.Exchange(ref _shutdownRequested, 1) == 0)
                _shutdownSignal.Set();
        }

        public Task ShutdownAsync(string fallbackSerial)
        {
            RequestShutdown();
            lock (_shutdownTaskLock)
            {
                if (_shutdownTask == null ||
                    _shutdownTask.IsFaulted ||
                    _shutdownTask.IsCanceled)
                {
                    _shutdownTask = Task.Run(delegate
                    {
                        lock (_operationGate)
                            ShutdownCore(fallbackSerial);
                    });
                }
                return _shutdownTask;
            }
        }

        public Task<bool> ApplyRuntimeSettingsAsync()
        {
            return Task.Run(delegate
            {
                lock (_operationGate)
                    return ApplyRuntimeSettingsCore();
            });
        }

        private void StartCore(string requestedSerial)
        {
            if (IsShutdownRequested) return;
            if (_scrcpyService.IsRunning)
            {
                _logService.Warning(LocalizationService.Get(
                    "Log.Dex.AlreadyRunning"));
                return;
            }

            var serial = requestedSerial;
            if (string.IsNullOrWhiteSpace(serial) ||
                !_adbService.IsAuthorizedDeviceConnected(serial))
            {
                throw new InvalidOperationException(
                    LocalizationService.Get(
                        "Error.Dex.NoAuthorizedDevice"));
            }
            CleanupStaleSession(serial);
            // Acquire restores any journal for the verified physical phone before
            // saving its original value. A separate Recover repeats that identity query.
            var runSettings = GetDeviceRunSettings();

            VirtualDisplayLease lease = null;
            var scrcpyStarted = false;
            try
            {
                _launchCoordinator.RunExclusive(delegate
                {
                    ThrowIfShutdownRequested();
                    lease = _virtualDisplayService.PrepareDesktop(serial);
                    _pendingDisplayCleanup.Remove(serial);
                    ThrowIfShutdownRequested();
                    _scrcpyService.StartDesktop(
                        runSettings.Scrcpy,
                        runSettings.VirtualDisplay,
                        serial,
                        id => _virtualDisplayService.ConfigureDesktop(lease, id));
                    scrcpyStarted = true;
                });

                if (IsShutdownRequested)
                    throw new OperationCanceledException();

                if (!_scrcpyService.IsRunning)
                    throw new InvalidOperationException(
                        LocalizationService.Get(
                            "Error.Scrcpy.ExitedBeforeWindow"));

                TrackSession("DeX", serial, lease);
                if (!_scrcpyService.IsRunning)
                    throw new InvalidOperationException(
                        LocalizationService.Get(
                            "Error.Scrcpy.ExitedBeforeWindow"));
                try
                {
                    SaveLastSuccess(serial, lease.DisplayId);
                }
                catch (Exception saveException)
                {
                    _logService.Error(
                        LocalizationService.Get(
                            "Log.Dex.LastSuccessSaveFailed"),
                        saveException);
                }
                _logService.Info(LocalizationService.Get(
                    "Log.Dex.StartCompleted"));
            }
            catch (OperationCanceledException ex)
            {
                lease = GetRetainedLease(ex, lease);
                CleanupFailedStart(scrcpyStarted, lease);
                _logService.Info(LocalizationService.Get(
                    "Log.Dex.StartCancelled"));
            }
            catch (Exception ex)
            {
                lease = GetRetainedLease(ex, lease);
                CleanupFailedStart(scrcpyStarted, lease);
                _logService.Error(
                    LocalizationService.Get("Log.Dex.StartFailed"),
                    ex);
                throw;
            }
        }

        private void StopCore()
        {
            var session = _currentSession;
            Exception stopException = null;
            try
            {
                _scrcpyService.Stop();
            }
            catch (Exception ex)
            {
                stopException = ex;
                _logService.Error(
                    LocalizationService.Get(
                        "Log.Dex.StopProcessFailed"),
                    ex);
            }

            if (_scrcpyService.IsRunning)
            {
                if (stopException != null) throw stopException;
                throw new InvalidOperationException(
                    LocalizationService.Get(
                        "Error.Scrcpy.StopTimeout"));
            }

            if (session != null)
            {
                if (!ReleaseDisplayLease(session.DisplayLease))
                    DeferDisplayCleanup(session);
            }
            ClearSession(session);
            _logService.Info(LocalizationService.Get(
                "Log.Dex.StopCleanupCompleted"));
            if (stopException != null) throw stopException;
        }

        private bool ApplyRuntimeSettingsCore()
        {
            if (IsShutdownRequested) return false;
            try
            {
                var serial = _currentSession == null
                    ? string.Empty
                    : _currentSession.Serial;
                if (string.IsNullOrWhiteSpace(serial) ||
                    !_adbService.IsAuthorizedDeviceConnected(serial))
                {
                    _logService.Warning(LocalizationService.Get(
                        "Log.Dex.ApplyDeferredNoDevice"));
                    return false;
                }

                _logService.Info(LocalizationService.Get(
                    "Log.Dex.RemovingDisplayForApply"));
                var session = _currentSession;
                _scrcpyService.Stop();
                if (session != null &&
                    !ReleaseDisplayLease(session.DisplayLease))
                {
                    DeferDisplayCleanup(session);
                    throw new InvalidOperationException(
                        LocalizationService.Get(
                            "Error.Dex.DisplayResetFailed"));
                }
                ClearSession(session);

                if (_shutdownSignal.WaitOne(1000)) return false;
                StartCore(serial);
                if (!_scrcpyService.IsRunning) return false;
                _logService.Info(LocalizationService.Get(
                    "Log.Dex.ApplyCompleted"));
                return true;
            }
            catch (Exception ex)
            {
                _logService.Error(
                    LocalizationService.Get("Log.Dex.ApplyFailed"),
                    ex);
                throw;
            }
        }

        private void ShutdownCore(string fallbackSerial)
        {
            var session = _currentSession;
            Exception stopException = null;
            try
            {
                _scrcpyService.Stop();
            }
            catch (Exception ex)
            {
                stopException = ex;
                _logService.Error(
                    LocalizationService.Get(
                        "Log.Dex.StopProcessFailed"),
                    ex);
            }

            if (_scrcpyService.IsRunning)
            {
                if (stopException != null) throw stopException;
                throw new InvalidOperationException(
                    LocalizationService.Get(
                        "Error.Scrcpy.StopTimeout"));
            }

            if (session != null &&
                !ReleaseDisplayLease(session.DisplayLease))
            {
                DeferDisplayCleanup(session);
            }
            else if (session == null)
            {
                CleanupConnectedTargetOverlay(fallbackSerial);
            }
            ClearSession(session);
            _logService.Info(LocalizationService.Get(
                "Log.Dex.ShutdownCleanupCompleted"));
        }

        private void ScrcpyService_RunningChanged(
            object sender,
            EventArgs e)
        {
            if (_scrcpyService.IsRunning) return;
            if (Interlocked.Exchange(
                ref _naturalExitCleanupScheduled,
                1) != 0) return;

            Task.Run(delegate
            {
                try
                {
                    lock (_operationGate)
                    {
                        if (!_scrcpyService.IsRunning)
                            CleanupNaturallyEndedSession();
                    }
                }
                finally
                {
                    Interlocked.Exchange(
                        ref _naturalExitCleanupScheduled,
                        0);
                }
            });
        }

        private void CleanupNaturallyEndedSession()
        {
            var session = _currentSession;
            if (session == null) return;
            if (!ReleaseDisplayLease(session.DisplayLease))
            {
                DeferDisplayCleanup(session);
                _logService.Warning(LocalizationService.Get(
                    "Log.Dex.NaturalExitCleanupDeferred"));
                return;
            }

            ClearSession(session);
            _logService.Info(LocalizationService.Get(
                "Log.Dex.NaturalExitCleanupCompleted"));
        }

        private void CleanupStaleSession(string nextSerial)
        {
            var stale = _currentSession;
            if (stale == null) return;
            if (!ReleaseDisplayLease(stale.DisplayLease)) DeferDisplayCleanup(stale);
            ClearSession(stale);
        }

        private void CleanupFailedStart(
            bool scrcpyStarted,
            VirtualDisplayLease lease)
        {
            if (scrcpyStarted || _scrcpyService.IsRunning)
            {
                try
                {
                    _scrcpyService.Stop();
                }
                catch (Exception cleanupException)
                {
                    _logService.Error(
                        LocalizationService.Get(
                            "Log.Dex.StopProcessFailed"),
                        cleanupException);
                }
            }

            if (_scrcpyService.IsRunning)
            {
                if (_currentSession == null && lease != null)
                    TrackSession("DeX", lease.Serial, lease);
                return;
            }

            try
            {
                if (lease != null &&
                    !ReleaseDisplayLease(lease))
                {
                    DeferDisplayCleanup(lease);
                    ClearSession(_currentSession);
                    return;
                }
                ClearSession(_currentSession);
            }
            catch (Exception cleanupException)
            {
                _logService.Error(
                    LocalizationService.Get(
                        "Log.Dex.ShutdownCleanupFailed"),
                    cleanupException);
            }
        }

        private bool RetryDeferredCleanupCore(string serial)
        {
            if (string.IsNullOrWhiteSpace(serial)) return true;
            if (_scrcpyService.IsRunning) return true;
            CleanupNaturallyEndedSession();
            // Recover by physical identity even after an app restart or a USB/Wi-Fi switch.
            if (!_virtualDisplayService.RecoverDesktop(serial)) return false;
            _pendingDisplayCleanup.Remove(serial);
            return true;
        }

        private bool ReleaseDisplayLease(VirtualDisplayLease lease)
        {
            if (lease == null) return true;
            if (lease.IsScrcpyOwned) return _virtualDisplayService.Release(lease);
            if (string.IsNullOrWhiteSpace(lease.Serial) ||
                !_adbService.IsAuthorizedDeviceConnected(lease.Serial))
            {
                return false;
            }
            return _virtualDisplayService.Release(lease);
        }

        private void CleanupConnectedTargetOverlay(string serial)
        {
            if (string.IsNullOrWhiteSpace(serial) ||
                !_adbService.IsAuthorizedDeviceConnected(serial))
            {
                return;
            }

            _virtualDisplayService.RecoverDesktop(serial);
        }

        private void DeferDisplayCleanup(ManagedDisplaySession session)
        {
            if (session == null) return;
            DeferDisplayCleanup(session.DisplayLease);
            ClearSession(session);
        }

        private void DeferDisplayCleanup(VirtualDisplayLease lease)
        {
            if (lease == null || string.IsNullOrWhiteSpace(lease.Serial))
                return;
            _pendingDisplayCleanup[lease.Serial] = lease;
            _logService.Warning(LocalizationService.Format(
                "Log.Dex.DeferredCleanupStored",
                lease.Serial));
        }

        private static VirtualDisplayLease GetRetainedLease(
            Exception error,
            VirtualDisplayLease current)
        {
            if (current != null || error == null) return current;
            return error.Data[VirtualDisplayService.RetainedLeaseDataKey]
                as VirtualDisplayLease;
        }

        private void ThrowIfShutdownRequested()
        {
            if (IsShutdownRequested)
                throw new OperationCanceledException();
        }

        private void SaveLastSuccess(string serial, int displayId)
        {
            _settingsService.UpdateAndSave(_settings, delegate(
                AppSettings settings)
            {
                var runSettings = GetDeviceRunSettings(settings);
                runSettings.LastSuccess.Width =
                    runSettings.VirtualDisplay.Width;
                runSettings.LastSuccess.Height =
                    runSettings.VirtualDisplay.Height;
                runSettings.LastSuccess.Dpi =
                    runSettings.VirtualDisplay.Dpi;
                runSettings.LastSuccess.AdbPath = _adbService.AdbPath;
                runSettings.LastSuccess.ScrcpyPath =
                    _scrcpyService.ScrcpyPath;
                runSettings.LastSuccess.ScrcpyArguments =
                    _scrcpyService.BuildDesktopArguments(
                        runSettings.Scrcpy,
                        runSettings.VirtualDisplay,
                        serial);
                runSettings.LastSuccess.DisplayId = displayId;
                runSettings.LastSuccess.SavedAtUtc =
                    DateTime.UtcNow.ToString("o");
            });
        }

        private void TrackSession(
            string mode,
            string serial,
            VirtualDisplayLease lease)
        {
            _currentSession = new ManagedDisplaySession
            {
                Mode = mode,
                Serial = serial,
                AppPackage = GetDeviceRunSettings()
                    .Scrcpy.StartAppPackage,
                DisplayId = lease.DisplayId,
                ScrcpyProcessId = _scrcpyService.CurrentProcessId,
                CreatedAtUtc = DateTime.UtcNow.ToString("o"),
                DisplayLease = lease
            };
            _runtimeSessions.SetDexSession(serial, _currentSession);
            _logService.Info(LocalizationService.Format(
                "Log.Dex.SessionStarted",
                _currentSession));
        }

        private DeviceRunSettingsProfile GetDeviceRunSettings()
        {
            return GetDeviceRunSettings(_settings);
        }

        private DeviceRunSettingsProfile GetDeviceRunSettings(
            AppSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(DeviceIdentity))
                return settings.GetOrCreateDeviceRunSettings(
                    DeviceIdentity);
            return new DeviceRunSettingsProfile
            {
                DeviceIdentity = string.Empty,
                VirtualDisplay = settings.VirtualDisplay,
                Scrcpy = settings.Scrcpy,
                LastSuccess = settings.LastSuccess,
                SingleWindowSlots = settings.SingleWindowSlots,
                SingleWindowAppProfiles =
                    settings.SingleWindowAppProfiles
            };
        }

        private void ClearSession(ManagedDisplaySession session)
        {
            if (session == null ||
                !ReferenceEquals(_currentSession, session)) return;
            _logService.Info(LocalizationService.Format(
                "Log.Dex.SessionEnded",
                session));
            _runtimeSessions.SetDexSession(session.Serial, null);
            _currentSession = null;
        }
    }
}
