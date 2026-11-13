namespace FolderMonitoring.Tests;

/// <summary>Class used to test the file name mask and which destination folders and names files get</summary>
public class FileRouterTests
{
    // Fake folder paths so tests never touch the real Desktop folders
    const string ENDOHIS = "ENDOHIS";
    const string KOLHIS = "KOLHIS";
    const string ENDODM = "ENDODM";
    const string KOLDM = "KOLDM";

    readonly FileRouter router = new(ENDOHIS, KOLHIS, ENDODM, KOLDM);

    // --------------------- Tests for file masks -------------------

    [Theory]
    [InlineData("111111-12345_010000234ABERG_212765CRth.PDF")]
    [InlineData("111111-12345_010000234ABERG_212765CRth.pdf")]
    [InlineData("111111-12345_nav_212765CRth.PDF")]
    [InlineData("111111-12345_nav_212765CRth.pdf")]
    [InlineData("222222-12345_010000234GFDSA_654321Kolp.PDF")]
    [InlineData("222222-12345_010000234GFDSA_654321Kolp.pdf")]
    [InlineData("222222-12345_nav_654321Kolp.PDF")]
    [InlineData("222222-12345_nav_654321Kolp.pdf")]
    [InlineData("333333-12345_010000234RT234_123456GAS.PDF")]
    [InlineData("333333-12345_010000234RT234_123456GAS.pdf")]
    [InlineData("333333-12345_nav_123456GAS.PDF")]
    [InlineData("333333-12345_nav_123456GAS.pdf")]

    public void IsValidFileName_NameMatchesMask_ReturnsTrue(string name)
    {
        Assert.True(router.IsValidFileName(name));
    }

    [Theory]
    [InlineData("111111-1234_010000234ABERG_212765CRth.PDF")]   // Person code too short
    [InlineData("111111-12345_navv_212765CRth.PDF")]   // Missing unique id is not replaced by "nav"
    [InlineData("111111-12345_010000234ABERG_212765CRth.xlsx")]   // Not a PDF file
    [InlineData("111111-12345_010000234ABERG_12345CRth.PDF")]   // Id before procedure name is too short
    public void IsValidFileName_NameDoesNotMatchMask_ReturnsFalse(string name)
    {
        Assert.False(router.IsValidFileName(name));
    }

    // --------------- Destination folder tests -------------------

    [Theory]
    [InlineData("111111-12345_010000234ABERG_212765CRth.PDF")]
    [InlineData("222222-12345_010000234GFDSA_654321Kolp.PDF")]
    public void GetDestinationDirectories_FilenameHasKolpOrCRth_ReturnsKolhisAndKoldm(string name)
    {
        Assert.Equal([KOLHIS, KOLDM], router.GetDestinationDirectories(name));
    }

    [Theory]
    [InlineData("333333-12345_010000234RT234_123456GAS.PDF")]
    [InlineData("333333-12345_010000234RT134_123456VAS.PDF")]
    public void GetDestinationDirectories_FilenameDoesNotHaveKolpOrCRth_ReturnsEndohisAndEndodm(string name)
    {
        Assert.Equal([ENDOHIS, ENDODM], router.GetDestinationDirectories(name));
    }

    // --------------------------- Destination file name tests --------------------

    [Theory]
    [InlineData("111111-12345_010000234ABERG_212765CRth.PDF", "111111-12345_010000234ABERG_212765_CRth.PDF")]
    [InlineData("222222-12345_010000234GFDSA_654321Kolp.PDF", "222222-12345_010000234GFDSA_654321_Kolp.PDF")]
    [InlineData("222222-12345_nav_654321Kolp.PDF", "222222-12345_nav_654321_Kolp.PDF")]
    public void GetDestinationFileName_KoldmFolder_AddsUnderscoreInFilenameBeforeProcedureName(string name, string expected)
    {
        Assert.Equal(expected, router.GetDestinationFileName(name, KOLDM));
    }

    [Theory]
    [InlineData(KOLHIS)]
    [InlineData(ENDOHIS)]
    [InlineData(ENDODM)]
    public void GetDestinationFileName_OtherFolder_ReturnsOriginalName(string destDir)
    {
        const string name = "333333-12345_010000234RT234_123456GAS.PDF";

        Assert.Equal(name, router.GetDestinationFileName(name, destDir));
    }

    // -------------------------- Full route

    [Fact]
    public void Routing_FileWithKolpInName_ReturnsKolhisWithOriginalNameAndKoldmWithUnderscore()
    {
        const string name = "123456-12345_010000234GFDSA_654321Kolp.pdf";

        var result = router.GetDestinationDirectories(name)
            .Select(dir => (dir, router.GetDestinationFileName(name, dir)));

        Assert.Equal(
            [(KOLHIS, "123456-12345_010000234GFDSA_654321Kolp.pdf"), (KOLDM, "123456-12345_010000234GFDSA_654321_Kolp.pdf")],
            result);
    }

    [Fact]
    public void Routing_FileWithoutKolpInName_ReturnsEndohisAndEndodmWithOriginalName()
    {
        const string name = "123456-12345_010000234GFDSA_654321Gastr.pdf";

        var result = router.GetDestinationDirectories(name)
            .Select(dir => (dir, router.GetDestinationFileName(name, dir)));

        Assert.Equal(
            [(ENDOHIS, name), (ENDODM, name)],
            result);
    }
}
