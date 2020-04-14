﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Schafkopf.Hubs;

namespace Schafkopf.Models
{
    public class Game
    {
        public List<Player> Players = new List<Player>();
        public List<Player> PlayingPlayers = new List<Player>();
        public static Card[] Cards = new Card[32];
        public Card[] MixedCards = null;
        public State CurrentGameState = State.Idle;
        public int[] Groups = new int[] { 0, 0, 0, 0 };
        //Rotates around after each game
        public int StartPlayer = 3;
        public int ActionPlayer = 0;
        public Boolean NewGame = false;
        public GameType AnnouncedGame = GameType.Ramsch;
        public Player Leader = null;
        public Player HusbandWife = null;
        public Trick Trick = new Trick();

        private Random random = new Random();

        public Game()
        {
            #region InitCards
            Cards[0] = new Card(Color.Schellen, 7);
            Cards[1] = new Card(Color.Schellen, 8);
            Cards[2] = new Card(Color.Schellen, 9);
            Cards[3] = new Card(Color.Schellen, 10);
            Cards[4] = new Card(Color.Schellen, 2);
            Cards[5] = new Card(Color.Schellen, 3);
            Cards[6] = new Card(Color.Schellen, 4);
            Cards[7] = new Card(Color.Schellen, 11);

            Cards[8] = new Card(Color.Herz, 7);
            Cards[9] = new Card(Color.Herz, 8);
            Cards[10] = new Card(Color.Herz, 9);
            Cards[11] = new Card(Color.Herz, 10);
            Cards[12] = new Card(Color.Herz, 2);
            Cards[13] = new Card(Color.Herz, 3);
            Cards[14] = new Card(Color.Herz, 4);
            Cards[15] = new Card(Color.Herz, 11);

            Cards[16] = new Card(Color.Gras, 7);
            Cards[17] = new Card(Color.Gras, 8);
            Cards[18] = new Card(Color.Gras, 9);
            Cards[19] = new Card(Color.Gras, 10);
            Cards[20] = new Card(Color.Gras, 2);
            Cards[21] = new Card(Color.Gras, 3);
            Cards[22] = new Card(Color.Gras, 4);
            Cards[23] = new Card(Color.Gras, 11);

            Cards[24] = new Card(Color.Eichel, 7);
            Cards[25] = new Card(Color.Eichel, 8);
            Cards[26] = new Card(Color.Eichel, 9);
            Cards[27] = new Card(Color.Eichel, 10);
            Cards[28] = new Card(Color.Eichel, 2);
            Cards[29] = new Card(Color.Eichel, 3);
            Cards[30] = new Card(Color.Eichel, 4);
            Cards[31] = new Card(Color.Eichel, 11);
            #endregion

            // Start the game
            CurrentGameState = State.Start;

        }

        public async Task DealCards(SchafkopfHub hub)
        {
            if (PlayingPlayers.Count < 4)
            {
                await hub.Clients.All.SendAsync("ReceiveSystemMessage", "Error: not enough players");
                return;
            }

            //New first player
            StartPlayer = (StartPlayer + 1) % 4;
            //Shuffle cards
            Shuffle();
            //Distribute cards to the players
            //Player 1 gets first 8 cards, Player 2 gets second 8 cards, an so on ...
            for (int i = 0; i < 4; i++)
            {
                Card[] HandCards = new Card[8];
                for (int j = i * 8; j < (i + 1) * 8; j++)
                {
                    HandCards[j % 8] = MixedCards[j];
                    PlayingPlayers[i].HandCards = new List<Card>(HandCards);
                }
                await PlayingPlayers[i].SendHand(hub);
            }

            ActionPlayer = StartPlayer;
            foreach (String connectionId in PlayingPlayers[ActionPlayer].GetConnectionIds())
            {
                await hub.Clients.Client(connectionId).SendAsync("AskAnnounce");
            }
        }

        public void DecideWhoIsPlaying()
        {
            ActionPlayer = StartPlayer;
            for (int i = 0; i < 4; i++)
            {
                if (PlayingPlayers[ActionPlayer].WantToPlay)
                {
                    if (AnnouncedGame < PlayingPlayers[ActionPlayer].AnnouncedGameType)
                    {
                        //Player announces a higher game to play
                        AnnouncedGame = PlayingPlayers[ActionPlayer].AnnouncedGameType;
                        Leader = PlayingPlayers[ActionPlayer];
                    }
                }
                ActionPlayer = (ActionPlayer + 1) % 4;
            }
        }

        public async Task StartGame(SchafkopfHub hub)
        {
            FindTeams();
            Trick.DetermineTrumpf(this);
            CurrentGameState = State.Playing;
            ActionPlayer = StartPlayer;
            await hub.Clients.All.SendAsync(
                "ReceiveSystemMessage",
                $"{PlayingPlayers[ActionPlayer].Name} kommt raus"
            );
        }
        private void FindTeams()
        {
            //Set up the team combination
            for (int i = 0; i < 4; i++)
            {
                if (AnnouncedGame == GameType.Ramsch)
                {
                    Groups[i] = 0;
                }
                else if (AnnouncedGame == GameType.Sauspiel)
                {
                    if (PlayingPlayers[i] == Leader)
                    {
                        Groups[i] = 1;
                    }
                    else
                    {
                        foreach (Card c in PlayingPlayers[i].HandCards)
                        {
                            if (c.Number == 11 && c.Color == Leader.AnnouncedColor)
                            {
                                Groups[i] = 1;
                            }
                            else
                            {
                                Groups[i] = 0;
                            }
                        }
                    }
                }
                else if (AnnouncedGame == GameType.Hochzeit)
                {
                    if (PlayingPlayers[i] == Leader || PlayingPlayers[i] == HusbandWife)
                    {
                        Groups[i] = 1;
                    }
                    else
                    {
                        Groups[i] = 0;
                    }
                }
                // Wenz, Farbsolo, WenzTout, FarbsoloTout
                else if ((int)AnnouncedGame >= 3)
                {
                    if (PlayingPlayers[i] == Leader)
                    {
                        Groups[i] = 1;
                    }
                    else
                    {
                        Groups[i] = 0;
                    }
                }
            }
        }

        public async Task PlayCard(Player player, Color cardColor, int cardNumber, SchafkopfHub hub)
        {
            if (player != PlayingPlayers[ActionPlayer] || CurrentGameState != State.Playing)
            {
                return;
            }
            Card playedCard = await player.PlayCard(cardColor, cardNumber, hub);
            await Trick.AddCard(playedCard, player, hub, this);

            if (Trick.Count < 4)
            {
                ActionPlayer = (ActionPlayer + 1) % 4;
            } else {
                Player winner = Trick.GetWinner();
                winner.TakeTrick(Trick);

                // Todo: check if game is over

                ActionPlayer = PlayingPlayers.FindIndex(p => p == winner);
                Trick = new Trick();
                Trick.DetermineTrumpf(this);
                await hub.Clients.All.SendAsync(
                    "ReceiveSystemMessage",
                    $"{PlayingPlayers[ActionPlayer].Name} kommt raus"
                );
            }
        }

        //-------------------------------------------------
        // The players can choose together to play another game,
        // there will be two options for the main-player
        // new game or quit
        //-------------------------------------------------
        private void EndGame()
        {
            //Show the amount of pointfor each team
            if (AnnouncedGame > 0)
            {
                int leaderPoints = 0;
                int followerPoints = 0;
                for (int i = 0; i < 4; i++)
                {
                    if (Groups[i] == 0)
                    {
                        followerPoints += PlayingPlayers[i].Balance;
                    }
                    else
                    {
                        leaderPoints += PlayingPlayers[i].Balance;
                    }
                }
                if (leaderPoints <= 60)
                {
                    //Leader has lost the game
                    //TODO::Display end result and replay button
                }
                else
                {
                    //Leader has won the game
                    //TODO::Display end result and replay button
                }
            }
            else
            {
                List<Player> player = new List<Player>();

                for (int i = 0; i < 4; i++)
                {
                    player.Add(PlayingPlayers[i]);
                }

                player.OrderBy(o => o.Balance).ToList();
                //TODO::Display end table and replay button
            }
            //Ask player whether they want to play another game
            //TODO::Wait for Button press
            //Do you want to play another game
            if (NewGame == false)
            {
                //End Game and push every player in the initial screen
                return;
            }
        }

        #region Player actions
        //-------------------------------------------------
        // Add a player to the game
        // The amount of player is limitless inside a game
        // The amount of playing players has to be excactly 4
        //-------------------------------------------------
        public async Task AddPlayer(Player player, SchafkopfHub hub)
        {
            if (player == null && Players.Contains(player))
            {
                throw new Exception("There is something wrong with the new player.");
            }
            Players.Add(player);
            await PlayerPlaysTheGame(player, hub);
        }

        //-------------------------------------------------
        // Remove player
        // Each player has the ability to leave the game, except when they are playing
        //-------------------------------------------------
        public void RemovePlayer(Player player)
        {
            if (player.Playing)
            {
                //Sorry, you can not leave the game during the game. You are able to quit the game.
                //TODO::Notification that the player is not allowed to leave the game;
                return;
            }
            if (!Players.Contains(player))
            {
                throw new Exception("There is something wrong with the player who wants to leave.");
            }
            PlayerDoesNotPlaysTheGame(player);
            Players.Remove(player);
        }

        //-------------------------------------------------
        // Player decides to play the game
        //-------------------------------------------------
        public async Task PlayerPlaysTheGame(Player player, SchafkopfHub hub)
        {
            if (PlayingPlayers.Count < 4)
            {
                player.Playing = true;
                PlayingPlayers.Add(player);
                if (PlayingPlayers.Count == 4) {
                    await DealCards(hub);
                }
            }
            else
            {
                //Sorry, there are too many players who want to play, what about some Netflix and Chill?
            }
        }

        //-------------------------------------------------
        // Player decides to not play the next game
        //-------------------------------------------------
        public void PlayerDoesNotPlaysTheGame(Player player)
        {
            if (player.Playing)
            {
                //Sorry, you can not pause the game during the game. You are able to pause afterwards.
                //TODO::Notification that the player is not allowed to leave the game;
                return;
            }
            player.Playing = false;
            if (PlayingPlayers.Contains(player))
                PlayingPlayers.Remove(player);
        }
        #endregion

        //-------------------------------------------------
        // Shuffle the cards using the FISHER-YATES-Method
        //-------------------------------------------------
        public void Shuffle()
        {
            MixedCards = Cards;
            int n = MixedCards.Length;
            for (int i = 0; i < (n - 1); i++)
            {
                int r = i + random.Next(n - i);
                Card t = MixedCards[r];
                MixedCards[r] = MixedCards[i];
                MixedCards[i] = t;
            }
        }

        //-------------------------------------------------
        // Determines the partner for a Marriage (Hochzeit)
        //-------------------------------------------------
        public void IWantToMarryU(Player p)
        {
            HusbandWife = p;
        }

    }
}
