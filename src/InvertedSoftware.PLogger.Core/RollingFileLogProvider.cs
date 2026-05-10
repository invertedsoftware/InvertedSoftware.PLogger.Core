/* Copyright (c) Year(2020), Inverted Software
 *
 * Permission to use, copy, modify, and/or distribute this software for any purpose with or without fee is hereby granted, 
 * provided that the above copyright notice and this permission notice appear in all copies.
 *
 * THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS.
 * IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE,
 * DATA OR PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
 */

using System;
using System.IO;
using System.Linq;

namespace InvertedSoftware.PLogger.Core
{
    /// <summary>
    /// This is a rolling file implementation of a log provider
    /// </summary>
    public sealed class RollingFileLogProvider : BaseLogProvider
    {
        #region Private Members
        /// <summary>
        /// The current log file name
        /// </summary>
        private string fileName;

        /// <summary>
        /// Handle to the current open file
        /// </summary>
        private StreamWriter sw = null;

        /// <summary>
        /// Object to manage old file erase
        /// </summary>
        private OldFileDeleter fileDeleter = null;

        /// <summary>
        /// object to show if there is space available
        /// </summary>
        private DiskSpaceChecker diskChecker = null;
        #endregion

        /// <summary>
        /// Constructor
        /// </summary>
        public RollingFileLogProvider(PLoggerSettings pConfig)
            : base(pConfig)
        {
            if (!Path.IsPathRooted(pConfig.BaseNameFile))
            {
                fileName = Path.Combine(Environment.CurrentDirectory, pConfig.BaseNameFile);

            }
            else
            {
                fileName = pConfig.BaseNameFile;
            }

            var baseFullFileName = fileName;
            LoadStartFileName();
            LoadFile();

            if (pConfig.PLogStopLoggingIfSpaceSmallerThanMB > 0)
                diskChecker = new DiskSpaceChecker(pConfig.PLogStopLoggingIfSpaceSmallerThanMB, Path.GetPathRoot(fileName));
            if (pConfig.PLogDeleteFilesOlderThanDays > 0)
                fileDeleter = new OldFileDeleter(baseFullFileName, pConfig.PLogDeleteFilesOlderThanDays, this.WriteLogItem);

        }

        /// <summary>
        /// Get a handle to a log file
        /// </summary>
        private void LoadFile()
        {
            if (sw != null)
            {
                sw.Close();
                sw.Dispose();
                sw = null;
            }

            sw = File.AppendText(fileName);
            sw.AutoFlush = true;
        }

        /// <summary>
        /// On startup load the default variables
        /// </summary>
        private void LoadStartFileName()
        {
            // Get file name from the configuration. If there is none use the default file name.
            // If the file doesnt exist, create it. If it exists, get the last rolling file.

            if (!Directory.Exists(Path.GetDirectoryName(fileName)))
                Directory.CreateDirectory(Path.GetDirectoryName(fileName));

            if (!File.Exists(fileName))
            {
                using (System.IO.FileStream fs = System.IO.File.Create(fileName))
                {
                    using (sw = new StreamWriter(fs))
                    {
                        sw.WriteLine("Created With PLogger");
                    }
                }
                return;
            }
            // Get the last created log file
            DirectoryInfo di = new DirectoryInfo(Path.GetDirectoryName(fileName));
            fileName = di.EnumerateFiles(Path.GetFileNameWithoutExtension(fileName) + "*" + Path.GetExtension(fileName)).
                OrderByDescending(f => f.CreationTimeUtc)
                .Take(1)
                .First().FullName;
        }

        /// <summary>
        /// Process a single log entry
        /// </summary>
        /// <param name="data">The string to write to a file</param>
        protected override void WriteLogItem(string data)
        {
            // provided we have enough space
            if (diskChecker == null || diskChecker.HasFreeSpaceOverLimit)
            {
                // Check if there is a maximum file size and see if the position of the StreamWriter passed it.
                // If it did, start a new file.
                // Append the text to the end of the file

                sw.WriteLine(data);
                if (sw.BaseStream.Position > pConfig.PLogFileMaxSizeKB * 1024)
                    MoveRollingFile();
            }
        }

        /// <summary>
        /// When a file is passed its max size create the next file
        /// </summary>
        private void MoveRollingFile()
        {
            string nextRollingFile = Path.Combine(Path.GetDirectoryName(fileName),
                String.Format("{0}{1}{2}",
                Path.GetFileNameWithoutExtension(pConfig.BaseNameFile),
                DateTime.UtcNow.Ticks.ToString(),
                Path.GetExtension(pConfig.BaseNameFile)));
            fileName = nextRollingFile;
            LoadFile();
        }
    }
}