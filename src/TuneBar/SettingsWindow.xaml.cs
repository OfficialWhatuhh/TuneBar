using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TuneBar
{
    public partial class SettingsWindow : Window
    {
        private static readonly string[] BackgroundColors =
        {
            "#202020", "#000000", "#F3F3F3", "#1E2A3A", "#2A1E3A", "#1E3326", "#3A1E22", "#3A2E1E",
        };

        private static readonly string[] AccentColors =
        {
            "#1DB954", "#FFFFFF", "#0A84FF", "#64D2FF", "#BF5AF2", "#FF375F", "#FF453A", "#FF9F0A", "#FFD60A",
        };

        private static readonly Brush GoodEdge = new SolidColorBrush(Color.FromArgb(0x33, 255, 255, 255));
        private static readonly Brush BadEdge = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A));

        private Settings S => Settings.Current;
        private bool _loading;

        public SettingsWindow()
        {
            InitializeComponent();

            foreach (var hex in BackgroundColors) BgSwatches.Children.Add(MakeSwatch(hex, BgSwatch_Click));
            foreach (var hex in AccentColors) AccentSwatches.Children.Add(MakeSwatch(hex, AccentSwatch_Click));

            var v = Assembly.GetExecutingAssembly().GetName().Version;
            VersionText.Text = v == null ? "" : $"v{v.Major}.{v.Minor}.{v.Build}";

            LoadValues();
            S.Changed += OnSettingsChanged;
            Closed += (_, _) => S.Changed -= OnSettingsChanged;
        }

        private Button MakeSwatch(string hex, RoutedEventHandler onClick)
        {
            var b = new Button
            {
                Style = (Style)FindResource("Swatch"),
                Background = new SolidColorBrush(Theme.ParseOr(hex, Colors.Black)),
                Tag = hex,
                ToolTip = hex,
            };
            b.Click += onClick;
            return b;
        }

        /// <summary>Copies the current settings into the controls.</summary>
        private void LoadValues()
        {
            _loading = true;
            try
            {
                StartupToggle.IsChecked = S.StartWithWindows;
                AnyPlayerToggle.IsChecked = S.AnyPlayer;

                InTaskbarRadio.IsChecked = !S.AboveTaskbar;
                AboveRadio.IsChecked = S.AboveTaskbar;

                AutoFitToggle.IsChecked = S.AutoFit;
                OffsetSlider.IsEnabled = !S.AutoFit || S.AboveTaskbar;
                OffsetSlider.Opacity = OffsetSlider.IsEnabled ? 1 : 0.4;

                OffsetSlider.Maximum = Math.Max(0, SystemParameters.PrimaryScreenWidth - S.WidgetWidth);
                OffsetSlider.Value = Math.Min(S.OffsetX, OffsetSlider.Maximum);
                WidthSlider.Value = S.WidgetWidth;

                BgAuto.IsChecked = S.BackgroundMode == "Auto";
                BgDark.IsChecked = S.BackgroundMode == "Dark";
                BgLight.IsChecked = S.BackgroundMode == "Light";
                BgCustom.IsChecked = S.BackgroundMode == "Custom";
                BgHexBox.Text = S.BackgroundHex;
                BgHexBox.BorderBrush = GoodEdge;

                OpacitySlider.Value = Math.Round(S.BackgroundOpacity * 100);
                AccentHexBox.Text = S.AccentHex;
                AccentHexBox.BorderBrush = GoodEdge;
                ArtAccentToggle.IsChecked = S.AccentFromArt;

                UpdateLabels();
            }
            finally
            {
                _loading = false;
            }
        }

        private void UpdateLabels()
        {
            OffsetLabel.Text = $"Distance from the left edge: {Math.Round(OffsetSlider.Value)} px";
            WidthLabel.Text = S.AutoFit && !S.AboveTaskbar
                ? $"Maximum width: {Math.Round(WidthSlider.Value)} px (shrinks to fit)"
                : $"Width: {Math.Round(WidthSlider.Value)} px";
            OpacityLabel.Text = $"Background opacity: {Math.Round(OpacitySlider.Value)}%";
        }

        // Someone else changed settings (e.g. the widget was dragged): refresh, unless we're mid-edit.
        private void OnSettingsChanged()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_loading || OffsetSlider.IsMouseCaptureWithin || WidthSlider.IsMouseCaptureWithin
                    || OpacitySlider.IsMouseCaptureWithin || BgHexBox.IsKeyboardFocused || AccentHexBox.IsKeyboardFocused)
                    return;
                LoadValues();
            }));
        }

        private void Commit()
        {
            if (_loading) return;
            _loading = true; // ignore the echo of our own change
            try
            {
                S.Commit();
            }
            finally
            {
                _loading = false;
            }
        }

        // ------------------------------------------------------------------ general

        private void StartupToggle_Click(object sender, RoutedEventArgs e)
        {
            S.StartWithWindows = StartupToggle.IsChecked == true;
            StartupManager.SetEnabled(S.StartWithWindows);
            Commit();
        }

        private void AnyPlayerToggle_Click(object sender, RoutedEventArgs e)
        {
            S.AnyPlayer = AnyPlayerToggle.IsChecked == true;
            Commit();
        }

        // ------------------------------------------------------------------ position

        private void Position_Click(object sender, RoutedEventArgs e)
        {
            S.AboveTaskbar = AboveRadio.IsChecked == true;
            Commit();
            LoadValues();
        }

        private void AutoFitToggle_Click(object sender, RoutedEventArgs e)
        {
            S.AutoFit = AutoFitToggle.IsChecked == true;
            Commit();
            LoadValues();
        }

        private void OffsetSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || !IsLoaded) return;
            S.OffsetX = Math.Round(OffsetSlider.Value);
            UpdateLabels();
            Commit();
        }

        private void WidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || !IsLoaded) return;
            S.WidgetWidth = Math.Round(WidthSlider.Value);
            OffsetSlider.Maximum = Math.Max(0, SystemParameters.PrimaryScreenWidth - S.WidgetWidth);
            UpdateLabels();
            Commit();
        }

        // ------------------------------------------------------------------ colors

        private void BgMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton { Tag: string mode })
            {
                S.BackgroundMode = mode;
                Commit();
            }
        }

        private void BgSwatch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string hex }) return;
            S.BackgroundMode = "Custom";
            S.BackgroundHex = hex;
            // Picking a color with the see-through setting would show nothing, so make it visible.
            if (S.BackgroundOpacity < 0.5) S.BackgroundOpacity = 0.9;
            Commit();
            LoadValues();
        }

        private void AccentSwatch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string hex }) return;
            S.AccentHex = hex;
            S.AccentFromArt = false;
            Commit();
            LoadValues();
        }

        private void BgHexBox_Commit(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            if (Theme.TryParse(BgHexBox.Text, out var c))
            {
                BgHexBox.BorderBrush = GoodEdge;
                string hex = Theme.ToHex(c);
                if (hex == S.BackgroundHex && S.BackgroundMode == "Custom") return;
                S.BackgroundHex = hex;
                S.BackgroundMode = "Custom";
                if (S.BackgroundOpacity < 0.5) S.BackgroundOpacity = 0.9;
                Commit();
                LoadValues();
            }
            else
            {
                BgHexBox.BorderBrush = BadEdge;
            }
        }

        private void AccentHexBox_Commit(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            if (Theme.TryParse(AccentHexBox.Text, out var c))
            {
                AccentHexBox.BorderBrush = GoodEdge;
                string hex = Theme.ToHex(c);
                if (hex == S.AccentHex && !S.AccentFromArt) return;
                S.AccentHex = hex;
                S.AccentFromArt = false;
                Commit();
                LoadValues();
            }
            else
            {
                AccentHexBox.BorderBrush = BadEdge;
            }
        }

        private void HexBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            if (sender == BgHexBox) BgHexBox_Commit(sender, e);
            else AccentHexBox_Commit(sender, e);
            e.Handled = true;
        }

        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || !IsLoaded) return;
            S.BackgroundOpacity = Math.Round(OpacitySlider.Value) / 100.0;
            UpdateLabels();
            Commit();
        }

        private void ArtAccentToggle_Click(object sender, RoutedEventArgs e)
        {
            S.AccentFromArt = ArtAccentToggle.IsChecked == true;
            Commit();
        }

        private void ResetColors_Click(object sender, RoutedEventArgs e)
        {
            S.ResetColors();
            Commit();
            LoadValues();
        }

        // ------------------------------------------------------------------ footer

        private void Done_Click(object sender, RoutedEventArgs e) => Close();

        private void Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
    }
}
