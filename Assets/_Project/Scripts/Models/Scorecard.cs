using System;
using System.Collections.Generic;
using Yahtzee.Core;

namespace Yahtzee.Models
{
    /// <summary>
    /// Classic Yahtzee score sheet — totals match Laravel YahtzeeScore.
    /// </summary>
    public sealed class Scorecard
    {
        public static int UpperBonusThreshold => YahtzeeConfig.Current.UpperBonusThreshold;
        public static int UpperBonusValue => YahtzeeConfig.Current.UpperBonusValue;
        public static int YahtzeeBonusPoints => YahtzeeConfig.Current.YahtzeeBonusPoints;
        public static int MaxYahtzeeBonuses => YahtzeeConfig.Current.MaxYahtzeeBonuses;

        private static readonly YahtzeeCategory[] UpperCategories =
        {
            YahtzeeCategory.Ones,
            YahtzeeCategory.Twos,
            YahtzeeCategory.Threes,
            YahtzeeCategory.Fours,
            YahtzeeCategory.Fives,
            YahtzeeCategory.Sixes
        };

        private static readonly YahtzeeCategory[] LowerCategories =
        {
            YahtzeeCategory.ThreeOfAKind,
            YahtzeeCategory.FourOfAKind,
            YahtzeeCategory.FullHouse,
            YahtzeeCategory.SmallStraight,
            YahtzeeCategory.LargeStraight,
            YahtzeeCategory.Yahtzee,
            YahtzeeCategory.Chance
        };

        private static readonly YahtzeeCategory[] AllCategories =
        {
            YahtzeeCategory.Ones,
            YahtzeeCategory.Twos,
            YahtzeeCategory.Threes,
            YahtzeeCategory.Fours,
            YahtzeeCategory.Fives,
            YahtzeeCategory.Sixes,
            YahtzeeCategory.ThreeOfAKind,
            YahtzeeCategory.FourOfAKind,
            YahtzeeCategory.FullHouse,
            YahtzeeCategory.SmallStraight,
            YahtzeeCategory.LargeStraight,
            YahtzeeCategory.Yahtzee,
            YahtzeeCategory.Chance
        };

        public Scorecard()
        {
            Scores = new Dictionary<YahtzeeCategory, int?>(AllCategories.Length);
            Reset();
        }

        public Dictionary<YahtzeeCategory, int?> Scores { get; }

        public int YahtzeeBonusCount { get; private set; }

        public void Reset()
        {
            for (var index = 0; index < AllCategories.Length; index++)
                Scores[AllCategories[index]] = null;
            YahtzeeBonusCount = 0;
        }

        public void SetYahtzeeBonusCount(int count)
        {
            YahtzeeBonusCount = Math.Clamp(count, 0, MaxYahtzeeBonuses);
        }

        public int GetUpperSectionScore()
        {
            return SumFilled(UpperCategories);
        }

        public bool IsUpperBonusAchieved()
        {
            return GetUpperSectionScore() >= UpperBonusThreshold;
        }

        public int GetUpperBonus()
        {
            return IsUpperBonusAchieved() ? UpperBonusValue : 0;
        }

        public int GetLowerSectionScore()
        {
            return SumFilled(LowerCategories);
        }

        public int GetYahtzeeBonusTotal()
        {
            return YahtzeeBonusCount * YahtzeeBonusPoints;
        }

        public int GetTotalScore()
        {
            return GetUpperSectionScore() + GetUpperBonus() + GetLowerSectionScore() + GetYahtzeeBonusTotal();
        }

        public bool RegisterScore(YahtzeeCategory category, int score)
        {
            return RegisterScore(category, score, null);
        }

        public bool RegisterScore(YahtzeeCategory category, int score, int[] dice)
        {
            if (!IsKnown(category))
                throw new ArgumentOutOfRangeException(nameof(category), category, "Неизвестная категория Yahtzee.");

            if (dice != null)
            {
                var required = DiceGameEngine.RequiredUpper(dice, this);
                if (required.HasValue && required.Value != category)
                    return false;

                var expected = DiceGameEngine.CalculateScore(category, dice, this);
                if (expected != score)
                    throw new ArgumentOutOfRangeException(nameof(score), score, "Очки не совпали с classic Yahtzee.");
            }
            else if (!IsAccepted(category, score))
            {
                throw new ArgumentOutOfRangeException(nameof(score), score, "Очки не подходят для этой категории.");
            }

            if (Scores[category].HasValue)
                return false;

            var awardBonus = dice != null && DiceGameEngine.AwardsBonus(dice, this);
            Scores[category] = score;
            if (awardBonus && YahtzeeBonusCount < MaxYahtzeeBonuses)
                YahtzeeBonusCount++;

            return true;
        }

        public bool IsFullyCompleted()
        {
            for (var index = 0; index < AllCategories.Length; index++)
            {
                if (!Scores[AllCategories[index]].HasValue)
                    return false;
            }

            return true;
        }

        private int SumFilled(YahtzeeCategory[] categories)
        {
            var total = 0;
            for (var index = 0; index < categories.Length; index++)
            {
                var score = Scores[categories[index]];
                if (score.HasValue)
                    total += score.Value;
            }

            return total;
        }

        private static bool IsKnown(YahtzeeCategory category)
        {
            for (var index = 0; index < AllCategories.Length; index++)
            {
                if (AllCategories[index] == category)
                    return true;
            }

            return false;
        }

        private static bool IsAccepted(YahtzeeCategory category, int score)
        {
            if (score < 0)
                return false;

            switch (category)
            {
                case YahtzeeCategory.Ones:
                    return score <= 5;
                case YahtzeeCategory.Twos:
                    return score <= 10 && score % 2 == 0;
                case YahtzeeCategory.Threes:
                    return score <= 15 && score % 3 == 0;
                case YahtzeeCategory.Fours:
                    return score <= 20 && score % 4 == 0;
                case YahtzeeCategory.Fives:
                    return score <= 25 && score % 5 == 0;
                case YahtzeeCategory.Sixes:
                    return score <= 30 && score % 6 == 0;
                case YahtzeeCategory.ThreeOfAKind:
                case YahtzeeCategory.FourOfAKind:
                case YahtzeeCategory.Chance:
                    return score == 0 || (score >= 5 && score <= 30);
                case YahtzeeCategory.FullHouse:
                    return score == 0 || score == DiceGameEngine.FullHouseScore;
                case YahtzeeCategory.SmallStraight:
                    return score == 0 || score == DiceGameEngine.SmallStraightScore;
                case YahtzeeCategory.LargeStraight:
                    return score == 0 || score == DiceGameEngine.LargeStraightScore;
                case YahtzeeCategory.Yahtzee:
                    return score == 0 || score == DiceGameEngine.YahtzeeScore;
                default:
                    return false;
            }
        }
    }
}
