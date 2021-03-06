﻿namespace TexasHoldem.Logic.GameMechanics
{
    using System.Collections.Generic;
    using System.Linq;

    using TexasHoldem.Logic.Players;

    internal class BettingLogic
    {
        private readonly int initialPlayerIndex;

        private readonly IList<IInternalPlayer> allPlayers;

        private readonly int smallBlind;

        private int lastRoundBet;

        private int lastStepBet;

        private string aggressorName;

        private List<SidePot> sidePots;

        private List<int> boundsOfSidePots;

        private int lowerBound;

        public BettingLogic(IList<IInternalPlayer> players, int smallBlind)
        {
            this.initialPlayerIndex = players.Count == 2 ? 0 : 1;
            this.allPlayers = players;
            this.smallBlind = smallBlind;
            this.RoundBets = new List<PlayerActionAndName>();

            this.lastRoundBet = 2 * smallBlind; // Big blind
            this.lastStepBet = this.lastRoundBet;
            this.aggressorName = string.Empty;

            this.sidePots = new List<SidePot>();

            this.boundsOfSidePots = new List<int>();
            this.lowerBound = 0;
        }

        public int Pot
        {
            get
            {
                return this.allPlayers.Sum(x => x.PlayerMoney.CurrentlyInPot);
            }
        }

        public IReadOnlyCollection<SidePot> SidePots
        {
            get
            {
                return this.sidePots.AsReadOnly();
            }
        }

        public List<PlayerActionAndName> RoundBets { get; }

        public void Bet(GameRoundType gameRoundType)
        {
            this.RoundBets.Clear();
            var playerIndex = gameRoundType == GameRoundType.PreFlop
                ? this.initialPlayerIndex
                : 1;

            if (gameRoundType == GameRoundType.PreFlop)
            {
                this.PlaceBlinds();
                playerIndex = this.initialPlayerIndex + 2;
            }

            if (this.allPlayers.Count(x => x.PlayerMoney.ShouldPlayInRound) <= 1)
            {
                return;
            }

            while (this.allPlayers.Count(x => x.PlayerMoney.InHand) >= 2
                   && this.allPlayers.Any(x => x.PlayerMoney.ShouldPlayInRound))
            {
                var player = this.allPlayers[playerIndex % this.allPlayers.Count];
                if (player.PlayerMoney.Money <= 0)
                {
                    // Players who are all-in are not involved in betting round
                    player.PlayerMoney.ShouldPlayInRound = false;
                    playerIndex++;
                    continue;
                }

                if (!player.PlayerMoney.InHand || !player.PlayerMoney.ShouldPlayInRound)
                {
                    if (player.PlayerMoney.InHand == player.PlayerMoney.ShouldPlayInRound)
                    {
                        playerIndex++;
                    }

                    continue;
                }

                var maxMoneyPerPlayer = this.allPlayers.Max(x => x.PlayerMoney.CurrentRoundBet);
                var action =
                    player.GetTurn(
                        new GetTurnContext(
                            gameRoundType,
                            this.RoundBets.AsReadOnly(),
                            this.smallBlind,
                            player.PlayerMoney.Money,
                            this.Pot,
                            player.PlayerMoney.CurrentRoundBet,
                            maxMoneyPerPlayer,
                            this.MinRaise(maxMoneyPerPlayer, player.Name)));

                action = player.PlayerMoney.DoPlayerAction(action, maxMoneyPerPlayer);
                this.RoundBets.Add(new PlayerActionAndName(player.Name, action));

                if (action.Type == PlayerActionType.Raise)
                {
                    // When raising, all players are required to do action afterwards in current round
                    foreach (var playerToUpdate in this.allPlayers)
                    {
                        playerToUpdate.PlayerMoney.ShouldPlayInRound = playerToUpdate.PlayerMoney.InHand ? true : false;
                    }
                }

                if (player.PlayerMoney.Money <= 0)
                {
                    this.boundsOfSidePots.Add(player.PlayerMoney.CurrentlyInPot);
                }

                player.PlayerMoney.ShouldPlayInRound = false;
                playerIndex++;
            }

            foreach (var upperBound in this.boundsOfSidePots.Where(x => x > this.lowerBound).OrderBy(x => x))
            {
                var namesOfParticipants = new List<string>();
                var amount = 0;
                foreach (var item in this.allPlayers)
                {
                    if (item.PlayerMoney.CurrentlyInPot > this.lowerBound)
                    {
                        // The player participates in the side pot
                        namesOfParticipants.Add(item.Name);
                        if (item.PlayerMoney.CurrentlyInPot >= upperBound)
                        {
                            amount += upperBound - this.lowerBound;
                        }
                        else
                        {
                            amount += item.PlayerMoney.CurrentlyInPot - this.lowerBound;
                        }
                    }
                }

                this.sidePots.Add(new SidePot(
                    amount,
                    namesOfParticipants));
                this.lowerBound = upperBound;
            }

            if (this.allPlayers.Count == 2)
            {
                // works only for head-up
                this.ReturnMoneyInCaseOfAllIn();
            }
            else
            {
                this.ReturnMoneyInCaseUncalledBet();
            }
        }

        private void PlaceBlinds()
        {
            // Small blind
            this.RoundBets.Add(
                new PlayerActionAndName(
                    this.allPlayers[this.initialPlayerIndex].Name,
                    this.allPlayers[this.initialPlayerIndex].PostingBlind(
                        new PostingBlindContext(
                            this.allPlayers[this.initialPlayerIndex].PlayerMoney.DoPlayerAction(PlayerAction.Post(this.smallBlind), 0),
                            0,
                            this.allPlayers[this.initialPlayerIndex].PlayerMoney.Money))));

            // Big blind
            this.RoundBets.Add(
                new PlayerActionAndName(
                    this.allPlayers[this.initialPlayerIndex + 1].Name,
                    this.allPlayers[this.initialPlayerIndex + 1].PostingBlind(
                        new PostingBlindContext(
                            this.allPlayers[this.initialPlayerIndex + 1].PlayerMoney.DoPlayerAction(PlayerAction.Post(2 * this.smallBlind), 0),
                            this.Pot,
                            this.allPlayers[this.initialPlayerIndex + 1].PlayerMoney.Money))));
        }

        private void ReturnMoneyInCaseOfAllIn()
        {
            var minMoneyPerPlayer = this.allPlayers.Min(x => x.PlayerMoney.CurrentRoundBet);
            foreach (var player in this.allPlayers)
            {
                player.PlayerMoney.NormalizeBets(minMoneyPerPlayer);
            }
        }

        private void ReturnMoneyInCaseUncalledBet()
        {
            var group = this.allPlayers.GroupBy(x => x.PlayerMoney.CurrentRoundBet).OrderByDescending(k => k.Key);
            if (group.First().Count() == 1)
            {
                group.First().First().PlayerMoney.NormalizeBets(group.ElementAt(1).First().PlayerMoney.CurrentRoundBet);
            }
        }

        private int MinRaise(int maxMoneyPerPlayer, string currentPlayerName)
        {
            /*
             * MinRaise =
             *      The investment of the current aggressor
             *      +
             *      (The investment of the current aggressor - The investment of the previous aggressor)
             * Examples:
             * 1. SB->5$; BB->10$; UTG->raise 35$; MP->MinRaise=60$ (35$ + 25$)
             * 2. SB->5$; BB->10$; UTG->raise 35$; MP->re-raise 150$; CO->call 150$; BTN->MinRaise=265$ (150$ + 115$)
             * 3. SB->5$; BB->10$; UTG->raise 12$(ALL-IN); MP->MinRaise=22$ (12$ + 10$(BB is step of initial raiser))
            */

            if (maxMoneyPerPlayer == 0)
            {
                // Start postflop. Players did not bet.
                this.lastRoundBet = 0;
                this.lastStepBet = 2 * this.smallBlind; // Big blind
                this.aggressorName = string.Empty;
            }

            if (this.aggressorName == currentPlayerName)
            {
                /*
                 * SB->5$; BB->10$; BTN->raise 32$; SB->re-raise 34$(ALL-IN); BB->call 34$; BTN->MinRaise=-1$ (SB has made a not full raise);
                 * Since no players completed a full raise over BTN's initial raise,
                 * neither BTN nor BB are allowed to re-raise here. Their only options are to call the 34$, or fold.
                */
                return -1;
            }

            if (maxMoneyPerPlayer > this.lastRoundBet)
            {
                // We check for the fullness of the raise
                if (this.lastRoundBet + this.lastStepBet <= maxMoneyPerPlayer)
                {
                    this.lastStepBet = maxMoneyPerPlayer - this.lastRoundBet;
                    this.lastRoundBet = maxMoneyPerPlayer;
                    this.aggressorName = this.RoundBets.Last().PlayerName;
                }
                else
                {
                    /*
                     * For example, we are sitting on CO
                     * SB->5$; BB->10$; UTG->raise 35$; MP->re-raise 37$(ALL-IN); CO->MinRaise 62$ (37$ + 25$(UTG is step of initial raiser))
                     * then maxMoneyPerPlayer=37$ and MinRaise=maxMoneyPerPlayer+step(25$)
                    */
                    this.lastRoundBet = maxMoneyPerPlayer;
                }
            }

            return this.lastRoundBet + this.lastStepBet;
        }
    }
}