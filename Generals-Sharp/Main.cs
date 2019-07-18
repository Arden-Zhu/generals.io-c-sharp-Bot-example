using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Quobject.SocketIoClientDotNet.Client;
using Newtonsoft.Json;

namespace Generals_Sharp
{
    public class Main
    {
        public event EventHandler OnDisconnect;
        public event EventHandler OnLog;

        private Socket socket;
        string user_id = "1358";
        string username = "[Bot] BigPig";

        int TILE_EMPTY = -1;
        int TILE_MOUNTAIN = -2;
        int TILE_FOG = -3;
        int TILE_FOG_OBSTACLE = -4; // Cities and Mountains show up as Obstacles in the fog of war.

        // Game data.
        int playerIndex = -1;
        private string replayId;
        int[] generals = new int[] { }; // The indicies of generals we have vision of.
        int[] cities = new int[0]; // The indicies of cities we have vision of.
        int[] map = new int[] { };

        public Main()
        {
            socket = IO.Socket("http://botws.generals.io");
            socket.On("disconnect", (data) =>
            {
                OnDisconnect?.Invoke(this, null);
            });
            socket.On("game_start", (d) =>
            {
                var data = JsonConvert.DeserializeObject<GameStart>(d.ToString());
                playerIndex = data.playerIndex;
                replayId = data.replay_id;

                Log($"Game starting! The replay will be available after the game at http://bot.generals.io/replays/{replayId}");
            });

            socket.On("game_update", (d) =>
            {
                // Writing the output to file.  Still can't quite figure out WTF is going on...
                // System.IO.File.WriteAllText("output2.txt", d.ToString());
                var data = JsonConvert.DeserializeObject<GameUpdate>(d.ToString());

                Log($"Turn {data.turn} {DateTime.Now.ToString("HH:mm:ss ff")}"); // Yes, 1/2s per turn
                foreach (var score in data.scores)
                {
                    Log($"    {score.i}) {score.total} {score.tiles}");
                }
                cities = patch(cities, data.cities_diff);
                map = patch(map, data.map_diff);
                generals = data.generals;

                // The first two terms in |map| are the dimensions.
                var width = map[0];
                var height = map[1];
                var size = width * height;

                // The next |size| terms are army values.
                // armies[0] is the top-left corner of the map.

                var armies = map.Skip(2).Take(size).ToArray();

                // The last |size| terms are terrain values.
                // terrain[0] is the top-left corner of the map.
                var terrain = map.Skip(size + 2).Take(size + 2 + size).ToArray();

                var rnd = new Random();
                // Make a random move.
                // Build an list of our tiles which has more than 1 solder
                var ourTiles = new List<int>();
                for (int i = 0; i < terrain.Length; i++)
                {
                    if (terrain[i] == playerIndex && armies[i] > 1)
                    {
                        ourTiles.Add(i);
                    }
                }

                while (ourTiles.Count > 0)
                {
                    // Pick a random tile.
                    var index = ourTiles[rnd.Next(ourTiles.Count)];

                    // If we own this tile, make a random move starting from it.
                    var row = Math.Floor(Convert.ToDecimal(index / width));
                    var col = index % width;
                    var endIndex = index;

                    var rand = rnd.NextDouble();
                    if (rand < 0.25 && col > 0)
                    { // left
                        endIndex--;
                    }
                    else if (rand < 0.5 && col < width - 1)
                    { // right
                        endIndex++;
                    }
                    else if (rand < 0.75 && row < height - 1)
                    { // down
                        endIndex += width;
                    }
                    else if (row > 0)
                    { //up
                        endIndex -= width;
                    }
                    else
                    {
                        continue;
                    }

                    // Would we be attacking a city? Don't attack cities.
                    if (Array.Exists(cities, ele => ele == endIndex))
                    {
                        continue;
                    }

                    if (terrain[endIndex] == TILE_MOUNTAIN)
                        continue;

                    socket.Emit("attack", index, endIndex);
                    break;
                }
            });

            socket.On("game_lost", () =>
            {
                Log("I lost.");
                LeaveGame();
            });

            socket.On("game_won", () =>
            {
                Log("I win!");
                LeaveGame();
            });
        }

        private void LeaveGame()
        {
            socket.Emit("leave_game");
        }

        public void Initialise()
        {
            socket.On("connect", () =>
            {
                OnLog?.Invoke(this, new Logging { Message = "Connected to server." });
                // Set the username for the bot.
                socket.Emit("set_username", user_id, username);
                OnLog?.Invoke(this, new Logging { Message = "Set Username" });

                // Join a custom game and force start immediately.
                // Custom games are a great way to test your bot while you develop it because you can play against your bot!
                var custom_game_id = "blister_bot_training_" + username.Replace(' ', '_').Replace("[Bot]", "_BOT_");
                socket.Emit("join_private", custom_game_id, user_id);
                socket.Emit("set_force_start", custom_game_id, true);

                Log("Joined custom game at http://bot.generals.io/games/" + System.Net.WebUtility.UrlEncode(custom_game_id));

            });
        }

        private void Log(string message)
        {
            OnLog?.Invoke(this, new Logging { Message = message });
        }

        public int[] patch(int[] old, int[] diff)
        {
            var ret = new List<int>();
            var i = 0;
            while (i < diff.Length)
            {
                if (diff[i] > 0)
                {  // matching        
                    //Array.prototype.push.apply(out, old.slice(out.length, out.length + diff[i]));            
                    ret.AddRange(old.Skip(ret.Count()).Take(diff[i]));
                }
                i++;
                if (i < diff.Length)
                {  // mismatching
                   // Array.prototype.push.apply(out, diff.slice(i + 1, i + 1 + diff[i]));

                    ret.AddRange(diff.Skip(i + 1).Take(diff[i]));
                    i += diff[i];
                }
                i++;
            }
            return ret.ToArray();
        }
    }

    public class Logging : EventArgs
    {
        public string Message { get; set; }
    }
}
