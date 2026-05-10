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
        PLoggerOptionsRegistration.AddConfigurationChangeTokenSource<PLoggerMcpOptions>(
            services, PLoggerMcpOptions.SectionName);
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
        var monitor = app.Services.GetRequiredService<IOptionsMonitor<PLoggerMcpOptions>>();
        var initial = monitor.CurrentValue;
        if (!initial.Enabled)
        {
            return app;
        }

        // Bearer guard. Reads the live options each request so rotated
        // tokens take effect without restart. The compare uses
        // FixedTimeEquals to avoid a timing oracle on the prefix.
        app.Use(async (context, next) =>
        {
            var current = monitor.CurrentValue;
            if (current.Enabled
                && !string.IsNullOrEmpty(current.BearerToken)
                && context.Request.Path.StartsWithSegments(current.RoutePrefix, StringComparison.OrdinalIgnoreCase))
            {
                if (!IsAuthorized(context.Request.Headers.Authorization.ToString(), current.BearerToken))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsync("Unauthorized");
                    return;
                }
            }
            await next();
        });

        app.MapMcp(initial.RoutePrefix);
        return app;
    }

    private static bool IsAuthorized(string authHeader, string expectedToken)
    {
        const string prefix = "Bearer ";
        if (authHeader.Length < prefix.Length || !authHeader.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        // Length-check first so the constant-time compare doesn't leak via
        // a side-channel on the buffer-allocation cost.
        var presented = authHeader.AsSpan(prefix.Length);
        if (presented.Length != expectedToken.Length)
        {
            return false;
        }

        // FixedTimeEquals over UTF-8 bytes — comparison time depends only on length.
        Span<byte> a = stackalloc byte[256];
        Span<byte> b = stackalloc byte[256];
        var aLen = System.Text.Encoding.UTF8.GetByteCount(presented);
        var bLen = System.Text.Encoding.UTF8.GetByteCount(expectedToken.AsSpan());
        if (aLen != bLen || aLen > a.Length)
        {
            return false;
        }
        System.Text.Encoding.UTF8.GetBytes(presented, a);
        System.Text.Encoding.UTF8.GetBytes(expectedToken.AsSpan(), b);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a[..aLen], b[..bLen]);
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
