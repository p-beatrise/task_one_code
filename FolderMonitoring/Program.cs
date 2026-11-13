using System.Threading.Channels;

// Paths
// Each directory can be passed as an argument. If no argument is passed then the default directory is used
string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
string sourceDir = GetDirectoryFromArguments("--dirA", Path.Combine(desktop, "uzHIS"));
string dirB = GetDirectoryFromArguments("--dirB", Path.Combine(desktop, "ENDOHIS"));
string dirC = GetDirectoryFromArguments("--dirC", Path.Combine(desktop, "KOLHIS"));
string dirD = GetDirectoryFromArguments("--dirD", Path.Combine(desktop, "ENDODM"));
string dirE = GetDirectoryFromArguments("--dirE", Path.Combine(desktop, "KOLDM"));
string[] allDestinationDirs = [dirB, dirC, dirD, dirE];
var router = new FileRouter(dirB, dirC, dirD, dirE);    // File name mask and destination folder rules

// Constants
const string ALLOWED_EXTENSION = ".pdf"; 
const string TEMP_FILE_SUFFIX = ".tmp";
const int MAX_RETRIES = 3;

// Log creation
string logDir = GetDirectoryFromArguments("--logs", Path.Combine(desktop, "FolderWatcherLogs"));    // Directory for action audit log files. Can be passed as an argument
var logLock = new object();
Directory.CreateDirectory(logDir);  // Create log directory in case it is missing

// Time units
var fileUnchangedTime = TimeSpan.FromSeconds(2);    // For how long the file should be unchanged before copying process begins
var retryDelay = TimeSpan.FromSeconds(3);   // After error how long time is till next copy retry

// Work tracking
var copySnapshots = new Dictionary<string, Snapshot>(StringComparer.OrdinalIgnoreCase);     // Processed file keys. Case insensitive
var queue = Channel.CreateUnbounded<CopyJob>(new UnboundedChannelOptions { SingleReader = true }); // Queue used for copying files to destination folders in order

foreach (string dir in allDestinationDirs.Prepend(sourceDir))
{
    Directory.CreateDirectory(dir);     // Create directory in case it is missing
}

DeleteTemporaryFiles();     // Remove temporary file copies from previous program runs if there are any in the destination folders

// Watcher for monitoring file changes in a directory
using var watcher = new FileSystemWatcher(sourceDir)
{
    NotifyFilter = NotifyFilters.FileName,
    InternalBufferSize = 64 * 1024
};

// Functions to be called when specific event happens
watcher.Created += OnFileDetected;
watcher.Renamed += OnFileDetected;
watcher.Error   += OnWatcherError;
watcher.EnableRaisingEvents = true;     // Start watcher


/// <summary>Queues a file for copying</summary>
/// <param name="sender">The FileSystemWatcher that raised the event</param>
/// <param name="e">Event data</param>
void OnFileDetected(object sender, FileSystemEventArgs e)
{
    PlaceFileInQueue(e.FullPath);
}

/// <summary>Handles a watcher failure by restarting it if needed and scanning the source directory</summary>
/// <param name="sender">The FileSystemWatcher that raised the event</param>
/// <param name="e">Error info</param>
void OnWatcherError(object sender, ErrorEventArgs e)
{
    var ex = e.GetException();
    Log($"Failu monitorētāja kļūda: {ex.Message}.", error: true);

    // Watcher restart happens in cases when an overflow did not occur
    // Otherwise just a source directory scan is done to get files that watcher missed while not working
    if (ex is not InternalBufferOverflowException)
    {
        RestartWatcher();
    }
        
    ScanSourceDirectoryFiles();
}

/// <summary>Performs watcher restart. Restart tries have no limit as watcher is needed for program to function properly</summary>
void RestartWatcher()
{
    for (int attempt = 1; ; attempt++)
    {
        try
        {
            watcher.EnableRaisingEvents = false;    // Stop watcher
            watcher.EnableRaisingEvents = true;     // Start watcher
            Log($"Failu monitorētājs sekmīgi restartēts.");
            return;
        }
        catch (Exception ex)
        {
            Log($"Kļūda failu monitorētāja restartēšanā : {ex.Message}.", error: true);
            Thread.Sleep(retryDelay);
        }
    }
}

// Worker that processes queued file copy jobs
var worker = Task.Run(async () =>
{
    await foreach (var job in queue.Reader.ReadAllAsync())
    {
        ProcessCopyJob(job);
    }
});

Log($"Monitorēšanas process uzsākts.");
Log($"Darbību audita faili atrodas {logDir}.");

ScanSourceDirectoryFiles();     // Queue files that were already in source directory before the watcher started

await worker;   // Keep worker running until program is stopped

/// <summary>Deletes temporary file copies from destination directories. Copies can be caused by stoping process before file renaming.</summary>
void DeleteTemporaryFiles()
{
    foreach (string dir in allDestinationDirs)
    {
        try
        {
            foreach (string path in Directory.EnumerateFiles(dir))
            {
                // Check for files with .tmp ending
                if (path.EndsWith(TEMP_FILE_SUFFIX, StringComparison.OrdinalIgnoreCase))
                {
                    if (DeleteFile(path))
                    {
                        Log($"Dzēsts pagaidu fails: {path}.");
                    }
                }
            }
        }
        catch (Exception ex) 
        { 
            Log($"Kļūda pagaidu failu dzēšanas procesā: {ex.Message}."); 
        }
    }
}

/// <summary>Scans the source directory files and queues for copying any that have not been previously copied</summary>
void ScanSourceDirectoryFiles()
{
    Log($"Sākta failu skenēšana uzHIS katalogā.");
    try
    {
        foreach (var file in new DirectoryInfo(sourceDir).EnumerateFiles())
        {
            var snapshot = new Snapshot(file.Length, file.LastWriteTimeUtc);
            bool alreadyCopied = true;
            lock (copySnapshots)  // Lock so it cannot be modified by other threads
            {
                // Look up if source directory file key matches to a known copied file key in a destination folder
                foreach (string destDir in router.GetDestinationDirectories(file.Name))
                {
                    string key = CreateKey(file.FullName, destDir);

                    // Check if a copied file snaphot based on a key can be retrieved and compare it when possible
                    if (!copySnapshots.TryGetValue(key, out Snapshot copied) || copied != snapshot)
                    {
                        alreadyCopied = false; 
                    }
                }
            }
            // If key is not found then the source folder file has not been copied and is queued up
            if (!alreadyCopied)
            {
                PlaceFileInQueue(file.FullName);
            }
        }
    }
    catch (Exception ex) 
    { 
        Log($"Kļūda kataloga uzHIS skenēšanā: {ex.Message}"); 
    }
}

/// <summary>Creates the key for copy job identification</summary>
/// <param name="path">Full path of the source file</param>
/// <param name="destDir">Destination directory</param>
string CreateKey(string path, string destDir)
{
    return path + "|" + destDir;
}

/// <summary>Validates a file and queues one copy job</summary>
/// <param name="path">Full path of the file in the source folder</param>
void PlaceFileInQueue(string path)
{
    string fileName = Path.GetFileName(path);

    // Check for pdf extension is case insensitive
    if (!fileName.EndsWith(ALLOWED_EXTENSION, StringComparison.OrdinalIgnoreCase))
    {
        Log($"Fails {fileName} netiek kopēts, jo nav {ALLOWED_EXTENSION} dokuments.");
        return;
    }

    // Check if file mask is matching
    if (!router.IsValidFileName(fileName))
    {
        Log($"Fails {fileName} netiek kopēts, jo tā nosaukums neatbilst pieņemtajām normām.", error: true);
        return;
    }

    // Add a copy job for a file to queue for each destination it should be copied to
    foreach (string destDir in router.GetDestinationDirectories(fileName))
    {
        queue.Writer.TryWrite(new CopyJob(path, destDir));
    }
}

/// <summary>Copies one file to one destination once it has uploaded to folder</summary>
/// <param name="job">Information needed to perform a copy job</param>
void ProcessCopyJob(CopyJob job)
{
    string originalFileName = Path.GetFileName(job.SourceFilePath);
    string destFileName = router.GetDestinationFileName(originalFileName, job.DestDir);
    string destPath = Path.Combine(job.DestDir, destFileName);
    string tempPath = destPath + TEMP_FILE_SUFFIX;

    try
    {
        var fileToCopy = new FileInfo(job.SourceFilePath);

        // Check if file still exists
        if (!fileToCopy.Exists) 
        { 
            CompleteCopyJob(job);
            return;
        }
        var sourceSnapshot = new Snapshot(fileToCopy.Length, fileToCopy.LastWriteTimeUtc);
        var fileInDestDir = new FileInfo(destPath);

        // A copy of the file can exist in destination folders in case when source directory is being rescanned
        // If the file by the same name in the destination folder has the same size and write time it is considered a copy
        if (fileInDestDir.Exists && new Snapshot(fileInDestDir.Length, fileInDestDir.LastWriteTimeUtc) == sourceSnapshot)
        {
            Log($"Fails {originalFileName} netiek kopēts, jo tam jau ir kopija {job.DestDir} katalogā.");
            CompleteCopyJob(job, sourceSnapshot);
            return;
        }

        // If snapshot has changed then file is still being written to source folder. Queue the file for later processing
        // Bigger files can take longer to upload to folder. Snapshot comparison is used to determine when file is done loading
        if (job.PreviousSnapshot != sourceSnapshot)
        {
            QueueForLater(job with { PreviousSnapshot = sourceSnapshot, Attempt = 1 }, fileUnchangedTime);
            return;
        }

        // Edge case when a file has the same name but different metadata in destination folder
        if (fileInDestDir.Exists)
        {
            Log($"Fails {originalFileName} netiek kopēts, jo {job.DestDir} ir fails ar šādu nosaukumu bet citādiem metadatiem.", error: true);
            return;
        }

        // File is copied with a temporary suffix and flushed so its data is written to disk before it gets the real destination name.
        // If the program stops before the rename only the temporary file remains and it is deleted on the next start
        // The rename is used as a guard for cases where file save to disk not successful
        File.Copy(job.SourceFilePath, tempPath, overwrite: false);
        FlushFile(tempPath);
        File.Move(tempPath, destPath, overwrite: false);

        Log($"Fails {originalFileName} pārkopēts uz {job.DestDir} katalogu.");
        CompleteCopyJob(job, sourceSnapshot);
    }
    catch (Exception ex)
    {
        DeleteFile(tempPath); // In case an error is thrown after a temporary file was created it is deleted from destination folder

        if (job.Attempt > MAX_RETRIES)
        {
            Log($"Fails {originalFileName} nav pārkopēts uz {job.DestDir} pēc {MAX_RETRIES} mēģinājumiem. Papildus mēģinājumi netiks veikti.", error: true);
            return;
        }

        Log($"Fails {originalFileName} nav pārkopēts uz {job.DestDir} ar {job.Attempt} mēģinājumu: {ex.Message}.");
        QueueForLater(job with { Attempt = job.Attempt + 1 }, retryDelay);
    }
}

/// <summary>Updates variables used for keeping info about copy jobs</summary>
/// <param name="job">The copy job that is finished</param>
/// <param name="copied">Snapshot of the copied or skipped source file</param>
void CompleteCopyJob(CopyJob job, Snapshot? copied = null)
{
    string key = CreateKey(job.SourceFilePath, job.DestDir);
    lock (copySnapshots)    // Lock to prevent it being used while updating its value
    {
        // Save copied file snapshot
        if (copied.HasValue)
        {
            // Info about copies in destination folders is incase the watcher restarts
            // The copies are used to determine if the file should be passed to worker to queue up a copy job
            copySnapshots[key] = copied.Value;
        }
        else
        {
            copySnapshots.Remove(key);
        }
    }
}

/// <summary>Requeues a copy job after a specific time. Meanwhile worker can queue up other jobs</summary>
/// <param name="next">The copy job to requeue</param>
/// <param name="delay">Time wait before requeueing</param>
void QueueForLater(CopyJob next, TimeSpan delay)
{
    _ = Task.Delay(delay).ContinueWith(_ => queue.Writer.TryWrite(next), TaskScheduler.Default);
}

/// <summary>Appends a message to the current days log file</summary>
/// <param name="message">Text to log</param>
/// <param name="error">True to log as error and false to log as info log</param>
void Log(string message, bool error = false)
{
    lock (logLock)
    {
        try
        {
            var now = DateTime.Now;
            string logFile = Path.Combine(logDir, $"FolderWatcher-{now:yyyy-MM-dd}.log");   // Create a new log file for each day if there is not one
            File.AppendAllText(logFile,
                $"{now:yyyy-MM-dd HH:mm:ss.fff} [{(error ? "ERROR" : "INFO")}] {message}{Environment.NewLine}");
        }
        catch (Exception ex) 
        { 
            Console.Error.WriteLine($"Nevar veikt ierakstu audita failā: {ex.Message}"); 
        }
    }
}

/// <summary>Forces system to write file data to disk</summary>
/// <param name="path">Full path to the file to flush</param>
static void FlushFile(string path)
{
    var attributes = File.GetAttributes(path);

    // A readonly file cannot be opened for writing, so the readonly flag is removed for the flush
    if (attributes.HasFlag(FileAttributes.ReadOnly))
    {
        File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);    // Change only file readonly attribute as it needs to be writable to be flushed
    }
        
    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
    {
        stream.Flush(flushToDisk: true);    // Write cached data to disk
    }

    File.SetAttributes(path, attributes);   // Reset original file attributes
}

/// <summary>Does file deletion</summary>
/// <param name="path">Full path of the file to delete</param>
bool DeleteFile(string path)
{
    if (!File.Exists(path))
    {
        return false;   // Check that file exists otherwise an error will be thrown
    }
        
    try
    {
        File.SetAttributes(path, FileAttributes.Normal); // File attributes are reset so file cannot be readonly and can be deleted
        File.Delete(path);
        return true;
    }
    catch (Exception ex)
    {
        Log($"Kļūda faila {path} dzēšanas procesā: {ex.Message}.", error: true);
        return false;
    }
}

/// <summary>Returns the directory flag passed from arguments or a fallback directory if the flag is not present</summary>
/// <param name="flag">Flag name</param>
/// <param name="fallback">Directory to use when the flag is missing</param>
string GetDirectoryFromArguments(string flag, string fallback)
{
    int index = Array.IndexOf(args, flag);

    // If flag is not found or there isn't a value after it return to fallback 
    if (index < 0 || index + 1 >= args.Length)
    {
        return fallback;
    }

    return args[index + 1];
}