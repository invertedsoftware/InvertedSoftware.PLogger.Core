// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

namespace InvertedSoftware.PLogger.Diagnostics.Tests;

/// <summary>
/// Test helper that throws + catches an exception so the captured instance
/// has a real stack trace. Per call returns a brand-new exception, but
/// stack-content is identical (same call site).
/// </summary>
internal static class SyntheticException
{
    public static Exception Create() => CaptureFrom(static () => throw new InvalidOperationException("test"));

    public static Exception Create<T>() where T : Exception, new()
        => CaptureFrom(static () => throw new T());

    private static Exception CaptureFrom(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            return ex;
        }
        throw new InvalidOperationException("expected throw");
    }
}
