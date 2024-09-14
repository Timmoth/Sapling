using System.Reflection;
using System.Runtime.Intrinsics.X86;

namespace Sapling;

internal class Program
{
    private static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--version")
        {
            // Get the version from the assembly information
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            Console.WriteLine($"{version.Major}-{version.Minor}-{version.Build}");
            return;
        }

#if AVX512
            if (!Avx512BW.IsSupported)
            {
                Console.WriteLine("[Error] Avx512BW is not supported on this system");
                return;
            }
#else
        if (!Avx2.IsSupported)
            {
                Console.WriteLine("[Error] Avx2 is not supported on this system");
                return;
            }
        #endif

        if (!Bmi1.IsSupported)
        {
            Console.WriteLine("[Error] Bmi1 is not supported on this system");
            return;
        }

        if (!Bmi2.IsSupported)
        {
            Console.WriteLine("[Error] Bmi2 is not supported on this system");
            return;
        }

        if (!Popcnt.IsSupported)
        {
            Console.WriteLine("[Error] Popcnt is not supported on this system");
            return;
        }

        if (!Sse.IsSupported)
        {
            Console.WriteLine("[Error] Sse is not supported on this system");
            return;
        }

        var logDirectory = Path.Combine(Environment.CurrentDirectory, "logs");
        if (!Directory.Exists(logDirectory))
        {
            Directory.CreateDirectory(logDirectory);
        }

        var fileName = (DateTime.Now.ToString("g") + Guid.NewGuid()).Replace("/", "-").Replace(" ", "_")
            .Replace(":", "-");
        var logFilePath = Path.Combine(logDirectory, $"{fileName}.txt");
        using var fileStream = new FileStream(logFilePath, FileMode.Append, FileAccess.Write);
        using var logWriter = new StreamWriter(fileStream);

        try
        {
            UciEngine engine = new(logWriter);

            string? command;
            while ((command = Console.ReadLine()) != "quit")
            {
                if (command == null)
                {
                    continue;
                }

                engine.ReceiveCommand(command);
            }
        }
        catch (Exception ex)
        {
            logWriter.WriteAsync("[FATAL ERROR]");
            logWriter.WriteAsync("----------");
            logWriter.WriteAsync(ex.ToString());
            logWriter.WriteAsync("----------");
        }

        logWriter.Flush();
    }
}