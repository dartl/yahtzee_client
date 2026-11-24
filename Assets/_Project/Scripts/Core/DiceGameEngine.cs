using System;
using System.Collections.Generic;
using Yahtzee.Models;

namespace Yahtzee.Core
{
    public enum YahtzeeCategory
    {
        Ones = 1,
        Twos = 2,
        Threes = 3,
        Fours = 4,
        Fives = 5,
        Sixes = 6,
        ThreeOfAKind = 7,
        FourOfAKind = 8,
        FullHouse = 9,
        SmallStraight = 10,
        LargeStraight = 11,
        Yahtzee = 12,
        Chance = 13
    }

    /// <summary>
    /// Classic Yahtzee scoring — must match Laravel CategoryScorer / YahtzeeScore.
    /// </summary>
    public static class DiceGameEngine
    {
        public static int DiceCount => YahtzeeConfig.Current.DiceCount;
        public static int FullHouseScore => YahtzeeConfig.Current.FullHouseScore;
        public static int SmallStraightScore => YahtzeeConfig.Current.SmallStraightScore;
        public static int LargeStraightScore => YahtzeeConfig.Current.LargeStraightScore;
        public static int YahtzeeScore => YahtzeeConfig.Current.YahtzeeScore;
        public static int YahtzeeBonusPoints => YahtzeeConfig.Current.YahtzeeBonusPoints;
        public static int MaxYahtzeeBonuses => YahtzeeConfig.Current.MaxYahtzeeBonuses;

        public static int CalculateScore(YahtzeeCategory category, int[] dice)
        {
            return CalculateScore(category, dice, null);
        }

        public static int CalculateScore(YahtzeeCategory category, int[] dice, Scorecard sheet)
        {
            Validate(dice);
            Span<int> counts = stackalloc int[7];
            var total = Tally(dice, counts);

            if (sheet != null && IsJoker(dice, sheet) && !IsUpper(category))
            {
                switch (category)
                {
                    case YahtzeeCategory.FullHouse:
                        return FullHouseScore;
                    case YahtzeeCategory.SmallStraight:
                        return SmallStraightScore;
                    case YahtzeeCategory.LargeStraight:
                        return LargeStraightScore;
                    case YahtzeeCategory.Yahtzee:
                        return YahtzeeScore;
                    case YahtzeeCategory.ThreeOfAKind:
                    case YahtzeeCategory.FourOfAKind:
                    case YahtzeeCategory.Chance:
                        return total;
                    default:
                        return Score(category, counts, total);
                }
            }

            return Score(category, counts, total);
        }

        public static bool IsJoker(int[] dice, Scorecard sheet)
        {
            if (sheet == null || dice == null)
                return false;

            if (!sheet.Scores.TryGetValue(YahtzeeCategory.Yahtzee, out var yahtzee) || yahtzee != YahtzeeScore)
                return false;

            return IsYahtzee(dice);
        }

        public static bool AwardsBonus(int[] dice, Scorecard sheet)
        {
            return IsJoker(dice, sheet);
        }

        /// <summary>
        /// After the first Yahtzee is filled with 50, further Yahtzees must go to the matching upper box if open.
        /// </summary>
        public static YahtzeeCategory? RequiredUpper(int[] dice, Scorecard sheet)
        {
            if (!IsJoker(dice, sheet) || dice.Length == 0)
                return null;

            var upper = UpperForFace(dice[0]);
            if (!sheet.Scores.TryGetValue(upper, out var value) || value.HasValue)
                return null;

            return upper;
        }

        public static bool IsYahtzee(int[] dice)
        {
            Validate(dice);
            Span<int> counts = stackalloc int[7];
            Tally(dice, counts);
            return MaxCount(counts) == DiceCount;
        }

        public static Dictionary<YahtzeeCategory, int> GetAllAvailableScores(int[] dice)
        {
            return GetAllAvailableScores(dice, null);
        }

        public static Dictionary<YahtzeeCategory, int> GetAllAvailableScores(int[] dice, Scorecard sheet)
        {
            Validate(dice);
            var map = new Dictionary<YahtzeeCategory, int>(13);
            for (var value = 1; value <= 13; value++)
            {
                var category = (YahtzeeCategory)value;
                map[category] = CalculateScore(category, dice, sheet);
            }

            return map;
        }

        public static void Validate(int[] dice)
        {
            if (dice == null || dice.Length != DiceCount)
                throw new ArgumentException("Массив костей должен содержать ровно 5 значений.", nameof(dice));

            for (var index = 0; index < DiceCount; index++)
            {
                var face = dice[index];
                if (face < 1 || face > 6)
                    throw new ArgumentException("Грань кости должна быть от 1 до 6.", nameof(dice));
            }
        }

        public static bool IsUpper(YahtzeeCategory category)
        {
            return category >= YahtzeeCategory.Ones && category <= YahtzeeCategory.Sixes;
        }

        public static YahtzeeCategory UpperForFace(int face)
        {
            switch (face)
            {
                case 1: return YahtzeeCategory.Ones;
                case 2: return YahtzeeCategory.Twos;
                case 3: return YahtzeeCategory.Threes;
                case 4: return YahtzeeCategory.Fours;
                case 5: return YahtzeeCategory.Fives;
                case 6: return YahtzeeCategory.Sixes;
                default:
                    throw new ArgumentOutOfRangeException(nameof(face), face, "Грань кости должна быть от 1 до 6.");
            }
        }

        private static int Score(YahtzeeCategory category, Span<int> counts, int total)
        {
            switch (category)
            {
                case YahtzeeCategory.Ones:
                    return counts[1];
                case YahtzeeCategory.Twos:
                    return 2 * counts[2];
                case YahtzeeCategory.Threes:
                    return 3 * counts[3];
                case YahtzeeCategory.Fours:
                    return 4 * counts[4];
                case YahtzeeCategory.Fives:
                    return 5 * counts[5];
                case YahtzeeCategory.Sixes:
                    return 6 * counts[6];
                case YahtzeeCategory.ThreeOfAKind:
                    return MaxCount(counts) >= 3 ? total : 0;
                case YahtzeeCategory.FourOfAKind:
                    return MaxCount(counts) >= 4 ? total : 0;
                case YahtzeeCategory.FullHouse:
                    return IsFullHouse(counts) ? FullHouseScore : 0;
                case YahtzeeCategory.SmallStraight:
                    return HasStraight(counts, 4) ? SmallStraightScore : 0;
                case YahtzeeCategory.LargeStraight:
                    return HasStraight(counts, 5) ? LargeStraightScore : 0;
                case YahtzeeCategory.Yahtzee:
                    return MaxCount(counts) == DiceCount ? YahtzeeScore : 0;
                case YahtzeeCategory.Chance:
                    return total;
                default:
                    throw new ArgumentOutOfRangeException(nameof(category), category, "Неизвестная категория Yahtzee.");
            }
        }

        private static int Tally(int[] dice, Span<int> counts)
        {
            var total = 0;
            for (var index = 0; index < DiceCount; index++)
            {
                var face = dice[index];
                counts[face]++;
                total += face;
            }

            return total;
        }

        private static int MaxCount(Span<int> counts)
        {
            var max = 0;
            for (var face = 1; face <= 6; face++)
            {
                if (counts[face] > max)
                    max = counts[face];
            }

            return max;
        }

        private static bool IsFullHouse(Span<int> counts)
        {
            var pair = false;
            var triple = false;
            for (var face = 1; face <= 6; face++)
            {
                switch (counts[face])
                {
                    case 0:
                        break;
                    case 2:
                        pair = true;
                        break;
                    case 3:
                        triple = true;
                        break;
                    default:
                        return false;
                }
            }

            return pair && triple;
        }

        private static bool HasStraight(Span<int> counts, int length)
        {
            var run = 0;
            var best = 0;
            for (var face = 1; face <= 6; face++)
            {
                if (counts[face] > 0)
                {
                    run++;
                    if (run > best)
                        best = run;
                }
                else
                {
                    run = 0;
                }
            }

            return best >= length;
        }
    }
}
