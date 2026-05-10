// Copyright (c) Inverted Software. All rights reserved.

using InvertedSoftware.PLogger.Core.Fingerprinting;

namespace InvertedSoftware.PLogger.Core.Tests;

public class StackFingerprinterTests
{
    [Fact]
    public void Normalize_NullException_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => StackFingerprinter.Normalize(null!));
    }

    [Fact]
    public void Normalize_SimpleException_ContainsTypeAndStackButNoLineNumbers()
    {
        var ex = CaptureThrown(static () => throw new InvalidOperationException("boom"));
        var normalized = StackFingerprinter.Normalize(ex);

        Assert.Contains("System.InvalidOperationException", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain(":line ", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void Normalize_NestedGeneric_CollapsesGenericArgs()
    {
        var input = "at MyApp.Foo[T](System.Collections.Generic.List`1[System.Int32] x) in C:\\src\\Foo.cs:line 42";

        // Simulate by injecting our normalised text via a synthetic exception
        // — easier: feed the regex directly through the public surface.
        var ex = SyntheticException.WithStack(input);
        var normalized = StackFingerprinter.Normalize(ex);

        Assert.Contains("List`1", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("[System.Int32]", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain(":line 42", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void Normalize_AsyncStateMachine_CollapsesMoveNextAndStateClass()
    {
        var input = "at MyApp.AsyncStuff.<DoWorkAsync>d__3.MoveNext() in C:\\src\\AsyncStuff.cs:line 17";
        var ex = SyntheticException.WithStack(input);
        var normalized = StackFingerprinter.Normalize(ex);

        Assert.DoesNotContain("d__3", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain(":line 17", normalized, StringComparison.Ordinal);
        Assert.Contains("<async>", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void Normalize_LambdaDisplayClass_IsCollapsed()
    {
        var input = "at MyApp.Caller.<>c__DisplayClass3_0.<HandleAsync>b__0()";
        var ex = SyntheticException.WithStack(input);
        var normalized = StackFingerprinter.Normalize(ex);

        Assert.DoesNotContain("DisplayClass3_0", normalized, StringComparison.Ordinal);
        Assert.Contains("<display>", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void Normalize_DeepInnerExceptionChain_Concatenates()
    {
        var inner3 = new InvalidOperationException("3");
        var inner2 = new ApplicationException("2", inner3);
        var inner1 = new AggregateException("1", inner2);

        var normalized = StackFingerprinter.Normalize(inner1);

        Assert.Contains("System.AggregateException", normalized, StringComparison.Ordinal);
        Assert.Contains("System.ApplicationException", normalized, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void Compute_TwoIdenticalExceptions_ProduceSameHash()
    {
        var a = SyntheticException.WithStack("at MyApp.Foo() in C:\\src\\F.cs:line 10");
        var b = SyntheticException.WithStack("at MyApp.Foo() in C:\\src\\F.cs:line 200"); // line differs

        var ha = StackFingerprinter.Compute(a);
        var hb = StackFingerprinter.Compute(b);

        Assert.Equal(16, ha.Length);
        Assert.Equal(16, hb.Length);
        Assert.Equal(ha, hb);
    }

    [Fact]
    public void Compute_DifferentTypes_ProduceDifferentHashes()
    {
        var a = SyntheticException.WithStack("at MyApp.Foo() in C:\\src\\F.cs:line 1", typeof(InvalidOperationException));
        var b = SyntheticException.WithStack("at MyApp.Foo() in C:\\src\\F.cs:line 1", typeof(ArgumentException));

        var ha = StackFingerprinter.Compute(a);
        var hb = StackFingerprinter.Compute(b);

        Assert.NotEqual(ha, hb);
    }

    private static Exception CaptureThrown(Action action)
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
