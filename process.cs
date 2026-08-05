using System;
using System.IO;
using System.Data;
using System.Data.Common;
using Snowflake.Data.Client;


///<summary>
/// Wrap boilerplate of executing sql on snowflake into a single class/method
///</summary>
class SnowflakeWrapper
{
    string authString = "";

    ///<param name="auth_string"> is connection string used for initiating snowflake connection</param>
    public SnowflakeWrapper(string auth_string)
    {
        this.authString = auth_string;
    }

    /// <summary>
    /// Execute list of snowflake sql statements using connection string of provided in constructor
    /// </summary>
    /// <param name="commands"> List of sql commands </param>
    /// <returns> If error was encountered during command execution </returns>
    public bool ExecuteCommandList(string[] commands)
    {
        bool sqlSuccess = true;
        try {
            using (IDbConnection conn = new SnowflakeDbConnection())
            {
                conn.ConnectionString = this.authString;
                conn.Open();
                using (IDbCommand cmd = conn.CreateCommand())
                {
                    //string[] commands = [ezpaarseFormat, deleteExisting1, deleteExisting2, stageFile, insertContent];
                    foreach (string cmdStr in commands)
                    {
                        cmd.CommandText = cmdStr;
                        cmd.ExecuteNonQuery();
                    }
                    conn.Close();
                }

            }
        }
        catch
        {
            sqlSuccess = false;
        }
        return sqlSuccess;
        
    }
}
/// <summary>
/// Put file operations around more readable function names, with 
/// a dependent element of baseDir
/// </summary>
class FileHandler {
    string baseDir = Directory.GetCurrentDirectory();

    public FileHandler(string baseDirPath)
    {
        this.baseDir = baseDirPath;
    }

    public FileHandler() {}
    /// <summary>
    /// List files to be processed i.e. ls baseDir/pending/
    /// </summary>
    /// <returns> List of files or exit with error message</returns>
    public string[] GetPendingFilesOrFail()
    {
        string pendingDir =  Path.Join(baseDir, "pending");
        try
        {
            string[] pendingFiles = Directory.GetFiles(pendingDir);
            if (pendingFiles.Length == 0)
            {
                Console.WriteLine($"No files match in {pendingDir}");
                Environment.Exit(1);
            }
            return pendingFiles;

        }
        catch
        {
            Console.WriteLine("pending Dir access failed");
            Environment.Exit(1);
            return []; // imagine your type system can't detect unreachable statements
        }

    }
    /// <summary>
    /// safety checks a path must pass to go through to next processing steps
    /// </summary>
    /// <param name="pendingPath">base path of path to be processed in /pending/</param>
    /// <returns>path doesn't exist in /working/ or /done/</returns>
    public bool CheckPendingShouldBeProcessed(string pendingPath)
    {
        string workingPath = Path.Join(baseDir, "working", pendingPath);
        string donePath = Path.Join(baseDir, "done", pendingPath);
        return !Path.Exists(donePath) && !Path.Exists(workingPath);
    }

    /// <summary>
    /// move file from /pending/file to /working/file 
    /// </summary>
    /// <param name="name">base path of file</param>
    /// <returns>full path of destination path i.e. /working/file for convenience</returns>
    public string MoveToWorking(string name)
    {
        string pendingPath = Path.Join(baseDir, "pending", name);
        string workingPath = Path.Join(baseDir, "working", name);
        File.Move(pendingPath, workingPath);
        return workingPath;
    }

    /// <summary>
    /// move file from working to done to indicate that processing has been done
    /// </summary>
    /// <param name="name">base path of file in /working/ to be moved</param>
    public void MoveToDone(string name)
    {
        string donePath = Path.Join(baseDir, "done", name);
        string workingPath = Path.Join(baseDir, "working", name);
        File.Move(workingPath, donePath);
    }

    /// <summary>
    /// remove files created during processing associated with the file being processed
    /// to be done after file has been processed
    /// </summary>
    /// <param name="associatedPath">base path of path that has been processed</param>
    public void RemoveTempFiles(string associatedPath)
    {
        string workingPath = Path.Join(baseDir, "working");
        string[] tempFiles = Directory.GetFiles(workingPath, $"{associatedPath}.*");
        foreach (string tempFile in tempFiles)
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Translation of following sed statement 
    /// <c>sed 1d < $EZPFILESDIR/working/$fname | sed "s/^/$fname;/" >> $EZPFILESDIR/working/$fname.data </c>
    /// i.e. replace first line of a file and store it in a different filename
    /// </summary>
    /// <param name="originalPath"> input file </param>
    /// <param name="outputPath"> output file </param>
    /// <param name="firstLine"> new first line </param>
    public static void ReplaceFirstLine(string originalPath, string outputPath, string firstLine)
    {
        using (StreamReader reader = new StreamReader(originalPath)) {
            reader.ReadLine(); // discard first line
            using StreamWriter writer = new StreamWriter(outputPath);
            writer.WriteLine(firstLine);
            while (reader.ReadLine() is { } line) {
                writer.WriteLine(line);
            }
       }

    }
}

/// <summary>
/// Entry point of file
/// </summary>
class ProcessEzpaarse
{
    /// <summary>
    /// List of sql statements, wrapped as a function to make inline substitution work
    /// </summary>
    /// <param name="basename">name of file being processed (also used as name of file in staged folder)</param>
    /// <param name="filepath">full path of the file to be uploaded to snowflake</param>
    /// <returns></returns>
    private static string[] GetCommands(string basename, string filepath) {
        string deleteExisting1 = $"DELETE FROM EZPAARSE_RESULT_DEPTS WHERE \"recordid\" IN (SELECT \"recordid\" FROM EZPAARSE_RESULTS WHERE \"loadid\" = '{basename}')";
        string deleteExisting2 = $"DELETE FROM EZPAARSE_RESULTS WHERE \"loadid\" = '{basename}';";
        string ezpaarseFormat = """
            CREATE TEMP FILE FORMAT 'ezpaarse_csv' 
                TYPE = CSV 
                RECORD_DELIMITER = ';' 
                FIELD_OPTIONALLY_ENCLOSED_BY = '\"' 
                TIMESTAMP_FORMAT = YYYY-MM-DD"T"HH24:MI:SS+TZH:TZM 
                DATE_FORMAT = 'YYYY-MM-DD'
        """;
        string stageFile = $"PUT file://{filepath} @~/ezpaarse_staged"; // upload file to snowflake server home of user account
        // record id is an autoincrement field in oracle, so using uuid as a number.
        string insertContent = $"""
                COPY INTO EZPAARSE_RESULTS FROM 
                (SELECT 
                    --HASH(UUID_STRING()) AS "recordid",
                    $1::CHAR(128) AS "loadid",
                    TO_CHAR ($2::TIMESTAMP_TZ) AS "datetime",
                    TO_CHAR($3::DATE) AS "date",
                    RTRIM(REGEXP_REPLACE($4, "@pitt.edu", ""))::CHAR(17) AS "login",
                    $5::CHAR(64) AS "platform",
                    $6::CHAR(128) AS "platform_name",
                    $7::CHAR(128) AS "publisher_name",
                    $8::CHAR(24) AS "rtype",
                    $9::CHAR(16) AS "mime",
                    $10::CHAR(32) AS "print_identifier",
                    $11::CHAR(32) AS "online_identifier",
                    $12::CHAR(256) AS "title_id",
                    $13::CHAR(256) AS "doi",
                    SUBSTR($14, 1, 256)::CHAR(8192) AS "publication_title",
                    $15::CHAR(10) AS "publication_date",
                    SUBSTR($16, 1, 1024)::CHAR(8192) AS "unitid",
                    $17::CHAR(128) AS "domain",
                    $18::CHAR(1) AS "on_campus",
                    $19::CHAR(64) AS "log_id",
                    $20::CHAR(64) AS "ezpaarse_version",
                    TO_CHAR($21::DATE) AS "ezpaarse_date",
                    $22::CHAR(64) AS "middlewares_version",
                    TO_CHAR($23::DATE) AS "middlewares_date",
                    $24::CHAR(64) AS "platforms_version",
                    TO_CHAR($25::DATE) AS "platforms_date",
                    $26::CHAR(256) AS "middlewares",
                    SUBSTR($27, 1, 1024)::CHAR(1024) AS "title",
                    $28::CHAR(32) AS "type",
                    $29::CHAR(512) AS "subject",
                    $30::CHAR(2) AS "geoip_country",
                    $31::CHAR AS "geoip_latitude",
                    $32::CHAR AS "geoip_longitude",
                    $33::CHAR(15) AS "host",
                    $34::CHAR(15) AS "ezproxy_session",
                    SUBSTR($35, 1, 1024) AS "url",
                    $36::CHAR AS "status",
                    $37::CHAR AS "size"
                    FROM @~/ezpaarse_staged/{basename} (FILE_FORMAT => 'ezpaarse_csv'))
                """;
        return  [ezpaarseFormat, deleteExisting1, deleteExisting2, stageFile, insertContent];
    }

    /// <summary>
    /// Process each file in /pending/ if possible i.e.
    /// upload content of each /pending/ file as a csv into the snowflake
    /// table EZPAARSE_RESULTS
    /// </summary>
    public static void Main()
    {
        string basePath = Directory.GetCurrentDirectory();
        FileHandler fileHandler =  new FileHandler(basePath);
        string[] pendingFiles = fileHandler.GetPendingFilesOrFail();
        foreach (string pendingFullPath in pendingFiles)
        {
            string pendingBasePath = Path.GetFileName(pendingFullPath);
            if (!fileHandler.CheckPendingShouldBeProcessed(pendingBasePath))
            {
                continue;
            }
            string workingPath = fileHandler.MoveToWorking(pendingBasePath);
            FileHandler.ReplaceFirstLine(workingPath, workingPath + ".data", basePath + ";");
            string[] commands = ProcessEzpaarse.GetCommands(basePath, workingPath + ".data");
            SnowflakeWrapper wrapper = new SnowflakeWrapper(Environment.GetEnvironmentVariable("SNOWFLAKE_AUTH_STRING") ?? "");
            bool success = wrapper.ExecuteCommandList(commands);

            if (success)
            {
                fileHandler.MoveToDone(basePath);
                fileHandler.RemoveTempFiles(basePath);
            }
            else
            {
                Console.WriteLine("failed to execute sql");
            }


        }
    }

}