// Featly.Cli — global dotnet tool. M1 placeholder; commands land in M12.

if (args.Length == 0)
{
    Console.WriteLine("Featly CLI — placeholder. Commands such as 'db migrate', 'env lock', and 'apikey generate' land in M12.");
    Console.WriteLine("See https://github.com/Featly-net/Featly/blob/main/PLAN.md for the implementation plan.");
    return 0;
}

Console.WriteLine($"Featly CLI received {args.Length} argument(s). Command parsing not implemented in M1.");
return 0;
