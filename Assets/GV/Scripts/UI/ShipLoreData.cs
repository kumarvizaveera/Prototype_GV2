using UnityEngine;

namespace GV.UI
{
    /// <summary>
    /// Holds all lore data for a single ship type.
    /// Assign one per ship via the inspector.
    /// </summary>
    [CreateAssetMenu(fileName = "NewShipLore", menuName = "GV/Ship Lore Data")]
    public class ShipLoreData : ScriptableObject
    {
        [Header("Identity")]
        public string shipName;
        public string tagline;

        [Header("Classification")]
        public string rarity;             // Common, Rare, Epic, Legendary
        public string shipClass;          // e.g. "Ancient Divine Carrier", "Nuclear Strike Vessel"

        [Header("Origin")]
        public string originLabel;        // "Realm" for Vimana, "Alliance" for Spaceship
        public string originName;         // "Akasa Raajyas", "Earth Countries in Akasya"
        public string factionLabel;       // "Pilots" for both
        public string factionName;        // "Sarathi", "Atom Riders"

        [Header("Technical")]
        public string powerSystem;        // "Astra-Powered Thara Core", "Nuclear-P Reactor Array"
        public string combatRole;         // "Multi-role divine platform", "Rapid assault strike craft"
        public string specialAbility;     // Unique ship mechanic

        [Header("Lore Details")]
        [TextArea(4, 12)]
        public string backstory;

        [TextArea(2, 6)]
        public string strengths;          // Line-break separated

        [TextArea(2, 6)]
        public string weaknesses;         // Line-break separated

        [TextArea(2, 6)]
        public string history;            // Line-break separated key events
    }
}
