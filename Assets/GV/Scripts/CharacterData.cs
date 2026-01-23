using UnityEngine;

namespace VSX.Engines3D
{
    [CreateAssetMenu(fileName = "New Character Data", menuName = "VSX/Engines3D/Character Data")]
    public class CharacterData : ScriptableObject
    {
        [Header("Identity")]
        public string characterName;
        // Icon is optional as user has sprites in scene, but keeping it for reference if needed
        [Tooltip("Optional icon for UI if needed later")]
        public Sprite icon; 

        [Header("Bonus Multipliers")]
        [Tooltip("Multiplier for movement forces. 1.1 = +10% bonus.")]
        public float speedMultiplier = 1.1f;

        [Tooltip("Multiplier for steering forces. 1.1 = +10% bonus.")]
        public float steeringMultiplier = 1.1f;

        [Tooltip("Multiplier for boost forces. 1.1 = +10% bonus.")]
        public float boostMultiplier = 1.1f;
    }
}
