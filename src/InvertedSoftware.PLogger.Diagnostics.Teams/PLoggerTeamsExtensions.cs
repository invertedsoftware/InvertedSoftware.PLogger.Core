// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using InvertedSoftware.PLogger.Diagnostics;
using InvertedSoftware.PLogger.Diagnostics.Teams;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.Logging;

public static class PLoggerTeamsExtensions
{
    public static PLoggerDiagnosticsBuilder AddTeamsNotifier(
        this PLoggerDiagnosticsBuilder builder,
        Action<TeamsNotifierOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;

        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IConfigureOptions<TeamsNotifierOptions>, TeamsConfigureOptions>());
        PLoggerOptionsRegistration.AddConfigurationChangeTokenSource<TeamsNotifierOptions>(
            services, TeamsNotifierOptions.SectionName);
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.AddHttpClient<TeamsWebhookNotifier>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDiagnosticNotifier, TeamsWebhookNotifier>());

        return builder;
    }

    private sealed class TeamsConfigureOptions : IConfigureOptions<TeamsNotifierOptions>
    {
        private readonly IConfiguration _config;
        public TeamsConfigureOptions(IConfiguration config) { _config = config; }

        public void Configure(TeamsNotifierOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            var section = _config.GetSection(TeamsNotifierOptions.SectionName);

            if (bool.TryParse(section["Enabled"], out var enabled))
            {
                options.Enabled = enabled;
            }
            var url = section["WebhookUrl"];
            if (!string.IsNullOrEmpty(url))
            {
                options.WebhookUrl = url;
            }
            var name = section["AppDisplayName"];
            if (!string.IsNullOrEmpty(name))
            {
                options.AppDisplayName = name;
            }
            if (long.TryParse(section["MinOccurrenceCount"], System.Globalization.CultureInfo.InvariantCulture, out var min) && min > 0)
            {
                options.MinOccurrenceCount = min;
            }
            if (TimeSpan.TryParse(section["RequestTimeout"], System.Globalization.CultureInfo.InvariantCulture, out var timeout))
            {
                options.RequestTimeout = timeout;
            }
        }
    }
}
