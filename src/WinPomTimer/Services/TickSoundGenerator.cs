using System.IO;
using System.Text;

namespace WinPomTimer.Services;

public static class TickSoundGenerator
{
    public static string EnsureDefaultTickWav()
    {
        var soundDir = Path.Combine(Paths.AppDataDir, "sounds");
        Directory.CreateDirectory(soundDir);
        var path = Path.Combine(soundDir, "tick_default.wav");
        GenerateTickWav(path);
        return path;
    }

    private static void GenerateTickWav(string path)
    {
        const int sampleRate = 22050;
        const short bitsPerSample = 16;
        const short channels = 1;
        const double durationSec = 0.10;
        var sampleCount = (int)(sampleRate * durationSec);
        var blockAlign = (short)(channels * (bitsPerSample / 8));
        var byteRate = sampleRate * blockAlign;
        var dataSize = sampleCount * blockAlign;

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var writer = new BinaryWriter(fs, Encoding.ASCII);

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);

        for (var i = 0; i < sampleCount; i++)
        {
            var t = i / (double)sampleRate;
            var pulse1 = Math.Exp(-70.0 * t);
            var pulse2Time = t - 0.028;
            var pulse2 = pulse2Time > 0 ? Math.Exp(-110.0 * pulse2Time) * 0.35 : 0.0;

            var body =
                (Math.Sin(2.0 * Math.PI * 1650.0 * t) * pulse1) +
                (Math.Sin(2.0 * Math.PI * 980.0 * t) * pulse2);

            // Add a tiny click component to feel more like a mechanical tick.
            var click = Math.Sin(2.0 * Math.PI * 4800.0 * t) * Math.Exp(-180.0 * t) * 0.18;
            var sample = body + click;
            var pcm = (short)(sample * short.MaxValue * 0.42);
            writer.Write(pcm);
        }
    }
}
