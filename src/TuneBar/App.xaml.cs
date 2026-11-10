using System;
using System.Threading;
using System.Windows;

namespace TuneBar
{
    public partial class App : Application
    {
        private const string MutexName = "TuneBar.SingleInstance.7c1f4e2a";
        private const string ShowSettingsEventName = "TuneBar.ShowSettings.7c1f4e2a";

        private Mutex? _mutex;
        private bool _ownsMutex;
        private EventWaitHandle? _showSettingsSignal;
        private static SettingsWindow? _settingsWindow;

        protected override void OnStartup(StartupEventArgs e)
        {
            _mutex = new Mutex(true, MutexName, out _ownsMutex);
            _showSettingsSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEventName);

            if (!_ownsMutex)
            {
                // TuneBar is already running: opening it again just opens its Settings.
                _showSettingsSignal.Set();
                Shutdown();
                return;
            }

            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var settings = Settings.Current;
            StartupManager.SetEnabled(settings.StartWithWindows); // also keeps the path up to date

            Theme.Floating = settings.AboveTaskbar;
            Theme.Apply();

            var widget = new MainWindow();
            MainWindow = widget;
            widget.Show();

            // Listen for "open settings" from a second launch (e.g. clicking the Start menu shortcut).
            var signal = _showSettingsSignal;
            var listener = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        signal.WaitOne();
                        Dispatcher.BeginInvoke(new Action(ShowSettings));
                    }
                    catch
                    {
                        return;
                    }
                }
            })
            { IsBackground = true, Name = "TuneBar settings listener" };
            listener.Start();

            if (settings.IsFirstRun)
            {
                settings.Save();
                ShowSettings();
            }
        }

        public static bool IsExiting { get; private set; }

        public static void ExitApp()
        {
            IsExiting = true;
            Current.Shutdown();
        }

        /// <summary>Brings the widget back if its window got destroyed (e.g. Explorer restarted).</summary>
        public static void RecreateWidgetSoon()
        {
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (IsExiting) return;
                var widget = new MainWindow();
                Current.MainWindow = widget;
                widget.Show();
            };
            timer.Start();
        }

        public static void ShowSettings()
        {
            if (_settingsWindow == null)
            {
                _settingsWindow = new SettingsWindow();
                _settingsWindow.Closed += (_, _) => _settingsWindow = null;
                _settingsWindow.Show();
            }

            if (_settingsWindow.WindowState == WindowState.Minimized)
                _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            IsExiting = true;
            if (_ownsMutex) _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            base.OnExit(e);
        }
    }
}
