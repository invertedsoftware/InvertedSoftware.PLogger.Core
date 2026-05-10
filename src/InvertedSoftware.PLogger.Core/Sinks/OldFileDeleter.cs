// Copyright (c) Inverted Software. All rights reserved.

using System.Globalization;
using InvertedSoftware.PLogger.Core.Internal;
using Microsoft.Extensions.Logging;

namespace InvertedSoftware.PLogger.Core.Sinks;

/// <summary>
/// Periodic cleanup of old rolling-log files. Runs once at construction and
/// every 24 hours afterward. Failures during deletion are logged and skipped.
/// </summary>
internal sealed class OldFileDeleter : IDisposable
{
    private readonly string _directory;
    private readonly string _baseName;
    private readonly string _extension;
    private readonly int _dayLimit;
    private readonly Action<string> _writeAudit;
    private readonly ILogger _selfLogger;
    private readonly Timer _timer;

    public OldFileDeleter(string baseFullPath, int dayLimit, Action<string> writeAudit, ILogger selfLogger)
    {
        _directory = Path.GetDirectoryName(baseFullPath) ?? Environment.CurrentDirectory;
        _baseName = Path.GetFileNameWithoutExtension(baseFullPath);
        _extension = Path.GetExtension(baseFullPath);
        _dayLimit = dayLimit;
        _writeAudit = writeAudit;
        _selfLogger = selfLogger;

        Tick();
        var period = TimeSpan.FromDays(1);
        _timer = new Timer(_ => Tick(), state: null, dueTime: period, period: period);
    }

    private void Tick()
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-_dayLimit);
            _writeAudit(string.Format(CultureInfo.InvariantCulture,
                "PLogger: deleting log files older than {0}.", cutoff.ToString("s", CultureInfo.InvariantCulture)));

            var pattern = $"{_baseName}*{_extension}";
            foreach (var path in Directory.EnumerateFiles(_directory, pattern, SearchOption.TopDirectoryOnly))
            {
                var fi = new FileInfo(path);
                if (fi.LastWriteTimeUtc < cutoff)
                {
                    try
                    {
                        fi.Delete();
                        _writeAudit(string.Format(CultureInfo.InvariantCulture, "PLogger: deleted {0}.", fi.Name));
                    }
                    catch (Exception ex)
                    {
                        PLoggerLog.FileCleanupFailed(_selfLogger, fi.Name, ex);
                    }
                }
            }

            _writeAudit("PLogger: deletion finished.");
        }
        catch
        {
            // ignored — this is best-effort housekeeping
        }
    }

    public void Dispose() => _timer.Dispose();
}
