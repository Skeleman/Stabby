using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Stabby.Lint;

public static class Linter
{
    public const string AllowFile = "lint-allow.txt";
    static readonly string[] Roots = { "src", "tools", "tests", "unity/Assets" };
    static readonly string[] Skip = { "/bin/", "/obj/", "/artifacts/" };
    const string GameDir = "unity/Assets/Game/";
    const string UnityDir = "unity/Assets/";
    static readonly string[] BlockingMembers = { "Wait", "WaitAll", "WaitAny", "GetResult", "Sleep", "Complete" };
    static readonly string[] NativeMembers = { "linear", "gamma", "GammaToLinearSpace", "LinearToGammaSpace", "PerlinNoise" };

    public static IEnumerable<string> SourceFiles(string repo) =>
        Roots.Select(r => Path.Combine(repo, r)).Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Select(f => Path.GetRelativePath(repo, f).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(f => !Skip.Any(s => ("/" + f).Contains(s, StringComparison.Ordinal)))
            .OrderBy(f => f, StringComparer.Ordinal);

    static SyntaxTree Parse(string text) =>
        CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.Parse));

    static IEnumerable<SyntaxTrivia> Comments(SyntaxNode root) =>
        root.DescendantTrivia(descendIntoTrivia: true).Where(t =>
            t.IsKind(SyntaxKind.SingleLineCommentTrivia) || t.IsKind(SyntaxKind.MultiLineCommentTrivia) ||
            t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) || t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));

    public static Dictionary<string, string> ReadAllow(string repo, List<string> problems)
    {
        var allow = new Dictionary<string, string>(StringComparer.Ordinal);
        var path = Path.Combine(repo, AllowFile);
        if (!File.Exists(path)) return allow;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            int sp = line.IndexOf(' ', StringComparison.Ordinal);
            if (sp < 0) { problems.Add($"{AllowFile}: '{line}' has no reason"); continue; }
            allow[line[..sp]] = line[(sp + 1)..].Trim();
        }
        return allow;
    }

    public static List<string> Check(string repo)
    {
        var problems = new List<string>();
        var allow = ReadAllow(repo, problems);
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in SourceFiles(repo))
        {
            var tree = Parse(File.ReadAllText(Path.Combine(repo, file)));
            var root = tree.GetRoot();
            foreach (var c in Comments(root))
                problems.Add($"{file}:{c.GetLocation().GetLineSpan().StartLinePosition.Line + 1}: comment (put reasons in the commit message)");
            if (file.StartsWith(GameDir, StringComparison.Ordinal) && !file.Contains("/Editor/", StringComparison.Ordinal))
                foreach (var p in EcsProblems(file, root, allow, used)) problems.Add(p);
            if (file.StartsWith(UnityDir, StringComparison.Ordinal) && !file.Contains("/Editor/", StringComparison.Ordinal))
                foreach (var p in BlockingProblems(file, root, allow, used)) problems.Add(p);
        }
        foreach (var key in allow.Keys.Where(k => !used.Contains(k)))
            problems.Add($"{AllowFile}: '{key}' matches nothing; remove it");
        return problems;
    }

    static IEnumerable<string> EcsProblems(string file, SyntaxNode root, Dictionary<string, string> allow, HashSet<string> used)
    {
        foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            var bases = type.BaseList?.Types.Select(b => b.Type.ToString()).ToList() ?? new List<string>();
            string name = type.Identifier.Text;
            int line = type.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            bool allowed = allow.ContainsKey(name);
            if (allowed) used.Add(name);
            if (type is ClassDeclarationSyntax && bases.Any(b => b is "SystemBase" or "MonoBehaviour") && !allowed)
                yield return $"{file}:{line}: {name} is managed; make it a Burst ISystem or list it in {AllowFile} with a reason";
            if (type is StructDeclarationSyntax && bases.Contains("ISystem") && !allowed &&
                !type.AttributeLists.SelectMany(a => a.Attributes).Any(a => a.Name.ToString() is "BurstCompile" or "BurstCompileAttribute"))
                yield return $"{file}:{line}: ISystem {name} lacks [BurstCompile]";
        }
    }

    static IEnumerable<string> BlockingProblems(string file, SyntaxNode root, Dictionary<string, string> allow, HashSet<string> used)
    {
        foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (call.Expression is not MemberAccessExpressionSyntax m || !BlockingMembers.Contains(m.Name.Identifier.Text)) continue;
            string owner = call.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text ?? "";
            string key = $"{owner}.{m.Name.Identifier.Text}";
            if (allow.ContainsKey(key)) { used.Add(key); continue; }
            int line = call.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            yield return $"{file}:{line}: {m} blocks the main thread; poll IsCompleted instead or list {key} in {AllowFile} with a reason";
        }
        foreach (var acc in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            if (acc.Name.Identifier.Text != "Result" || acc.Parent is InvocationExpressionSyntax) continue;
            if (IsGuarded(acc)) continue;
            string owner = acc.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text ?? "";
            string key = $"{owner}.Result";
            if (allow.ContainsKey(key)) { used.Add(key); continue; }
            int line = acc.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            yield return $"{file}:{line}: {acc} may block; read Result only after an IsCompleted check in the same method";
        }
        foreach (var acc in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            string name = acc.Name.Identifier.Text;
            if (!NativeMembers.Contains(name)) continue;
            string owner = acc.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text ?? "";
            string key = $"{owner}.{name}";
            if (allow.ContainsKey(key)) { used.Add(key); continue; }
            int line = acc.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            yield return $"{file}:{line}: {acc} calls native engine code, unsafe on worker threads; use a managed version or list {key} in {AllowFile} with a reason";
        }
    }

    static bool IsGuarded(MemberAccessExpressionSyntax acc)
    {
        var body = acc.Ancestors().FirstOrDefault(a => a is BaseMethodDeclarationSyntax or AccessorDeclarationSyntax or LocalFunctionStatementSyntax);
        if (body == null) return false;
        string target = acc.Expression.ToString();
        return body.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
            .Any(x => x.Name.Identifier.Text == "IsCompleted" && x.Expression.ToString() == target && x.SpanStart < acc.SpanStart);
    }

    public static int Strip(string repo)
    {
        int removed = 0;
        foreach (var file in SourceFiles(repo))
        {
            var path = Path.Combine(repo, file);
            var text = File.ReadAllText(path);
            var tree = Parse(text);
            var lines = tree.GetText().Lines;
            var cuts = new List<(int Start, int End)>();
            foreach (var c in Comments(tree.GetRoot()))
            {
                var span = c.FullSpan;
                int first = lines.GetLineFromPosition(span.Start).LineNumber;
                int last = lines.GetLineFromPosition(Math.Max(span.Start, span.End - 1)).LineNumber;
                var lineStart = lines[first].Start;
                var lineEnd = lines[last].End;
                bool alone = text[lineStart..span.Start].Trim().Length == 0 && text[Math.Min(span.End, lineEnd)..lineEnd].Trim().Length == 0;
                if (alone) cuts.Add((lineStart, lines[last].EndIncludingLineBreak));
                else
                {
                    int s = span.Start;
                    while (s > lineStart && char.IsWhiteSpace(text[s - 1])) s--;
                    cuts.Add((s, Math.Min(span.End, lineEnd)));
                }
                removed++;
            }
            if (cuts.Count == 0) continue;
            var merged = cuts.OrderBy(c => c.Start).ToList();
            var sb = new System.Text.StringBuilder();
            int pos = 0;
            foreach (var (s, e) in merged)
            {
                if (e <= pos) continue;
                sb.Append(text, pos, Math.Max(0, s - pos));
                pos = Math.Max(pos, e);
            }
            sb.Append(text, pos, text.Length - pos);
            File.WriteAllText(path, sb.ToString());
        }
        return removed;
    }

    public static string FindRepo(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Stabby.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Stabby.slnx not found above " + start);
    }
}
