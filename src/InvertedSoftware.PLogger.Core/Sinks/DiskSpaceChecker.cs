// Copyright (c) Inverted Software. All rights reserved.

namespace InvertedSoftware.PLogger.Core.Sinks;

/// <summary>
/// Polls drive free space on a periodic timer and exposes
/// <see cref="HasFreeSpaceOverLimit"/> for the file sink to gate writes on.
/// </summary>
internal sealed class DiskSpaceChecker : IDisposable
{
    private readonly long _freeSpaceLimitBytes;
    private readonly DriveInfo _drive;
    private readonly Timer _timer;
    private long _hasFreeSpaceOverLimit;

    public DiskSpaceChecker(long freeSpaceLimitMB, string driveName)
    {
        _freeSpaceLimitBytes = freeSpaceLimitMB * 1024L * 1024L;
        _drive = new DriveInfo(driveName);
        Volatile.Write(ref _hasFreeSpaceOverLimit, _drive.AvailableFreeSpace > _freeSpaceLimitBytes ? 1 : 0);

        var interval = HasFreeSpaceOverLimit ? TimeSpan.FromMinutes(15) : TimeSpan.FromMinutes(3);
        _timer = new Timer(_ => Tick(), state: null, dueTime: interval, period: interval);
    }

    public bool HasFreeSpaceOverLimit => Volatile.Read(ref _hasFreeSpaceOverLimit) == 1;

    private void Tick()
    {
        try
        {
            var current = _drive.AvailableFreeSpace > _freeSpaceLimitBytes ? 1 : 0;
            var previous = Interlocked.Exchange(ref _hasFreeSpaceOverLimit, current);
            if (previous != current)
            {
                var interval = current == 1 ? TimeSpan.FromMinutes(15) : TimeSpan.FromMinutes(3);
                _timer.Change(interval, interval);
            }
        }
        catch
        {
            // ignored — best-effort polling
        }
    }

    public void Dispose() => _timer.Dispose();
}
