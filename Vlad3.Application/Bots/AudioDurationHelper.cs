using System.Diagnostics;
using System.Globalization;

namespace Vlad3.Application.Bots;

/// <summary>
/// Получение длительности аудиофайла через ffprobe.
/// </summary>
internal static class AudioDurationHelper
{
    /// <summary>
    /// Байт в секунду для PCM 48kHz stereo s16le (используется для пересчёта позиции).
    /// </summary>
    public const double PcmBytesPerSecond = 48000 * 2 * 2;

    /// <summary>
    /// Получить длительность файла в секундах через ffprobe.
    /// </summary>
    /// <param name="filePath">Путь к файлу.</param>
    /// <param name="ffprobePath">Путь к ffprobe или null для "ffprobe" из PATH.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Длительность в секундах или null при ошибке.</returns>
    public static async Task<double?> GetDurationSecondsAsync(
        string filePath,
        string? ffprobePath,
        CancellationToken cancellationToken = default)
    {
        var path = string.IsNullOrWhiteSpace(ffprobePath) ? "ffprobe" : ffprobePath.Trim();
        var startInfo = new ProcessStartInfo(path)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-show_entries");
        startInfo.ArgumentList.Add("format=duration");
        startInfo.ArgumentList.Add("-of");
        startInfo.ArgumentList.Add("default=noprint_wrappers=1:nokey=1");
        startInfo.ArgumentList.Add(filePath);

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                return null;
            }

            var line = output.Trim();
            if (string.IsNullOrEmpty(line))
            {
                return null;
            }

            if (double.TryParse(line, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                && seconds >= 0)
            {
                return seconds;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}
