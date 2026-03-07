using UnityEngine;

namespace GV.UI
{
    /// <summary>
    /// Holds all lore data for a single character.
    /// Assign one per character via the inspector or link from CharacterData.
    /// </summary>
    [CreateAssetMenu(fileName = "NewCharacterLore", menuName = "GV/Character Lore Data")]
    public class CharacterLoreData : ScriptableObject
    {
        [Header("Identity")]
        public string characterName;
        public string tagline;            // e.g. "The crown of Nandana does not kneel."

        [Header("Classification")]
        public string rarity;             // Epic, Rare, Legendary, Common
        public string role;               // Balanced Striker, Precision / Control, etc.

        [Header("Origin")]
        public string regionLabel;        // "Kingdom" for Sarathi, "Country" for Atom Riders
        public string regionName;         // Nandana, Aurkana, etc.
        public string factionLabel;       // "Faction" for Sarathi, "Culture" for Atom Riders
        public string factionName;        // Devas, NEO-TERRANs, etc.

        [Header("Powers")]
        public string powerClass;         // Supreme Astra Powers / Antimatter Class Nuclear P
        public string terrainMastery;     // Single / Multi / Dual Terrain Mastery
        public string tharaResonance;     // Normal / Advance / Extreme Thara Resonance

        [Header("Lore Details")]
        [TextArea(4, 12)]
        public string backstory;

        [TextArea(2, 6)]
        public string strengths;          // Line-break separated list

        [TextArea(2, 6)]
        public string weaknesses;         // Line-break separated list

        [TextArea(2, 6)]
        public string rivals;             // Line-break separated: "Name — reason"
    }
}
