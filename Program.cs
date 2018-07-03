using System;
using Saliens;
using System.Linq;
using System.Threading.Tasks;
using Console = Colorful.Console;
using System.Drawing;
using System.Collections.Generic;
using Colorful;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Net;

namespace Saliens_Test
{
    class Program
    {
        private static StyleSheet PrimaryStyle = new StyleSheet(Color.White);
        private static StyleSheet NumberStyle = new StyleSheet(Color.White);
        public static ConcurrentDictionary<string, PlayerInfo> Players { get; private set; }  = new ConcurrentDictionary<string, PlayerInfo>();

        static void SetupStyle()
        {
            PrimaryStyle.AddStyle("(?i)active", Color.ForestGreen);
            PrimaryStyle.AddStyle("(?i)captured", Color.DarkRed);
            PrimaryStyle.AddStyle("(?i)locked", Color.Yellow);
            NumberStyle.AddStyle("[0-9.]*%", Color.Orange);
        }

        static void PrintHeader()
        {
            FigletFont font = FigletFont.Load("poison.flf");
            Figlet figlet = new Figlet(font);

            Console.WriteWithGradient(figlet.ToAscii("Sale Bot").ToString(), Color.FromArgb(0,255,0), Color.FromArgb(0, 64, 0), 3);
            Console.WriteLine();
            Console.ResetColor();
        }
        static async Task PrintPlanetInfo()
        {
            await Planet.UpdateActive();
            Console.WriteLineStyled($"Planets, {Planet.Active.Count()} Active / {Planet.Captured.Count()} Captured / {Planet.Locked.Count()} Locked / {Planet.All.Count()} Total", PrimaryStyle);
            Console.WriteLineStyled($"Total Zone Completion {Planet.All.CompletionPercent()}%, Active Zone Completion {Planet.Active.CompletionPercent()}%", NumberStyle);
        }

        static void PrintPlayerInfo()
        {
            foreach(PlayerInfo player in Players.Values)
            {
                Console.WriteLineStyled($"{{{player.Token}}} Level {player.Level} [{player.Score - PlayerInfo.XPForLevel(player.Level)} -> {player.NextLevelScore - PlayerInfo.XPForLevel(player.Level)}] {Math.Round((float)(player.Score - PlayerInfo.XPForLevel(player.Level)) / (player.NextLevelScore - PlayerInfo.XPForLevel(player.Level)) * 100, 2)}%", NumberStyle);
            }
        }

        static async Task SubmitScore(PlayerInfo player)
        {
            int retries = 0;
            while (retries < 3)
            {
                try
                {
                    int Score = player.MaxMatchScore;
                    bool useheal = false;
                    if (player.Zone.IsActiveBossZone)
                    {
                        useheal = (DateTimeOffset.Now > player.BossMatch.HealLastUsed);
                        await player.ReportBossDamage(0, useheal);
                    }
                    else
                    {
                        await player.ReportScore();
                    }
                    
                    Console.WriteLine($"{{{player.Token}}} Score {Score} Submitted", Color.Green);
                    return;
                }
                catch (GameTimeNotSync TooEarly)
                {
                    retries++;
                    Console.WriteLine($"Score Submission Too Early [{TooEarly.EResult}] -> Wait 1 Second and Retry", Color.Red);
                    await Task.Delay(1000);
                }
                catch (GameExpired)
                {
                    Console.WriteLine($"{{{player.Token}}} Zone was Captured (EXPIRED), Unable to Submit Score.", Color.Red);
                    return;
                }
                catch (GameNoMatch)
                {
                    Console.WriteLine($"{{{player.Token}}} Zone was Captured (NO MATCH), Unable to Submit Score.", Color.Red);
                    return;
                }
                catch (InvalidGameResponse IGR)
                {
                    Console.WriteLine($"{{{player.Token}}} Bad Response {IGR.EResult} - {IGR.EReason}.", Color.Red);
                    return;
                }
            }
            Console.WriteLine($"{{{player.Token}}} Score Submission Failure After 3 Retries", Color.Red);
        }

        static void GetPlayers()
        {
            if (System.IO.File.Exists($"{Environment.CurrentDirectory}/tokens.txt"))
            {
                foreach(string Token in System.IO.File.ReadAllLines($"{Environment.CurrentDirectory}/tokens.txt"))
                {
                    try
                    {
                        PlayerInfo player = new PlayerInfo(Token);
                        Players.AddOrUpdate(Token, player, (k,v) => v = player);
                        Console.WriteLine($"{Token} -> Added", Color.Green);
                    }
                    catch(Exception)
                    {
                        Console.WriteLine($"{Token} -> Failed To Add", Color.Red);
                    }
                }
            }
            else
            {
                Console.WriteLine($"No Tokens! -> Put Token Here [{Environment.CurrentDirectory}/tokens.txt]", Color.Red);
                Environment.Exit(1);
            }
        }

        static async Task<Planet> JoinPlanet()
        {
            Planet[] planets = Planet.SortedPlanets.ToArray();
            int skip = 0;
            while (skip < planets.Count())
            {
                try
                {
                    await Task.WhenAll(Players.Select(x => x.Value.JoinPlanet(planets[skip])));
                    return planets[skip];
                }
                catch (GameFail)
                {
                    skip++;
                }
            }
            throw new NoPlanetException();
        }

        static async Task JoinZone(PlayerInfo player)
        {
            Zone zone = player.Planet.FirstAvailableZone;
            try
            {
                if (zone.IsActiveBossZone) await player.JoinBossZone(zone.Position); else await player.JoinZone(zone.Position);
            }
            catch(GameInvalidState)
            {
                await player.LeaveZone();
                await JoinZone(player);
            }
            catch (InvalidGameResponse IGR)
            {
                Console.WriteLine($"{{{player.Token}}} Bad Response {IGR.EResult} - {IGR.EReason}.", Color.Red);
            }
        }

        static async Task Run()
        {
            //WebServer.Start(); -> Not Used Currently.
            SetupStyle();
            PrintHeader();
            GetPlayers();
            await Planet.UpdateAll();

            while (true)
            {
                try
                {
                    if (Planet.Active.AllZones().Any(x => x.Type == ZoneType.Boss && x.BossActive == true))
                    {
                        Console.WriteLine("!!! BOSS IS ACTIVE !!!");
                    }
                    await PrintPlanetInfo();
                    Planet planet = await JoinPlanet();
                    Zone zone = planet.FirstAvailableZone;
                    PrintPlayerInfo();
                    await Task.WhenAll(Players.Select(x => x.Value.JoinPlanet(planet)));
                    Console.WriteLine($"Joined Planet {planet.Info.Name}", Color.Orange);

                    await Task.WhenAll(Players.Select(x => JoinZone(x.Value)));

                    Console.WriteLine($"Joined Zone {zone.Position} [{Math.Round(zone.CaptureProgress * 100, 2)}%] - {zone.Difficulty}", NumberStyle);

                    Console.WriteLine($"Sleeping {zone.Tickrate / 1000} seconds", Color.Yellow);
                    await Task.Delay(zone.Tickrate);
                    Console.WriteLine($"Last Score {DateTime.Now.ToString()}", Color.Yellow);
                    await Task.WhenAll(Players.Select(x => SubmitScore(x.Value)));

                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    //weeee
                }
            }
        }

        static void Main(string[] args)
        {
            try
            {
                Run().GetAwaiter().GetResult();
            }
            catch (Exception) { }
        }
    }
}
