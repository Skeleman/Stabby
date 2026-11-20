using Stabby.Lint;

var repo = Linter.FindRepo(Directory.GetCurrentDirectory());
if (args.Contains("--strip"))
{
    Console.WriteLine($"removed {Linter.Strip(repo)} comments");
    return 0;
}
var problems = Linter.Check(repo);
foreach (var p in problems) Console.WriteLine(p);
Console.WriteLine(problems.Count == 0 ? "lint clean" : $"{problems.Count} lint problems");
return problems.Count == 0 ? 0 : 1;
