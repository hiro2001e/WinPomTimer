using System.IO;
using System.Media;
using WinPomTimer.Domain;

namespace WinPomTimer.Services;

public sealed class AudioService
{
    private const long MaxWaveFileBytes = 10L * 1024L * 1024L;

    private PomodoroSettings _settings;

    public AudioService(PomodoroSettings settings)
    {
        _settings = settings;
    }

    public void UpdateSettings(PomodoroSettings settings)
    {
        _settings = settings;
    }

    public void PlaySessionSwitch()
    {
        PlayConfiguredOrDefault(_settings.SessionSwitchSoundPath, SystemSounds.Asterisk, _settings.MasterVolumePercent);
    }

    public void PlayWorkEnd()
    {
        var path = ResolveWorkEndSoundPath();
        PlayConfiguredOrDefault(path, SystemSounds.Asterisk, _settings.MasterVolumePercent);
    }

    public void PlayWorkStart()
    {
        PlayConfiguredOrDefault(_settings.WorkStartSoundPath, SystemSounds.Exclamation, _settings.MasterVolumePercent);
    }

    public void PlayPreBreakAlert()
    {
        PlayConfiguredOrDefault(_settings.PreBreakAlertSoundPath, SystemSounds.Hand, _settings.MasterVolumePercent);
    }

    public void PlayTick(TimerMode mode)
    {
        if (_settings.MuteAll || !_settings.TickSoundEnabled)
        {
            return;
        }

        if (_settings.TickSoundMode == TickSoundMode.WorkOnly && mode != TimerMode.Work)
        {
            return;
        }

        var path = ResolveTickSoundPath();

        var effectiveVolume = Math.Clamp(
            (int)Math.Round((_settings.MasterVolumePercent / 100.0) * _settings.TickVolumePercent),
            0,
            100);

        PlayConfiguredOrDefault(path, SystemSounds.Beep, effectiveVolume);
    }

    private string ResolveTickSoundPath()
    {
        if (TryGetUsableWavePath(_settings.TickSoundPath, out var configured))
        {
            return configured;
        }

        if (TryGetUsableWavePath(PomodoroSettings.PreferredTickSoundPath, out var preferred))
        {
            return preferred;
        }
        
        return string.Empty;
    }

    private string? ResolveWorkEndSoundPath()
    {
        if (TryGetUsableWavePath(_settings.WorkEndSoundPath, out var configured))
        {
            return configured;
        }

        var appLocalSoundPath = Path.Combine(AppContext.BaseDirectory, PomodoroSettings.DefaultWorkEndSoundFileName);
        if (TryGetUsableWavePath(appLocalSoundPath, out var appLocal))
        {
            return appLocal;
        }

        return null;
    }

    private void PlayConfiguredOrDefault(string? path, SystemSound fallback, int volumePercent)
    {
        if (_settings.MuteAll || volumePercent <= 0)
        {
            return;
        }

        try
        {
            if (TryGetUsableWavePath(path, out var safePath))
            {
                PlayWave(safePath, volumePercent);
                return;
            }
        }
        catch
        {
            // fall through to default system sound
        }

        // System sound fallback does not support software volume scaling.
        fallback.Play();
    }

    private static void PlayWave(string path, int volumePercent)
    {
        volumePercent = Math.Clamp(volumePercent, 0, 100);

        if (volumePercent >= 99)
        {
            using var player = new SoundPlayer(path);
            player.Play();
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                var original = File.ReadAllBytes(path);
                if (TryScalePcm16Wav(original, volumePercent / 100.0, out var scaled))
                {
                    using var stream = new MemoryStream(scaled, writable: false);
                    using var player = new SoundPlayer(stream);
                    player.PlaySync();
                    return;
                }

                using var fallbackPlayer = new SoundPlayer(path);
                fallbackPlayer.PlaySync();
            }
            catch
            {
                // swallow playback failures
            }
        });
    }

    private static bool TryScalePcm16Wav(byte[] input, double gain, out byte[] output)
    {
        output = input;
        if (input.Length < 44)
        {
            return false;
        }

        if (ReadAscii(input, 0, 4) != "RIFF" || ReadAscii(input, 8, 4) != "WAVE")
        {
            return false;
        }

        var offset = 12;
        var bitsPerSample = 0;
        var audioFormat = 0;
        var dataOffset = -1;
        var dataSize = 0;

        while (offset + 8 <= input.Length)
        {
            var chunkId = ReadAscii(input, offset, 4);
            var chunkSize = BitConverter.ToInt32(input, offset + 4);
            var chunkDataOffset = offset + 8;
            if (chunkSize < 0 || chunkDataOffset + chunkSize > input.Length)
            {
                return false;
            }

            if (chunkId == "fmt " && chunkSize >= 16)
            {
                audioFormat = BitConverter.ToInt16(input, chunkDataOffset);
                bitsPerSample = BitConverter.ToInt16(input, chunkDataOffset + 14);
            }
            else if (chunkId == "data")
            {
                dataOffset = chunkDataOffset;
                dataSize = chunkSize;
                break;
            }

            offset = chunkDataOffset + chunkSize + (chunkSize % 2);
        }

        if (dataOffset < 0 || dataSize <= 0)
        {
            return false;
        }

        if (audioFormat != 1 || bitsPerSample != 16)
        {
            return false;
        }

        output = (byte[])input.Clone();
        for (var i = dataOffset; i + 1 < dataOffset + dataSize; i += 2)
        {
            var sample = BitConverter.ToInt16(output, i);
            var scaled = (int)Math.Round(sample * gain);
            scaled = Math.Clamp(scaled, short.MinValue, short.MaxValue);
            var bytes = BitConverter.GetBytes((short)scaled);
            output[i] = bytes[0];
            output[i + 1] = bytes[1];
        }

        return true;
    }

    private static string ReadAscii(byte[] bytes, int offset, int length)
    {
        return System.Text.Encoding.ASCII.GetString(bytes, offset, length);
    }

    private static bool TryGetUsableWavePath(string? path, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (!TryNormalizeToLocalPath(path, out var fullPath))
        {
            return false;
        }

        if (!fullPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        FileInfo info;
        try
        {
            info = new FileInfo(fullPath);
        }
        catch
        {
            return false;
        }

        if (!info.Exists)
        {
            return false;
        }

        if (info.Length <= 0 || info.Length > MaxWaveFileBytes)
        {
            return false;
        }

        normalizedPath = info.FullName;
        return true;
    }

    private static bool TryNormalizeToLocalPath(string? path, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path.Trim());
        }
        catch
        {
            return false;
        }

        if (fullPath.StartsWith(@"\\", StringComparison.Ordinal) ||
            fullPath.StartsWith(@"\\.\", StringComparison.OrdinalIgnoreCase) ||
            fullPath.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Block alternate data streams like "file.wav:stream".
        if (fullPath.IndexOf(':', 2) >= 0)
        {
            return false;
        }

        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        try
        {
            var drive = new DriveInfo(root);
            if (drive.DriveType == DriveType.Network)
            {
                return false;
            }
        }
        catch
        {
            return false;
        }

        normalizedPath = fullPath;
        return true;
    }
}

