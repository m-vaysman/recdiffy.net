using RecDiffy;
using RecDiffy.Capacity;

if (CapacityOptions.IsHelpRequest(args))
{
    Console.WriteLine(CapacityOptions.Usage);
    return 0;
}

try
{
    return CapacityRunner.Execute(CapacityOptions.Parse(args));
}
catch (CapacityUsageException error)
{
    Console.Error.WriteLine(error.Message);
    return 2;
}
catch (ProfileCompileException error)
{
    Console.Error.WriteLine("The capacity profile does not compile:");
    foreach (var problem in error.Errors)
        Console.Error.WriteLine($"  {problem.Code}: {problem.Message}");
    return 1;
}
