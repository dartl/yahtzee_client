using UnityEngine;

namespace Yahtzee.Core
{
    public sealed class TutorialStep
    {
        public TutorialStep(
            string title,
            string hint,
            string holdHint,
            YahtzeeCategory category,
            params int[][] rolls)
        {
            Title = title;
            Hint = hint;
            HoldHint = holdHint;
            Category = category;
            Rolls = rolls;
        }

        public string Title { get; }
        public string Hint { get; }
        public string HoldHint { get; }
        public YahtzeeCategory Category { get; }
        public int[][] Rolls { get; }
    }

    public static class TutorialCampaign
    {
        public const string PrefKey = "yahtzee.onboarding.tips.v1";

        public static bool Pending { get; private set; }

        public static readonly TutorialStep[] Steps =
        {
            new TutorialStep(
                "Верхняя секция",
                "В «Единицах» считаются только кости с 1. Чем больше единиц — тем выше очки (бонус за верх при сумме ≥ 63).",
                "Оставьте единицы и нажмите «Бросок» ещё раз.",
                YahtzeeCategory.Ones,
                new[] { 1, 1, 3, 4, 5 },
                new[] { 1, 1, 1, 2, 6 }),
            new TutorialStep(
                "Тройка",
                "«Тройка» даёт сумму всех костей, если есть хотя бы три одинаковых. Иначе — 0.",
                "Держите тройку двоек и перебросьте остальное.",
                YahtzeeCategory.ThreeOfAKind,
                new[] { 2, 2, 5, 6, 3 },
                new[] { 2, 2, 2, 4, 1 }),
            new TutorialStep(
                "Фулл-хаус",
                "«Фулл-хаус» — три одинаковых + пара (например 3+3+3+5+5). Фиксированные 25 очков.",
                "Соберите тройку и пару — оставьте нужные кости.",
                YahtzeeCategory.FullHouse,
                new[] { 3, 3, 5, 2, 6 },
                new[] { 3, 3, 3, 5, 5 }),
            new TutorialStep(
                "Шанс",
                "«Шанс» — просто сумма всех пяти костей. Сюда записывают удачный бросок, если нет нужной комбинации.",
                "Оставьте крупные кости и перебросьте мелкие, чтобы поднять сумму.",
                YahtzeeCategory.Chance,
                new[] { 6, 5, 2, 1, 3 },
                new[] { 6, 5, 6, 4, 3 }),
            new TutorialStep(
                "Большой стрит",
                "«Большой стрит» — пять подряд (1–5 или 2–6), обычно 40 очков. Малый стрит — четыре подряд.",
                "Оставьте последовательность и добейте стрит.",
                YahtzeeCategory.LargeStraight,
                new[] { 1, 2, 3, 4, 6 },
                new[] { 1, 2, 3, 4, 5 }),
            new TutorialStep(
                "Яцзи",
                "«Яцзи» — все пять костей одинаковые. Это 50 очков и самая редкая комбинация.",
                "Держите шестёрки до пяти одинаковых.",
                YahtzeeCategory.Yahtzee,
                new[] { 6, 6, 6, 2, 4 },
                new[] { 6, 6, 6, 6, 1 },
                new[] { 6, 6, 6, 6, 6 }),
        };

        public static bool HasSeen => PlayerPrefs.GetInt(PrefKey, 0) != 0;

        public static void Prepare()
        {
            Pending = true;
        }

        public static void ConsumePending()
        {
            Pending = false;
        }

        public static void MarkSeen()
        {
            PlayerPrefs.SetInt(PrefKey, 1);
            PlayerPrefs.Save();
        }

        public static string CategoryTitle(YahtzeeCategory category)
        {
            switch (category)
            {
                case YahtzeeCategory.Ones: return "Единицы";
                case YahtzeeCategory.ThreeOfAKind: return "Тройка";
                case YahtzeeCategory.FullHouse: return "Фулл-хаус";
                case YahtzeeCategory.Chance: return "Шанс";
                case YahtzeeCategory.LargeStraight: return "Большой стрит";
                case YahtzeeCategory.Yahtzee: return "Яцзи";
                default: return category.ToString();
            }
        }

        public static string ScoreHint(TutorialStep step)
        {
            return "Выберите «" + CategoryTitle(step.Category) + "» и нажмите «Записать».";
        }
    }
}
