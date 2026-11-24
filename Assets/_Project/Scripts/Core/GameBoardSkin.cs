using UnityEngine;

namespace Yahtzee.Core
{
    [CreateAssetMenu(fileName = "GameBoardSkin", menuName = "Yahtzee/Board Skin")]
    public sealed class GameBoardSkin : ScriptableObject
    {
        public string skinId = "classic";
        public string skinName = "Классика";
        public Material trayBottomMaterial;
        public Material borderMaterial;
        public int price;
    }
}
