using AutoHDRManager.Core;
using Xunit;

namespace AutoHDRManager.Tests;

public sealed class ApplicationVersionTests
{
    [Theory]
    [InlineData("1.3.0-local.1+abcdef", "v1.3.0")]
    [InlineData("1.3.0-rc.1+abcdef", "v1.3.0")]
    [InlineData("1.3.0", "v1.3.0")]
    [InlineData("2.15.42.1234", "v2.15.42")]
    public void DisplayOmitsBuildDetailsWithoutLosingDiagnosticIdentity(string full, string display)
    {
        var version = new ApplicationVersion(full);
        Assert.Equal(display, version.Display);
        Assert.Equal(full, version.Diagnostic);
    }
}
