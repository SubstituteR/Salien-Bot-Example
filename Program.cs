using System;
using Saliens;

namespace Saliens_Test
{
    class Program
    {
        static void Main(string[] args)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            while (true)
            {
                try
                {
                    PlayerInfo player = PlayerInfo.Get("Your Token Here");
                    Console.WriteLine(string.Format("Player XP: {0} , Next XP: {1} , {2}%", player.Score, player.NextLevelScore, Math.Round((float)player.Score / player.NextLevelScore * 100, 2)));

                    int val = ((player.NextLevelScore - player.Score) / 2400) * 110;

                    DateTimeOffset est = now.AddSeconds(val);
                    Console.WriteLine(string.Format("ETA to next level {0} hr {1} min {2} sec", est.Hour, est.Minute, est.Second));
                    player.LeavePlanet(false);
                    Planet planet = Planet.FirstAvailable;
                    Zone zone = planet.FirstAvailableZone;
                    Console.WriteLine(Planet.FirstAvailable.Info.Name);
                    Console.WriteLine(string.Format("zone percentage {0}, zone position {1}, zone difficulty {2}", zone.CaptureProgress, zone.Position, zone.Difficulty));
                    player.JoinPlanet(planet.ID, false);
                    player.JoinZone(zone.Position);
                    Console.WriteLine("Sleeping 110 seconds");
                    System.Threading.Thread.Sleep(110 * 1000);
                    player.ReportScore();
                    Console.WriteLine("-----");
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
            
            Console.WriteLine("==");
            Console.Read();
        }
    }
}
