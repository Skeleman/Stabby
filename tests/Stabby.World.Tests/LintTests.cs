using Stabby.Lint;

namespace Stabby.World.Tests;

public class LintTests
{
    [Fact]
    public void RepoPassesLint() => Assert.Empty(Linter.Check(Linter.FindRepo(AppContext.BaseDirectory)));

    const string GameFile = "unity/Assets/Game/Sample.cs";

    static List<string> Metres(string body, Dictionary<string, string>? allow = null) =>
        Linter.MetreProblems(GameFile, "class Grass { float nearM = 5f, reach; int defs; void Draw(float3 a, float3 b, float d, float dist) { " + body + " } }",
            allow ?? new Dictionary<string, string>());

    [Theory]
    [InlineData("if (math.distance(a, b) < 80f) return;")]
    [InlineData("bool near = dist <= nearM;")]
    [InlineData("bool far = 25 < Vector3.Distance(a, b);")]
    [InlineData("bool on = (a - b).magnitude > 2.5f;")]
    public void FlagsMetreDistanceThresholds(string body) => Assert.Single(Metres(body));

    [Theory]
    [InlineData("if (math.distancesq(a, b) < 0.0001f) return;")]
    [InlineData("for (int i = 0; i < defs; i++) { }")]
    [InlineData("bool far = d > 80f;")]
    [InlineData("bool on = math.distance(a, b) < reach * 1.2f;")]
    [InlineData("bool on = Detail.ScreenSize(1f, math.distance(a, b), 935f) > 86.4f;")]
    public void IgnoresOtherComparisons(string body) => Assert.Empty(Metres(body));

    [Fact]
    public void AllowListSilencesAMember() =>
        Assert.Empty(Metres("if (math.distance(a, b) < 80f) return;", new Dictionary<string, string> { ["Grass.Draw"] = "reason" }));

    [Fact]
    public void OnlyDetailCodeIsChecked()
    {
        Assert.True(Linter.IsDetailCode(GameFile));
        Assert.True(Linter.IsDetailCode("src/Stabby.World/Detail.cs"));
        Assert.False(Linter.IsDetailCode("src/Stabby.World/Rivers.cs"));
        Assert.False(Linter.IsDetailCode("unity/Assets/Game/Editor/GameCapture.cs"));
    }

    static List<string> Colours(string body) =>
        Linter.ColourProblems(Linter.KitShader, "float3 Other() { return float3(0.1, 0.2, 0.3); }\nfloat3 Built(float3 a)\n{\n" + body + "\n}\nfloat3 After() { return float3(0.4, 0.5, 0.6); }");

    [Theory]
    [InlineData("return ToLinear(float3(0.42, 0.37, 0.30));")]
    [InlineData("float3 c = a.x < 0.5 ? float3(0.5, 0.4, 0.3) : a; return c;")]
    [InlineData("return lerp(a, float3(0.2f, 0.17f, 0.14f), 0.5);")]
    public void FlagsBuiltInColours(string body) => Assert.Single(Colours(body));

    [Theory]
    [InlineData("return ToLinear(_StabbyKitSrgb[3].rgb) * float3(0.95, 1.05, 0.9);")]
    [InlineData("return lerp(a, dot(a, float3(0.2126, 0.7152, 0.0722)), 0.2);")]
    [InlineData("if (a.x > 0) { a = a * float3(0.5, 0.47, 0.43); } return a;")]
    public void IgnoresKitColoursAndWeights(string body) => Assert.Empty(Colours(body));

    [Fact]
    public void MissingKitFunctionIsAProblem() => Assert.Single(Linter.ColourProblems(Linter.KitShader, "float3 Other() { return 0; }"));

    static List<string> Shader(string body, Dictionary<string, string>? allow = null) =>
        Linter.ShaderProblems("S.shader", "float3 Surf(float3 wp, float fp)\n{\n" + body + "\n}\n", allow ?? new Dictionary<string, string>());

    [Theory]
    [InlineData("float g = OrgNoise3(wp * 29 + 3.1, int3(0, 0, 0)).x;")]
    [InlineData("float w = OrgFade(73, fp);")]
    [InlineData("float4 n = OrgLN3(wp, 20.8, 1.7, fp);")]
    [InlineData("float s = frac(wp.x * 4);")]
    [InlineData("float s = sin(wp.y * 12);")]
    [InlineData("float s = floor(wp.y);")]
    public void FlagsPrivateDetail(string body) => Assert.Single(Shader(body));

    [Theory]
    [InlineData("float4 g = OrgRL3(wp, OrgF(2), 3.1, 2);")]
    [InlineData("float w = OrgRungW(3, fp);")]
    [InlineData("float m = OrgNoise3(wp * 0.4 + 2, int3(0, 0, 0)).x;")]
    [InlineData("float s = sin(wp.y * 12 + OrgNoise3(wp, int3(0, 0, 0)).x * 3);")]
    [InlineData("float a = sin(_Time.y * 2);")]
    public void IgnoresSharedLadderAndMacro(string body) => Assert.Empty(Shader(body));

    [Fact]
    public void AllowListSilencesAFunction() =>
        Assert.Empty(Shader("float s = frac(wp.x * 4);", new Dictionary<string, string> { ["S.shader:Surf"] = "planks" }));

    [Fact]
    public void ShadowPassIsSkipped() =>
        Assert.Empty(Linter.ShaderProblems("S.shader", "Pass { Name \"ShadowCaster\"\nfloat3 F(float3 wp) { return frac(wp); }\n}", new Dictionary<string, string>()));

    static List<string> Ecs(string code, Dictionary<string, string>? allow = null) =>
        Linter.EcsProblems(GameFile, code, allow ?? new Dictionary<string, string>());

    [Theory]
    [InlineData("struct Tag : IComponentData { public string Name; }")]
    [InlineData("struct Tag : IBufferElementData { public int[] Values; }")]
    [InlineData("struct Tag : Unity.Entities.ICleanupComponentData { public List<int> Items; }")]
    [InlineData("class Kit { } struct Tag : IComponentData { public Kit Value; }")]
    [InlineData("struct Tag : IComponentData { public Mesh Shape { get; set; } }")]
    [InlineData("class Tag : IComponentData { public int Value; }")]
    public void FlagsManagedComponents(string code) => Assert.Single(Ecs(code));

    [Theory]
    [InlineData("struct Tag : IComponentData { public int A; public float3 B; public Entity C; public FixedString64Bytes D; }")]
    [InlineData("struct Tag : IComponentData { public UnityObjectRef<Mesh> Shape; public BlobAssetReference<Kit> Blob; public static string Name; }")]
    [InlineData("struct Tag : IComponentData { public string Name => \"x\"; public const string Key = \"k\"; }")]
    [InlineData("struct Plain { public string Name; }")]
    public void IgnoresUnmanagedComponents(string code) => Assert.Empty(Ecs(code));

    [Fact]
    public void AllowListSilencesAManagedComponent() =>
        Assert.Empty(Ecs("class Tag : IComponentData { public int Value; }", new Dictionary<string, string> { ["Tag"] = "reason" }));

    const string Copying = "[BurstCompile] partial struct Sys : ISystem { EntityQuery q; [BurstCompile] public void OnUpdate(ref SystemState s) { Step(); } void Step() { var a = q.ToComponentDataArray<Tag>(Allocator.Temp); } }";

    [Theory]
    [InlineData("[BurstCompile] partial struct Sys : ISystem { EntityQuery q; [BurstCompile] public void OnUpdate(ref SystemState s) { var a = q.ToEntityArray(Allocator.Temp); } }")]
    [InlineData(Copying)]
    [InlineData("[BurstCompile] partial struct Sys : ISystem { EntityQuery q; [BurstCompile] public void OnUpdate(ref SystemState s) { this.Step(); } void Step() { var a = q.ToComponentDataListAsync<Tag>(Allocator.Temp, out var h); } }")]
    public void FlagsRecurringCopies(string code) => Assert.Single(Ecs(code));

    [Theory]
    [InlineData("[BurstCompile] partial struct Sys : ISystem { EntityQuery q; [BurstCompile] public void OnCreate(ref SystemState s) { var a = q.ToEntityArray(Allocator.Temp); } [BurstCompile] public void OnUpdate(ref SystemState s) { } }")]
    [InlineData("[BurstCompile] partial struct Sys : ISystem { EntityQuery q; [BurstCompile] public void OnUpdate(ref SystemState s) { } void Unused() { var a = q.ToEntityArray(Allocator.Temp); } }")]
    [InlineData("static class Tools { static void OnUpdate(EntityQuery q) { foreach (var t in SystemAPI.Query<RefRO<Tag>>()) { } } }")]
    public void IgnoresCopiesOutsideUpdate(string code) => Assert.Empty(Ecs(code));

    [Fact]
    public void AllowListSilencesACopy() =>
        Assert.Empty(Ecs(Copying, new Dictionary<string, string> { ["Sys.ToComponentDataArray"] = "reason" }));

    const string Structural = "[BurstCompile] partial struct Sys : ISystem { [BurstCompile] public void OnUpdate(ref SystemState s) { foreach (var (t, e) in SystemAPI.Query<RefRO<Tag>>().WithEntityAccess()) s.EntityManager.DestroyEntity(e); } }";

    [Theory]
    [InlineData(Structural)]
    [InlineData("[BurstCompile] partial struct Sys : ISystem { [BurstCompile] public void OnUpdate(ref SystemState s) { foreach (var (t, e) in SystemAPI.Query<RefRO<Tag>>().WithEntityAccess()) { if (t.ValueRO.A > 0) s.EntityManager.AddComponent<Gone>(e); } } }")]
    [InlineData("partial class Sys : SystemBase { protected override void OnUpdate() { foreach (var (t, e) in SystemAPI.Query<RefRO<Tag>>().WithEntityAccess()) EntityManager.Instantiate(e); } }")]
    [InlineData("partial class Sys : SystemBase { protected override void OnUpdate() { Entities.ForEach((Entity e, in Tag t) => { EntityManager.RemoveComponent<Tag>(e); }).Run(); } }")]
    public void FlagsStructuralChangesInEntityLoops(string code) =>
        Assert.Single(Ecs(code, new Dictionary<string, string> { ["Sys"] = "registers meshes with Entities Graphics" }));

    [Theory]
    [InlineData("[BurstCompile] partial struct Sys : ISystem { [BurstCompile] public void OnUpdate(ref SystemState s) { var ecb = new EntityCommandBuffer(Allocator.Temp); foreach (var (t, e) in SystemAPI.Query<RefRO<Tag>>().WithEntityAccess()) ecb.DestroyEntity(e); ecb.Playback(s.EntityManager); } }")]
    [InlineData("[BurstCompile] partial struct Sys : ISystem { [BurstCompile] public void OnUpdate(ref SystemState s) { foreach (var (t, e) in SystemAPI.Query<RefRO<Tag>>().WithEntityAccess()) { } s.EntityManager.CreateEntity(); } }")]
    [InlineData("partial class Sys : SystemBase { protected override void OnUpdate() { Entities.WithStructuralChanges().ForEach((Entity e, in Tag t) => { EntityManager.RemoveComponent<Tag>(e); }).Run(); } }")]
    public void IgnoresBufferedOrOutsideStructuralChanges(string code) =>
        Assert.Empty(Ecs(code, new Dictionary<string, string> { ["Sys"] = "registers meshes with Entities Graphics" }));

    [Fact]
    public void AllowListSilencesAStructuralChange() =>
        Assert.Empty(Ecs(Structural, new Dictionary<string, string> { ["Sys.DestroyEntity"] = "reason" }));

    const string Managed = "partial class Sys : SystemBase { protected override void OnUpdate() { } }";

    [Theory]
    [InlineData("builds the managed world")]
    [InlineData("streams regions near players")]
    [InlineData("mentions a mesh and a camera in lower case")]
    public void FlagsVagueSystemReasons(string reason) => Assert.Single(Ecs(Managed, new Dictionary<string, string> { ["Sys"] = reason }));

    [Theory]
    [InlineData("creates the ghost prefab once")]
    [InlineData("drives managed UnityEngine.Light objects")]
    [InlineData("dispatches compute on managed GraphicsBuffers")]
    [InlineData("registers meshes with Entities Graphics")]
    [InlineData("debt: rebuilds a managed list every frame")]
    public void AcceptsHonestSystemReasons(string reason) => Assert.Empty(Ecs(Managed, new Dictionary<string, string> { ["Sys"] = reason }));

    static List<string> MainThread(string code, Dictionary<string, string>? allow = null) =>
        Linter.MainThreadProblems(GameFile, code, allow ?? new Dictionary<string, string>());

    const string Looping = "[BurstCompile] partial struct Sys : ISystem { [BurstCompile] public void OnUpdate(ref SystemState s) { foreach (var t in SystemAPI.Query<RefRW<Tag>>()) t.ValueRW.A++; } }";

    [Theory]
    [InlineData(Looping)]
    [InlineData("partial class Sys : SystemBase { protected override void OnUpdate() { foreach (var (t, e) in SystemAPI.Query<RefRO<Tag>>().WithEntityAccess()) { } } }")]
    [InlineData("[BurstCompile] partial struct Sys : ISystem { public void OnUpdate(ref SystemState s) { new StepJob().Run(); } }")]
    [InlineData("partial class Sys : SystemBase { protected override void OnUpdate() { Entities.ForEach((ref Tag t) => t.A++).Run(); } }")]
    [InlineData("[BurstCompile] partial struct Sys : ISystem { EntityQuery q; public void OnUpdate(ref SystemState s) { new StepJob().Run(q); } }")]
    public void FlagsMainThreadLoops(string code) => Assert.Single(MainThread(code));

    [Theory]
    [InlineData("[BurstCompile] partial struct Sys : ISystem { public void OnUpdate(ref SystemState s) { s.Dependency = new StepJob().ScheduleParallel(s.Dependency); } }")]
    [InlineData("partial class Sys : SystemBase { Task<int> t; protected override void OnUpdate() { t = Task.Run(() => 1); } }")]
    [InlineData("[BurstCompile] partial struct Sys : ISystem { public void OnUpdate(ref SystemState s) { float r = Pose.Run(ref s, 1f); } }")]
    [InlineData("static class Tools { static void Each() { foreach (var t in SystemAPI.Query<RefRO<Tag>>()) { } new StepJob().Run(); } }")]
    public void IgnoresScheduledOrNonSystemWork(string code) => Assert.Empty(MainThread(code));

    [Fact]
    public void AllowListSilencesAMainThreadLoop() =>
        Assert.Empty(MainThread(Looping, new Dictionary<string, string> { ["Sys.Query"] = "debt: b/sim-jobs" }));

    [Theory]
    [InlineData("[BurstCompile] partial struct Sys : ISystem { public void OnUpdate(ref SystemState s) { s.Dependency.Complete(); } }")]
    [InlineData("partial class Sys : SystemBase { protected override void OnUpdate() { EntityManager.CompleteAllTrackedJobs(); } }")]
    [InlineData("[BurstCompile] partial struct Sys : ISystem { public void OnUpdate(ref SystemState s) { s.CompleteDependency(); } }")]
    [InlineData("[BurstCompile] partial struct Sys : ISystem { NativeArray<JobHandle> h; public void OnUpdate(ref SystemState s) { JobHandle.CompleteAll(h); } }")]
    public void FlagsJobCompletes(string code) => Assert.Single(Linter.BlockingProblems(GameFile, code, new Dictionary<string, string>()));

    static List<string> Teardown(string code, Dictionary<string, string>? allow = null) =>
        Linter.TeardownProblems(GameFile, code, allow ?? new Dictionary<string, string>());

    const string Leaking = "partial class Sys : SystemBase { JobHandle scan; protected override void OnUpdate() { if (JobPoll.Done(scan)) scan = default; } protected override void OnDestroy() { } }";

    [Theory]
    [InlineData(Leaking)]
    [InlineData("partial class Sys : SystemBase { JobHandle scan; protected override void OnUpdate() { } }")]
    [InlineData("public struct Slot { JobHandle h; } [BurstCompile] partial struct Sys : ISystem { Slot slot; public void OnDestroy(ref SystemState s) { slot = default; } }")]
    [InlineData("sealed class Batch { public JobHandle Handle; public void Dispose() { } } partial class Sys : SystemBase { List<Batch> pending; protected override void OnUpdate() { } protected override void OnDestroy() { pending.Clear(); } }")]
    public void FlagsPendingJobsLeftAtTeardown(string code) => Assert.Single(Teardown(code));

    [Theory]
    [InlineData("partial class Sys : SystemBase { JobHandle scan; protected override void OnUpdate() { } protected override void OnDestroy() { JobPoll.Teardown(scan); } }")]
    [InlineData("sealed class Batch { public JobHandle Handle; public void Dispose() { } } partial class Sys : SystemBase { Batch batch; protected override void OnUpdate() { } protected override void OnDestroy() { batch?.Dispose(); } }")]
    [InlineData("partial class Sys : SystemBase { JobHandle scan; protected override void OnUpdate() { } protected override void OnDestroy() { Stop(); } void Stop() { scan.Complete(); } }")]
    [InlineData("public struct Slot { JobHandle h; public void Finish() { h.Complete(); } } [BurstCompile] partial struct Sys : ISystem { Slot slot; public void OnDestroy(ref SystemState s) { slot.Finish(); } }")]
    [InlineData("sealed class Batch { public JobHandle Handle; public void Dispose() { } } partial class Sys : SystemBase { List<Batch> pending; protected override void OnUpdate() { } protected override void OnDestroy() { foreach (var p in pending) { JobPoll.Teardown(p.Handle); p.Dispose(); } } }")]
    [InlineData("[BurstCompile] partial struct Sys : ISystem { public JobHandle Gpu; NativeArray<int> a; public void OnDestroy(ref SystemState s) { var dep = JobHandle.CombineDependencies(s.Dependency, Gpu); a.Dispose(dep); } }")]
    [InlineData("static class Tools { static JobHandle h; } struct Plain { JobHandle h; }")]
    public void AcceptsPendingJobsTornDown(string code) => Assert.Empty(Teardown(code));

    [Fact]
    public void AllowListSilencesALeftJob() =>
        Assert.Empty(Teardown(Leaking, new Dictionary<string, string> { ["Sys.scan"] = "the job writes nothing the system owns" }));

    [Fact]
    public void OnlyRuntimeCodeGetsEcsChecks()
    {
        Assert.True(Linter.IsRuntime(GameFile));
        Assert.False(Linter.IsRuntime("unity/Assets/Game/Editor/GameCapture.cs"));
        Assert.False(Linter.IsRuntime("unity/Assets/Viewer/Viewer.cs"));
    }
}
