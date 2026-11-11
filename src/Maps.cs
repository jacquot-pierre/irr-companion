using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace IRRCompanion
{
    public sealed class MapFloor
    {
        public string Name, Image;
        public double CenterX, CenterY, Extent, CenterZ, ExtentZ;
        private Image _bitmap;

        /// <summary>Minimap image (embedded JPEG, loaded on first use).</summary>
        public Image Bitmap
        {
            get
            {
                if (_bitmap == null)
                {
                    using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("maps/" + Image))
                        if (s != null) _bitmap = new Bitmap(System.Drawing.Image.FromStream(s));
                }
                return _bitmap;
            }
        }

        /// <summary>World position -> normalized image position (0..1). World +X is up, +Y is right.</summary>
        public PointF ToImage(double x, double y)
        {
            double u = (x - (CenterX - Extent)) / (2 * Extent);
            double v = (y - (CenterY - Extent)) / (2 * Extent);
            return new PointF((float)v, (float)(1 - u));
        }
    }

    public sealed class MapPoint
    {
        /// <summary>Concrete, Food, Metal, Oil, Extraction, PlayerSpawn, or a lock kind (CompactSafe, LargeSafe, HasDoor, LockedDoor).</summary>
        public string Type;
        /// <summary>Name of a player spawn zone, as offered when deploying ("Train Track"...).</summary>
        public string Name;
        /// <summary>Spawn zone named from its place on the map: the game files do not say which infill name it is.</summary>
        public bool Guessed;
        /// <summary>Key gameplay tag needed (locks only).</summary>
        public string Key;
        public double X, Y, Z;
        public int Floor;
        public bool IsExtraction { get { return Type == MapData.Extraction; } }
        public bool IsPlayerSpawn { get { return Type == MapData.PlayerSpawn; } }
        public bool IsLock { get { return Key != null; } }
        public bool IsResource { get { return !IsExtraction && !IsPlayerSpawn && !IsLock; } }
    }

    public sealed class KeyInfo
    {
        public string Tag, Id, Name;
    }

    public sealed class GameMap
    {
        public string Name;
        public readonly List<MapFloor> Floors = new List<MapFloor>();
        public readonly List<MapPoint> Points = new List<MapPoint>();

        public int Count(string type, int floor = -1)
        {
            return Points.Count(p => p.Type == type && (floor < 0 || p.Floor == floor));
        }

        public override string ToString() { return Name; }
    }

    public sealed class ResourceSpawn
    {
        public string Type;
        public double Chance;
        public int Amount;
    }

    public static class MapData
    {
        public const string Extraction = "Extraction";
        public const string PlayerSpawn = "PlayerSpawn";
        public static readonly string[] LockKinds = { "CompactSafe", "LargeSafe", "HasDoor", "LockedDoor" };
        public static readonly Dictionary<string, KeyInfo> Keys = new Dictionary<string, KeyInfo>();

        public static string Label(string type)
        {
            switch (type)
            {
                case Extraction: return "Extraction points";
                case PlayerSpawn: return "Player spawns";
                case "CompactSafe": return "Compact safes";
                case "LargeSafe": return "Large safes";
                case "HasDoor": return "HAS doors";
                case "LockedDoor": return "Locked doors";
                default: return type;
            }
        }

        public static string SingleLabel(string type)
        {
            switch (type)
            {
                case Extraction: return "Extraction point";
                case PlayerSpawn: return "Player spawn";
                case "CompactSafe": return "Compact safe";
                case "LargeSafe": return "Large safe";
                case "HasDoor": return "HAS door";
                case "LockedDoor": return "Locked door";
                default: return type;
            }
        }
        public static readonly List<GameMap> Maps = new List<GameMap>();
        public static readonly List<ResourceSpawn> Resources = new List<ResourceSpawn>();

        public static void Load()
        {
            if (Maps.Count > 0) return;
            string text;
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("maps.json"))
            using (var r = new StreamReader(s, Encoding.UTF8)) text = r.ReadToEnd();
            var root = (JObj)Json.Parse(text);
            foreach (JObj k in root.Arr("keys") ?? new List<object>())
                Keys[k.Str("tag")] = new KeyInfo { Tag = k.Str("tag"), Id = k.Str("id"), Name = k.Str("name") };
            foreach (JObj r in root.Arr("resources"))
                Resources.Add(new ResourceSpawn { Type = r.Str("type"), Chance = r.Num("chance"), Amount = (int)r.Num("amount") });
            foreach (JObj m in root.Arr("maps"))
            {
                var map = new GameMap { Name = m.Str("name") };
                foreach (JObj f in m.Arr("floors"))
                    map.Floors.Add(new MapFloor
                    {
                        Name = f.Str("name"), Image = f.Str("image"), CenterX = f.Num("centerX"), CenterY = f.Num("centerY"),
                        Extent = f.Num("extent"), CenterZ = f.Num("centerZ"), ExtentZ = f.Num("extentZ"),
                    });
                foreach (JObj p in m.Arr("resources"))
                    map.Points.Add(new MapPoint { Type = p.Str("type"), X = p.Num("x"), Y = p.Num("y"), Z = p.Num("z"), Floor = (int)p.Num("floor") });
                foreach (JObj p in m.Arr("extractions"))
                    map.Points.Add(new MapPoint { Type = Extraction, X = p.Num("x"), Y = p.Num("y"), Z = p.Num("z"), Floor = (int)p.Num("floor") });
                foreach (JObj p in m.Arr("spawns") ?? new List<object>())
                    map.Points.Add(new MapPoint { Type = PlayerSpawn, Name = p.Str("name"), Guessed = Equals(p["guessed"], true), X = p.Num("x"), Y = p.Num("y"), Z = p.Num("z"), Floor = (int)p.Num("floor") });
                foreach (JObj p in m.Arr("locks") ?? new List<object>())
                    map.Points.Add(new MapPoint { Type = p.Str("kind"), Key = p.Str("key"), X = p.Num("x"), Y = p.Num("y"), Z = p.Num("z"), Floor = (int)p.Num("floor") });
                Maps.Add(map);
            }
        }

        public static ResourceSpawn Spawn(string type)
        {
            return Resources.FirstOrDefault(r => r.Type == type);
        }
    }
}
