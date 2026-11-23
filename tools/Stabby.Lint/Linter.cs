using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.RegularExpressions;

namespace Stabby.Lint;

public static class Linter
{
    public const string AllowFile = "lint-allow.txt";
    static readonly string[] Roots = { "src", "tools", "tests", "unity/Assets" };
    static readonly string[] Skip = { "/bin/", "/obj/", "/artifacts/" };
    const string GameDir = "unity/Assets/Game/";
    const string UnityDir = "unity/Assets/";
    public const string KitShader = "unity/Assets/Game/Resources/Object.shader";
    const string KitFunction = "float3 Built(";
    static readonly Regex Colour = new(@"float3\(\s*[0-9.]+f?\s*,\s*[0-9.]+f?\s*,\s*[0-9.]+f?\s*\)");
    static readonly Regex DotWeights = new(@"dot\([^()]*,\s*$");
    static readonly string[] BlockingMembers = { "Wait", "WaitAll", "WaitAny", "GetResult", "Sleep", "Complete" };
    static readonly string[] NativeMembers = { "linear", "gamma", "GammaToLinearSpace", "LinearToGammaSpace", "PerlinNoise" };
    static readonly string[] WorldDetailFiles = { "src/Stabby.World/Detail.cs", "src/Stabby.World/FoliageDefs.cs", "src/Stabby.World/ObjectDefs.cs" };
    static readonly string[] DistanceCalls = { "distance", "distancesq", "Distance", "RectDistance", "length", "lengthsq", "magnitude", "sqrMagnitude" };

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
            if (IsDetailCode(file))
                foreach (var p in MetreProblems(file, root, allow, used)) problems.Add(p);
        }
        foreach (var file in ShaderFiles(repo))
            problems.AddRange(ShaderProblems(file, File.ReadAllText(Path.Combine(repo, file)), allow, used));
        var kit = Path.Combine(repo, KitShader);
        if (File.Exists(kit)) problems.AddRange(ColourProblems(KitShader, File.ReadAllText(kit)));
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

    public static List<string> ColourProblems(string file, string text)
    {
        var problems = new List<string>();
        int start = text.IndexOf(KitFunction, StringComparison.Ordinal);
        if (start < 0) { problems.Add($"{file}: {KitFunction.TrimEnd('(')} not found"); return problems; }
        int open = text.IndexOf('{', start), depth = 0, end = open;
        for (; end < text.Length; end++)
        {
            if (text[end] == '{') depth++;
            else if (text[end] == '}' && --depth == 0) break;
        }
        foreach (Match m in Colour.Matches(text[..end], open))
        {
            var before = text[..m.Index].TrimEnd();
            if (before.EndsWith('*') || DotWeights.IsMatch(before)) continue;
            int line = text.AsSpan(0, m.Index).Count('\n') + 1;
            problems.Add($"{file}:{line}: {m.Value} is a built-in colour; name a library material in content/materials.json kit");
        }
        return problems;
    }

    public static IEnumerable<string> ShaderFiles(string repo)
    {
        var dir = Path.Combine(repo, UnityDir);
        if (!Directory.Exists(dir)) return Enumerable.Empty<string>();
        return Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".shader", StringComparison.Ordinal) || f.EndsWith(".hlsl", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(repo, f).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(f => !ShaderExempt.Contains(Path.GetFileName(f)))
            .OrderBy(f => f, StringComparer.Ordinal);
    }

    static readonly string[] ShaderExempt = { "Organic.hlsl", "Water.shader", "FallMist.shader", "Look.hlsl", "TerrainStitch.hlsl", "Contact.hlsl", "Wind.hlsl" };
    static readonly Regex NoiseCall = new(@"\b(OrgFade|OrgL2|OrgL3|OrgLN2|OrgLN3|OrgFbm2|OrgFbm3|OrgNoise2|OrgNoise3|OrgRL2|OrgRL3|OrgRP2|OrgRP3|OrgVRL3|OrgValue3)\(");
    static readonly Regex PatternCall = new(@"\b(floor|frac|step|sin|cos)\(");
    static readonly Regex Scaled = new(@"\*\s*(\d+(?:\.\d+)?)(?![\w.])");
    static readonly Regex Number = new(@"^\s*(\d+(?:\.\d+)?)\s*$");
    static readonly Regex Position = new(@"\b(wp|p|q|l|pos|positionWS|worldPos|lp|pw|qa|wa)\b");
    static readonly Regex Function = new(@"^\s*(?:inline\s+|static\s+)?(?:float|half|int|uint|bool|void|fixed)[1-4]?(?:x[1-4])?\s+(\w+)\s*\(", RegexOptions.Multiline);
    static readonly Regex PassStart = new(@"\bPass\s*\{");
    const double PrivateFrequency = 2.5;

    public static List<string> ShaderProblems(string file, string text, Dictionary<string, string> allow) =>
        ShaderProblems(file, text, allow, new HashSet<string>(StringComparer.Ordinal)).ToList();

    static IEnumerable<string> ShaderProblems(string file, string text, Dictionary<string, string> allow, HashSet<string> used)
    {
        text = DropShadowPasses(text);
        var funcs = Function.Matches(text).Select(m => (m.Index, m.Groups[1].Value)).ToList();
        string Key(int at) => $"{file}:{funcs.LastOrDefault(f => f.Index <= at).Value ?? ""}";
        int Line(int at) => text.AsSpan(0, at).Count('\n') + 1;
        foreach (Match m in NoiseCall.Matches(text))
        {
            var args = SplitArgs(text, m.Index + m.Length);
            if (args.Count == 0) continue;
            string name = m.Groups[1].Value;
            bool hit = name switch
            {
                "OrgFade" => IsHigh(args[0]),
                "OrgNoise2" or "OrgNoise3" or "OrgValue3" => Scaled.Matches(args[0]).Any(s => IsHigh(s.Groups[1].Value)),
                _ => args.Count > 1 && IsHigh(args[1]),
            };
            if (!hit) continue;
            string key = Key(m.Index);
            if (allow.ContainsKey(key)) { used.Add(key); continue; }
            yield return $"{file}:{Line(m.Index)}: {name} uses a private detail frequency; use OrgF(k)/OrgRungW(k) from Organic.hlsl or list {key} in {AllowFile} with a reason";
        }
        foreach (Match m in PatternCall.Matches(text))
        {
            var args = SplitArgs(text, m.Index + m.Length);
            string all = string.Join(",", args);
            if (!Position.IsMatch(all) || all.Contains("Org", StringComparison.Ordinal)) continue;
            string key = Key(m.Index);
            if (allow.ContainsKey(key)) { used.Add(key); continue; }
            yield return $"{file}:{Line(m.Index)}: {m.Groups[1].Value} on a position draws a clean line; warp it with Organic.hlsl noise or list {key} in {AllowFile} with a reason";
        }
    }

    static bool IsHigh(string arg)
    {
        var m = Number.Match(arg);
        return m.Success && double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) >= PrivateFrequency;
    }

    static string DropShadowPasses(string text)
    {
        int at;
        while ((at = text.IndexOf("Name \"ShadowCaster\"", StringComparison.Ordinal)) >= 0)
        {
            var next = PassStart.Match(text, at);
            int end = next.Success ? next.Index : text.Length;
            text = text[..at] + new string('\n', text.AsSpan(at, end - at).Count('\n')) + text[end..];
        }
        return text;
    }

    static List<string> SplitArgs(string text, int start)
    {
        var args = new List<string>();
        var cur = new System.Text.StringBuilder();
        int depth = 0;
        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '(' || c == '[') depth++;
            else if (c == ')' || c == ']')
            {
                if (depth == 0) { args.Add(cur.ToString()); return args; }
                depth--;
            }
            if (c == ',' && depth == 0) { args.Add(cur.ToString()); cur.Clear(); continue; }
            cur.Append(c);
        }
        return args;
    }

    public static bool IsDetailCode(string file) =>
        WorldDetailFiles.Contains(file) || file.StartsWith(GameDir, StringComparison.Ordinal) && !file.Contains("/Editor/", StringComparison.Ordinal);

    public static List<string> MetreProblems(string file, string text, Dictionary<string, string> allow) =>
        MetreProblems(file, Parse(text).GetRoot(), allow, new HashSet<string>(StringComparer.Ordinal)).ToList();

    static IEnumerable<string> MetreProblems(string file, SyntaxNode root, Dictionary<string, string> allow, HashSet<string> used)
    {
        foreach (var cmp in root.DescendantNodes().OfType<BinaryExpressionSyntax>())
        {
            if (!cmp.IsKind(SyntaxKind.LessThanExpression) && !cmp.IsKind(SyntaxKind.LessThanOrEqualExpression) &&
                !cmp.IsKind(SyntaxKind.GreaterThanExpression) && !cmp.IsKind(SyntaxKind.GreaterThanOrEqualExpression)) continue;
            ExpressionSyntax limit;
            if (IsDistance(cmp.Left)) limit = cmp.Right;
            else if (IsDistance(cmp.Right)) limit = cmp.Left;
            else continue;
            if (!IsMetres(limit)) continue;
            string owner = cmp.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text ?? "";
            string member = cmp.Ancestors().OfType<MemberDeclarationSyntax>().Select(MemberName).FirstOrDefault(n => n.Length > 0) ?? "";
            string key = $"{owner}.{member}";
            if (allow.ContainsKey(key)) { used.Add(key); continue; }
            int line = cmp.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            yield return $"{file}:{line}: {cmp} compares a distance with metres; use Detail.ScreenSize/Reach with a pixel threshold in content/ or list {key} in {AllowFile} with a reason";
        }
    }

    static string MemberName(MemberDeclarationSyntax m) => m switch
    {
        MethodDeclarationSyntax x => x.Identifier.Text,
        PropertyDeclarationSyntax x => x.Identifier.Text,
        ConstructorDeclarationSyntax x => x.Identifier.Text,
        FieldDeclarationSyntax x => x.Declaration.Variables.First().Identifier.Text,
        _ => "",
    };

    static bool IsDistance(ExpressionSyntax e) =>
        !e.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().Any(c => CalledName(c.Expression) == "ScreenSize") &&
        e.DescendantNodesAndSelf().Any(n => n switch
        {
            InvocationExpressionSyntax call => DistanceCalls.Contains(CalledName(call.Expression)),
            MemberAccessExpressionSyntax acc => acc.Name.Identifier.Text is "magnitude" or "sqrMagnitude",
            IdentifierNameSyntax id => IsDistanceName(id.Identifier.Text),
            _ => false,
        });

    static string CalledName(ExpressionSyntax e) => e switch
    {
        MemberAccessExpressionSyntax m => m.Name.Identifier.Text,
        IdentifierNameSyntax i => i.Identifier.Text,
        _ => "",
    };

    static bool IsDistanceName(string n) =>
        n is "dist" or "distance" || n.EndsWith("Distance", StringComparison.Ordinal) || n.EndsWith("Dist", StringComparison.Ordinal);

    static bool IsMetres(ExpressionSyntax e) =>
        e.DescendantNodesAndSelf().Any(n => n switch
        {
            LiteralExpressionSyntax lit when lit.IsKind(SyntaxKind.NumericLiteralExpression) => !IsFactor(lit, e) && Math.Abs(Convert.ToDouble(lit.Token.Value, System.Globalization.CultureInfo.InvariantCulture)) >= 1,
            IdentifierNameSyntax id => IsMetreName(id.Identifier.Text),
            _ => false,
        });

    static bool IsFactor(SyntaxNode lit, SyntaxNode top) =>
        lit.Ancestors().TakeWhile(n => n != top.Parent).Any(n => n.IsKind(SyntaxKind.MultiplyExpression) || n.IsKind(SyntaxKind.DivideExpression));

    static bool IsMetreName(string n) =>
        n.Length > 1 && (n.EndsWith("Meters", StringComparison.Ordinal) || n.EndsWith("Metres", StringComparison.Ordinal) ||
        n[^1] == 'M' && char.IsLower(n[^2]));

    static bool IsGuarded(MemberAccessExpressionSyntax acc)
    {
        var body = acc.Ancestors().FirstOrDefault(a => a is BaseMethodDeclarationSyntax or AccessorDeclarationSyntax or LocalFunctionStatementSyntax);
        if (body == null) return false;
        string target = acc.Expression.ToString();
        return body.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
            .Any(x => x.Name.Identifier.Text is "IsCompleted" or "IsCompletedSuccessfully" && x.Expression.ToString() == target && x.SpanStart < acc.SpanStart);
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
