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
using System.Collections.Generic;
using System.Text;

namespace InvertedSoftware.PLogger.Core
{
	public class PLoggerProvider : ILoggerProvider, ISupportExternalScope
	{
		private readonly PLoggerSettings _settings;
		private static readonly object padlock = new object();
		private PLogger _plogger = null;

		private IExternalScopeProvider _scopeProvider;
		public PLoggerProvider()
			: this(settings: null)
		{
		}

		public PLoggerProvider(PLoggerSettings settings)
		{
			_settings = settings;
		}

		public ILogger CreateLogger(string name)
		{
			if (_plogger == null)
			{
				lock (padlock)
				{
					if (_plogger == null)
					{
						_plogger = new PLogger(name, _settings ?? new PLoggerSettings());
					}
				}
			}
			return _plogger;
		}

		public void Dispose()
		{
		}

		public void SetScopeProvider(IExternalScopeProvider scopeProvider)
		{
			_scopeProvider = scopeProvider;
		}
	}
}
