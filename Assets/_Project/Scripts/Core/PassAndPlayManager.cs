using System.Text;
using Yahtzee.Models;

namespace Yahtzee.Core
{
    public sealed class PassAndPlayManager
    {
        public const int MinPlayers = 2;
        public const int MaxPlayers = 4;

        private static int _nextSessionId = 1;

        private readonly string[] _names;

        private PassAndPlayManager(int playerCount)
        {
            SessionId = _nextSessionId++;
            PlayerCount = playerCount;
            Sheets = new Scorecard[playerCount];
            _names = new string[playerCount];
            for (var index = 0; index < playerCount; index++)
            {
                Sheets[index] = new Scorecard();
                _names[index] = "Игрок " + (index + 1);
            }

            CurrentIndex = 0;
            AwaitingHandoff = true;
        }

        public static PassAndPlayManager Active { get; private set; }

        public int SessionId { get; }
        public int PlayerCount { get; }
        public int CurrentIndex { get; private set; }
        public bool AwaitingHandoff { get; private set; }
        public bool MatchOver { get; private set; }
        public Scorecard[] Sheets { get; }

        public Scorecard CurrentSheet => Sheets[CurrentIndex];

        public string CurrentName => NameOf(CurrentIndex);

        public static PassAndPlayManager Begin(int playerCount)
        {
            var count = playerCount < MinPlayers ? MinPlayers : playerCount > MaxPlayers ? MaxPlayers : playerCount;
            Active = new PassAndPlayManager(count);
            return Active;
        }

        public static void Close()
        {
            Active = null;
        }

        public string NameOf(int index)
        {
            if (index < 0 || index >= PlayerCount)
                return "Игрок";

            return _names[index];
        }

        public void ConfirmReady()
        {
            if (MatchOver)
                return;

            AwaitingHandoff = false;
        }

        public bool TryScore(YahtzeeCategory category, int[] dice)
        {
            if (MatchOver || AwaitingHandoff)
                return false;

            var score = DiceGameEngine.CalculateScore(category, dice, CurrentSheet);
            if (!CurrentSheet.RegisterScore(category, score, dice))
                return false;

            if (EveryoneFinished())
            {
                MatchOver = true;
                AwaitingHandoff = false;
                return true;
            }

            CurrentIndex = NextOpenIndex(CurrentIndex);
            AwaitingHandoff = true;
            return true;
        }

        public string StandingsText()
        {
            var builder = new StringBuilder();
            for (var index = 0; index < PlayerCount; index++)
            {
                if (index > 0)
                    builder.Append('\n');

                builder.Append(NameOf(index));
                builder.Append(": ");
                builder.Append(Sheets[index].GetTotalScore());
            }

            return builder.ToString();
        }

        private bool EveryoneFinished()
        {
            for (var index = 0; index < PlayerCount; index++)
            {
                if (!Sheets[index].IsFullyCompleted())
                    return false;
            }

            return true;
        }

        private int NextOpenIndex(int from)
        {
            for (var step = 1; step <= PlayerCount; step++)
            {
                var index = (from + step) % PlayerCount;
                if (!Sheets[index].IsFullyCompleted())
                    return index;
            }

            return from;
        }
    }
}
