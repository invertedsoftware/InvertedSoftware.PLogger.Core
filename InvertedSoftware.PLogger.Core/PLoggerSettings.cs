/* Copyright (c) Year(2020), Inverted Software
 *
 * Permission to use, copy, modify, and/or distribute this software for any purpose with or without fee is hereby granted, 
 * provided that the above copyright notice and this permission notice appear in all copies.
 *
 * THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS.
 * IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE,
 * DATA OR PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
 */

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace InvertedSoftware.PLogger.Core
{
	public class PLoggerSettings
	{
		private readonly IConfiguration _configuration;

		public PLoggerSettings()
		{
			PLogType = "file";
			PLogEnabled = true;
			BaseNameFile = "plogger.txt";
			PLogFileMaxSizeKB = 1024;
			PLogFileMessageTemplate = "{0}";
			StringConnection = "ErrorsConnection";
			StoredProcedureName = "AddError";
			MessageParameterName = "@ErrorName";
		}
		public PLoggerSettings(IConfiguration configuration)
		{
			_configuration = configuration;
			PLogType = _configuration["Logging:PLogger:PLogType"];
			bool pLogEnabled = false;
			if (bool.TryParse(_configuration["Logging:PLogger:PLogEnabled"], out pLogEnabled))
				PLogEnabled = pLogEnabled;
			BaseNameFile = _configuration["Logging:PLogger:BaseNameFile"];
			int pLogFileMaxSizeKB = 0;
			if (int.TryParse(_configuration["Logging:PLogger:PLogFileMaxSizeKB"], out pLogFileMaxSizeKB))
				PLogFileMaxSizeKB = pLogFileMaxSizeKB;
			PLogFileMessageTemplate = _configuration["Logging:PLogger:PLogFileMessageTemplate"];
			StringConnection = _configuration["Logging:PLogger:StringConnection"];
			StoredProcedureName = _configuration["Logging:PLogger:StoredProcedureName"];
			MessageParameterName = _configuration["Logging:PLogger:MessageParameterName"];
			int pLogDeleteFilesOlderThanDays = 0;
			if (int.TryParse(_configuration["Logging:PLogger:PLogDeleteFilesOlderThanDays"], out pLogDeleteFilesOlderThanDays))
				PLogDeleteFilesOlderThanDays = pLogDeleteFilesOlderThanDays;
			long pLogStopLoggingIfSpaceSmallerThanMB = 0;
			if (long.TryParse(_configuration["Logging:PLogger:PLogStopLoggingIfSpaceSmallerThanMB"], out pLogStopLoggingIfSpaceSmallerThanMB))
				PLogStopLoggingIfSpaceSmallerThanMB = pLogStopLoggingIfSpaceSmallerThanMB;
		}
		public string PLogType { get; set; }
		public bool PLogEnabled { get; set; }
		public string BaseNameFile { get; set; }
		public int PLogFileMaxSizeKB { get; set; }
		public string PLogFileMessageTemplate { get; set; }
		public Func<string, LogLevel, bool> Filter { get; set; }
		public string StringConnection { get; set; }
		public string StoredProcedureName { get; set; }
		public string MessageParameterName { get; set; }
		public int PLogDeleteFilesOlderThanDays { get; set; }
		public long PLogStopLoggingIfSpaceSmallerThanMB { get; set; }
	}
}
