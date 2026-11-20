using UnityEngine;

namespace Stabby.Viewer
{
    public sealed class ViewerHud : MonoBehaviour
    {
        public WorldView View;
        float frameMs = 16f;

        void Update() => frameMs = Mathf.Lerp(frameMs, Time.unscaledDeltaTime * 1000f, 0.05f);

        void OnGUI()
        {
            if (View == null) return;
            var p = transform.position;
            GUI.Label(new Rect(10, 10, 600, 140),
                $"{1000f / frameMs:F0} fps ({frameMs:F1} ms)\n{View.Stats()}\npos {p.x:F0}, {p.y:F0}, {p.z:F0}  ground {View.GroundHeight(p):F0} m\n" +
                "RMB look, WASD move, Q/E down/up, Shift fast, wheel speed");
        }
    }
}
