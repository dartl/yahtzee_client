using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Yahtzee.Models;
using Yahtzee.UI;

namespace Yahtzee.Core
{
    public enum AIDifficulty
    {
        Beginner,
        Professional
    }

    public sealed class AIOpponent
    {
        public const float BeginnerKeepMistakeChance = 0.44f;
        public const float BeginnerScoreMistakeChance = 0.34f;
        public const float ProfessionalKeepMistakeChance = 0f;
        public const float ProfessionalScoreMistakeChance = 0f;

        private const int ThinkMilliseconds = 1200;

        public static AIDifficulty RequestedDifficulty { get; private set; } = AIDifficulty.Professional;

        public AIDifficulty currentDifficulty = AIDifficulty.Professional;

        private static readonly YahtzeeCategory[] ScratchOrder =
        {
            YahtzeeCategory.Yahtzee,
            YahtzeeCategory.LargeStraight,
            YahtzeeCategory.FullHouse,
            YahtzeeCategory.SmallStraight,
            YahtzeeCategory.FourOfAKind,
            YahtzeeCategory.Ones,
            YahtzeeCategory.Twos,
            YahtzeeCategory.Threes,
            YahtzeeCategory.ThreeOfAKind,
            YahtzeeCategory.Fours,
            YahtzeeCategory.Fives,
            YahtzeeCategory.Sixes,
            YahtzeeCategory.Chance
        };

        public static void ChooseForNextMatch(AIDifficulty difficulty)
        {
            RequestedDifficulty = difficulty;
        }

        public void SetDifficulty(AIDifficulty difficulty)
        {
            currentDifficulty = difficulty;
        }

        public async Task PlayTurn(GameplayScreen screen, DicePhysicsController physics, Scorecard aiScorecard, int epoch)
        {
            if (!StillPlaying(screen, epoch) || aiScorecard == null || aiScorecard.IsFullyCompleted())
                return;

            await Task.Delay(ThinkMilliseconds);
            if (!StillPlaying(screen, epoch))
                return;

            var count = DiceGameEngine.DiceCount;
            var dice = new int[count];
            var rollsLeft = YahtzeeConfig.Current.RollsPerTurn;
            RollOpenDice(dice, null);
            rollsLeft--;
            await screen.PresentBotRoll(aiScorecard, dice, rollsLeft);
            if (!StillPlaying(screen, epoch))
                return;

            while (rollsLeft > 0 && !IsContent(dice, aiScorecard))
            {
                var keep = await DecideWhichDiceToKeep(dice, aiScorecard, rollsLeft);
                if (!StillPlaying(screen, epoch))
                    return;

                if (keep.Length >= count)
                    break;

                if (physics != null)
                    physics.SetKeptDice(keep);

                await Task.Delay(700);
                if (!StillPlaying(screen, epoch))
                    return;

                RollOpenDice(dice, keep);
                rollsLeft--;
                await screen.PresentBotRoll(aiScorecard, dice, rollsLeft);
                if (!StillPlaying(screen, epoch))
                    return;
            }

            await Task.Delay(ThinkMilliseconds);
            if (!StillPlaying(screen, epoch))
                return;

            var category = DecideFinalCategory(dice, aiScorecard);
            var score = DiceGameEngine.CalculateScore(category, dice, aiScorecard);
            aiScorecard.RegisterScore(category, score, dice);
            if (!StillPlaying(screen, epoch))
                return;

            if (physics != null)
                physics.ClearTable();

            screen.ShowBotResult(aiScorecard);
            await Task.Delay(ThinkMilliseconds);
        }

        private static bool StillPlaying(GameplayScreen screen, int epoch)
        {
            return screen != null && screen.BotStillPlaying(epoch);
        }

        public async Task<int[]> DecideWhichDiceToKeep(int[] currentDice, Scorecard aiScorecard, int rollsLeft)
        {
            await Task.Delay(ThinkMilliseconds);
            DiceGameEngine.Validate(currentDice);
            if (aiScorecard == null || rollsLeft <= 0)
                return AllIndices(currentDice.Length);

            var planned = PlanProfessionalKeep(currentDice, aiScorecard);
            if (UnityEngine.Random.value < KeepMistakeChance)
                return PlanBeginnerKeep(planned);

            return planned;
        }

        public YahtzeeCategory DecideFinalCategory(int[] finalDice, Scorecard aiScorecard)
        {
            DiceGameEngine.Validate(finalDice);
            if (aiScorecard == null)
                throw new ArgumentNullException(nameof(aiScorecard));

            var forced = DiceGameEngine.RequiredUpper(finalDice, aiScorecard);
            if (forced.HasValue && Open(aiScorecard, forced.Value))
                return forced.Value;

            var scoring = OpenByScore(finalDice, aiScorecard, true);
            if (scoring.Count > 0)
                return ChooseScoringCategory(finalDice, aiScorecard, scoring);

            var nothing = OpenByScore(finalDice, aiScorecard, false);
            if (nothing.Count == 0)
                throw new InvalidOperationException("У бота не осталось свободных категорий.");

            return nothing[0];
        }

        private static int[] PlanProfessionalKeep(int[] currentDice, Scorecard aiScorecard)
        {
            var counts = CountFaces(currentDice);
            var ofAKindOpen = OfAKindOpen(aiScorecard);
            var straightsOpen = Open(aiScorecard, YahtzeeCategory.SmallStraight) || Open(aiScorecard, YahtzeeCategory.LargeStraight);

            if (HighestCount(counts) >= 5 && (Open(aiScorecard, YahtzeeCategory.Yahtzee) || ofAKindOpen))
                return AllIndices(currentDice.Length);

            if (HighestCount(counts) >= 4 && (Open(aiScorecard, YahtzeeCategory.FourOfAKind) || Open(aiScorecard, YahtzeeCategory.Yahtzee) || ofAKindOpen))
                return IndicesOfFace(currentDice, FaceWithCount(counts, 4));

            if (IsFullHouse(counts) && Open(aiScorecard, YahtzeeCategory.FullHouse))
                return AllIndices(currentDice.Length);

            if (straightsOpen)
            {
                var run = LongestRun(counts, 4, out var runStart);
                if (run >= 4)
                    return IndicesOfRun(currentDice, runStart, run);
            }

            if (HighestCount(counts) >= 3 && ofAKindOpen)
                return IndicesOfFace(currentDice, FaceWithCount(counts, 3));

            if (straightsOpen)
            {
                var run = LongestRun(counts, 3, out var runStart);
                if (run >= 3)
                    return IndicesOfRun(currentDice, runStart, run);
            }

            if (ofAKindOpen || HasOpenUpper(aiScorecard, 1))
            {
                var pair = BestPairFace(counts, aiScorecard);
                if (pair > 0)
                    return IndicesOfFace(currentDice, pair);
            }

            var upper = KeepBestOpenUpper(currentDice, aiScorecard);
            if (upper.Length > 0)
                return upper;

            if (ofAKindOpen || Open(aiScorecard, YahtzeeCategory.Chance))
                return IndicesAtLeast(currentDice, 4);

            return Array.Empty<int>();
        }

        private static int[] PlanBeginnerKeep(int[] ideal)
        {
            if (ideal == null || ideal.Length < 2)
                return ideal ?? Array.Empty<int>();

            return DropOneKeptDie(ideal, DiceGameEngine.DiceCount);
        }

        private static List<YahtzeeCategory> OpenByScore(int[] dice, Scorecard card, bool withPoints)
        {
            var open = new List<YahtzeeCategory>(ScratchOrder.Length);
            for (var index = 0; index < ScratchOrder.Length; index++)
            {
                var category = ScratchOrder[index];
                if (!Open(card, category))
                    continue;

                var scores = DiceGameEngine.CalculateScore(category, dice, card) > 0;
                if (scores == withPoints)
                    open.Add(category);
            }

            return open;
        }

        private YahtzeeCategory ChooseScoringCategory(int[] dice, Scorecard card, List<YahtzeeCategory> scoring)
        {
            var best = PlanProfessionalCategory(dice, card, scoring);
            if (scoring.Count < 2 || UnityEngine.Random.value >= ScoreMistakeChance)
                return best;

            return SecondBestCategory(dice, card, scoring, best);
        }

        private float KeepMistakeChance => currentDifficulty == AIDifficulty.Beginner
            ? BeginnerKeepMistakeChance
            : ProfessionalKeepMistakeChance;

        private float ScoreMistakeChance => currentDifficulty == AIDifficulty.Beginner
            ? BeginnerScoreMistakeChance
            : ProfessionalScoreMistakeChance;

        private static YahtzeeCategory PlanProfessionalCategory(int[] finalDice, Scorecard aiScorecard, List<YahtzeeCategory> candidates)
        {
            var forced = DiceGameEngine.RequiredUpper(finalDice, aiScorecard);
            if (forced.HasValue && Open(aiScorecard, forced.Value))
                return forced.Value;

            var bestCategory = candidates[0];
            var bestValue = int.MinValue;
            var bestScratch = int.MaxValue;

            for (var index = 0; index < candidates.Count; index++)
            {
                var category = candidates[index];

                var value = EffectiveScore(aiScorecard, category, DiceGameEngine.CalculateScore(category, finalDice, aiScorecard));
                if (DiceGameEngine.AwardsBonus(finalDice, aiScorecard))
                    value += DiceGameEngine.YahtzeeBonusPoints;
                var scratch = ScratchIndex(category);
                if (index > 0 && (value < bestValue || (value == bestValue && scratch >= bestScratch)))
                    continue;

                bestCategory = category;
                bestValue = value;
                bestScratch = scratch;
            }

            return bestCategory;
        }

        private static YahtzeeCategory SecondBestCategory(int[] dice, Scorecard card, List<YahtzeeCategory> candidates, YahtzeeCategory best)
        {
            var second = best;
            var secondValue = int.MinValue;
            var found = false;
            for (var index = 0; index < candidates.Count; index++)
            {
                var category = candidates[index];
                if (category == best)
                    continue;

                var required = DiceGameEngine.RequiredUpper(dice, card);
                if (required.HasValue && required.Value != category)
                    continue;

                var value = EffectiveScore(card, category, DiceGameEngine.CalculateScore(category, dice, card));
                if (DiceGameEngine.AwardsBonus(dice, card))
                    value += DiceGameEngine.YahtzeeBonusPoints;
                if (found && value <= secondValue)
                    continue;

                found = true;
                second = category;
                secondValue = value;
            }

            return second;
        }

        private static int ScratchIndex(YahtzeeCategory category)
        {
            for (var index = 0; index < ScratchOrder.Length; index++)
            {
                if (ScratchOrder[index] == category)
                    return index;
            }

            return ScratchOrder.Length;
        }

        private static int[] DropOneKeptDie(int[] ideal, int diceCount)
        {
            if (ideal == null || ideal.Length == 0)
                return RandomKeep(diceCount, 1);

            if (ideal.Length == 1)
                return Array.Empty<int>();

            var drop = ideal[UnityEngine.Random.Range(0, ideal.Length)];
            var kept = new int[ideal.Length - 1];
            var write = 0;
            for (var index = 0; index < ideal.Length; index++)
            {
                if (ideal[index] == drop)
                    continue;

                kept[write] = ideal[index];
                write++;
            }

            return kept;
        }

        private static int[] RandomKeep(int diceCount, int keepCount)
        {
            keepCount = Mathf.Clamp(keepCount, 1, Math.Max(1, diceCount));
            var pool = AllIndices(diceCount);
            for (var index = pool.Length - 1; index > 0; index--)
            {
                var swap = UnityEngine.Random.Range(0, index + 1);
                var value = pool[index];
                pool[index] = pool[swap];
                pool[swap] = value;
            }

            var kept = new int[keepCount];
            Array.Copy(pool, kept, keepCount);
            Array.Sort(kept);
            return kept;
        }

        private static bool IsContent(int[] dice, Scorecard card)
        {
            if (DiceGameEngine.CalculateScore(YahtzeeCategory.Yahtzee, dice) > 0 && Open(card, YahtzeeCategory.Yahtzee))
                return true;

            if (DiceGameEngine.CalculateScore(YahtzeeCategory.LargeStraight, dice) > 0 && Open(card, YahtzeeCategory.LargeStraight))
                return true;

            if (DiceGameEngine.CalculateScore(YahtzeeCategory.FullHouse, dice) > 0 && Open(card, YahtzeeCategory.FullHouse))
                return true;

            var counts = CountFaces(dice);
            var best = HighestCount(counts);
            if (best >= 4 && Open(card, YahtzeeCategory.Yahtzee))
                return false;

            if (DiceGameEngine.CalculateScore(YahtzeeCategory.FourOfAKind, dice) > 0 && Open(card, YahtzeeCategory.FourOfAKind))
                return true;

            if (DiceGameEngine.CalculateScore(YahtzeeCategory.SmallStraight, dice) > 0
                && Open(card, YahtzeeCategory.SmallStraight)
                && (!Open(card, YahtzeeCategory.LargeStraight) || DiceGameEngine.CalculateScore(YahtzeeCategory.LargeStraight, dice) > 0))
                return true;

            if (OfAKindOpen(card))
                return false;

            var straightsOpen = Open(card, YahtzeeCategory.SmallStraight) || Open(card, YahtzeeCategory.LargeStraight);
            if (straightsOpen)
                return false;

            for (var face = 6; face >= 1; face--)
            {
                if (counts[face] >= 3 && Open(card, (YahtzeeCategory)face))
                    return true;
            }

            return false;
        }

        private static int EffectiveScore(Scorecard card, YahtzeeCategory category, int raw)
        {
            if (raw <= 0 || category > YahtzeeCategory.Sixes)
                return raw;

            var upper = card.GetUpperSectionScore();
            if (upper < Scorecard.UpperBonusThreshold && upper + raw >= Scorecard.UpperBonusThreshold)
                return raw + Scorecard.UpperBonusValue;

            return raw;
        }

        private static void RollOpenDice(int[] dice, int[] keep)
        {
            var held = new bool[dice.Length];
            if (keep != null)
            {
                for (var index = 0; index < keep.Length; index++)
                {
                    var slot = keep[index];
                    if (slot >= 0 && slot < held.Length)
                        held[slot] = true;
                }
            }

            for (var index = 0; index < dice.Length; index++)
            {
                if (held[index])
                    continue;

                dice[index] = UnityEngine.Random.Range(1, 7);
            }
        }

        private static int[] CountFaces(int[] dice)
        {
            var counts = new int[7];
            for (var index = 0; index < dice.Length; index++)
                counts[dice[index]]++;

            return counts;
        }

        private static int HighestCount(int[] counts)
        {
            return counts[BestFace(counts)];
        }

        private static int BestFace(int[] counts)
        {
            var face = 1;
            for (var value = 2; value <= 6; value++)
            {
                if (counts[value] > counts[face])
                    face = value;
            }

            return face;
        }

        private static int FaceWithCount(int[] counts, int minimum)
        {
            var face = 0;
            for (var value = 1; value <= 6; value++)
            {
                if (counts[value] >= minimum && (face == 0 || counts[value] > counts[face] || (counts[value] == counts[face] && value > face)))
                    face = value;
            }

            return face;
        }

        private static int BestPairFace(int[] counts, Scorecard card)
        {
            var ofAKind = OfAKindOpen(card);
            for (var value = 6; value >= 1; value--)
            {
                if (counts[value] < 2)
                    continue;

                if (ofAKind || Open(card, (YahtzeeCategory)value))
                    return value;
            }

            return 0;
        }

        private static int LongestRun(int[] counts, int minimum, out int startFace)
        {
            startFace = 0;
            var best = 0;
            var run = 0;
            var runStart = 1;
            for (var face = 1; face <= 6; face++)
            {
                if (counts[face] > 0)
                {
                    if (run == 0)
                        runStart = face;

                    run++;
                    if (run >= minimum && run >= best)
                    {
                        best = run;
                        startFace = runStart;
                    }
                }
                else
                {
                    run = 0;
                }
            }

            return best;
        }

        private static int[] KeepBestOpenUpper(int[] dice, Scorecard card)
        {
            var counts = CountFaces(dice);
            var bestFace = 0;
            var bestCount = 0;
            for (var face = 1; face <= 6; face++)
            {
                if (counts[face] == 0 || !Open(card, (YahtzeeCategory)face))
                    continue;

                if (counts[face] < bestCount || (counts[face] == bestCount && face < bestFace))
                    continue;

                bestFace = face;
                bestCount = counts[face];
            }

            if (bestFace == 0)
                return Array.Empty<int>();

            return IndicesOfFace(dice, bestFace);
        }

        private static int[] IndicesOfFace(int[] dice, int face)
        {
            var kept = new List<int>(dice.Length);
            for (var index = 0; index < dice.Length; index++)
            {
                if (dice[index] == face)
                    kept.Add(index);
            }

            return kept.ToArray();
        }

        private static int[] IndicesOfRun(int[] dice, int startFace, int length)
        {
            var kept = new List<int>(length);
            var used = new bool[7];
            for (var index = 0; index < dice.Length; index++)
            {
                var face = dice[index];
                if (face < startFace || face >= startFace + length || used[face])
                    continue;

                used[face] = true;
                kept.Add(index);
            }

            return kept.ToArray();
        }

        private static int[] IndicesAtLeast(int[] dice, int minimumFace)
        {
            var kept = new List<int>(dice.Length);
            for (var index = 0; index < dice.Length; index++)
            {
                if (dice[index] >= minimumFace)
                    kept.Add(index);
            }

            return kept.ToArray();
        }

        private static int[] AllIndices(int length)
        {
            var indices = new int[length];
            for (var index = 0; index < length; index++)
                indices[index] = index;

            return indices;
        }

        private static bool IsFullHouse(int[] counts)
        {
            var pair = false;
            var triple = false;
            for (var face = 1; face <= 6; face++)
            {
                if (counts[face] == 2)
                    pair = true;
                else if (counts[face] == 3)
                    triple = true;
                else if (counts[face] != 0)
                    return false;
            }

            return pair && triple;
        }

        private static bool OfAKindOpen(Scorecard card)
        {
            return Open(card, YahtzeeCategory.ThreeOfAKind)
                || Open(card, YahtzeeCategory.FourOfAKind)
                || Open(card, YahtzeeCategory.FullHouse)
                || Open(card, YahtzeeCategory.Yahtzee);
        }

        private static bool HasOpenUpper(Scorecard card, int minimumFace)
        {
            for (var face = minimumFace; face <= 6; face++)
            {
                if (Open(card, (YahtzeeCategory)face))
                    return true;
            }

            return false;
        }

        private static bool Open(Scorecard card, YahtzeeCategory category)
        {
            return card.Scores.TryGetValue(category, out var score) && !score.HasValue;
        }
    }
}
