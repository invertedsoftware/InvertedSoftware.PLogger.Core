// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using InvertedSoftware.PLogger.Diagnostics;
using InvertedSoftware.PLogger.Diagnostics.Mcp;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.Logging;

/// <summary>
/// <c>AddPLoggerMcpServer()</c> registers the MCP service surface;
/// <c>MapPLoggerMcp()</c> mounts it on a route prefix.
/// </summary>
public static class PLoggerMcpExtensions
{
    public static PLoggerDiagnosticsBuilder AddPLoggerMcpServer(
        this PLoggerDiagnosticsBuilder builder,
        Action<PLoggerMcpOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IConfigureOptions<PLoggerMcpOptions>, PLoggerMcpConfigureOptions>());
        if (configure is not null)
        {
            services.Configure(configure);
        }

        // Wire up the MCP SDK with our tools class.
        services.AddMcpServer()
                .WithHttpTransport()
                .WithTools<PLoggerDiagnosticsTools>();

        return builder;
    }

    /// <summary>
    /// Mounts the MCP HTTP transport at the configured route prefix
    /// (<see cref="PLoggerMcpOptions.RoutePrefix"/>). Adds a bearer-token
    /// guard middleware in front when <see cref="PLoggerMcpOptions.BearerToken"/>
    /// is set.
    /// </summary>
    public static IEndpointRouteBuilder MapPLoggerMcp(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var opts = app.Services.GetRequiredService<IOptions<PLoggerMcpOptions>>().Value;
        if (!opts.Enabled)
        {
            return app;
        }

        // Bearer guard. Lightweight — just header check + 401.
        if (!string.IsNullOrEmpty(opts.BearerToken))
        {
            app.Use(async (context, next) =>
            {
                if (context.Request.Path.StartsWithSegments(opts.RoutePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var auth = context.Request.Headers.Authorization.ToString();
                    if (!auth.Equals($"Bearer {opts.BearerToken}", StringComparison.Ordinal))
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsync("Unauthorized");
                        return;
                    }
                }
                await next();
            });
        }

        app.MapMcp(opts.RoutePrefix);
        return app;
    }

    private sealed class PLoggerMcpConfigureOptions : IConfigureOptions<PLoggerMcpOptions>
    {
        private readonly IConfiguration _config;
        public PLoggerMcpConfigureOptions(IConfiguration config) { _config = config; }

        public void Configure(PLoggerMcpOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            var section = _config.GetSection(PLoggerMcpOptions.SectionName);

            if (bool.TryParse(section["Enabled"], out var enabled))
            {
                options.Enabled = enabled;
            }

            var prefix = section["RoutePrefix"];
            if (!string.IsNullOrEmpty(prefix))
            {
                options.RoutePrefix = prefix;
            }

            var token = section["BearerToken"];
            if (!string.IsNullOrEmpty(token))
            {
                options.BearerToken = token;
            }
        }
    }
}
