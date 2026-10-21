using System.Reflection;
using System.Runtime.Versioning;
using Xunit;

// Runs the supported xUnit Fact/Theory subset in browser Wasm.

var assembly = Assembly.GetEntryAssembly()!;
// Native tests read these files beside their assembly. Supply the same fixtures
// through the browser runtime's virtual filesystem before MemberData discovery.
foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("TestFixtures.")))
{
    using var input = assembly.GetManifestResourceStream(name)!;
    using var output = File.Create(Path.Combine(AppContext.BaseDirectory, name["TestFixtures.".Length..]));
    input.CopyTo(output);
}
var passed = 0;
var failed = 0;
var skipped = 0;

// Inventory floor: the shared suite is large; an empty or truncated discovery means
// the lane is broken, not that the engine is. Fail loudly rather than reporting a
// vacuous pass.
const int ExpectedTestCases = 650; // Update deliberately alongside the native discovered-case count.

foreach (var type in assembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract))
{
    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                 .Where(m => m.GetCustomAttribute<FactAttribute>() != null
                             || m.GetCustomAttribute<TheoryAttribute>() != null))
    {
        var label = $"{type.Name}.{method.Name}";
        var fact = method.GetCustomAttribute<FactAttribute>();

        // Explicit rejection: anything the driver cannot execute must fail the lane,
        // never be silently ignored. (TheoryAttribute derives from FactAttribute, so
        // Theory+InlineData already runs through the Fact path below.)
        if (method.GetCustomAttributes().Any(a => a is Xunit.Sdk.DataAttribute && a is not InlineDataAttribute && a is not MemberDataAttribute)
            || method.GetCustomAttributes().Any(a => a is Xunit.Sdk.BeforeAfterTestAttribute)
            || type.GetCustomAttributes().Any(a => a is Xunit.Sdk.BeforeAfterTestAttribute)
            || type.GetInterfaces().Any(i => i == typeof(IAsyncLifetime)
                || (i.IsGenericType && (i.GetGenericTypeDefinition() == typeof(IClassFixture<>)
                    || i.GetGenericTypeDefinition() == typeof(ICollectionFixture<>))))
            || type.GetCustomAttribute<CollectionAttribute>() != null
            || (method.ReturnType != typeof(void) && method.ReturnType != typeof(ValueTask)
                && !typeof(Task).IsAssignableFrom(method.ReturnType)))
        {
            failed++;
            Console.WriteLine($"FAIL {label}: unsupported fixture, hook, data attribute or return type");
            continue;
        }

        var dataAttributes = method.GetCustomAttributes<Xunit.Sdk.DataAttribute>().ToList();
        var inlineDatas = dataAttributes
            .SelectMany(a => a.GetData(method) ?? Array.Empty<object[]>())
            .ToList();

        if (inlineDatas.Count == 0)
        {
            if (method.GetCustomAttribute<TheoryAttribute>() != null)
            {
                failed++;
                Console.WriteLine($"FAIL {label}: Theory must supply at least one data row");
                continue;
            }
            inlineDatas.Add(Array.Empty<object>());
        }

        var nativeOnly = type.GetCustomAttributes<UnsupportedOSPlatformAttribute>()
            .Concat(method.GetCustomAttributes<UnsupportedOSPlatformAttribute>())
            .Any(a => a.PlatformName == "browser");
        var skipReason = fact?.Skip ?? (nativeOnly ? "requires native execution (processes, threads or full-depth recursive search); covered by the native suite" : null);
        if (skipReason != null)
        {
            skipped += inlineDatas.Count;
            Console.WriteLine($"SKIP {label}: {skipReason} ({inlineDatas.Count} cases)");
            continue;
        }

        var parameters = method.GetParameters();
        foreach (var rawArgs in inlineDatas)
        {
            var callArgs = new object?[rawArgs.Length];
            for (var i = 0; i < rawArgs.Length; i++)
            {
                var pt = parameters[i].ParameterType;
                callArgs[i] = rawArgs[i] is null || pt.IsInstanceOfType(rawArgs[i])
                    ? rawArgs[i]
                    : Convert.ChangeType(rawArgs[i], Nullable.GetUnderlyingType(pt) ?? pt);
            }

            var argText = callArgs.Length > 0 ? "(" + string.Join(", ", callArgs.Select(Format)) + ")" : "";
            try
            {
                var instance = method.IsStatic ? null : Activator.CreateInstance(type);
                try
                {
                    var result = method.Invoke(instance, callArgs);
                    if (result is Task task) await task;
                    else if (result is ValueTask valueTask) await valueTask;
                }
                finally
                {
                    if (instance is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync();
                    else if (instance is IDisposable disposable) disposable.Dispose();
                }
                passed++;
            }
            catch (TargetInvocationException ex)
            {
                failed++;
                Console.WriteLine($"FAIL {label}{argText}: {ex.InnerException?.GetType().Name}: {FirstLine(ex.InnerException?.Message)}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"FAIL {label}{argText}: {ex.GetType().Name}: {FirstLine(ex.Message)}");
            }
        }
    }
}

var total = passed + failed + skipped;
Console.WriteLine($"TOTAL passed={passed} failed={failed} skipped={skipped} total={total}");
if (total != ExpectedTestCases)
{
    Console.WriteLine($"FAIL inventory: expected exactly {ExpectedTestCases} tests, discovered {total}");
    return 1;
}
return failed > 0 || passed == 0 ? 1 : 0;

static string Format(object? arg) => arg switch
{
    null => "null",
    string s when s.Length > 24 => "\"" + s[..24] + "...\"",
    string s => "\"" + s + "\"",
    _ => arg.ToString() ?? "null",
};

static string FirstLine(string? message)
{
    var line = (message ?? "").Split('\n')[0];
    return line.Length > 160 ? line[..160] : line;
}
