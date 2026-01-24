using UnityEngine;

namespace VSX.Engines3D
{
    [CreateAssetMenu(fileName = "New Artifact Data", menuName = "VSX/Engines3D/Artifact Data")]
    public class ArtifactData : ScriptableObject
    {
        [Header("Identity")]
        public string artifactName;
        [TextArea] public string description;

        [Header("Bonus Multipliers")]
        [Tooltip("Multiplier for movement forces. 1.05 = +5% bonus.")]
        public float speedMultiplier = 1.0f;

        [Tooltip("Multiplier for steering forces. 1.05 = +5% bonus.")]
        public float steeringMultiplier = 1.0f;

        [Tooltip("Multiplier for boost forces. 1.05 = +5% bonus.")]
        public float boostMultiplier = 1.0f;
    }
}
