namespace Yahtzee.Models
{
    public sealed class LocalPlayerState
    {
        public LocalPlayerState()
            : this(string.Empty, 0, 0)
        {
        }

        public LocalPlayerState(string nickname, int softCurrency, int mmr)
        {
            Nickname = nickname ?? string.Empty;
            SoftCurrency = softCurrency;
            Mmr = mmr;
            Scorecard = new Scorecard();
        }

        public string Nickname { get; set; }

        public int SoftCurrency { get; set; }

        public int Mmr { get; set; }

        public Scorecard Scorecard { get; }
    }
}
