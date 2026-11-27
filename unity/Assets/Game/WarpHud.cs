#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

namespace Stabby.Game
{
    public sealed class WarpHud : MonoBehaviour
    {
        const float ShowSeconds = 2f;
        static WarpHud hud;
        string text;
        float until;
        GUIStyle style;

        public static void Show(string line)
        {
            if (hud == null)
            {
                var go = new GameObject(nameof(WarpHud)) { hideFlags = HideFlags.DontSave };
                DontDestroyOnLoad(go);
                hud = go.AddComponent<WarpHud>();
            }
            hud.text = line;
            hud.until = Time.unscaledTime + ShowSeconds;
        }

        void OnGUI()
        {
            if (text == null || Time.unscaledTime > until) return;
            style ??= new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            var r = new Rect(20, Screen.height - 60, Screen.width - 40, 40);
            style.normal.textColor = Color.black;
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, style);
            style.normal.textColor = Color.white;
            GUI.Label(r, text, style);
        }
    }
}
#endif
