#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using VSX.Engines3D;

/// <summary>
/// Editor-only script to create all 8 CharacterData ScriptableObject assets.
/// Run via menu: Tools > GV > Create Character Data Assets
/// Safe to re-run — skips existing assets.
/// </summary>
public class CreateCharacterDataAssets
{
    private const string BasePath = "Assets/GV/Data/Characters/";

    [MenuItem("Tools/GV/Create Character Data Assets")]
    public static void CreateAll()
    {
        // 1) Aaryaveer — Balanced striker
        Create("Aaryaveer", new StatBlock
        {
            speed = 1.03f, steering = 1.03f, boost = 1.02f,
            projDmg = 1.08f, projRange = 1.05f, projSpeed = 1.05f, projFireRate = 1.03f, projReload = 1.02f,
            missDmg = 1.05f, missRange = 1.03f, missSpeed = 1.03f, missFireRate = 1.02f, missReload = 1.02f
        });

        // 2) Ishvaya — Precision / control
        Create("Ishvaya", new StatBlock
        {
            speed = 1.02f, steering = 1.08f, boost = 1.01f,
            projDmg = 1.05f, projRange = 1.10f, projSpeed = 1.08f, projFireRate = 1.02f, projReload = 1.02f,
            missDmg = 1.03f, missRange = 1.08f, missSpeed = 1.06f, missFireRate = 1.02f, missReload = 1.03f
        });

        // 3) Vyanika — Mobility / evasive
        Create("Vyanika", new StatBlock
        {
            speed = 1.08f, steering = 1.10f, boost = 1.08f,
            projDmg = 1.00f, projRange = 1.02f, projSpeed = 1.05f, projFireRate = 1.04f, projReload = 1.03f,
            missDmg = 0.98f, missRange = 1.00f, missSpeed = 1.03f, missFireRate = 1.03f, missReload = 1.02f
        });

        // 4) Rudraansh — Heavy burst
        Create("Rudraansh", new StatBlock
        {
            speed = 0.98f, steering = 0.99f, boost = 1.00f,
            projDmg = 1.15f, projRange = 1.03f, projSpeed = 1.02f, projFireRate = 1.00f, projReload = 0.98f,
            missDmg = 1.12f, missRange = 1.04f, missSpeed = 1.02f, missFireRate = 1.00f, missReload = 0.98f
        });

        // 5) Zorvan — Missile hunter
        Create("Zorvan", new StatBlock
        {
            speed = 1.02f, steering = 1.01f, boost = 1.03f,
            projDmg = 1.02f, projRange = 1.03f, projSpeed = 1.04f, projFireRate = 1.02f, projReload = 1.02f,
            missDmg = 1.12f, missRange = 1.10f, missSpeed = 1.10f, missFireRate = 1.04f, missReload = 1.04f
        });

        // 6) Kaevik — Rapid assault
        Create("Kaevik", new StatBlock
        {
            speed = 1.05f, steering = 1.04f, boost = 1.04f,
            projDmg = 1.06f, projRange = 1.02f, projSpeed = 1.06f, projFireRate = 1.10f, projReload = 1.08f,
            missDmg = 1.00f, missRange = 1.00f, missSpeed = 1.02f, missFireRate = 1.03f, missReload = 1.03f
        });

        // 7) Nysera — Long-range specialist
        Create("Nysera", new StatBlock
        {
            speed = 1.01f, steering = 1.04f, boost = 1.02f,
            projDmg = 1.07f, projRange = 1.12f, projSpeed = 1.10f, projFireRate = 1.00f, projReload = 1.01f,
            missDmg = 1.04f, missRange = 1.10f, missSpeed = 1.08f, missFireRate = 1.00f, missReload = 1.01f
        });

        // 8) Virexa — Glass cannon
        Create("Virexa", new StatBlock
        {
            speed = 1.04f, steering = 1.03f, boost = 1.05f,
            projDmg = 1.12f, projRange = 1.04f, projSpeed = 1.07f, projFireRate = 1.06f, projReload = 1.02f,
            missDmg = 1.08f, missRange = 1.03f, missSpeed = 1.05f, missFireRate = 1.04f, missReload = 1.01f
        });

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[CreateCharacterDataAssets] Done! Created 8 CharacterData assets in " + BasePath);
    }

    private struct StatBlock
    {
        public float speed, steering, boost;
        public float projDmg, projRange, projSpeed, projFireRate, projReload;
        public float missDmg, missRange, missSpeed, missFireRate, missReload;
    }

    private static void Create(string characterName, StatBlock stats)
    {
        string path = BasePath + characterName + ".asset";

        // Skip if already exists
        if (AssetDatabase.LoadAssetAtPath<CharacterData>(path) != null)
        {
            Debug.Log($"[CreateCharacterDataAssets] {characterName} already exists, skipping.");
            return;
        }

        var asset = ScriptableObject.CreateInstance<CharacterData>();
        asset.characterName = characterName;

        // Movement
        asset.speedMultiplier = stats.speed;
        asset.steeringMultiplier = stats.steering;
        asset.boostMultiplier = stats.boost;

        // Projectile
        asset.projectileDamageMultiplier = stats.projDmg;
        asset.projectileRangeMultiplier = stats.projRange;
        asset.projectileSpeedMultiplier = stats.projSpeed;
        asset.projectileFireRateMultiplier = stats.projFireRate;
        asset.projectileReloadMultiplier = stats.projReload;

        // Missile
        asset.missileDamageMultiplier = stats.missDmg;
        asset.missileRangeMultiplier = stats.missRange;
        asset.missileSpeedMultiplier = stats.missSpeed;
        asset.missileFireRateMultiplier = stats.missFireRate;
        asset.missileReloadMultiplier = stats.missReload;

        AssetDatabase.CreateAsset(asset, path);
        Debug.Log($"[CreateCharacterDataAssets] Created: {path}");
    }
}
#endif
