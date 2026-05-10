// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using System.Globalization;
using InvertedSoftware.PLogger.Diagnostics;
using InvertedSoftware.PLogger.Diagnostics.SourceLink;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.Logging;

/// <summary>
/// <c>AddPLoggerSourceLink()</c> chains off the <see cref="PLoggerDiagnosticsBuilder"/>.
/// </summary>
public static class PLoggerSourceLinkExtensions
{
    public static PLoggerDiagnosticsBuilder AddPLoggerSourceLink(
        this PLoggerDiagnosticsBuilder builder,
        Action<SourceLinkOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;

        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IConfigureOptions<SourceLinkOptions>, SourceLinkConfigureOptions>());
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.AddMemoryCache();
        services.TryAddSingleton<ISourceLinkResolver, SourceLinkResolver>();

        // Replace the default fingerprint-only enricher with one that
        // populates SourceSnippet when reachable.
        services.Replace(ServiceDescriptor.Singleton<IDiagnosticEnricher, SourceLinkDiagnosticEnricher>());

        return builder;
    }

    private sealed class SourceLinkConfigureOptions : IConfigureOptions<SourceLinkOptions>
    {
        private readonly IConfiguration _config;
        public SourceLinkConfigureOptions(IConfiguration config) { _config = config; }

        public void Configure(SourceLinkOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            var section = _config.GetSection(SourceLinkOptions.SectionName);

            if (bool.TryParse(section["Enabled"], out var enabled))
            {
                options.Enabled = enabled;
            }

            var token = section["GitHubToken"];
            if (!string.IsNullOrEmpty(token))
            {
                options.GitHubToken = token;
            }

            var allowedSection = section.GetSection("AllowedNamespaces");
            var allowed = new List<string>();
            foreach (var child in allowedSection.GetChildren())
            {
                if (!string.IsNullOrEmpty(child.Value))
                {
                    allowed.Add(child.Value);
                }
            }
            if (allowed.Count > 0)
            {
                options.AllowedNamespaces = allowed;
            }

            if (int.TryParse(section["ContextLines"], CultureInfo.InvariantCulture, out var ctx) && ctx > 0)
            {
                options.ContextLines = ctx;
            }

            if (TimeSpan.TryParse(section["FetchTimeout"], CultureInfo.InvariantCulture, out var fetch))
            {
                options.FetchTimeout = fetch;
            }
        }
    }
}
