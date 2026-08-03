
using Amazon.S3.Internal;

public class CLI
{
    private static void usage()
    {
        Console.WriteLine("Usage: ./<name> <process|postprocess>");
    }
    public static void Main(string[] args)
    {
        if (args.Length() != 2)
        {
            CLI.usage();
        }
        switch (args[1])
        {
            case "process":
                ProcessEzpaarse.Main();
                break;
            case "postprocess":
                PostProcess.Main();
                break;
            default:
                usage();
        }
        
    }

}