using UnityEngine;

namespace PartyTaxes
{
    public enum SoulAttribute { Might, Agility, Constitution, Sense, Luck }

    /// <summary>
    /// A name prefix that can be randomly applied to a soul, e.g. "Mighty Salen the Human".
    /// The modifier is clamped to the range [-2, +3] and applied to one attribute.
    /// Configure instances in the PTSoulGen Inspector array.
    /// </summary>
    [System.Serializable]
    public class PTSoulPrefix
    {
        [Tooltip("The word that appears before the soul's first name, e.g. 'Mighty'.")]
        public string prefixName = "Strong";

        [Tooltip("Which attribute this prefix modifies.")]
        public SoulAttribute attribute = SoulAttribute.Might;

        [Tooltip("Stat modifier. Positive = bonus, negative = debuff. Clamped to [-2, +3].")]
        [Range(-2, 3)]
        public int modifier = 1;

        /// <summary>
        /// Applies this prefix's modifier to the given soul (clamped to [1, 99]).
        /// </summary>
        public void Apply(PTSoul soul)
        {
            soul.ChangeAttribute(attribute, Mathf.Clamp(modifier, -2, 3), false);
        }
    }
}
