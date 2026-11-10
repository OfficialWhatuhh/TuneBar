using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace TuneBar
{
    public partial class MainWindow : Window
    {
        private const string GlyphPlay = "";
        private const string GlyphPause = "";

        private Settings S => Settings.Current;
        private readonly MediaService _media = new();
        private readonly VolumeService _volume = new();

        // Fast timer: follows the taskbar as it slides in/out. Slow timer: media, volume, z-order.
        private readonly DispatcherTimer _followTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };
        private readonly DispatcherTimer _slowTimer = new() { Interval = TimeSpan.FromMilliseconds(750) };

        private IntPtr _hwnd;
        private int _tick, _followTick;

        // where we are / where the taskbar is
        private double _anchorLeft;          // left edge we measure OffsetX from (DIPs)
        private double _anchorRight;         // right edge we must stay inside (DIPs)
        private double _lastLeft = double.NaN, _lastTop = double.NaN, _lastHeight = double.NaN, _lastWidth = double.NaN;
        private bool _shown = true;
        private bool _fullscreen;

        // media state
        private bool _refreshing, _refreshPending;
        private bool _isPlaying, _hasSession;
        private ImageSource? _shownArt;
        private Color? _shownArtColor;

        // dragging the widget along the taskbar
        private bool _mouseDown, _moved, _downOnInfo;
        private double _downScreenX, _downLeft;

        private bool _volumeDragging, _menuOpen;

        // free space on the taskbar (physical px, relative to taskbar left), refreshed in the background
        private volatile object? _zone;
        private bool _zoneBusy;

        public MainWindow()
        {
            InitializeComponent();
            _media.AnyPlayer = S.AnyPlayer;

            SourceInitialized += OnSourceInitialized;
            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        // ------------------------------------------------------------------ lifecycle

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.AddExtendedStyle(_hwnd, NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            FollowTaskbar(force: true);

            S.Changed += OnSettingsChanged;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            _media.Changed += () => Dispatcher.BeginInvoke(new Action(() => _ = RefreshMediaAsync()));
            try
            {
                await _media.InitAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Media init failed: {ex}");
            }

            UpdateVolumeUi();
            RefreshZone();
            await RefreshMediaAsync();

            _followTimer.Tick += (_, _) =>
            {
                FollowTaskbar(force: false);
                if (++_followTick % 8 == 0) KeepOnTop(); // ~4 times a second
            };
            _slowTimer.Tick += OnSlowTick;
            _followTimer.Start();
            _slowTimer.Start();
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            _followTimer.Stop();
            _slowTimer.Stop();
            S.Changed -= OnSettingsChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        }

        private void OnSettingsChanged()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_media.AnyPlayer != S.AnyPlayer)
                {
                    _media.AnyPlayer = S.AnyPlayer;
                    _media.PickSession();
                }
                Theme.Floating = S.AboveTaskbar;
                Theme.Apply();
                FollowTaskbar(force: true);
            }));
        }

        private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
            Dispatcher.BeginInvoke(new Action(() => FollowTaskbar(force: true)));

        private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e) =>
            Dispatcher.BeginInvoke(new Action(Theme.Apply));

        private void OnSlowTick(object? sender, EventArgs e)
        {
            _tick++;
            _fullscreen = _hwnd != IntPtr.Zero && NativeMethods.IsForegroundFullscreen(_hwnd);
            if (_tick % 4 == 1) RefreshZone();

            if (!_volumeDragging && _tick % 2 == 0) UpdateVolumeUi();

            // Safety net: Windows media events occasionally stop firing.
            if (_tick % 3 == 0) _ = RefreshMediaAsync();
            if (_tick % 4 == 0 && !_media.HasSession) _media.PickSession();
        }

        // ------------------------------------------------------------------ living in the taskbar

        private double DpiScale => VisualTreeHelper.GetDpi(this).DpiScaleX;

        /// <summary>
        /// Puts the widget exactly on top of the taskbar. If the taskbar auto-hides,
        /// it slides away together with it.
        /// </summary>
        private void FollowTaskbar(bool force)
        {
            if (_mouseDown && !force) return; // user is dragging us

            double scale = DpiScale;
            double width = Math.Clamp(S.WidgetWidth, 260, 700);

            double left, top, height;
            bool shown;

            bool haveTaskbar = NativeMethods.TryGetTaskbar(out var tb, out var mon, out bool trayVisible);
            int tbW = tb.Right - tb.Left, tbH = tb.Bottom - tb.Top;
            bool horizontal = haveTaskbar && tbW > tbH;

            if (horizontal && !S.AboveTaskbar)
            {
                bool atBottom = tb.Top + tbH / 2 > (mon.Top + mon.Bottom) / 2;
                int visiblePx = atBottom ? mon.Bottom - tb.Top : tb.Bottom - mon.Top;
                shown = trayVisible && visiblePx > 6 && !_fullscreen;

                height = tbH / scale;
                _anchorLeft = tb.Left / scale;
                _anchorRight = tb.Right / scale;

                if (shown)
                    top = tb.Top / scale;
                else // tucked away: park completely off-screen so we don't block the auto-hide hot edge
                    top = atBottom ? (mon.Bottom + 20) / scale : (mon.Top - tbH - 20) / scale;
            }
            else
            {
                // Float just above the bottom of the work area (also used for side taskbars).
                Rect work = SystemParameters.WorkArea;
                height = 56;
                _anchorLeft = work.Left;
                _anchorRight = work.Right;
                shown = !_fullscreen;
                top = shown ? work.Bottom - height - 6 : SystemParameters.PrimaryScreenHeight + 20;
            }

            if (S.AutoFit && !S.AboveTaskbar && horizontal && _zone is TaskbarLayout.Zone z)
            {
                // Sit in the gap between the Widgets button and Start, shrinking if needed.
                const double gap = 6;
                double zoneLeft = _anchorLeft + z.Left / scale + gap;
                double zoneWidth = z.Width / scale - gap * 2;
                if (zoneWidth >= 200)
                {
                    width = Math.Min(width, zoneWidth);
                    left = zoneLeft;
                }
                else
                {
                    left = Math.Max(_anchorLeft, Math.Min(_anchorLeft + S.OffsetX, _anchorRight - width));
                }
            }
            else
            {
                left = Math.Max(_anchorLeft, Math.Min(_anchorLeft + S.OffsetX, _anchorRight - width));
            }

            bool changed = force
                           || Math.Abs(left - _lastLeft) > 0.5 || Math.Abs(top - _lastTop) > 0.5
                           || Math.Abs(height - _lastHeight) > 0.5 || Math.Abs(width - _lastWidth) > 0.5;
            if (!changed) return;

            bool becameVisible = shown && !_shown;
            _shown = shown;

            if (Math.Abs(height - _lastHeight) > 0.5 || force)
            {
                double art = Math.Max(24, height - 16);
                ArtBorder.Width = art;
                ArtBorder.Height = art;
                Root.CornerRadius = new CornerRadius(S.AboveTaskbar ? 8 : 6);
            }

            Width = width;
            Height = height;
            // Not much room: drop the volume slider (the speaker button still mutes, and scrolling still works).
            VolumeBar.Visibility = width < 330 ? Visibility.Collapsed : Visibility.Visible;
            Left = left;
            Top = top;
            _lastLeft = left;
            _lastTop = top;
            _lastHeight = height;
            _lastWidth = width;

            // While the taskbar slides in, keep putting ourselves in front of it.
            if (shown && (becameVisible || force || !_menuOpen)) NativeMethods.MakeTopmost(_hwnd);
        }

        /// <summary>Measures the gap between the Widgets and Start buttons (in the background).</summary>
        private void RefreshZone()
        {
            if (_zoneBusy || !S.AutoFit || S.AboveTaskbar || !_shown) return;
            _zoneBusy = true;
            Task.Run(() =>
            {
                var zone = TaskbarLayout.FindFreeZone();
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _zoneBusy = false;
                    object? boxed = zone;
                    if (!Equals(boxed, _zone))
                    {
                        _zone = boxed;
                        FollowTaskbar(force: true);
                    }
                }));
            });
        }

        /// <summary>
        /// The taskbar jumps in front of other windows when it's clicked or slides in,
        /// so we put ourselves back on top.
        /// </summary>
        private void KeepOnTop()
        {
            if (_hwnd == IntPtr.Zero || !_shown) return;
            if (_menuOpen || _volumeDragging || _mouseDown) return;

            // Don't cover the Start menu, search, or tray pop-ups.
            string fg = NativeMethods.ForegroundClass();
            if (fg is "Windows.UI.Core.CoreWindow" or "XamlExplorerHostIslandWindow"
                or "TopLevelWindowForOverflowXamlIsland" or "NotifyIconOverflowWindow")
                return;

            NativeMethods.MakeTopmost(_hwnd);
        }

        // ------------------------------------------------------------------ media

        private async Task RefreshMediaAsync()
        {
            if (_refreshing)
            {
                _refreshPending = true;
                return;
            }

            _refreshing = true;
            try
            {
                do
                {
                    _refreshPending = false;
                    var snapshot = await _media.GetSnapshotAsync();
                    ApplySnapshot(snapshot);
                } while (_refreshPending);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
            finally
            {
                _refreshing = false;
            }
        }

        private void ApplySnapshot(MediaSnapshot s)
        {
            _hasSession = s.HasSession;

            if (!s.HasSession)
            {
                TitleText.Text = S.AnyPlayer ? "Nothing playing" : "Spotify isn't playing";
                ArtistText.Text = "Click to open Spotify";
                InfoPanel.ToolTip = null;
                SetArt(null, null);
                SetPlaying(false);
                PrevButton.IsEnabled = false;
                NextButton.IsEnabled = false;
                _volume.ProcessName = "Spotify";
                return;
            }

            string title = string.IsNullOrWhiteSpace(s.Title) ? "Unknown song" : s.Title;
            string artist = string.IsNullOrWhiteSpace(s.Artist) ? " " : s.Artist;

            TitleText.Text = title;
            ArtistText.Text = artist;
            InfoPanel.ToolTip = string.IsNullOrWhiteSpace(s.Artist) ? title : $"{title}\n{s.Artist}";
            SetArt(s.Art, s.ArtColor);
            SetPlaying(s.IsPlaying);
            PrevButton.IsEnabled = s.CanPrevious;
            NextButton.IsEnabled = s.CanNext;
            _volume.ProcessName = VolumeService.ProcessNameFromAppId(s.SourceApp) ?? "Spotify";
        }

        private void SetArt(ImageSource? image, Color? artColor)
        {
            if (!ReferenceEquals(image, _shownArt))
            {
                _shownArt = image;
                ArtImage.Background = image == null ? null : new ImageBrush(image) { Stretch = Stretch.UniformToFill };
                ArtGlyph.Visibility = image == null ? Visibility.Visible : Visibility.Collapsed;
            }

            if (artColor != _shownArtColor)
            {
                _shownArtColor = artColor;
                Theme.ArtColor = artColor;
                if (S.AccentFromArt) Theme.Apply();
            }
        }

        private void SetPlaying(bool playing)
        {
            _isPlaying = playing;
            PlayButton.Content = playing ? GlyphPause : GlyphPlay;
            PlayButton.ToolTip = playing ? "Pause" : "Play";
        }

        private async Task AfterCommandAsync()
        {
            await Task.Delay(300);
            await RefreshMediaAsync();
        }

        private async void Play_Click(object sender, RoutedEventArgs e)
        {
            if (!_hasSession)
            {
                OpenSpotify();
                return;
            }

            SetPlaying(!_isPlaying); // feel instant
            try { await _media.TogglePlayPauseAsync(); } catch { }
            await AfterCommandAsync();
        }

        private async void Next_Click(object sender, RoutedEventArgs e)
        {
            try { await _media.NextAsync(); } catch { }
            await AfterCommandAsync();
        }

        private async void Prev_Click(object sender, RoutedEventArgs e)
        {
            try { await _media.PreviousAsync(); } catch { }
            await AfterCommandAsync();
        }

        private static void OpenSpotify()
        {
            try
            {
                Process.Start(new ProcessStartInfo("spotify:") { UseShellExecute = true });
            }
            catch
            {
                try
                {
                    Process.Start(new ProcessStartInfo("https://open.spotify.com") { UseShellExecute = true });
                }
                catch
                {
                    // nothing else to try
                }
            }
        }

        // ------------------------------------------------------------------ volume

        private void UpdateVolumeUi()
        {
            var state = _volume.Read();
            ShowVolume(state.Level, state.Muted, state.IsApp);
        }

        private void ShowVolume(float level, bool muted, bool isApp)
        {
            double width = VolumeBar.ActualWidth > 0 ? VolumeBar.ActualWidth : VolumeBar.Width;
            double shown = muted ? 0 : level;

            VolumeFill.Width = Math.Max(0, shown * width);
            VolumeThumb.Margin = new Thickness(shown * (width - VolumeThumb.Width), 0, 0, 0);

            MuteButton.Content = muted || level <= 0.001f ? ""
                : level < 0.34f ? ""
                : level < 0.67f ? ""
                : "";

            int pct = (int)Math.Round(level * 100);
            string who = isApp ? _volume.ProcessName : "System";
            VolumeBar.ToolTip = muted ? $"{who} volume: muted" : $"{who} volume: {pct}%";
            MuteButton.ToolTip = muted ? "Unmute" : "Mute";
        }

        private void SetVolumeFromMouse(MouseEventArgs e)
        {
            double width = VolumeBar.ActualWidth;
            if (width <= 0) return;
            float level = (float)Math.Clamp(e.GetPosition(VolumeBar).X / width, 0, 1);
            _volume.Set(level);
            ShowVolume(level, false, _volume.Read().IsApp);
        }

        private void VolumeBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _volumeDragging = true;
            VolumeBar.CaptureMouse();
            SetVolumeFromMouse(e);
            e.Handled = true;
        }

        private void VolumeBar_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_volumeDragging) return;
            SetVolumeFromMouse(e);
            e.Handled = true;
        }

        private void VolumeBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_volumeDragging) return;
            _volumeDragging = false;
            VolumeBar.ReleaseMouseCapture();
            e.Handled = true;
        }

        private void VolumeBar_LostMouseCapture(object sender, MouseEventArgs e) => _volumeDragging = false;

        private void Mute_Click(object sender, RoutedEventArgs e)
        {
            _volume.ToggleMute();
            UpdateVolumeUi();
        }

        private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            var state = _volume.Read();
            float step = e.Delta > 0 ? 0.05f : -0.05f;
            _volume.Set(state.Level + step);
            UpdateVolumeUi();
            e.Handled = true;
        }

        // ------------------------------------------------------------------ drag to move

        private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _mouseDown = true;
            _moved = false;
            _downOnInfo = ArtBorder.IsMouseOver || InfoPanel.IsMouseOver;
            _downScreenX = PointToScreen(e.GetPosition(this)).X;
            _downLeft = Left;
            Root.CaptureMouse();
            e.Handled = true;
        }

        private void Root_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_mouseDown) return;

            double dx = (PointToScreen(e.GetPosition(this)).X - _downScreenX) / DpiScale;
            if (!_moved && Math.Abs(dx) < 4) return;

            _moved = true;
            Left = Math.Max(_anchorLeft, Math.Min(_downLeft + dx, _anchorRight - Width));
        }

        private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_mouseDown) return;
            bool moved = _moved;
            Root.ReleaseMouseCapture(); // triggers LostMouseCapture, which saves the position
            if (!moved && _downOnInfo) OpenSpotify();
        }

        private void Root_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (!_mouseDown) return;
            _mouseDown = false;
            if (_moved)
            {
                _moved = false;
                S.OffsetX = Math.Max(0, Left - _anchorLeft);
                S.AutoFit = false; // the user picked a spot by hand
                S.Commit();
            }
        }

        // ------------------------------------------------------------------ right-click menu

        private void Menu_Opened(object sender, RoutedEventArgs e) => _menuOpen = true;

        private void Menu_Closed(object sender, RoutedEventArgs e) => _menuOpen = false;

        private void MiSettings_Click(object sender, RoutedEventArgs e) => App.ShowSettings();

        private void MiOpenSpotify_Click(object sender, RoutedEventArgs e) => OpenSpotify();

        private void MiExit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
    }
}
