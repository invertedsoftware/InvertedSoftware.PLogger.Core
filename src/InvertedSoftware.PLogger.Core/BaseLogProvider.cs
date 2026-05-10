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
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace InvertedSoftware.PLogger.Core
{
    /// <summary>
    /// A base log class for all providers
    /// </summary>
    public abstract class BaseLogProvider : IPLogProvider
    {
        /// <summary>
        /// The configuration section.
        /// </summary>
        protected PLoggerSettings pConfig = null;

        /// <summary>
        /// The main collaction holding messages.
        /// </summary>
        protected BlockingCollection<string> logItems = new BlockingCollection<string>();

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="pConfig"></param>
        protected BaseLogProvider(PLoggerSettings pConfig)
        {
            this.pConfig = pConfig;
            StartLogProsessing();
        }

        /// <summary>
        /// On startup start listening to log messages to be processed.
        /// </summary>
        protected void StartLogProsessing()
        {
            Task.Factory.StartNew(() =>
            {
                while (!logItems.IsCompleted)
                {
                    WriteLogItem(logItems.Take());
                }
            });
        }

        /// <summary>
        /// Implement this in each provider
        /// </summary>
        /// <param name="data"></param>
        protected virtual void WriteLogItem(string data)
        {
            throw new NotImplementedException();
        }

        #region ILogProvider
        /// <summary>
        /// Add a log message to logItems
        /// </summary>
        /// <param name="message">The message</param>
        /// <param name="useTemplate">Use a formatted template</param>
        /// <param name="logLevelAndOver">The level of messages to log</param>
        /// <param name="e">An exception</param>
        /// <param name="templateValues">The values to format using the template</param>
        public void WriteLog(string message, LogLevel logLevel, int eventId)
        {
            string level = Enum.GetName(typeof(LogLevel), logLevel);
            logItems.Add($"{level} Level Message. EventID: {eventId} Message: {message}");
        }
        #endregion
    }
}