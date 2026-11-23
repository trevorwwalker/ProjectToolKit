using StartupBench;

// Writes a pool of generated projects for the scheduler baseline (1041-B) and prints its pool file and the project
// names and folders, one per line:   PoolMaker <root-folder> <projects> <cards> <port>
// The pool has <projects> projects of <cards> cards and one more of <cards> cards (PoolData adds a "big" one).
if (args.Length < 4)
{
    Console.Error.WriteLine("usage: PoolMaker <root-folder> <projects> <cards> <port>");
    return 2;
}

var pool = PoolData.Create(args[0], int.Parse(args[1]) - 1, int.Parse(args[2]), int.Parse(args[2]), int.Parse(args[3]));
Console.WriteLine("pool\t" + pool.PoolFile);
foreach (var name in pool.ProjectNames)
{
    Console.WriteLine("project\t" + name + "\t" + Path.Combine(args[0], name));
}

return 0;
