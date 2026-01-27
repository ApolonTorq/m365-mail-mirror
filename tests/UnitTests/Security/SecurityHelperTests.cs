using M365MailMirror.Core.Security;

namespace M365MailMirror.UnitTests.Security;

/// <summary>
/// Unit tests for the SecurityHelper class.
/// </summary>
public class SecurityHelperTests
{
    #region IsExecutable Tests

    [Theory]
    [InlineData("virus.exe", true)]
    [InlineData("library.dll", true)]
    [InlineData("script.bat", true)]
    [InlineData("script.cmd", true)]
    [InlineData("installer.msi", true)]
    [InlineData("script.ps1", true)]
    [InlineData("script.vbs", true)]
    [InlineData("app.dmg", true)]
    [InlineData("package.deb", true)]
    [InlineData("script.sh", true)]
    [InlineData("script.py", true)]
    [InlineData("app.jar", true)]
    [InlineData("app.apk", true)]
    [InlineData("document.pdf", false)]
    [InlineData("image.jpg", false)]
    [InlineData("data.csv", false)]
    [InlineData("archive.zip", false)]
    [InlineData("text.txt", false)]
    [InlineData("SCRIPT.EXE", true)]  // Case insensitive
    [InlineData("Script.Dll", true)]  // Mixed case
    public void IsExecutable_ReturnsExpectedResult(string filename, bool expected)
    {
        var result = SecurityHelper.IsExecutable(filename);
        result.Should().Be(expected);
    }

    [Fact]
    public void GetBlockedExtension_ReturnsExtension_WhenBlocked()
    {
        var result = SecurityHelper.GetBlockedExtension("virus.exe");
        result.Should().Be(".exe");
    }

    [Fact]
    public void GetBlockedExtension_ReturnsNull_WhenAllowed()
    {
        var result = SecurityHelper.GetBlockedExtension("document.pdf");
        result.Should().BeNull();
    }

    #endregion

    #region Path Traversal Tests

    [Theory]
    [InlineData("../etc/passwd", true)]
    [InlineData("..\\windows\\system32", true)]
    [InlineData("foo/../bar", true)]
    [InlineData("foo/bar/..", true)]
    [InlineData("foo/bar/baz", false)]
    [InlineData("normal/path/file.txt", false)]
    [InlineData("..something", true)]  // Conservative: Contains ".." which could be risky
    [InlineData("something..", true)]  // Conservative: Contains ".." which could be risky
    public void HasPathTraversal_ReturnsExpectedResult(string path, bool expected)
    {
        var result = SecurityHelper.HasPathTraversal(path);
        result.Should().Be(expected);
    }

    #endregion

    #region Absolute Path Tests

    [Theory]
    [InlineData("C:\\Windows\\System32", true)]
    [InlineData("D:\\data\\file.txt", true)]
    [InlineData("\\\\server\\share", true)]
    [InlineData("/etc/passwd", true)]
    [InlineData("/usr/local/bin", true)]
    [InlineData("relative/path", false)]
    [InlineData("./local/path", false)]
    [InlineData("", false)]
    public void IsAbsolutePath_ReturnsExpectedResult(string path, bool expected)
    {
        var result = SecurityHelper.IsAbsolutePath(path);
        result.Should().Be(expected);
    }

    #endregion

    #region ZIP Entry Safety Tests

    [Theory]
    [InlineData("normal/file.txt", true)]
    [InlineData("folder/subfolder/data.csv", true)]
    [InlineData("../etc/passwd", false)]
    [InlineData("C:\\Windows\\System32\\cmd.exe", false)]
    [InlineData("/etc/passwd", false)]
    [InlineData("", false)]
    public void IsZipEntrySafe_ReturnsExpectedResult(string entryPath, bool expected)
    {
        var result = SecurityHelper.IsZipEntrySafe(entryPath);
        result.Should().Be(expected);
    }

    #endregion
}
