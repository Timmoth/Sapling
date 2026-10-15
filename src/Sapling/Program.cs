using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Intrinsics.X86;
using System.Text;
using Sapling.Engine;
using Sapling.Engine.DataGen;
using Sapling.Engine.Evaluation;
using Sapling.Engine.MoveGen;
using Sapling.Engine.Search;
using Sapling.Engine.Tuning;

namespace Sapling;

public static class UciOptions
{
    public static bool IsDebug = false;
}
internal class Program
{
    private static readonly ConcurrentQueue<string> CommandQueue = new();
    private static readonly ManualResetEventSlim CommandAvailable = new(false);
    private const string QuitCommand = "quit";
    private static volatile bool hasQuit;

    private static FileStream _fileStream;
    private static StreamWriter _logWriter;

private static void Main(string[] args)
{
        Console.SetIn(new StreamReader(Console.OpenStandardInput(), Encoding.UTF8, false, 2048 * 4));

        if (args.Length > 0 && args[0] == "--version")
        {
            // Get the version from the assembly information
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            Console.WriteLine($"{version.Major}-{version.Minor}-{version.Build}");
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
        _fileStream = new FileStream(logFilePath, FileMode.Append, FileAccess.Write);
        _logWriter = new StreamWriter(_fileStream);


        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            // Log the exception or take appropriate action
            Console.WriteLine("Unhandled Exception: " + ((Exception)e.ExceptionObject).Message);
            _logWriter.WriteLine("Unhandled Exception: " + ((Exception)e.ExceptionObject).Message);
            _logWriter.Flush();
            _logWriter.Close();

        };

        // Force the static constructors to be called
        var tasks = new[]
        {
            Task.Run(() => System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(NnueWeights).TypeHandle)),
            Task.Run(() => System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(NnueExtensions).TypeHandle)),
            Task.Run(() => System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(AttackTables).TypeHandle)),
            Task.Run(() => System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(PieceValues).TypeHandle)),
            Task.Run(() => System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(RepetitionDetector).TypeHandle)),
            Task.Run(() => System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(HistoryHeuristicExtensions).TypeHandle)),
            Task.Run(() => System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(PVTable).TypeHandle)),
            Task.Run(() => System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(Zobrist).TypeHandle)),
            Task.Run(() => System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(SpsaOptions).TypeHandle))
        };
        // Wait for all tasks to complete
        Task.WaitAll(tasks);

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
        GC.WaitForPendingFinalizers();

        if (args.Contains("bench"))
        {
            Bench.Run();
            return;
        }

        UciOptions.IsDebug = args.Contains("debug");

      
        try
        {
            using UciEngine engine = new(_logWriter);

            // Start the command reading task
            _ = Task.Run(() =>
            {
                ReadCommands(engine);
            });

            // Process commands in the main loop
            ProcessCommands(engine);
        }
        catch (ObjectDisposedException) when (hasQuit)
        {
            // Shutdown may win the race immediately before a queued search starts.
        }
        catch (Exception ex)
        {
            Environment.ExitCode = 1;
            Console.Error.WriteLine(ex.Message);
            _logWriter.WriteLine("[FATAL ERROR]");
            _logWriter.WriteLine("----------");
            _logWriter.WriteLine(ex.ToString());
            _logWriter.WriteLine("----------");
        }
        finally
        {
            _logWriter.Flush();
            _logWriter.Close();
        }
    }

    private static void ReadCommands(UciEngine engine)
    {
        try
        {
            while (true)
            {
                var command = Console.ReadLine();
                if (command == null)
                {
                    // EOF is a quit: the session ends, but commands already queued are
                    // still drained by the consumer instead of being dropped.
                    break;
                }

                command = command.Trim();

                if (command.Length == 0) continue;

                if (command.Equals("stop", StringComparison.OrdinalIgnoreCase) ||
                    command.Equals("ponderhit", StringComparison.OrdinalIgnoreCase))
                {
                    // Process the stop command immediately; it must never queue behind a search.
                    engine.ReceiveCommand(command);
                    continue;
                }

                CommandQueue.Enqueue(command);
                CommandAvailable.Set(); // Signal that a command is available

                if (command.Equals(QuitCommand, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }
        }
        finally
        {
            // The consumer must never wait on a reader that has stopped, so always close the stream
            // with a quit and stop/refuse searches so a queued 'go' cannot outrun the shutdown.
            CommandQueue.Enqueue(QuitCommand);
            CommandAvailable.Set();
            engine.Abort();
        }
    }

    private static void ProcessCommands(UciEngine engine)
    {
        while (true)
        {
            CommandAvailable.Wait(); // Wait until a command is available
            CommandAvailable.Reset(); // Reset the event for the next wait

            while (CommandQueue.TryDequeue(out var command))
            {
                if (command.Equals(QuitCommand, StringComparison.OrdinalIgnoreCase))
                {
                    // Everything queued before quit has been answered; now the session ends.
                    hasQuit = true;
                    return;
                }

                engine.ReceiveCommand(command);
            }
        }
    }
}
