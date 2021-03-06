﻿namespace TexasHoldem.Logic.Tests.GameMechanics
{
    using System.Collections.Generic;
    using System.Linq;

    using Moq;
    using NUnit.Framework;

    using TexasHoldem.Logic.GameMechanics;
    using TexasHoldem.Logic.Players;

    [TestFixture]
    public class BettingLogicTests
    {
        [Test]
        public void MinRaiseShouldReturnANegativeValueOnce()
        {
            var minRaises = new List<int>();

            var mockBasePlayer1 = new Mock<BasePlayer>();
            mockBasePlayer1.SetupGet(x => x.Name).Returns("Player_1");
            mockBasePlayer1.Setup(x => x.GetTurn(It.IsAny<IGetTurnContext>()))
                .Returns(PlayerAction.CheckOrCall())
                .Callback<IGetTurnContext>(x => minRaises.Add(x.MinRaise));

            var mockBasePlayer2 = new Mock<BasePlayer>();
            mockBasePlayer2.SetupGet(x => x.Name).Returns("Player_2");
            mockBasePlayer2.Setup(x => x.GetTurn(It.IsAny<IGetTurnContext>()))
                .Returns(() =>
                {
                    if (minRaises.Count > 0)
                    {
                        return PlayerAction.CheckOrCall();
                    }
                    else
                    {
                        return PlayerAction.Raise(35);
                    }
                })
                .Callback<IGetTurnContext>(x => minRaises.Add(x.MinRaise));

            var mockBasePlayer3 = new Mock<BasePlayer>();
            mockBasePlayer3.SetupGet(x => x.Name).Returns("Player_3");
            mockBasePlayer3.Setup(x => x.GetTurn(It.IsAny<IGetTurnContext>()))
                .Returns(PlayerAction.Raise(2))
                .Callback<IGetTurnContext>(x => minRaises.Add(x.MinRaise));

            var players = new List<IInternalPlayer>
            {
                new InternalPlayer(mockBasePlayer1.Object),
                new InternalPlayer(mockBasePlayer2.Object),
                new InternalPlayer(mockBasePlayer3.Object)
            };
            var playerNames = players.Select(x => x.Name).ToList().AsReadOnly();
            players[0].StartGame(new StartGameContext(playerNames, 100));
            players[1].StartGame(new StartGameContext(playerNames, 100));
            players[2].StartGame(new StartGameContext(playerNames, 37));

            var bettingLogic = new BettingLogic(players, 10);
            bettingLogic.Bet(GameRoundType.Flop);

            Assert.AreEqual(20, minRaises[0]); // The beginning of the round. There were no bets yet. MinRaise = 2 * smallBlind
            Assert.AreEqual(70, minRaises[1]); // Bet(initiator) = 35. MinRaise = 35 + 35
            Assert.AreEqual(72, minRaises[2]); // Not full raise(All-In) to 37. MinRaise = 37 + 35
            Assert.AreEqual(-1, minRaises[3]); // Raising is not possible because the queue has returned to the initiator
        }
    }
}
