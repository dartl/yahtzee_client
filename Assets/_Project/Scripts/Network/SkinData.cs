namespace Yahtzee.Network
{
    public enum SkinType
    {
        Dice,
        TableBox
    }

    public sealed class SkinData
    {
        public int Id { get; set; }
        public string Type { get; set; }
        public string Name { get; set; }
        public string Slug { get; set; }
        public int Price { get; set; }
        public bool Owned { get; set; }
        public bool Equipped { get; set; }

        public SkinType Kind => Type == "table_box" ? SkinType.TableBox : SkinType.Dice;
    }

    public sealed class ShopCatalogResponse
    {
        public int SoftCurrency { get; set; }
        public SkinData[] Skins { get; set; }
    }

    public sealed class SkinActionResponse
    {
        public int SoftCurrency { get; set; }
        public SkinData Skin { get; set; }
        public ProfileResponse User { get; set; }
    }
}
