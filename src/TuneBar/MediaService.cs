using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace TuneBar
{
    public sealed record MediaSnapshot(
        bool HasSession,
        string Title,
        string Artist,
        ImageSource? Art,
        Color? ArtColor,
        bool IsPlaying,
        bool CanPrevious,
        bool CanNext,
        string? SourceApp)
    {
        public static readonly MediaSnapshot Empty = new(false, "", "", null, null, false, false, false, null);
    }

    /// <summary>
    /// Talks to Windows' built-in media controls (the same thing the volume flyout uses).
    /// Spotify publishes its track info there, so no Spotify login or API key is needed.
    /// </summary>
    public sealed class MediaService
    {
        private readonly object _lock = new();
        private GlobalSystemMediaTransportControlsSessionManager? _manager;
        private GlobalSystemMediaTransportControlsSession? _session;
        private string? _artKey;
        private ImageSource? _art;
        private Color? _artColor;

        /// <summary>When true, falls back to whatever app is playing if Spotify isn't.</summary>
        public bool AnyPlayer { get; set; }

        /// <summary>Raised (on a background thread) whenever something changes.</summary>
        public event Action? Changed;

        public async Task InitAsync()
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.SessionsChanged += (_, _) => PickSession();
            _manager.CurrentSessionChanged += (_, _) => PickSession();
            PickSession();
        }

        public bool HasSession
        {
            get { lock (_lock) return _session != null; }
        }

        private static bool IsSpotify(GlobalSystemMediaTransportControlsSession s)
        {
            try
            {
                return s.SourceAppUserModelId?.IndexOf("spotify", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        public void PickSession()
        {
            var manager = _manager;
            if (manager == null) return;

            GlobalSystemMediaTransportControlsSession? pick = null;
            try
            {
                var all = manager.GetSessions();
                pick = all.FirstOrDefault(IsSpotify);
                if (pick == null && AnyPlayer)
                    pick = manager.GetCurrentSession() ?? all.FirstOrDefault();
            }
            catch
            {
                pick = null;
            }

            lock (_lock)
            {
                if (_session != null)
                {
                    _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                    _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                }

                _session = pick;

                if (_session != null)
                {
                    _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
                    _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
                }
            }

            Changed?.Invoke();
        }

        private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender,
            MediaPropertiesChangedEventArgs args) => Changed?.Invoke();

        private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender,
            PlaybackInfoChangedEventArgs args) => Changed?.Invoke();

        public async Task<MediaSnapshot> GetSnapshotAsync()
        {
            GlobalSystemMediaTransportControlsSession? s;
            lock (_lock) s = _session;
            if (s == null) return MediaSnapshot.Empty;

            try
            {
                var props = await s.TryGetMediaPropertiesAsync();
                var playback = s.GetPlaybackInfo();

                string title = props?.Title ?? "";
                string artist = props?.Artist ?? "";
                if (string.IsNullOrWhiteSpace(artist)) artist = props?.AlbumArtist ?? "";
                string album = props?.AlbumTitle ?? "";

                // Only re-decode the album art when the track changes (or its art shows up late).
                string key = $"{title}|{artist}|{album}|{props?.Thumbnail != null}";
                if (key != _artKey)
                {
                    _artKey = key;
                    _art = await LoadThumbnailAsync(props?.Thumbnail);
                    _artColor = Theme.DominantColor(_art as BitmapSource);
                }

                var controls = playback?.Controls;
                return new MediaSnapshot(
                    HasSession: true,
                    Title: title,
                    Artist: artist,
                    Art: _art,
                    ArtColor: _artColor,
                    IsPlaying: playback?.PlaybackStatus ==
                               GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                    CanPrevious: controls?.IsPreviousEnabled ?? true,
                    CanNext: controls?.IsNextEnabled ?? true,
                    SourceApp: s.SourceAppUserModelId);
            }
            catch
            {
                return MediaSnapshot.Empty;
            }
        }

        private static async Task<ImageSource?> LoadThumbnailAsync(IRandomAccessStreamReference? reference)
        {
            if (reference == null) return null;
            try
            {
                using IRandomAccessStreamWithContentType winStream = await reference.OpenReadAsync();
                using Stream stream = winStream.AsStreamForRead();
                var memory = new MemoryStream();
                await stream.CopyToAsync(memory);
                memory.Position = 0;

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 128;
                bmp.StreamSource = memory;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        private GlobalSystemMediaTransportControlsSession? Current
        {
            get { lock (_lock) return _session; }
        }

        public async Task TogglePlayPauseAsync()
        {
            var s = Current;
            if (s != null) await s.TryTogglePlayPauseAsync();
        }

        public async Task NextAsync()
        {
            var s = Current;
            if (s != null) await s.TrySkipNextAsync();
        }

        public async Task PreviousAsync()
        {
            var s = Current;
            if (s != null) await s.TrySkipPreviousAsync();
        }
    }
}
