// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

namespace InvertedSoftware.PLogger.Core.Sinks;

/// <summary>
/// Polls drive free space on a periodic schedule and exposes
/// <see cref="HasFreeSpaceOverLimit"/> for the file sink to gate writes on.
/// Uses <see cref="PeriodicTimer"/> so callbacks can never overlap (unlike
/// <see cref="System.Threading.Timer"/>) and the cadence change happens
/// inline in the loop rather than across a re-arm race.
/// </summary>
internal sealed class DiskSpaceChecker : IDisposable
{
    private static readonly TimeSpan IntervalAboveLimit = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan IntervalBelowLimit = TimeSpan.FromMinutes(3);

    private readonly long _freeSpaceLimitBytes;
    private readonly DriveInfo _drive;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    private long _hasFreeSpaceOverLimit;

    public DiskSpaceChecker(long freeSpaceLimitMB, string driveName)
    {
        _freeSpaceLimitBytes = freeSpaceLimitMB * 1024L * 1024L;
        _drive = new DriveInfo(driveName);
        Volatile.Write(ref _hasFreeSpaceOverLimit, _drive.AvailableFreeSpace > _freeSpaceLimitBytes ? 1 : 0);
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    public bool HasFreeSpaceOverLimit => Volatile.Read(ref _hasFreeSpaceOverLimit) == 1;

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var interval = HasFreeSpaceOverLimit ? IntervalAboveLimit : IntervalBelowLimit;
        var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                long current;
                try
                {
                    current = _drive.AvailableFreeSpace > _freeSpaceLimitBytes ? 1 : 0;
                }
                catch
                {
                    // Best-effort polling — drive read failure shouldn't kill the loop.
                    continue;
                }

                var previous = Interlocked.Exchange(ref _hasFreeSpaceOverLimit, current);
                if (previous != current)
                {
                    var nextInterval = current == 1 ? IntervalAboveLimit : IntervalBelowLimit;
                    timer.Dispose();
                    timer = new PeriodicTimer(nextInterval);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
        finally
        {
            timer.Dispose();
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _loop.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // ignored
        }
        _cts.Dispose();
    }
}
