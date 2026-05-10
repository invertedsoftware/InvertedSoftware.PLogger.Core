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
using System.Timers;

namespace InvertedSoftware.PLogger
{
    /// <summary>
    /// Class that deletes old log files
    /// delete happens at start up and every 24 hours since
    /// files deleted must be older than a number of days specified in config
    /// </summary>
    internal class OldFileDeleter
    {
        /// <summary>
        /// sample fileme used for sreach pattern
        /// </summary>
        private string fileName;
        /// <summary>
        /// number of days to  decide if file is old
        /// </summary>
        private int dayLimit;
        /// <summary>
        /// timer to run at 24 hours
        /// </summary>
        private Timer timer;
        /// <summary>
        ///  action to write in current log file, what files are deleted 
        /// </summary>
        Action<string> writeItem;
        /// <summary>
        /// Constuctor, lauches a timer
        /// </summary>
        /// <param name="fileName">filename used for pattern</param>
        /// <param name="dayLimit">days to keeps a file</param>
        /// <param name="writeItem"> function to write into log</param>
        public OldFileDeleter(string fileName, int dayLimit, Action<string> writeItem)
        {
            this.fileName = fileName;
            this.dayLimit = dayLimit;
            this.writeItem = writeItem;

            timer_Elapsed(null, null);
            timer = new Timer();
            timer.Interval = TimeSpan.FromDays(1).TotalMilliseconds;
            timer.Elapsed += timer_Elapsed;
            timer.Start();
            timer.AutoReset = true;
        }
        /// <summary>
        /// timer function that executes deletes
        /// </summary>
        /// <param name="sender">sender object, not used </param>
        /// <param name="e">arguments to this call, not used</param>
        private void timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            var now = DateTime.Now;
            writeItem(string.Format("PLogger: deleting log files older than {0}. ", now.AddDays(-dayLimit).ToString("s")));
            foreach (var s in Directory.GetFiles(Path.GetDirectoryName(fileName), Path.GetFileNameWithoutExtension(fileName) + "*" + Path.GetExtension(fileName), SearchOption.TopDirectoryOnly))
            {
                var fi = new FileInfo(s);
                if ((now - fi.LastWriteTime).TotalDays > dayLimit)
                {
                    try
                    {
                        fi.Delete();
                        writeItem(string.Format("PLogger: deleting {0}. ", fi.Name));
                    }
                    catch (Exception ex)
                    {
                        writeItem(string.Format("PLogger: deleting {0} get exception {1}. ", fi.Name, ex));
                    }
                }
            }
            writeItem(string.Format("PLogger: deleting finished."));
        }
    }
}