using UnityEngine;

namespace Yahtzee.Core
{
    public readonly struct DiceLook
    {
        public DiceLook(Color body, Color pips)
        {
            Body = body;
            Pips = pips;
        }

        public Color Body { get; }
        public Color Pips { get; }
    }

    public readonly struct TableLook
    {
        public TableLook(Color felt, Color rails, bool goldCorners)
        {
            Felt = felt;
            Rails = rails;
            GoldCorners = goldCorners;
        }

        public Color Felt { get; }
        public Color Rails { get; }
        public bool GoldCorners { get; }
    }

    public static class SkinVisuals
    {
        public const string ClassicDice = "classic-dice";
        public const string ClassicTable = "classic-felt";

        public static DiceLook Dice(string slug)
        {
            switch (slug)
            {
                case "ivory-dice":
                    return new DiceLook(new Color(0.96f, 0.93f, 0.86f, 1f), new Color(0.22f, 0.16f, 0.12f, 1f));
                case "obsidian-dice":
                    return new DiceLook(new Color(0.14f, 0.15f, 0.18f, 1f), new Color(0.92f, 0.92f, 0.94f, 1f));
                default:
                    return BoardSkinConfig.Active.ToDiceLook();
            }
        }

        public static TableLook Table(string slug)
        {
            switch (slug)
            {
                case "royal-blue":
                    return new TableLook(new Color(0.78f, 0.86f, 0.96f, 1f), new Color(0.55f, 0.65f, 0.78f, 1f), false);
                case "gold-mahogany":
                    return new TableLook(new Color(0.93f, 0.86f, 0.78f, 1f), new Color(0.55f, 0.35f, 0.22f, 1f), true);
                default:
                    return BoardSkinConfig.Active.ToTableLook();
            }
        }
    }
}
