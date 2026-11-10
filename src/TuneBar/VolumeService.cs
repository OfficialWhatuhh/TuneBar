using System;
using System.Collections.Generic;
using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace TuneBar
{
    public readonly record struct VolumeState(float Level, bool Muted, bool IsApp);

    /// <summary>
    /// Controls the volume of the player app (e.g. Spotify) in the Windows volume mixer.
    /// Falls back to the system master volume if the app has no audio session yet.
    /// </summary>
    public sealed class VolumeService
    {
        private readonly MMDeviceEnumerator _enumerator = new();
        private MMDevice? _device;
        private DateTime _deviceFetchedAt;

        /// <summary>Process name without ".exe", e.g. "Spotify".</summary>
        public string ProcessName { get; set; } = "Spotify";

        private MMDevice? Device()
        {
            // Re-check the default output every few seconds so switching headphones/speakers works.
            if (_device == null || (DateTime.UtcNow - _deviceFetchedAt).TotalSeconds > 5)
            {
                try
                {
                    _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    _deviceFetchedAt = DateTime.UtcNow;
                }
                catch
                {
                    _device = null;
                }
            }
            return _device;
        }

        private List<SimpleAudioVolume> AppVolumes()
        {
            var result = new List<SimpleAudioVolume>();
            var device = Device();
            if (device == null) return result;

            var pids = new HashSet<uint>();
            foreach (var p in Process.GetProcessesByName(ProcessName))
            {
                pids.Add((uint)p.Id);
                p.Dispose();
            }
            if (pids.Count == 0) return result;

            try
            {
                var manager = device.AudioSessionManager;
                manager.RefreshSessions();
                var sessions = manager.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    var session = sessions[i];
                    if (pids.Contains(session.GetProcessID))
                        result.Add(session.SimpleAudioVolume);
                }
            }
            catch
            {
                _device = null;
            }
            return result;
        }

        public VolumeState Read()
        {
            try
            {
                var apps = AppVolumes();
                if (apps.Count > 0)
                    return new VolumeState(apps[0].Volume, apps[0].Mute, true);

                var device = Device();
                if (device != null)
                    return new VolumeState(device.AudioEndpointVolume.MasterVolumeLevelScalar,
                        device.AudioEndpointVolume.Mute, false);
            }
            catch
            {
                _device = null;
            }
            return new VolumeState(0, false, false);
        }

        public void Set(float level)
        {
            level = Math.Clamp(level, 0f, 1f);
            try
            {
                var apps = AppVolumes();
                if (apps.Count > 0)
                {
                    foreach (var a in apps)
                    {
                        a.Volume = level;
                        a.Mute = false;
                    }
                    return;
                }

                var device = Device();
                if (device != null)
                {
                    device.AudioEndpointVolume.MasterVolumeLevelScalar = level;
                    device.AudioEndpointVolume.Mute = false;
                }
            }
            catch
            {
                _device = null;
            }
        }

        public void ToggleMute()
        {
            try
            {
                var apps = AppVolumes();
                if (apps.Count > 0)
                {
                    bool mute = !apps[0].Mute;
                    foreach (var a in apps) a.Mute = mute;
                    return;
                }

                var device = Device();
                if (device != null)
                    device.AudioEndpointVolume.Mute = !device.AudioEndpointVolume.Mute;
            }
            catch
            {
                _device = null;
            }
        }

        /// <summary>
        /// Turns a media app id into a process name.
        /// "Spotify.exe" -> "Spotify", "SpotifyAB.SpotifyMusic_xxx!Spotify" -> "Spotify".
        /// </summary>
        public static string? ProcessNameFromAppId(string? appId)
        {
            if (string.IsNullOrWhiteSpace(appId)) return null;
            string s = appId;
            int bang = s.LastIndexOf('!');
            if (bang >= 0) s = s.Substring(bang + 1);
            if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 4);
            return s.Length > 0 ? s : null;
        }
    }
}
