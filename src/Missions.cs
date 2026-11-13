using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace IRRCompanion
{
    public sealed class MissionReward
    {
        public string Id, Name;
        public int Count;
        /// <summary>The game has an icon for it (icons/item_&lt;id&gt;.png).</summary>
        public bool Icon;
    }

    public sealed class MissionStep
    {
        public string Title;
        public readonly List<string> Objectives = new List<string>();
    }

    public sealed class Mission
    {
        public string Id, Title, Kind, Place;
        /// <summary>Position in the story (1..), 0 for a side mission.</summary>
        public int Number;
        public readonly List<string> Maps = new List<string>();
        public readonly List<MissionStep> Steps = new List<MissionStep>();
        public readonly List<string> QuestItems = new List<string>();
        public readonly List<string> Briefing = new List<string>();
        public readonly List<string> Tips = new List<string>();
        public readonly List<MissionReward> Rewards = new List<MissionReward>();
        /// <summary>Side missions this one gives (reconnaissance).</summary>
        public readonly List<string> Unlocks = new List<string>();
        /// <summary>Waits until every story mission given so far is done.</summary>
        public bool WaitAll;
        /// <summary>Missions that come right before it (ids), and the mission that gives it (side missions).</summary>
        public readonly List<string> After = new List<string>();
        public string GivenBy;
        /// <summary>A side mission the game counts as a story mission (bStoryMission): the waits of the story
        /// (yellow bars) hold until it is done. Reconnaissance - Traders today.</summary>
        public bool BlocksStory;
        public bool IsSide { get { return Number == 0; } }
        public string MapText { get { return Maps.Count >= 4 ? "Any map" : string.Join(" / ", Maps); } }
        public override string ToString() { return Title; }
    }

    /// <summary>One element of the story: a mission, or branches done side by side (each a list of elements).</summary>
    public sealed class StoryItem
    {
        public Mission Mission;
        public List<List<StoryItem>> Branches;
    }

    /// <summary>The missions and their order (src/Resources/missions.json, made by tools/build_missions.py).</summary>
    public static class MissionData
    {
        public static readonly Dictionary<string, Mission> Missions = new Dictionary<string, Mission>();
        public static readonly List<StoryItem> Story = new List<StoryItem>();
        public static string Achievement;

        public static readonly Dictionary<string, Color> MapColors = new Dictionary<string, Color>
        {
            { "Delta", Color.FromArgb(139, 195, 74) },
            { "Quarry", Color.FromArgb(240, 160, 64) },
            { "Outskirts", Color.FromArgb(93, 169, 233) },
            { "Bunker", Color.FromArgb(186, 140, 220) },
        };

        public static Color MapColor(Mission m)
        {
            Color c;
            return m.Maps.Count == 1 && MapColors.TryGetValue(m.Maps[0], out c) ? c : Theme.TextDim;
        }

        public static void Load()
        {
            if (Missions.Count > 0) return;
            string text;
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("missions.json"))
            using (var r = new StreamReader(s, Encoding.UTF8)) text = r.ReadToEnd();
            var root = (JObj)Json.Parse(text);
            Achievement = root.Str("achievement");
            var all = root.Obj("missions");
            foreach (var id in all.Keys)
            {
                var o = all.Obj(id);
                var m = new Mission { Id = id, Title = o.Str("title"), Kind = o.Str("kind"), Place = o.Str("place"), BlocksStory = o["blocksStory"] is bool && (bool)o["blocksStory"] };
                foreach (var x in o.Arr("maps")) m.Maps.Add((string)x);
                foreach (JObj st in o.Arr("steps"))
                {
                    var step = new MissionStep { Title = st.Str("title") };
                    foreach (var x in st.Arr("objectives")) step.Objectives.Add((string)x);
                    m.Steps.Add(step);
                }
                Strings(o, "questItems", m.QuestItems);
                Strings(o, "briefing", m.Briefing);
                Strings(o, "tips", m.Tips);
                Strings(o, "unlocks", m.Unlocks);
                foreach (JObj rw in o.Arr("rewards"))
                    m.Rewards.Add(new MissionReward { Id = rw.Str("id"), Name = rw.Str("name"), Count = (int)rw.Num("count", 1), Icon = rw["icon"] is bool && (bool)rw["icon"] });
                Missions[id] = m;
            }
            int number = 0;
            List<Mission> last = new List<Mission>();
            Story.AddRange(Items(root.Arr("story"), ref number, ref last));
            foreach (var m in Missions.Values)
                foreach (var u in m.Unlocks)
                {
                    Mission side;
                    if (Missions.TryGetValue(u, out side)) side.GivenBy = m.Id;
                }
        }

        /// <summary>Side missions that hold the wait before story mission m: given by a mission before it.</summary>
        public static List<Mission> Blockers(Mission m)
        {
            return Missions.Values.Where(x => x.BlocksStory && x.GivenBy != null && Missions[x.GivenBy].Number < m.Number).ToList();
        }

        /// <summary>"Recon Traders".</summary>
        public static string ShortTitle(Mission m) { return m.Title.Replace("Reconnaissance - ", "Recon "); }

        private static void Strings(JObj o, string key, List<string> into)
        {
            var a = o.Arr(key);
            if (a != null) into.AddRange(a.Cast<string>());
        }

        /// <summary>Story elements of a JSON list; numbers the missions and links each one to the ones before it.</summary>
        private static List<StoryItem> Items(List<object> list, ref int number, ref List<Mission> before)
        {
            var items = new List<StoryItem>();
            foreach (JObj o in list)
            {
                var branches = o.Arr("branches");
                if (branches != null)
                {
                    var item = new StoryItem { Branches = new List<List<StoryItem>>() };
                    var ends = new List<Mission>();
                    foreach (List<object> br in branches)
                    {
                        var b = before;
                        item.Branches.Add(Items(br, ref number, ref b));
                        ends.AddRange(b);
                    }
                    before = ends;
                    items.Add(item);
                    continue;
                }
                Mission m;
                if (!Missions.TryGetValue(o.Str("id"), out m)) continue;
                m.Number = ++number;
                m.WaitAll = o["waitAll"] is bool && (bool)o["waitAll"];
                m.After.AddRange(before.Select(x => x.Id));
                before = new List<Mission> { m };
                items.Add(new StoryItem { Mission = m });
            }
            return items;
        }
    }
}
