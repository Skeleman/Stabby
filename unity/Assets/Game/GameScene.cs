using UnityEngine;

namespace Stabby.Game
{
    public sealed class GameScene : MonoBehaviour
    {
        public Material TerrainMaterial, WaterMaterial, MistMaterial;
        GUIStyle style;

        void OnGUI()
        {
            if (GameStatus.TerrainBuilt) return;
            style ??= new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(14, Screen.height / 40) };
            string text = GameStatus.Failure != null ? "World failed to load; see Player.log"
                : $"Loading world... {Time.realtimeSinceStartup:F0} s{(string.IsNullOrEmpty(GameStatus.TerrainStage) ? "" : $" ({GameStatus.TerrainStage})")}";
            GUI.Label(new Rect(20, Screen.height - 3 * style.fontSize, Screen.width - 40, 2 * style.fontSize), text, style);
        }
    }
}
