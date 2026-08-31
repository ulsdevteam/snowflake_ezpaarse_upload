using System.Data;
using System.Text;
using System.Security.Cryptography;
using Snowflake.Data.Client;

public class PostProcess {
        public static void Main() {
                string? salt_source_env = Environment.GetEnvironmentVariable("USER_HASH_SALT");
                if (salt_source_env == null)
                {
                        Console.WriteLine("Expected environment variable USER_HASH_SALT not found. This is used for creating user hashes in db. Exiting...");
                        Environment.Exit(0);
                }
                string salt_source = salt_source_env; // cast from nullable to string
                
                // validate the salt_source somehow since inserting into a complex query, maybe hash it?
                
                byte[] salt_bytes = Encoding.UTF8.GetBytes(salt_source_env);

                // One-liner for modern .NET (Returns uppercase hex digest)
                string salt_digest = Convert.ToHexString(SHA256.HashData(salt_bytes)); 
                // translated sql statement from postprocess.sql
                string exeString = $"""
                BEGIN

                INSERT INTO EZPAARSE_RESULT_DEPTS
                SELECT                  -- Associate rc_cd and department_cd for students in EZPAARSE_RESULTS
                        ez.recordid,
                        stu.rc_cd,
                        stu.department_cd,
                        'Student'
                FROM
                        EZPAARSE_RESULTS ez
                        INNER JOIN
                        (
                                SELECT DISTINCT
                                        st.username,
                                        DATEADD(second, -1, DATEADD(day, 1, cal.full_dt)) AS end_dt, 
                                        DATEADD(day, 1, ADD_MONTHS(cal.full_dt, -1)) AS start_dt,
                                        dp.responsibility_center_cd AS rc_cd,
                                        dp.department_cd,
                                FROM
                                        UD_DATA.ST_ENROLLMENT en
                                        INNER JOIN PITT_MD.calendar cal ON cal.calendar_key = en.calendar_key
                                        INNER JOIN UD_DATA.ud_term t ON t.term_key = en.term_key
                                        INNER JOIN UD_DATA.ud_student st ON st.student_key = en.student_key
                                        INNER JOIN UD_DATA.ud_academic_plan_subplan ap ON en.academic_plan_subplan_key = ap.academic_plan_subplan_key
                                        INNER JOIN UD_DATA.ud_major_department md ON md.academic_plan_subplan_key = ap.academic_plan_subplan_key
                                        INNER JOIN PITT_MD.department dp ON dp.department_cd = md.department_cd
                                WHERE
                                        dp.is_current = TRUE
                                        AND cal.st_monthly_retain_flg = TRUE
                                        AND cal.full_dt > '2019-01-01'::DATE
                        ) stu ON ez.login = stu.username AND (ez.datetime BETWEEN stu.start_dt AND stu.end_dt)
                WHERE
                        ez.recordid NOT IN (SELECT recordid FROM EZPAARSE_RESULT_DEPTS)
                        AND ez.datetime < DATE_TRUNC('MONTH', CURRENT_TIMESTAMP)
                UNION
                SELECT -- Associate department_cd and rc_cd to ezpaarze_results using login name, corresponding to period/month of ezpaarse_results datetime 
                        ez.recordid,
                        em.rc_cd,
                        em.department_cd,
                        em.job_type
                FROM
                        EZPAARSE_RESULTS ez
                        INNER JOIN
                        (
                                SELECT DISTINCT
                                        emp.username,
                                        pd.period_date_beg AS date_begin,
                                        pd.period_date_end AS date_end,
                                        emp.responsibility_center_cd AS rc_cd,
                                        emp.department_cd,
                                        emp.job_type
                                FROM
                                        hr.employee emp
                                        INNER JOIN PITT_MD.PERIOD pd ON emp.period_name = pd.period_name
                                        INNER JOIN PITT_MD.department dep ON emp.department_cd = dep.department_cd
                                WHERE
                                        dep.is_current = TRUE
                                        AND emp.job_type != 'Student'
                                        AND date_begin > '2019-01-01'
                        ) em ON ez.login = em.username AND (ez.datetime::DATE BETWEEN date_begin AND date_end)
                WHERE
                        ez.recordid NOT IN (SELECT recordid FROM EZPAARSE_RESULT_DEPTS)
                        AND ez.datetime < DATE_TRUNC('MONTH', CURRENT_TIMESTAMP)
                UNION
                SELECT 
                        ez.recordid,
                        sp.SPONSORSHIP_RESPONSIBILITY_CENTER_CD,
                        '00000',
                        'Sponsored Account'
                FROM
                        EZPAARSE_RESULTS ez
                        INNER JOIN PITT_MD.SPONSORED_ACCOUNTS sp ON ez.login = sp.SPONSORED_USERNAME
                WHERE
                        ez.recordid NOT IN (SELECT recordid FROM R60.EZPAARSE_RESULT_DEPTS)
                        AND ez.datetime < DATE_TRUNC('MONTH', CURRENT_TIMESTAMP)
                ;

                UPDATE EZPAARSE_RESULTS
                SET user_hash = (
                        SHA2('{salt_digest}' || EZPAARSE_RESULTS.login)
                )
                WHERE user_hash IS NULL;

                COMMIT;

                EXCEPTION
                WHEN OTHER THEN
                ROLLBACK;
                RAISE;
                END;
                """;


                try {
                        using (IDbConnection conn = new SnowflakeDbConnection())
                        {
                                conn.ConnectionString = Environment.GetEnvironmentVariable("SNOWFLAKE_AUTH_STRING");
                                conn.Open();
                                using (IDbCommand allow_multiple_statements = conn.CreateCommand())
                                {
                                        allow_multiple_statements.CommandText = "alter session set MULTI_STATEMENT_COUNT=0";
                                        allow_multiple_statements.ExecuteNonQuery();
                                        // risk of setting this to 0 is that arbitrarily many commands can be run in this session
                                        // from one connection string which causes an injection risk
                                        // however, the string being used is a static string (changing the string would require
                                        //        access to process memory which would create way more issues than SQL injections)
                                        //

                                }
                                using (IDbCommand cmd = conn.CreateCommand())
                                {
                                cmd.CommandText = exeString;
                                cmd.ExecuteNonQuery();
                                
                                }
                                conn.Close();

                        }
                }
                catch (Exception e)
                {
                        // original script also prints out log and checks for canary in the log for success status
                        Console.WriteLine("failed to map accounts due to " + e.ToString());
                }
        }
}