using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using Stabby.World;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        static bool OwnPlayer(Unity.Entities.World server, int id, out float3 pos, out PlayerMotion motion)
        {
            pos = default;
            motion = default;
            using var q = server.EntityManager.CreateEntityQuery(typeof(Player), typeof(LocalTransform), typeof(GhostOwner), typeof(PlayerMotion));
            using var es = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            foreach (var e in es)
            {
                if (server.EntityManager.GetComponentData<GhostOwner>(e).NetworkId != id) continue;
                pos = server.EntityManager.GetComponentData<LocalTransform>(e).Position;
                motion = server.EntityManager.GetComponentData<PlayerMotion>(e);
                return true;
            }
            return false;
        }

        static float FlatRelief(Ground ground, float2 at)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = -2; i <= 2; i++)
                for (int j = -2; j <= 2; j++)
                {
                    float h = ground.HeightAt(at.x + i * 8f, at.y + j * 8f);
                    lo = math.min(lo, h);
                    hi = math.max(hi, h);
                }
            return lo > 0.5f ? hi - lo : float.MaxValue;
        }

        static System.Collections.IEnumerator ControlsTour(Camera cam, string dir)
        {
            var server = ServerWorld();
            var view = ViewWorld();
            Ground ground = default;
            bool hasGround = false;
            foreach (var gw in Unity.Entities.World.All)
            {
                using var gq = gw.EntityManager.CreateEntityQuery(typeof(Ground));
                if (gq.CalculateEntityCount() == 1) { ground = gq.GetSingleton<Ground>(); hasGround = true; break; }
            }
            int id = -1;
            if (view != null)
            {
                using var nq = view.EntityManager.CreateEntityQuery(typeof(NetworkId));
                if (nq.CalculateEntityCount() == 1) id = nq.GetSingleton<NetworkId>().Value;
            }
            if (server == null || view == null || !hasGround || id < 0) { GameStatus.Lines["~controls"] = "controls: no worlds, ground or network id"; yield break; }
            var lines = new List<string>();
            cam.fieldOfView = 60f;
            PlayerControls.Scripted = true;
            PlayerControls.ScriptMove = PlayerControls.ScriptLook = float2.zero;
            PlayerControls.Pitch = 20f;
            GameStatus.HoldCamera = false;

            float2 flat = new float2(home.x, home.z);
            float best = float.MaxValue;
            for (int r = 0; r <= 600 && best > 1.5f; r += 40)
                for (int a = 0; a < 16; a++)
                {
                    math.sincos(a * math.PI / 8f, out float s, out float c);
                    var p = new float2(home.x, home.z) + new float2(c, s) * r;
                    float relief = FlatRelief(ground, p);
                    if (relief < best) { best = relief; flat = p; }
                }
            PlaceServerPlayers(server, (Vector2)flat);
            for (var w = Settle(180); w.MoveNext();) yield return null;
            lines.Add($"flat spot {flat.x:F0},{flat.y:F0}, relief {best:F2} m over 32 m");

            float yaw0 = PlayerControls.Yaw;
            PlayerControls.ScriptLook = new float2(90f, 0);
            for (int k = 0; k < 60; k++) yield return null;
            PlayerControls.ScriptLook = float2.zero;
            lines.Add($"scripted look turned yaw {yaw0:F1} -> {PlayerControls.Yaw:F1} deg");

            PlayerControls.Yaw = 35f;
            for (int k = 0; k < 10; k++) yield return null;
            var camFwd = math.normalize(new float2(cam.transform.forward.x, cam.transform.forward.z));
            foreach (var (label, sprint) in new[] { ("walk", false), ("sprint", true) })
            {
                PlayerControls.ScriptMove = new float2(0, 1);
                PlayerControls.ScriptSprint = sprint;
                for (int k = 0; k < 30; k++) yield return null;
                OwnPlayer(server, id, out var a0, out _);
                double t0 = server.Time.ElapsedTime;
                for (int k = 0; k < 90; k++) yield return null;
                OwnPlayer(server, id, out var a1, out _);
                double t1 = server.Time.ElapsedTime;
                var step = new float2(a1.x - a0.x, a1.z - a0.z);
                float speed = math.length(step) / (float)math.max(1e-3, t1 - t0);
                float align = math.dot(math.normalizesafe(step), camFwd);
                lines.Add($"{label} forward: {speed:F2} m/s, alignment with camera forward {align:F3}");
            }
            PlayerControls.ScriptSprint = false;
            PlayerControls.ScriptSprintPress = true;
            for (int k = 0; k < 30; k++) yield return null;
            lines.Add($"L3 toggle: sprint latched {PlayerControls.SprintOn}");
            PlayerControls.ScriptMove = float2.zero;
            for (int k = 0; k < 5; k++) yield return null;
            lines.Add($"movement stopped: sprint latched {PlayerControls.SprintOn}");

            for (int k = 0; k < 30; k++) yield return null;
            OwnPlayer(server, id, out var g0, out _);
            PlayerControls.ScriptJump = true;
            float peak = g0.y, landAt = -1;
            bool rose = false;
            double jt0 = server.Time.ElapsedTime;
            for (int k = 0; k < 240; k++)
            {
                yield return null;
                if (!OwnPlayer(server, id, out var pj, out var m)) break;
                peak = math.max(peak, pj.y);
                if (m.Airborne) rose = true;
                if (rose && !m.Airborne) { landAt = (float)(server.Time.ElapsedTime - jt0); break; }
            }
            OwnPlayer(server, id, out var g1, out var mEnd);
            float expect = GameSettings.JumpSpeed * GameSettings.JumpSpeed / (2 * GameSettings.Gravity);
            lines.Add($"jump: peak {peak - g0.y:F2} m (ballistic {expect:F2}), landed after {landAt:F2} s, ground drift {g1.y - g0.y:F3} m, airborne at end {mEnd.Airborne}");

            PlayerControls.ScriptMove = new float2(0, 1);
            for (int k = 0; k < 20; k++) yield return null;
            PlayerControls.ScriptMove = float2.zero;
            for (var w = Settle(30); w.MoveNext();) yield return null;
            lines.Add($"flat boom {PlayerControls.BoomNow:F2} m of {PlayerControls.Boom:F0}");
            Shot(cam, dir, "controls-flat");

            float2 slope = flat;
            float slopeYaw = 0, slopeBoom = float.MaxValue;
            for (int r = 40; r <= 1600 && slopeBoom > 10f; r += 40)
                for (int a = 0; a < 24 && slopeBoom > 10f; a++)
                {
                    math.sincos(a * math.PI / 12f, out float s, out float c);
                    var p = new float2(home.x, home.z) + new float2(c, s) * r;
                    float h = ground.HeightAt(p.x, p.y);
                    if (h < 0.5f) continue;
                    for (int y = 0; y < 8; y++)
                    {
                        PlayerControls.Yaw = y * 45f;
                        var pivot = new float3(p.x, h + PlayerControls.Lift, p.y);
                        float b = PlayerControls.BoomClear(ground, pivot, PlayerControls.Back);
                        if (b > 4f && b < slopeBoom) { slopeBoom = b; slope = p; slopeYaw = PlayerControls.Yaw; }
                    }
                }
            PlayerControls.Yaw = slopeYaw;
            PlaceServerPlayers(server, (Vector2)slope);
            for (var w = Settle(180); w.MoveNext();) yield return null;
            lines.Add($"hillside spot {slope.x:F0},{slope.y:F0} yaw {slopeYaw:F0}: clear boom {slopeBoom:F2} m, eased boom {PlayerControls.BoomNow:F2} m");
            Shot(cam, dir, "controls-slope");

            PlaceServerPlayers(server, (Vector2)flat);
            float before = PlayerControls.BoomNow;
            for (int k = 0; k < 30; k++) yield return null;
            lines.Add($"ease out: boom {before:F2} -> {PlayerControls.BoomNow:F2} m after 30 frames back on flat ground");

            PlayerControls.Scripted = false;
            GameStatus.HoldCamera = true;
            GameStatus.Lines["~controls"] = string.Join("\n", lines);
        }
    }
}
