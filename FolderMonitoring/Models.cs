using System.Text.RegularExpressions;

/// <summary>File size and modification time used for determining if a file is still being written or already copied to a destination folder</summary>
/// <param name="Size">File size in bytes</param>
/// <param name="LastModified">Last modification time. Not reset when creating a copy</param>
record struct Snapshot(long Size, DateTime LastModified);

/// <summary>All data needed to create a file copy in a destination folder</summary>
/// <param name="SourceFilePath">Full path of the source file</param>
/// <param name="DestDir">Destination folder</param>
/// <param name="Attempt">Copy attempt counts. Always starts as 1</param>
/// <param name="PreviousSnapshot">Snapshot used to determine if file is still being written to folder. No snapshot on first file check</param>
record class CopyJob(string SourceFilePath, string DestDir, int Attempt = 1, Snapshot? PreviousSnapshot = null);

/// <summary>Decides which file names are accepted, which destination folders they go to and what name they get there</summary>
/// <param name="dirB">ENDOHIS folder</param>
/// <param name="dirC">KOLHIS folder</param>
/// <param name="dirD">ENDODM folder</param>
/// <param name="dirE">KOLDM folder</param>
/// <remarks>File routing functions are defined out of the main program file so they can be tested</remarks>
class FileRouter(string dirB, string dirC, string dirD, string dirE)
{
    static readonly IReadOnlyList<string> PROCEDURE_NAMES = ["Kolp", "CRth"];

    // Regex
    // All file name checks are case insensitive
    // Groups: 1 = last id, 2 = procedure name, 3 = extension
    static readonly Regex LAST_ID_AND_PROCEDURE = new(
        @"([0-9]{6})([A-Z]+)(\.pdf)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    static readonly Regex FILE_NAME_MASK = new(
        @"^[0-9]{6}-[0-9]{5}_(010000234[0-9A-Z]+|nav)_[0-9]{6}[A-Z]+\.pdf$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>Checks if the file name matches the accepted file name mask</summary>
    /// <param name="name">File name</param>
    public bool IsValidFileName(string name)
    {
        return FILE_NAME_MASK.IsMatch(name);
    }

    /// <summary>Returns the destination directories for a file based on the procedure name after the last id in the file name</summary>
    /// <param name="name">File name</param>
    public string[] GetDestinationDirectories(string name)
    {
        // Procedure name after the last id is checked
        var match = LAST_ID_AND_PROCEDURE.Match(name);
        string procedure = match.Success ? match.Groups[2].Value : "";

        return PROCEDURE_NAMES.Any(k => procedure.Contains(k, StringComparison.OrdinalIgnoreCase)) ? [dirC, dirE] : [dirB, dirD];
    }

    /// <summary>Returns the name the file gets in the destination folder. Used for edge case of file rename when moving to folder KOLDM</summary>
    /// <param name="name">File name</param>
    /// <param name="destDir">Destination directory</param>
    public string GetDestinationFileName(string name, string destDir)
    {
        return string.Equals(destDir, dirE, StringComparison.OrdinalIgnoreCase) ?LAST_ID_AND_PROCEDURE.Replace(name, "$1_$2$3") : name;
    }
}
