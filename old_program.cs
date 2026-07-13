/// One to one translation of process.sh. process.sh is currently represented by
/// Program.cs
using System;
using System.IO;
using System.Data;
using System.Data.Common;
using Snowflake.Data.Client;


string baseDir = Directory.GetCurrentDirectory();
Console.WriteLine(baseDir);
string pendingDir   = Path.Join(baseDir, "pending");
string doneDir      = Path.Join(baseDir, "done");
string workingDir   = Path.Join(baseDir, "working");


// check if pending dir empty
string[] pendingFiles = [];
try {
    pendingFiles = Directory.GetFiles(pendingDir);
    if (pendingFiles.Length == 0) {
        Console.WriteLine($"No files match in {pendingDir}");
        Environment.Exit(1);
    }
}
catch
{
    Console.WriteLine("pending Dir access failed");
    Environment.Exit(1);

}


// fetch from /pending/ -> work on /working/ -> push to /done/
try {

    foreach (string pendingPath in pendingFiles) {
        string pendingBaseName = Path.GetFileName(pendingPath);

        string workingPath = Path.Join(workingDir, pendingBaseName);
        string donePath = Path.Join(doneDir, pendingBaseName);

        if (Path.Exists(workingPath)) {
            Console.WriteLine($"{pendingPath} already processed and exists in \"done\"");
            continue;
        }

        if (Path.Exists(donePath)) {
            Console.WriteLine($"{pendingPath} already queued for processing and exists in \"working\"");
            continue;

        }

        File.Move(pendingPath, workingPath);
        // replace first line, i.e. data pre-processing
        string workingDataPath = workingPath + ".data";
        using (StreamReader reader = new StreamReader(workingPath)) {
            reader.ReadLine(); // discard first line
            using StreamWriter writer = new StreamWriter(workingDataPath);
            writer.WriteLine(pendingBaseName + ";");
            while (reader.ReadLine() is { } line) {
                writer.WriteLine(line);
            }
       }

        string deleteExisting1 = $"DELETE FROM EZPAARSE_RESULT_DEPTS WHERE \"recordid\" IN (SELECT \"recordid\" FROM EZPAARSE_RESULTS WHERE \"loadid\" = '{pendingBaseName}')";
        string deleteExisting2 = $"DELETE FROM EZPAARSE_RESULTS WHERE \"loadid\" = '{pendingBaseName}';";
        string ezpaarseFormat = """
            CREATE TEMP FILE FORMAT 'ezpaarse_csv' 
                TYPE = CSV 
                RECORD_DELIMITER = ';' 
                FIELD_OPTIONALLY_ENCLOSED_BY = '\"' 
                TIMESTAMP_FORMAT = YYYY-MM-DD"T"HH24:MI:SS+TZH:TZM 
                DATE_FORMAT = 'YYYY-MM-DD'
        """;
        string stageFile = $"PUT file://{workingDataPath} @~/ezpaarse_staged"; // upload file to snowflake server home of user account
        string insertContent = $"""
                COPY INTO EZPAARSE_RESULTS FROM 
                (SELECT 
                    $1::CHAR(128) AS "loadid",
                    $2::TIMESTAMP_TZ AS "datetime",
                    $3::DATE AS "date",
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
                    $21::DATE AS "ezpaarse_date",
                    $22::CHAR(64) AS "middlewares_version",
                    $23::DATE AS "middlewares_date",
                    $24::CHAR(64) AS "platforms_version",
                    $25::DATE AS "platforms_date",
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
                    FROM @~/ezpaarse_staged/{pendingBaseName} (FILE_FORMAT => 'ezpaarse_csv'))
                """;
/*
$1::CHAR(128) AS "loadid",
$2::TIMESTAMP_TZ AS "datetime",
$3::DATE AS "date",
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
$21::DATE AS "ezpaarse_date",
$22::CHAR(64) AS "middlewares_version",
$23::DATE AS "middlewares_date",
$24::CHAR(64) AS "platforms_version",
$25::DATE AS "platforms_date",
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

  "loadid" CHAR(128),
  "datetime" TIMESTAMP WITH TIME ZONE,
  "date" DATE,
  "login" CHAR(17) "RTRIM(REPLACE(:\"login\", '@pitt.edu', ''))",
  "platform" CHAR(64),
  "platform_name" CHAR(128),
  "publisher_name" CHAR(128),
  "rtype" CHAR(24),                                                                                                                                                                          
  "mime" CHAR(16),
  "print_identifier" CHAR(32),
  "online_identifier" CHAR(32),
  "title_id" CHAR(256),
  "doi" CHAR(256),
  "publication_title" CHAR(8192) "SUBSTR(:\"publication_title\", 1, 256)",
  "publication_date" CHAR(10),
  "unitid" CHAR(8192) "SUBSTR(:\"unitid\", 1, 1024)",
  "domain" CHAR(128),                                                                                                                                                                         
  "on_campus" CHAR(1),                                                                                                                                                                        
  "log_id" CHAR(64),                                                                                                                                                                          
  "ezpaarse_version" CHAR(64),
  "ezpaarse_date" DATE,
  "middlewares_version" CHAR(64),
  "middlewares_date" DATE,
  "platforms_version" CHAR(64),
  "platforms_date" DATE,
  "middlewares" CHAR(256),
  "title" CHAR(8192) "SUBSTR(:\"title\", 1, 1024)",
  "type" CHAR(32),
  "subject" CHAR(512),
  "geoip_country" CHAR(2),
  "geoip_latitude" CHAR,
  "geoip_longitude" CHAR,
  "host" CHAR(15),
  "ezproxy_session" CHAR(15),
  "url" CHAR(8192) "SUBSTR(:\"url\", 1, 1024)",
  "status" CHAR,
  "size" CHAR
*///

        bool sqlSuccess = true;
        try {
            using (IDbConnection conn = new SnowflakeDbConnection())
            {
                conn.ConnectionString = Environment.GetEnvironmentVariable("SNOWFLAKE_AUTH_STRING");
                conn.Open();
                using (IDbCommand cmd = conn.CreateCommand())
                {
                    string[] commands = [ezpaarseFormat, deleteExisting1, deleteExisting2, stageFile, insertContent];
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
        if (sqlSuccess)
        {
            File.Move(workingPath, donePath);
            string[] tempFiles = Directory.GetFiles(workingDir, $"{pendingBaseName}.*");
            foreach (string tempFile in tempFiles)
            {
                File.Delete(tempFile);
            }
        }
        else
        {
            Console.WriteLine($"Failed to write {pendingBaseName}");
            continue;
        }
    }
}
catch
{
    Console.WriteLine("loop failed");
    Environment.Exit(1);

}
