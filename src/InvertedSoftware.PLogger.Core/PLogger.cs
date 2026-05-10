/* Copyright (c) Year(2020), Inverted Software
 *
 * Permission to use, copy, modify, and/or distribute this software for any purpose with or without fee is hereby granted, 
 * provided that the above copyright notice and this permission notice appear in all copies.
 *
 * THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS.
 * IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE,
 * DATA OR PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
 */

using Microsoft.Extensions.Logging;
using System;

namespace InvertedSoftware.PLogger.Core
{
    public class PLogger : ILogger
    {
        private readonly string _name;
        private readonly PLoggerSettings _settings;
        private readonly IExternalScopeProvider _externalScopeProvider;
        public static Lazy<IPLogProvider> logProvider = null;

        public PLogger(string name)
            : this(name, settings: new PLoggerSettings())
        {
        }

        public PLogger(string name, PLoggerSettings settings)
            : this(name, settings, new LoggerExternalScopeProvider())
        {

        }

        public PLogger(string name, PLoggerSettings settings, IExternalScopeProvider externalScopeProvider)
        {
            _name = string.IsNullOrEmpty(name) ? nameof(PLogger) : name;
            _settings = settings;
            _externalScopeProvider = externalScopeProvider;
            // Initialize the singleton type
            if (settings.PLogType.ToLower() == "file")
                logProvider = new Lazy<IPLogProvider>(() => new RollingFileLogProvider(_settings), true);
            else
                logProvider = new Lazy<IPLogProvider>(() => new SqlLogProvider(_settings), true);
        }
        public IDisposable BeginScope<TState>(TState state)
        {
            return _externalScopeProvider?.Push(state);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return _settings.PLogEnabled && logLevel != LogLevel.None &&
                (_settings.Filter == null || _settings.Filter(_name, logLevel));
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            if (formatter == null)
            {
                throw new ArgumentNullException(nameof(formatter));
            }

            var message = formatter(state, exception);

            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            if (!String.IsNullOrWhiteSpace(_settings.PLogFileMessageTemplate))
                message = string.Format(_settings.PLogFileMessageTemplate, message);

            message = _name + Environment.NewLine + message;

            if (exception != null)
            {
                message += Environment.NewLine + Environment.NewLine + exception;
            }

            _externalScopeProvider?.ForEachScope<object>((scope, _) => message += Environment.NewLine + scope, null);

            logProvider.Value.WriteLog(message, logLevel, eventId.Id);
        }
    }
}
