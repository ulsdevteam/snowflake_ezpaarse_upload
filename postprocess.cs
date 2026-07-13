using System.Data;
using Snowflake.Data.Client;

public class PostProcess {
        public static void Main() {

                // translated sql statement from postprocess.sql
                string exeString = """
                BEGIN

                INSERT INTO EZPAARSE_RESULT_DEPTS
                SELECT
                        ez."recordid",
                        stu.rc_cd,
                        stu.department_cd,
                        'Student'
                FROM
                        UDA4ULA.EZPAARSE_RESULTS ez
                        INNER JOIN
                        (
                                SELECT DISTINCT
                                        st.username,
                                        DATEADD(second, -1, DATEADD(day, 1, cal.full_dt)) AS end_dt,
                                        DATEADD(day, 1, ADD_MONTHS(cal.full_dt, -1)) AS start_dt,
                                        CASE WHEN dp.responsibility_center_cd = '00' THEN rc.responsibility_center_cd ELSE dp.responsibility_center_cd END AS rc_cd,
                                        CASE WHEN dp.responsibility_center_cd = '00' THEN rc.responsibility_center_descr ELSE dp.responsibility_center_descr END AS rc_descr,
                                        dp.department_cd,
                                        dp.department_descr,
                                        cal.full_dt
                                FROM
                                        UD_DATA.ST_ENROLLMENT en
                                        INNER JOIN UD_DATA.ud_calendar cal ON cal.calendar_key = en.calendar_key
                                        INNER JOIN UD_DATA.ud_term t ON t.term_key = en.term_key
                                        INNER JOIN UD_DATA.ud_student st ON st.student_key = en.student_key
                                        INNER JOIN UD_DATA.ud_academic_plan_subplan ap ON en.academic_plan_subplan_key = ap.academic_plan_subplan_key
                                        INNER JOIN UD_DATA.ud_major_department md ON md.academic_plan_subplan_key = ap.academic_plan_subplan_key
                                        INNER JOIN UD_DATA.ud_department dp ON dp.department_cd = md.department_cd
                                        INNER JOIN UD_DATA.ud_responsibility_center rc ON rc.responsibility_center_cd = ap.responsibility_center_cd
                                WHERE
                                        dp.current_flg = 1
                                        AND rc.current_flg = 1
                                        AND cal.st_monthly_retain_flg = 'Y'
                                        AND cal.full_dt > '2019-01-01'
                                -- ORDER BY removed: not valid in Snowflake subqueries without TOP/LIMIT
                        ) stu ON ez."login" = stu.username AND (ez."datetime" BETWEEN stu.start_dt AND stu.end_dt)
                WHERE
                        ez."recordid" NOT IN (SELECT "recordid" FROM UDA4ULA.EZPAARSE_RESULT_DEPTS)
                        AND ez."datetime" < DATE_TRUNC('MONTH', CURRENT_TIMESTAMP)
                UNION
                SELECT
                        ez."recordid",
                        em.rc_cd,
                        em.department_cd,
                        em.job_type
                FROM
                        UDA4ULA.EZPAARSE_RESULTS ez
                        INNER JOIN
                        (
                                SELECT DISTINCT
                                        em.username,
                                        DATEADD(second, -1, DATEADD(day, 1, cal.full_dt)) AS end_dt,
                                        DATEADD(day, 1, ADD_MONTHS(cal.full_dt, -1)) AS start_dt,
                                        dep.responsibility_center_cd AS rc_cd,
                                        dep.responsibility_center_descr AS rc_descr,
                                        dep.department_cd,
                                        dep.department_descr,
                                        jb.job_type
                                FROM
                                        ud_data.py_employment py
                                        INNER JOIN ud_data.ud_calendar cal ON cal.calendar_key = py.calendar_key
                                        INNER JOIN UD_DATA.ud_employee em ON py.employee_key = em.employee_key
                                        INNER JOIN UD_DATA.ud_department dep ON dep.department_cd = em.department_cd
                                        INNER JOIN ud_data.ud_job jb ON py.job_key = jb.job_key
                                WHERE
                                        dep.current_flg = 1
                                        AND cal.py_month_end_flg = 'Y'
                                        AND jb.job_type != 'Student'
                                        AND cal.full_dt > '2019-01-01'
                                -- ORDER BY removed: not valid in Snowflake subqueries without TOP/LIMIT
                        ) em ON ez."login" = em.username AND (ez."datetime" BETWEEN em.start_dt AND em.end_dt)
                WHERE
                        ez."recordid" NOT IN (SELECT "recordid" FROM UDA4ULA.EZPAARSE_RESULT_DEPTS)
                        AND ez."datetime" < DATE_TRUNC('MONTH', CURRENT_TIMESTAMP)
                ;

                INSERT INTO EZPAARSE_RESULT_DEPTS
                SELECT
                        "recordid",
                        "rc",
                        '00000',
                        'Sponsored Account'
                FROM
                        EZPAARSE_RESULTS
                        JOIN EZPAARSE_SPACCT_RCS ON (EZPAARSE_RESULTS."login" = EZPAARSE_SPACCT_RCS."login")
                WHERE
                        "recordid" NOT IN (SELECT "recordid" FROM EZPAARSE_RESULT_DEPTS)
                        AND EZPAARSE_RESULTS."datetime" < DATE_TRUNC('MONTH', CURRENT_TIMESTAMP)
                ;

                UPDATE EZPAARSE_RESULTS
                SET "user_hash" = (
                        SELECT SHA2(s."salt" || EZPAARSE_RESULTS."login")
                        FROM EZPAARSE_SALT s
                )
                WHERE "user_hash" IS NULL;

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
                                using (IDbCommand cmd = conn.CreateCommand())
                                {
                                cmd.CommandText = exeString;
                                cmd.ExecuteNonQuery();
                                conn.Close();
                                }

                        }
                }
                catch
                {
                        // original script also prints out log and checks for canary in the log for success status
                        Console.WriteLine("failed to map accounts");
                }
        }
}