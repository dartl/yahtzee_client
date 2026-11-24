using UnityEngine;

namespace Yahtzee.Core
{
    public static class DiceSkinApplier
    {
        public const string PrefKey = "yahtzee.skin.dice";

        public static void ApplySaved()
        {
            var slug = PlayerPrefs.GetString(PrefKey, SkinVisuals.ClassicDice);
            Apply(slug);
        }

        public static void Apply(string slug)
        {
            var look = SkinVisuals.Dice(slug);
            DicePhysicsController.SetSkinColors(look.Body, look.Pips);
            ProceduralDiceVisual.SetColors(look.Body, look.Pips);
        }
    }
}
