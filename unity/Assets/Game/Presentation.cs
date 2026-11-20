using System.Collections.Generic;
using Stabby.Viewer;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Stabby.Game
{
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class ClientViewSystem : SystemBase
    {
        static readonly Color[] Colors = { new Color(0.85f, 0.25f, 0.2f), new Color(0.2f, 0.45f, 0.9f), new Color(0.95f, 0.8f, 0.2f), new Color(0.3f, 0.75f, 0.35f) };
        readonly Dictionary<Entity, GameObject> capsules = new Dictionary<Entity, GameObject>();
        readonly HashSet<Entity> seen = new HashSet<Entity>();
        WorldView view;

        protected override void OnCreate()
        {
            if (World.Name != GameSettings.ViewClient) Enabled = false;
        }

        protected override void OnUpdate()
        {
            var map = World.GetExistingSystemManaged<ClientMapSystem>();
            if (view == null && map.Map != null)
            {
                var scene = Object.FindFirstObjectByType<GameScene>();
                var go = new GameObject("Terrain");
                view = go.AddComponent<WorldView>();
                view.Material = scene != null ? scene.TerrainMaterial : null;
                view.Build(map.Map, map.Defs);
            }
            if (view != null) GameStatus.TerrainBuilt = view.Ready;
            Frames();

            int me = SystemAPI.TryGetSingleton<NetworkId>(out var id) ? id.Value : -1;
            Transform mine = null;
            seen.Clear();
            foreach (var (t, owner, e) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<GhostOwner>>().WithAll<Player>().WithEntityAccess())
            {
                seen.Add(e);
                if (!capsules.TryGetValue(e, out var c))
                {
                    c = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    c.name = $"Player {owner.ValueRO.NetworkId}";
                    Object.Destroy(c.GetComponent<Collider>());
                    var r = c.GetComponent<Renderer>();
                    r.material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = Colors[owner.ValueRO.NetworkId % Colors.Length] };
                    c.transform.localScale = new Vector3(1.5f, 1.5f, 1.5f);
                    capsules[e] = c;
                }
                c.transform.SetPositionAndRotation((Vector3)t.ValueRO.Position + Vector3.up * 1.5f, t.ValueRO.Rotation);
                if (owner.ValueRO.NetworkId == me) mine = c.transform;
            }
            var gone = new List<Entity>();
            foreach (var kv in capsules) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var e in gone) { Object.Destroy(capsules[e]); capsules.Remove(e); }
            GameStatus.Capsules = capsules.Count;

            var cam = Camera.main;
            if (cam != null && mine != null)
            {
                cam.transform.position = mine.position + new Vector3(0, 10, -22);
                cam.transform.LookAt(mine.position + Vector3.up * 2);
            }
        }

        int frames, slow;
        float worst;

        void Frames()
        {
            if (++frames <= 10) return;
            float ms = UnityEngine.Time.unscaledDeltaTime * 1000f;
            if (ms > worst) worst = ms;
            if (ms > 33f) slow++;
            GameStatus.Lines["~frames"] = $"frames {frames - 10}, worst {worst:F0} ms, {slow} over 33 ms";
        }
    }
}
