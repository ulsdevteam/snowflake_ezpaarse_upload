

public class CLI
{
    private static void usage()
    {
        Console.WriteLine("Usage: ./<name> <process|postprocess>");
    }
    public static void Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.WriteLine("Insufficient Number of arguments provided");
            CLI.usage();
            Environment.Exit(0);
        }
        switch (args[0])
        {
            case "process":
                ProcessEzpaarse.Main();
                break;
            case "postprocess":
                PostProcess.Main();
                break;
            default:
                usage();
                break;
        }
        
    }

}