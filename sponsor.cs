using System;
using System.Diagnostics;
using System.Data;
using Snowflake.Data.Client;

public class Sponsor {
    public static void Main() {
        string commandName = "ldapsearch";
        string ldapUser = Environment.GetEnvironmentVariable("LDAPUSER") ?? throw new Exception("expected ldap username");
        string ldapPass = Environment.GetEnvironmentVariable("LDAPPASS") ?? throw new Exception("expected ldap password");
        string ldapHost = Environment.GetEnvironmentVariable("LDAPHOST") ?? throw new Exception("expected ldap host name");
        string ldapBase = Environment.GetEnvironmentVariable("LDAPBASE") ?? throw new Exception("expected ldap base");
        string args = $"""-LLL -D "{ldapUser}" -w "{ldapPass}" -p 389 -h "{ldapHost}" -b "{ldapBase}" -s sub -E pr=10000/noprompt '(&(PittSponsorRC=*)(!(PittSponsorRC=AL)))' cn PittSponsorRC""";

        using (Process ldapProcess = new Process())
        {
            ldapProcess.StartInfo.FileName = commandName;
            ldapProcess.StartInfo.Arguments = args;
            ldapProcess.StartInfo.UseShellExecute = false;
            ldapProcess.StartInfo.RedirectStandardError = true;
            ldapProcess.StartInfo.RedirectStandardOutput = true;

            ldapProcess.Start();

            string stdout = ldapProcess.StandardOutput.ReadToEnd();
            ldapProcess.WaitForExit();
            //int pid = Environment.ProcessId;
            //string outfile = $"sponsored.{pid}.ldap"; 
            //File.WriteAllText(outfile, stdout);

            string[] lines = stdout.Split(new[] {'\r', '\n'}, StringSplitOptions.RemoveEmptyEntries);
            List<string> queryList = new List<string> {""};
            foreach (string line in lines)
            {
                if (line.StartsWith("dn: ") ||  line.StartsWith("#") || line.StartsWith(" "))
                {
                    continue;
                }
                // confirm line starts with cn:
                if (line.StartsWith("cn: "))
                {
                    string insertData = line.Substring("cn: ".Length);
                    queryList[^1] = queryList[^1] + "INSERT INTO EZPAARSE_SPACCTS VALUES (" + insertData + ",";
                    continue;

                }
                if (line.StartsWith("PittSponsorRC: ")) {
                    // slightly ambiguous whether a line can have structure cn: PittSponsorRC: ..., so ignoring that case
                    string sponsoredAccountName = line.Substring("PittSponsorRC: ".Length);
                    queryList[^1] = queryList[^1] + sponsoredAccountName + ");";
                    queryList.Add("");
                }
            }
            // original script has rollback if failed, which is not implemented here
            using (IDbConnection conn = new SnowflakeDbConnection())
            {
                conn.ConnectionString = Environment.GetEnvironmentVariable("SNOWFLAKE_AUTH_STRING");
                conn.Open();
                foreach (string query in queryList)
                {
                    if (query.Equals(""))
                    {
                        continue;
                    }
                    using (IDbCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = query;
                        cmd.ExecuteNonQuery();
                        conn.Close();
                    }
                }


            }
        }
    }
}