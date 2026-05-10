// Copyright (c) Inverted Software. All rights reserved.

using System.Globalization;
using System.Text;
using InvertedSoftware.PLogger.Core.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvertedSoftware.PLogger.Core.Sinks;

/// <summary>
/// Async rolling-file sink. Single reader (the drain), so file access is
/// serialized by construction — no extra locking needed.
/// </summary>
public sealed class RollingFileLogSink : IPLogSink
{
    private readonly IOptionsMonitor<PLoggerOptions> _options;
    private readonly ILogger<RollingFileLogSink> _selfLogger;
    private readonly DiskSpaceChecker? _diskChecker;
    private readonly OldFileDeleter? _fileDeleter;

    private readonly string _baseDirectory;
    private readonly string _baseName;
    private readonly string _baseExtension;
    private readonly string _baseFullPath;

    private string _currentFile;
    private FileStream? _stream;
    private StreamWriter? _writer;
    private bool _disabled;

    public RollingFileLogSink(IOptionsMonitor<PLoggerOptions> options, ILogger<RollingFileLogSink> selfLogger)
    {
        _options = options;
        _selfLogger = selfLogger;

        var opts = options.CurrentValue;
        _baseFullPath = Path.IsPathRooted(opts.BaseNameFile)
            ? opts.BaseNameFile
            : Path.Combine(Environment.CurrentDirectory, opts.BaseNameFile);

        _baseDirectory = Path.GetDirectoryName(_baseFullPath) ?? Environment.CurrentDirectory;
        _baseName = Path.GetFileNameWithoutExtension(_baseFullPath);
        _baseExtension = Path.GetExtension(_baseFullPath);
        _currentFile = _baseFullPath;

        try
        {
            EnsureDirectoryExists();
            _currentFile = ResolveLatestRollingFile();
            OpenStream(_currentFile);
        }
        catch (Exception ex)
        {
            PLoggerLog.FileSinkInitFailed(_selfLogger, _baseFullPath, ex);
            _disabled = true;
        }

        if (opts.PLogStopLoggingIfSpaceSmallerThanMB > 0)
        {
            _diskChecker = new DiskSpaceChecker(opts.PLogStopLoggingIfSpaceSmallerThanMB,
                Path.GetPathRoot(_baseFullPath) ?? Environment.CurrentDirectory);
        }

        if (opts.PLogDeleteFilesOlderThanDays > 0)
        {
            _fileDeleter = new OldFileDeleter(_baseFullPath, opts.PLogDeleteFilesOlderThanDays,
                msg => TryAppendInternal(msg), _selfLogger);
        }
    }

    public async ValueTask WriteAsync(LogEvent evt, CancellationToken cancellationToken)
    {
        if (_disabled || _writer is null || _stream is null)
        {
            return;
        }

        if (_diskChecker is { HasFreeSpaceOverLimit: false })
        {
            return;
        }

        var template = _options.CurrentValue.PLogFileMessageTemplate;
        var line = LogEventFormatter.Format(evt, template);

        try
        {
            await _writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);

            var maxBytes = (long)_options.CurrentValue.PLogFileMaxSizeKB * 1024;
            if (maxBytes > 0 && _stream.Position > maxBytes)
            {
                RollFile();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            PLoggerLog.FileSinkInitFailed(_selfLogger, _currentFile, ex);
            _disabled = true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _fileDeleter?.Dispose();
        _diskChecker?.Dispose();
        if (_writer is not null)
        {
            try
            {
                await _writer.FlushAsync().ConfigureAwait(false);
                await _writer.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // swallowed — see drain invariant
            }
            _writer = null;
        }
        if (_stream is not null)
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
            _stream = null;
        }
    }

    private void EnsureDirectoryExists()
    {
        if (!string.IsNullOrEmpty(_baseDirectory) && !Directory.Exists(_baseDirectory))
        {
            Directory.CreateDirectory(_baseDirectory);
        }
    }

    private string ResolveLatestRollingFile()
    {
        if (!File.Exists(_baseFullPath))
        {
            using var fs = File.Create(_baseFullPath);
            using var sw = new StreamWriter(fs);
            sw.WriteLine("Created With PLogger");
            return _baseFullPath;
        }

        var di = new DirectoryInfo(_baseDirectory);
        var pattern = $"{_baseName}*{_baseExtension}";
        FileInfo? latest = null;
        foreach (var fi in di.EnumerateFiles(pattern, SearchOption.TopDirectoryOnly))
        {
            if (latest is null || fi.CreationTimeUtc > latest.CreationTimeUtc)
            {
                latest = fi;
            }
        }
        return latest?.FullName ?? _baseFullPath;
    }

    private void OpenStream(string path)
    {
        _stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read,
            bufferSize: 4096, useAsync: true);
        _writer = new StreamWriter(_stream, Encoding.UTF8) { AutoFlush = false };
    }

    private void RollFile()
    {
        try
        {
            _writer?.Flush();
            _writer?.Dispose();
            _stream?.Dispose();
        }
        catch
        {
            // ignored — recreating below
        }

        var ticks = DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture);
        _currentFile = Path.Combine(_baseDirectory, $"{_baseName}{ticks}{_baseExtension}");
        OpenStream(_currentFile);
    }

    private void TryAppendInternal(string raw)
    {
        if (_disabled || _writer is null)
        {
            return;
        }
        try
        {
            _writer.WriteLine(raw);
            _writer.Flush();
        }
        catch
        {
            // ignored — internal book-keeping write
        }
    }
}
