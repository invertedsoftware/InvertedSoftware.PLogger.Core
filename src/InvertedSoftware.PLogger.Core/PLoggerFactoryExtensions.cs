/* Copyright (c) Year(2020), Inverted Software
 *
 * Permission to use, copy, modify, and/or distribute this software for any purpose with or without fee is hereby granted, 
 * provided that the above copyright notice and this permission notice appear in all copies.
 *
 * THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS.
 * IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE,
 * DATA OR PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
 */

using InvertedSoftware.PLogger.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Extensions.Logging
{
	public static class PLoggerFactoryExtensions
	{
		public static ILoggingBuilder AddPLogger(this ILoggingBuilder builder)
		{
			if (builder == null)
			{
				throw new ArgumentNullException(nameof(builder));
			}

			builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, PLoggerProvider>());

			return builder;
		}

		public static ILoggerFactory AddPLogger(this ILoggerFactory factory, IConfiguration configuration)
		{
			var settings = new PLoggerSettings(configuration);
			return factory.AddPLogger(settings);
		}

		public static ILoggingBuilder AddPLogger(this ILoggingBuilder builder, PLoggerSettings settings)
		{
			if (builder == null)
			{
				throw new ArgumentNullException(nameof(builder));
			}

			if (settings == null)
			{
				throw new ArgumentNullException(nameof(settings));
			}

			builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider>(new PLoggerProvider(settings)));

			return builder;
		}
		public static ILoggerFactory AddPLogger(this ILoggerFactory factory)
		{
			if (factory == null)
			{
				throw new ArgumentNullException(nameof(factory));
			}

			return AddPLogger(factory, LogLevel.Information);
		}

		public static ILoggerFactory AddPLogger(this ILoggerFactory factory, LogLevel minLevel)
		{
			if (factory == null)
			{
				throw new ArgumentNullException(nameof(factory));
			}

			return AddPLogger(factory, new PLoggerSettings()
			{
				Filter = (_, logLevel) => logLevel >= minLevel
			});
		}

		public static ILoggerFactory AddPLogger(
			this ILoggerFactory factory,
			PLoggerSettings settings)
		{
			if (factory == null)
			{
				throw new ArgumentNullException(nameof(factory));
			}

			if (settings == null)
			{
				throw new ArgumentNullException(nameof(settings));
			}

			factory.AddProvider(new PLoggerProvider(settings));
			return factory;
		}
	}
}
