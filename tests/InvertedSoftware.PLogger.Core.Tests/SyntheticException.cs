// Copyright (c) Inverted Software. All rights reserved.

namespace InvertedSoftware.PLogger.Core.Tests;

/// <summary>
/// Test helper: produces exceptions with arbitrary, deterministic
/// <see cref="Exception.StackTrace"/> values so the fingerprinter rules can
/// be driven independently of the real runtime stack format.
/// </summary>
internal static class SyntheticException
{
    public static Exception WithStack(string stack) => new DefaultSynthetic(stack);

    public static Exception WithStack(string stack, Type asType)
    {
        if (asType == typeof(InvalidOperationException))
        {
            return new InvalidOpSynthetic(stack);
        }
        if (asType == typeof(ArgumentException))
        {
            return new ArgumentSynthetic(stack);
        }
        if (asType == typeof(ApplicationException))
        {
            return new ApplicationSynthetic(stack);
        }
        return new DefaultSynthetic(stack);
    }

    private sealed class DefaultSynthetic(string stack) : Exception("synthetic")
    {
        public override string? StackTrace => stack;
    }

    private sealed class InvalidOpSynthetic(string stack) : InvalidOperationException("synthetic")
    {
        public override string? StackTrace => stack;
    }

    private sealed class ArgumentSynthetic(string stack) : ArgumentException("synthetic")
    {
        public override string? StackTrace => stack;
    }

    private sealed class ApplicationSynthetic(string stack) : ApplicationException("synthetic")
    {
        public override string? StackTrace => stack;
    }
}
