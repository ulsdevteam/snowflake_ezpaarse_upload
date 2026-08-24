using System;
using System.IO;
using System.Data;
using System.Data.Common;
using Snowflake.Data.Client;
using System.Runtime.CompilerServices;
using Org.BouncyCastle.Crypto.Modes;



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
/// Convert a C# native type to corresponding DbType used by snowflake connector
/// </summary>
/// <param name="t"> Input type </param>
/// <returns> DbType equivalent of Input `t`</returns>
 public static DbType? to_db_type(Type t)
    {
        switch (Type.GetTypeCode(t))
        {
            case TypeCode.Int32:
                return DbType.Int32;
            case TypeCode.Int64:
                return DbType.Int64;
            case TypeCode.Int16:
                return DbType.Int16;
            case TypeCode.String:
                return DbType.String;
            case TypeCode.Boolean:
                return DbType.Boolean;
            default: 
                return null;
                
        }
    }

    /// <summary>
    /// Create a command requiring 0 parameters, and only a raw string literal.
    /// 
    /// Note: It is responsibility of caller to call Dispose on generated command/returned value
    /// </summary>
    /// <param name="conn"> connection object to database</param>
    /// <param name="query"> query string </param>
    /// <returns></returns>
    public static IDbCommand PrepareCommand(IDbConnection conn, string query)
    {
        IDbCommand cmd = conn.CreateCommand();
        cmd.CommandText = query;
        return cmd;   
    }

 
    /// <summary>
    /// Create a command that consists of 1 or more parameters. 
    /// Single parameters are passed as the value
    /// Two or more parameters are passed as tuples.
    /// 
    /// For example: If a query (qry) requires parameters string "hi" and 5, the command would be created as follows
    /// > IDbCommand cmd = PrepareCommand<(string, int)>(conn, qry, ("hi", 5));
    /// For example: For a single parameter, 7
    /// > IDbCommand cmd = PrepareCommand<int>(conn, qry, 7);
    /// </summary>
    /// <typeparam name="T"> a type, or a tuple of types, that can be mapped via to_db_type</typeparam>
    /// <param name="conn"> Connection object that allows for command creation</param>
    /// <param name="query"> Query template string, with ? substituted for parameter values (named parameters not supported)</param>
    /// <param name="parameter_tuple"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    public static IDbCommand PrepareCommand<T>(IDbConnection conn, string query, T parameter_tuple)
    {
        IDbCommand cmd = conn.CreateCommand();
        //cmd.Parameters.Clear(); 
        // (double x, int y) = (5, 7)
        cmd.CommandText = query;
        ITuple generic_tuple;
        if (parameter_tuple is not ITuple)
        {
            generic_tuple = ValueTuple.Create<T>(parameter_tuple);
        }
        else {
            generic_tuple = (ITuple)parameter_tuple;
        }
        Type[] types = generic_tuple.GetType().GetGenericArguments();
        for (int i = 0; i < generic_tuple.Length; i++)
        {
            var param = cmd.CreateParameter();
            param.ParameterName = 'p' + (i).ToString();
            if (to_db_type(types[i]) == null)
            {
                throw new ArgumentException($"{types[i]} does not have a mapping to corresponding DbType for parameters");
            }
            
            param.DbType = to_db_type(types[i]).Value;
            
            param.Value = generic_tuple[i];
            cmd.Parameters.Add(param);
        }
        return cmd;

    }
    
    /// <summary>
    /// More abstract version of ExecuteCommandArray, which allows arbitrary IDbCommands 
    /// to be specified for execution. See ICommandList for details.
    /// 
    /// Generated commands are explicitly disposed (IDbCommand implements IDispose, and corresponding Dispose is called).
    /// </summary>
    /// <param name="commands"></param>
    /// <returns></returns>
    public bool ExecuteCommandList(ICommandList commands)
    {
        bool sqlSuccess = true;
        string executing_command = "";
        try
        {
            using (IDbConnection conn = new SnowflakeDbConnection())
            {
                conn.ConnectionString = this.authString;
                conn.Open();
                commands.SetConnection(conn);
                foreach (IDbCommand cmd in commands.GetCommands())
                {
                    try 
                    {
                        executing_command = cmd.CommandText;
                        cmd.ExecuteNonQuery();
                    }
                    catch (SnowflakeDbException e)
                    {
                        foreach (IDbCommand recovery_cmd in commands.GetRecoveryCommands())
                        {
                            recovery_cmd.ExecuteNonQuery();
                            recovery_cmd.Dispose();
                        }
                        Console.Error.WriteLine($"failed to execute: {e.Message}");
                        Console.Error.WriteLine($"failed command: {executing_command}");
                        sqlSuccess = false;
                    }
                    finally {
                        cmd.Dispose();
                    }
                }

            }

        }
        catch (SnowflakeDbException e)
        {
            Console.Error.WriteLine($"failed to execute: {e.Message}");
            Console.Error.WriteLine($"failed command: {executing_command}");
            sqlSuccess = false;
        }
        //catch (Exception exc)
        //{
        //    Console.Error.WriteLine("Encountered Generic Exception: ");
        //    Console.Error.WriteLine(exc.Message);
            
        //}
         return sqlSuccess;
    }
    /// <summary>
    /// Execute array of snowflake sql statements using connection string of provided in constructor
    /// </summary>
    /// <param name="commands"> List of sql commands </param>
    /// <returns> If error was encountered during command execution </returns>
    public bool ExecuteCommandArray(string[] commands, string[] failure_recovery_commands)
    {
        bool sqlSuccess = true;
        string executing_command = "";
        try {
            using (IDbConnection conn = new SnowflakeDbConnection())
            {
                conn.ConnectionString = this.authString;
                conn.Open();
                using (IDbCommand cmd = conn.CreateCommand())
                {
                    //string[] commands = [ezpaarseFormat, deleteExisting1, deleteExisting2, stageFile, insertContent];
                    try {
                        foreach (string cmdStr in commands)
                        {
                            cmd.CommandText = cmdStr;
                            executing_command = cmdStr;
                            cmd.ExecuteNonQuery();
                        }
                    } catch (SnowflakeDbException e)
                    {
                        foreach (string recovery_command in failure_recovery_commands)
                        {
                            cmd.CommandText = recovery_command;
                            cmd.ExecuteNonQuery();
                        }
                        Console.Error.WriteLine($"failed to execute: {e.Message}");
                        Console.Error.WriteLine($"failed command: {executing_command}");
                        sqlSuccess = false;
                    } finally {
                    conn.Close();
                    }
                }

            }
        }
        catch (SnowflakeDbException e)
        {
            Console.Error.WriteLine($"failed to execute: {e.Message}");
            Console.Error.WriteLine($"failed command: {executing_command}");
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
                Environment.Exit(0); // fail gracefully if no files found
            }
            return pendingFiles;

        }
        catch
        {
            Console.Error.WriteLine("pending Dir access failed");
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

     public void MoveToPending(string name)
    {
        string pendingPath = Path.Join(baseDir, "pending", name);
        string workingPath = Path.Join(baseDir, "working", name);
        File.Move(workingPath, pendingPath);
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
    /// i.e. Truncate first line (for eg. csv header) and insert $fname to start of each line 
    /// </summary>
    /// <param name="originalPath"> input file </param>
    /// <param name="outputPath"> output file </param>
    /// <param name="firstColumn"> new first line </param>
    public static void TruncateFirstLineAndInsertColumn(string originalPath, string outputPath, string firstColumn)
    {
        using (StreamReader reader = new StreamReader(originalPath)) {
            reader.ReadLine(); // discard first line
            using StreamWriter writer = new StreamWriter(outputPath);
            //writer.WriteLine(firstLine);
            while (reader.ReadLine() is { } line) {
                writer.WriteLine(firstColumn + line);
            }
       }

    }
}


/// <summary>
/// An abstract object (interface was cleaner than inheriting from an abstract class)
/// that requires ability to set a connection object, and provides iterators for a 
/// sequence of commands and recovery commands.
/// 
/// Using iterable allows connection object to be bound late, while still being able to instantiate
/// the prerequisite dependent variables to be bound to the implementing object. In simpler words, the 
/// user of the IDbCommand doesn't need to care about how the IDbCommand was produced
/// </summary>
interface ICommandList
{
    /// <summary>
    /// SQL queries to be ran in the happy path. 
    /// Using SnowflakeWrapper.PrepareCommand simplifies the creation of IDbCommand object from a 
    /// query and parameters
    /// </summary>
    /// <returns> Iterable of IDbCommand objects that are ready to be execute (ExeNonQuery())</returns>
    public IEnumerable<IDbCommand> GetCommands();

    /// <summary>
    /// SQL queries to be ran if an SQL error occurs. Primarily used to make sure that database isn't left in a 
    /// transitionary state (for example, csv uploaded to server, but failed to be processed)
    /// Using SnowflakeWrapper.PrepareCommand simplifies the creation of IDbCommand object from a 
    /// query and parameters
    /// </summary>
    /// <returns>Iterable of IDbCommand objects that are ready to be execute (ExeNonQuery())</returns>
    public IEnumerable<IDbCommand> GetRecoveryCommands();

    /// <summary>
    /// Getter method for a connection object. Can be used to check if connection has been initialized
    /// </summary>
    /// <returns>null if connection not initialized, otherwise connection object</returns>
    public IDbConnection? GetConnection();

    /// <summary>
    /// Setter method for a connection object. Used for late binding the connection to the 
    /// associated queries
    /// </summary>
    /// <param name="conn"> Initialized connection object that can produce commands</param>
    public void SetConnection(IDbConnection conn);
}

/// <summary>
/// implementation of ICommandList. Contains the actual queries to be executed 
/// on the server.
/// </summary>
class CommandList : ICommandList
{
    /// <summary>
    /// ezpYYYYMMDD.log i.e. the basename of the csv that is being used for ingestion. Used as loadid.
    /// </summary>
    string basename;
    /// <summary>
    /// full path of the file to be uploaded to the server for ingestion,
    /// </summary>
    string filename;
    /// <summary>
    /// connection object used to create IDbCommands for command iterable
    /// </summary>
    IDbConnection? conn = null;
    
    // connection getter
    public IDbConnection? GetConnection()
    {
        return this.conn;
    }

    // connection setter
    public void SetConnection(IDbConnection conn)
    {
        this.conn = conn;
    }

    // constructor for values that queries depend on.
    public CommandList(string basename, string filename)
    {
        this.basename = basename;
        this.filename = filename;
    }

    /// <summary>
    /// Using yield return, create an iterator for IDbCommands using hard coded query strings, 
    /// and query parameters provided via instance fields
    /// </summary>
    /// <returns> iterator of commands to be executed</returns>
    /// <exception cref="ArgumentException"> safety check for this.conn. Should never be encountered in prod, since all code paths
    /// should initialize this.conn before trying to iterate through commands.</exception>
    public IEnumerable<IDbCommand> GetCommands()
    {
           if (this.conn == null)
        {
            throw new ArgumentException("Connection not initialized");
        }

        string deleteExisting1 = $"DELETE FROM EZPAARSE_RESULT_DEPTS WHERE recordid IN (SELECT recordid FROM EZPAARSE_RESULTS WHERE loadid = :p0)";
        IDbCommand cmd = SnowflakeWrapper.PrepareCommand<string>(this.conn, deleteExisting1, this.basename);

        yield return cmd;

        string deleteExisting2 = $"DELETE FROM EZPAARSE_RESULTS WHERE loadid = :p0;";
        cmd = SnowflakeWrapper.PrepareCommand<string>(this.conn, deleteExisting2, this.basename);

    
        yield return cmd;
        
        string ezpaarseFormat = """
            CREATE OR REPLACE TEMP FILE FORMAT ezpaarse_csv
                TYPE = CSV 
                FIELD_DELIMITER = ';' 
                FIELD_OPTIONALLY_ENCLOSED_BY = '"' 
                TIMESTAMP_FORMAT = 'YYYY-MM-DD"T"HH24:MI:SS+TZH:TZM' 
                ESCAPE=NONE
                ESCAPE_UNENCLOSED_FIELD=NONE
                DATE_FORMAT = 'YYYY-MM-DD'
        """;
        cmd = SnowflakeWrapper.PrepareCommand(this.conn, ezpaarseFormat);
        yield return cmd;
        
        // PUT does not support substitution
        string stageFile = $"PUT file://{this.filename} @%EZPAARSE_RESULTS OVERWRITE=TRUE"; // upload file to snowflake server home of user account
        cmd = SnowflakeWrapper.PrepareCommand(this.conn, stageFile);
        yield return cmd;  
        
        // record id is an autoincrement field in oracle, so using uuid as a number.
        // explicitly rewriting column names such that recordid can be implicitly added and user_hash set to null
        string insertContent = """
                COPY INTO EZPAARSE_RESULTS (
                loadid, datetime, date, login, platform, platform_name,
                publisher_name, rtype, mime, print_identifier, online_identifier,
                title_id, doi, publication_title, publication_date, unitid, domain,
                on_campus, log_id, ezpaarse_version, ezpaarse_date,
                middlewares_version, middlewares_date, platforms_version,
                platforms_date, middlewares, title, type, subject, geoip_country,
                geoip_latitude, geoip_longitude, host, ezproxy_session, url,
                status, size
                ) FROM 
                (SELECT 
                    $1::CHAR(128) AS loadid,
                    $2::TIMESTAMP_TZ AS datetime,
                    $3::DATE AS date,
                    RTRIM(REGEXP_REPLACE($4, '@pitt\\.edu', ''))::CHAR(17) AS login,
                    $5::CHAR(64) AS platform,
                    $6::CHAR(128) AS platform_name,
                    $7::CHAR(128) AS publisher_name,
                    $8::CHAR(24) AS rtype,
                    $9::CHAR(16) AS mime,
                    $10::CHAR(32) AS print_identifier,
                    $11::CHAR(32) AS online_identifier,
                    $12::CHAR(256) AS title_id,
                    $13::CHAR(256) AS doi,
                    SUBSTR($14, 1, 256)::CHAR(8192) AS publication_title,
                    $15::CHAR(10) AS publication_date,
                    SUBSTR($16, 1, 1024)::CHAR(8192) AS unitid,
                    $17::CHAR(128) AS domain,
                    TO_BOOLEAN($18::CHAR(1)) AS on_campus,
                    $19::CHAR(64) AS log_id,
                    $20::CHAR(64) AS ezpaarse_version,
                    $21::DATE AS ezpaarse_date,
                    $22::CHAR(64) AS middlewares_version,
                    $23::DATE AS middlewares_date,
                    $24::CHAR(64) AS platforms_version,
                    $25::DATE AS platforms_date,
                    $26::CHAR(256) AS middlewares,
                    SUBSTR($27, 1, 1024)::CHAR(1024) AS title,
                    $28::CHAR(32) AS type,
                    $29::CHAR(512) AS subject,
                    $30::CHAR(2) AS geoip_country,
                    $31::NUMBER(7,4) AS geoip_latitude,
                    $32::NUMBER(7,4) AS geoip_longitude,
                    $33::CHAR(15) AS host,
                    $34::CHAR(15) AS ezproxy_session,
                    SUBSTR($35, 1, 1024) AS url,
                    $36::NUMBER(3) AS status,
                    $37::NUMBER(10) AS size
                    FROM @%EZPAARSE_RESULTS (FILE_FORMAT => 'ezpaarse_csv')) 
                """;
        cmd = SnowflakeWrapper.PrepareCommand(this.conn, insertContent);
        yield return cmd;

        // Remove does not support parameters
        string remove_uploaded = $"REMOVE @%EZPAARSE_RESULTS PATTERN={this.basename}.data.gz";
        cmd = SnowflakeWrapper.PrepareCommand(this.conn, remove_uploaded);
        yield return cmd;
        yield break;
    }

    /// <summary>
    /// same as GetCommands, but for recovery commands (see interface docs for details)
    /// </summary>
    /// <returns> iterator of commands to be executed if happy path fails</returns>
    /// <exception cref="ArgumentException"> safety check for this.conn. Should never be encountered in prod, since all code paths
    /// should initialize this.conn before trying to iterate through commands.</exception>

    public IEnumerable<IDbCommand> GetRecoveryCommands()
    {
        if (this.conn == null)
        {
            throw new ArgumentException("Connection not initialized");
        }
        string remove_uploaded = $"REMOVE @%EZPAARSE_RESULTS PATTERN={basename}.data.gz";
        IDbCommand cmd = SnowflakeWrapper.PrepareCommand(this.conn, remove_uploaded);
        yield return cmd;
        yield break;
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
        string deleteExisting1 = $"DELETE FROM EZPAARSE_RESULT_DEPTS WHERE recordid IN (SELECT recordid FROM EZPAARSE_RESULTS WHERE loadid = \'{basename}\')";
        string deleteExisting2 = $"DELETE FROM EZPAARSE_RESULTS WHERE loadid = \'{basename}\';";
        string ezpaarseFormat = """
            CREATE OR REPLACE TEMP FILE FORMAT ezpaarse_csv
                TYPE = CSV 
                FIELD_DELIMITER = ';' 
                FIELD_OPTIONALLY_ENCLOSED_BY = '"' 
                TIMESTAMP_FORMAT = 'YYYY-MM-DD"T"HH24:MI:SS+TZH:TZM' 
                ESCAPE=NONE
                ESCAPE_UNENCLOSED_FIELD=NONE
                DATE_FORMAT = 'YYYY-MM-DD'
        """;
        string stageFile = $"PUT file://{filepath} @%EZPAARSE_RESULTS OVERWRITE=TRUE"; // upload file to snowflake server home of user account
        // record id is an autoincrement field in oracle, so using uuid as a number.
        // explicitly rewriting column names such that recordid can be implicitly added and user_hash set to null
        string insertContent = """
                COPY INTO EZPAARSE_RESULTS (
                loadid, datetime, date, login, platform, platform_name,
                publisher_name, rtype, mime, print_identifier, online_identifier,
                title_id, doi, publication_title, publication_date, unitid, domain,
                on_campus, log_id, ezpaarse_version, ezpaarse_date,
                middlewares_version, middlewares_date, platforms_version,
                platforms_date, middlewares, title, type, subject, geoip_country,
                geoip_latitude, geoip_longitude, host, ezproxy_session, url,
                status, size
                ) FROM 
                (SELECT 
                    $1::CHAR(128) AS loadid,
                    $2::TIMESTAMP_TZ AS datetime,
                    $3::DATE AS date,
                    RTRIM(REGEXP_REPLACE($4, '@pitt\\.edu', ''))::CHAR(17) AS login,
                    $5::CHAR(64) AS platform,
                    $6::CHAR(128) AS platform_name,
                    $7::CHAR(128) AS publisher_name,
                    $8::CHAR(24) AS rtype,
                    $9::CHAR(16) AS mime,
                    $10::CHAR(32) AS print_identifier,
                    $11::CHAR(32) AS online_identifier,
                    $12::CHAR(256) AS title_id,
                    $13::CHAR(256) AS doi,
                    SUBSTR($14, 1, 256)::CHAR(8192) AS publication_title,
                    $15::CHAR(10) AS publication_date,
                    SUBSTR($16, 1, 1024)::CHAR(8192) AS unitid,
                    $17::CHAR(128) AS domain,
                    TO_BOOLEAN($18::CHAR(1)) AS on_campus,
                    $19::CHAR(64) AS log_id,
                    $20::CHAR(64) AS ezpaarse_version,
                    $21::DATE AS ezpaarse_date,
                    $22::CHAR(64) AS middlewares_version,
                    $23::DATE AS middlewares_date,
                    $24::CHAR(64) AS platforms_version,
                    $25::DATE AS platforms_date,
                    $26::CHAR(256) AS middlewares,
                    SUBSTR($27, 1, 1024)::CHAR(1024) AS title,
                    $28::CHAR(32) AS type,
                    $29::CHAR(512) AS subject,
                    $30::CHAR(2) AS geoip_country,
                    $31::NUMBER(7,4) AS geoip_latitude,
                    $32::NUMBER(7,4) AS geoip_longitude,
                    $33::CHAR(15) AS host,
                    $34::CHAR(15) AS ezproxy_session,
                    SUBSTR($35, 1, 1024) AS url,
                    $36::NUMBER(3) AS status,
                    $37::NUMBER(10) AS size
                    FROM @%EZPAARSE_RESULTS (FILE_FORMAT => 'ezpaarse_csv')) 
                """;
        string remove_uploaded = $"REMOVE @%EZPAARSE_RESULTS PATTERN={basename}.data.gz";
        return  [ezpaarseFormat, deleteExisting1, deleteExisting2, stageFile, insertContent, remove_uploaded];
    }

    public static string[] GetRecoveryCommands(string basename, string filepath)
    {
        string remove_uploaded = $"REMOVE @%EZPAARSE_RESULTS PATTERN={basename}.data.gz";
        return [remove_uploaded];
    }
    /// <summary>
    /// Process each file in /pending/ if possible i.e.
    /// upload content of each /pending/ file as a csv into the snowflake
    /// table EZPAARSE_RESULTS
    /// </summary>
    public static void Main()
    {
        string? basePath = Environment.GetEnvironmentVariable("BASE_DIR");
        if (basePath == null) { 
            Console.WriteLine("BASE_DIR environment variable not provided, using current directory");
            basePath = Directory.GetCurrentDirectory();
        }
        FileHandler fileHandler =  new FileHandler(basePath);
        string[] pendingFiles = fileHandler.GetPendingFilesOrFail();
        foreach (string pendingFullPath in pendingFiles)
        {
            string pendingFileName = Path.GetFileName(pendingFullPath);
            string loadid = Path.GetFileName(pendingFullPath);
            if (!fileHandler.CheckPendingShouldBeProcessed(pendingFileName))
            {
                Console.WriteLine($"{pendingFileName} already exists in pending or done, skipping...");
                continue;
            }
            string workingPath = fileHandler.MoveToWorking(pendingFileName);
            // preprocess csv
            FileHandler.TruncateFirstLineAndInsertColumn(workingPath, workingPath + ".data", loadid + ";"); 
            ICommandList commands = new CommandList(loadid, workingPath + ".data");
            // string[] commands = ProcessEzpaarse.GetCommands(loadid, workingPath + ".data");
            // string[] recover_commands = ProcessEzpaarse.GetRecoveryCommands(loadid, workingPath+".data");
            SnowflakeWrapper wrapper = new SnowflakeWrapper(Environment.GetEnvironmentVariable("SNOWFLAKE_AUTH_STRING") ?? "");
            bool success = wrapper.ExecuteCommandList(commands);

            if (success)
            {
                fileHandler.MoveToDone(pendingFileName);
                fileHandler.RemoveTempFiles(pendingFileName);
            }
            else
            {
                fileHandler.MoveToPending(pendingFileName);
                fileHandler.RemoveTempFiles(pendingFileName);
                Console.WriteLine("failed to execute sql");
            }


        }
    }

}