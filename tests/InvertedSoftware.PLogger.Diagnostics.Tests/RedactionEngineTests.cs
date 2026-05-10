// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using InvertedSoftware.PLogger.Diagnostics.AI;

namespace InvertedSoftware.PLogger.Diagnostics.Tests;

public class RedactionEngineTests
{
    [Theory]
    [InlineData("AKIAIOSFODNN7EXAMPLE", "<redacted-aws-key>")]
    [InlineData("aws_secret_access_key=wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY", "aws_secret_access_key=<redacted-aws-secret>")]
    [InlineData("AIzaSyA-vP3GVB1sTudHSTuPpfPNLMr3wfFlH88", "<redacted-google-key>")]
    public void Redact_ApiKeys_AreRedacted(string input, string expected)
    {
        Assert.Equal(expected, RedactionEngine.Redact(input));
    }

    [Fact]
    public void Redact_Jwt_IsRedacted()
    {
        var jwt = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4iLCJpYXQiOjE1MTYyMzkwMjJ9.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";
        var redacted = RedactionEngine.Redact(jwt);
        Assert.DoesNotContain(jwt, redacted, StringComparison.Ordinal);
        Assert.Contains("<redacted-jwt>", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Redact_AdoConnectionString_IsRedacted()
    {
        var conn = "Server=tcp:myserver.database.windows.net;Database=db;User=admin;Password=hunter2;Encrypt=true";
        var redacted = RedactionEngine.Redact(conn);
        Assert.DoesNotContain("hunter2", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Redact_PasswordKeyValue_IsRedacted()
    {
        var input = "{ \"password\": \"hunter2\", \"user\": \"admin\" }";
        var redacted = RedactionEngine.Redact(input);
        Assert.DoesNotContain("hunter2", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Redact_Email_IsRedacted()
    {
        Assert.Equal("contact <redacted-email> for help",
            RedactionEngine.Redact("contact alice@example.com for help"));
    }

    [Fact]
    public void Redact_Ssn_IsRedacted()
    {
        Assert.Equal("SSN: <redacted-ssn>", RedactionEngine.Redact("SSN: 123-45-6789"));
    }

    [Theory]
    [InlineData("4242 4242 4242 4242", true)]
    [InlineData("4242-4242-4242-4242", true)]
    [InlineData("4242424242424242", true)]
    [InlineData("1234567890123456", false)] // fails Luhn
    public void Redact_CreditCard_IsRedactedWhenLuhnPasses(string input, bool shouldRedact)
    {
        var redacted = RedactionEngine.Redact(input);
        if (shouldRedact)
        {
            Assert.Equal("<redacted-card>", redacted);
        }
        else
        {
            Assert.Equal(input, redacted);
        }
    }

    [Fact]
    public void Redact_LongMixedInput_RemovesAllSensitiveValues()
    {
        var input = """
            DEBUG: Connecting to mongodb://admin:secret123@cluster0.mongodb.net/db
            User alice@corp.com (SSN 987-65-4321) hit AKIAIOSFODNN7EXAMPLE
            Card 4242 4242 4242 4242 / IPv4 192.168.1.5
            JWT: eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4iLCJpYXQiOjE1MTYyMzkwMjJ9.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c
            """;
        var redacted = RedactionEngine.Redact(input);
        Assert.DoesNotContain("admin:secret123", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("alice@corp.com", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("987-65-4321", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("AKIAIOSFODNN7EXAMPLE", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("4242 4242 4242 4242", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("192.168.1.5", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Redact_EmptyOrNull_IsSafe()
    {
        Assert.Equal(string.Empty, RedactionEngine.Redact(string.Empty));
        Assert.Equal(string.Empty, RedactionEngine.Redact(null!));
    }
}
