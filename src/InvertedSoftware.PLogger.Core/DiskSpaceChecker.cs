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

namespace InvertedSoftware.PLogger.Core
{
    /// <summary>
    /// check reguraly for available space and show if it is over or under free space
    /// </summary>
    internal class DiskSpaceChecker
    {
        /// <summary>
        /// timer to run at 24 hours
        /// </summary>
        private Timer timer;
        /// <summary>
        /// limit under which we no longer write to disk
        /// </summary>
        private long freeSpaceLimit;

        /// <summary>
        /// drive to check for space
        /// </summary>
        private DriveInfo drive;

        /// <summary>
        /// constructor
        /// </summary>
        /// <param name="freeSpaceLimitMB">free space over which we write to disk</param>
        /// <param name="driveName">drive to check for space</param>
        public DiskSpaceChecker(long freeSpaceLimitMB, string driveName)
        {
            this.freeSpaceLimit = freeSpaceLimitMB * 1024 * 1024;
            this.drive = new DriveInfo(driveName);

            timer = new Timer();
            timer.Elapsed += timer_Elapsed;
            timer.AutoReset = true;

            HasFreeSpaceOverLimit = (drive.AvailableFreeSpace > freeSpaceLimit);
            timer.Interval = HasFreeSpaceOverLimit ? TimeSpan.FromMinutes(15).TotalMilliseconds : TimeSpan.FromMinutes(3).TotalMilliseconds;
            timer.Start();
        }


        /// <summary>
        /// shows if we have space over configured limit
        /// </summary>
        public bool HasFreeSpaceOverLimit { get; set; }


        /// <summary>
        /// timer function that checks for free space
        /// timer run every 15 minutes while free space is over the limit or every 3 minutes when free space is under limit
        /// </summary>
        /// <param name="sender">sender object, not used </param>
        /// <param name="e">arguments to this call, not used</param>
        private void timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            var oldstate = HasFreeSpaceOverLimit;
            HasFreeSpaceOverLimit = (drive.AvailableFreeSpace > freeSpaceLimit);
            if (oldstate != HasFreeSpaceOverLimit)
            {
                timer.Stop();
                timer.Interval = HasFreeSpaceOverLimit ? TimeSpan.FromMinutes(15).TotalMilliseconds : TimeSpan.FromMinutes(3).TotalMilliseconds;
                timer.Start();
            }
        }
    }
}