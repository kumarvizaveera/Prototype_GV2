using UnityEngine;
using UnityEditor;

namespace GV.EditorTools
{
    public class FixURPEmissionStripping : EditorWindow
    {
        [MenuItem("Tools/GV/Fix Engine Glow Materials (Build Stripping)")]
        public static void FixEmissionMaterials()
        {
            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { "Assets/GV" });
            int fixedCount = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);

                if (mat != null && mat.shader != null && mat.shader.name.Contains("Universal Render Pipeline/Lit"))
                {
                    bool changed = false;

                    // Force the Emission keyword to remain
                    if (!mat.IsKeywordEnabled("_EMISSION"))
                    {
                        mat.EnableKeyword("_EMISSION");
                        changed = true;
                    }

                    // Force Global Illumination flags to evaluate in real-time
                    if (mat.globalIlluminationFlags != MaterialGlobalIlluminationFlags.RealtimeEmissive)
                    {
                        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                        changed = true;
                    }

                    // Under URP, if the EmissionColor is pure black, the shader compiler might STILL strip the emission pass
                    // to optimize performance. Give it a tiny, imperceptible base value so the compiler respects it.
                    if (mat.HasProperty("_EmissionColor"))
                    {
                        Color currentEmi = mat.GetColor("_EmissionColor");
                        if (currentEmi.maxColorComponent < 0.001f) // Effectively black
                        {
                            mat.SetColor("_EmissionColor", new Color(0.005f, 0.005f, 0.005f, 1f));
                            changed = true;
                        }
                    }

                    if (changed)
                    {
                        EditorUtility.SetDirty(mat);
                        fixedCount++;
                    }
                }
            }

            if (fixedCount > 0)
            {
                AssetDatabase.SaveAssets();
                Debug.Log($"[FixURPEmissionStripping] Successfully fixed {fixedCount} materials in Assets/GV to retain Emission in builds.");
            }
            else
            {
                Debug.Log("[FixURPEmissionStripping] All materials are already configured to retain Emission.");
            }
        }
    }
}
