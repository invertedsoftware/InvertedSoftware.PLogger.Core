// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using InvertedSoftware.PLogger.Diagnostics;
using InvertedSoftware.PLogger.Diagnostics.GitHub;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.Logging;

public static class PLoggerGitHubExtensions
{
    public static PLoggerDiagnosticsBuilder AddGitHubNotifier(
        this PLoggerDiagnosticsBuilder builder,
        Action<GitHubNotifierOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;

        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IConfigureOptions<GitHubNotifierOptions>, GitHubConfigureOptions>());
        PLoggerOptionsRegistration.AddConfigurationChangeTokenSource<GitHubNotifierOptions>(
            services, GitHubNotifierOptions.SectionName);
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.AddHttpClient<GitHubIssueNotifier>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDiagnosticNotifier, GitHubIssueNotifier>());

        return builder;
    }

    private sealed class GitHubConfigureOptions : IConfigureOptions<GitHubNotifierOptions>
    {
        private readonly IConfiguration _config;
        public GitHubConfigureOptions(IConfiguration config) { _config = config; }

        public void Configure(GitHubNotifierOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            var section = _config.GetSection(GitHubNotifierOptions.SectionName);

            if (bool.TryParse(section["Enabled"], out var enabled))
            {
                options.Enabled = enabled;
            }

            var repo = section["Repository"];
            if (!string.IsNullOrEmpty(repo))
            {
                options.Repository = repo;
            }

            var token = section["Token"];
            if (!string.IsNullOrEmpty(token))
            {
                options.Token = token;
            }

            var labels = new List<string>();
            foreach (var child in section.GetSection("Labels").GetChildren())
            {
                if (!string.IsNullOrEmpty(child.Value))
                {
                    labels.Add(child.Value);
                }
            }
            if (labels.Count > 0)
            {
                options.Labels = labels;
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
