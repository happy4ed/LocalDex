using System.Reflection;
using Xunit;

// No sockets or test-host process. Execute the same xUnit Fact/InlineData tests.
var assembly = typeof(DexManager.Tests.DexFreeformJournalTests).Assembly;
int passed = 0, failed = 0, skipped = 0;
foreach (var type in assembly.GetTypes().OrderBy(t => t.FullName))
foreach (var method in type.GetMethods().OrderBy(m => m.Name))
{
    var fact = method.GetCustomAttribute<FactAttribute>();
    if (fact == null) continue;
    if (fact.Skip != null) { Console.WriteLine("SKIP " + type.Name + "." + method.Name + ": " + fact.Skip); skipped++; continue; }
    var data = method.GetCustomAttributes<InlineDataAttribute>().SelectMany(a => a.GetData(method)).ToArray();
    if (fact is TheoryAttribute && data.Length == 0) throw new InvalidOperationException("Unsupported theory data: " + method.Name);
    if (data.Length == 0) data = new[] { Array.Empty<object>() };
    foreach (var row in data)
    {
        object instance = null;
        var name = type.Name + "." + method.Name + "(" + string.Join(",", row) + ")";
        try
        {
            instance = Activator.CreateInstance(type);
            if (method.Invoke(instance, row) is Task task) await task;
            Console.WriteLine("PASS " + name); passed++;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + name + ": " + (ex is TargetInvocationException ? ex.InnerException : ex)); failed++;
        }
        finally { (instance as IDisposable)?.Dispose(); }
    }
}
Console.WriteLine($"Tests: {passed} passed, {failed} failed, {skipped} skipped.");
return failed == 0 ? 0 : 1;
