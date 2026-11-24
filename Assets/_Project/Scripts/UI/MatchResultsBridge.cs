using System;
using System.Collections.Generic;
using Yahtzee.Network;

namespace Yahtzee.UI
{
    public sealed class MatchResultRow
    {
        public string Nickname;
        public int Score;
        public int MmrDelta;
        public bool IsLocal;
        public bool IsBot;
    }

    public static class MatchResultsBridge
    {
        public static IReadOnlyList<MatchResultRow> Rows { get; private set; } = Array.Empty<MatchResultRow>();
        public static string Headline { get; private set; } = "Итоги партии";
        public static bool CanRematch { get; private set; }
        public static string RematchLabel { get; private set; } = "Ещё раз";
        public static Action RematchAction { get; private set; }
        public static int PresentationId { get; private set; }

        public static void Present(
            IList<MatchResultRow> rows,
            string headline,
            bool canRematch,
            Action rematch = null,
            string rematchLabel = null)
        {
            Rows = rows != null ? new List<MatchResultRow>(rows) : new List<MatchResultRow>();
            Headline = string.IsNullOrWhiteSpace(headline) ? "Итоги партии" : headline;
            CanRematch = canRematch;
            RematchAction = rematch;
            RematchLabel = string.IsNullOrWhiteSpace(rematchLabel) ? "Ещё раз" : rematchLabel;
            PresentationId++;
            MonetizationService.TryShowInterstitialAfterMatch();

            if (UIManager.Instance != null)
                UIManager.Instance.SwitchToScreen(ScreenType.MatchResults);
        }

        public static void PresentFromStandings(
            Standing[] standings,
            int? localUserId,
            string reason,
            int[] winnerUserIds = null)
        {
            var rows = new List<MatchResultRow>();
            if (standings != null)
            {
                for (var index = 0; index < standings.Length; index++)
                {
                    var seat = standings[index];
                    if (seat == null)
                        continue;

                    rows.Add(new MatchResultRow
                    {
                        Nickname = string.IsNullOrWhiteSpace(seat.Nickname) ? (seat.IsBot ? "Бот" : "Игрок") : seat.Nickname,
                        Score = seat.FinalScore,
                        MmrDelta = seat.MmrDelta,
                        IsLocal = localUserId.HasValue && seat.UserId == localUserId,
                        IsBot = seat.IsBot
                    });
                }
            }

            var forfeitWin = false;
            if (localUserId.HasValue && winnerUserIds != null)
            {
                for (var index = 0; index < winnerUserIds.Length; index++)
                {
                    if (winnerUserIds[index] == localUserId.Value)
                    {
                        forfeitWin = true;
                        break;
                    }
                }
            }

            var isForfeit = reason == "forfeit";
            var isAbandoned = reason == "abandoned";
            if (isForfeit && forfeitWin)
            {
                rows.Sort((a, b) =>
                {
                    if (a.IsLocal != b.IsLocal)
                        return a.IsLocal ? -1 : 1;
                    return b.Score.CompareTo(a.Score);
                });
            }
            else
            {
                rows.Sort((a, b) => b.Score.CompareTo(a.Score));
            }

            var won = forfeitWin;
            if (!won && !isForfeit && !isAbandoned)
            {
                for (var index = 0; index < rows.Count; index++)
                {
                    if (rows[index].IsLocal && (index == 0 || rows[index].Score == rows[0].Score))
                        won = true;
                }
            }

            string headline;
            if (won)
                headline = "Победа!";
            else if (isAbandoned)
                headline = "Матч завершён";
            else if (isForfeit)
                headline = "Партия окончена";
            else
                headline = "Партия окончена";

            Present(rows, headline, false);
        }

        public static void Clear()
        {
            Rows = Array.Empty<MatchResultRow>();
            RematchAction = null;
            CanRematch = false;
            RematchLabel = "Ещё раз";
        }
    }
}
