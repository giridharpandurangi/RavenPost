using System.Collections.Generic;
using UnityEngine;

namespace RavenPost
{
    /// <summary>A bundle of items (and maybe a letter) in flight to, or waiting at, a perch.</summary>
    internal class Delivery
    {
        public double ArriveAt;
        public string From;
        public bool Notified; // unused since v0.7 (the server tracks notifications); kept for the save format
        public byte[] Items;
        public Vector3 FromPos; // zero for deliveries saved by v0.1.0

        // v3: an optional letter riding with the raven
        public string Note = "";
        public string FromPlayer = "";
        public double SentAt;

        public bool HasNote => !string.IsNullOrEmpty(Note);

        public bool HasArrived(double now) => now >= ArriveAt;

        public bool HasCargo => PostOffice.UnpackItems(Items).NrOfItems() > 0;

        /// <summary>Identifies one delivery to one perch, for notifications and landing animations.</summary>
        public string Key(ZDOID perch) => $"{perch}:{ArriveAt:R}";
    }

    /// <summary>What a client needs to know about a perch it may not have loaded.</summary>
    internal class PerchInfo
    {
        public ZDOID Id;
        public string Name;
        public Vector3 Position;
    }

    /// <summary>
    /// Shared post office logic. Deliveries are stored on the destination perch's own saved data (its "inbox"),
    /// so nothing depends on the destination area being loaded. Only the server sees every perch; clients get
    /// the perch list from it (see RavenNet).
    /// </summary>
    internal static class PostOffice
    {
        public const string NameKey = "ravenpost_name";
        public const string InboxKey = "ravenpost_inbox";
        private const int InboxVersion = 3;

        private static readonly List<ZDO> s_perches = new List<ZDO>();
        private static float s_perchesRefreshedAt = -999f;

        private static List<PerchInfo> s_clientPerches = new List<PerchInfo>();
        private static float s_clientPerchesRequestedAt = -999f;

        public static bool IsActive => ZNet.instance && ZDOMan.instance != null && ZRoutedRpc.instance != null;

        public static bool IsServer => ZNet.instance && ZNet.instance.IsServer();

        public static double Now => ZNet.instance.GetTimeSeconds();

        // ------------------------------------------------------------ finding perches

        /// <summary>Every perch ZDO in the world. Server only: clients only know the area around them.</summary>
        public static List<ZDO> GetAllPerchZdos()
        {
            if (Time.time - s_perchesRefreshedAt > 3f)
            {
                s_perches.Clear();
                int index = 0;
                while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(Plugin.PerchPrefabName, s_perches, ref index))
                {
                }
                s_perchesRefreshedAt = Time.time;
            }
            s_perches.RemoveAll(z => z == null || !z.IsValid());
            return s_perches;
        }

        /// <summary>All perches, sorted by name. On a client this is the server's last answer (refreshed every few seconds).</summary>
        public static List<PerchInfo> GetPerches()
        {
            if (IsServer)
            {
                var list = new List<PerchInfo>();
                foreach (var zdo in GetAllPerchZdos())
                {
                    list.Add(new PerchInfo { Id = zdo.m_uid, Name = GetName(zdo), Position = zdo.GetPosition() });
                }
                list.Sort((a, b) => string.Compare(a.Name, b.Name, System.StringComparison.OrdinalIgnoreCase));
                return list;
            }

            if (Time.time - s_clientPerchesRequestedAt > 3f)
            {
                s_clientPerchesRequestedAt = Time.time;
                RavenNet.RequestPerches();
            }
            return s_clientPerches;
        }

        public static void SetClientPerches(List<PerchInfo> perches)
        {
            perches.Sort((a, b) => string.Compare(a.Name, b.Name, System.StringComparison.OrdinalIgnoreCase));
            s_clientPerches = perches;
        }

        /// <summary>Forget cached perch lists, e.g. after a rename or a new perch.</summary>
        public static void InvalidatePerches()
        {
            s_perchesRefreshedAt = -999f;
            s_clientPerchesRequestedAt = -999f;
        }

        public static void ResetForNewWorld()
        {
            s_perches.Clear();
            s_clientPerches = new List<PerchInfo>();
            InvalidatePerches();
        }

        public static string GetName(ZDO zdo)
        {
            string name = zdo.GetString(NameKey);
            return string.IsNullOrEmpty(name) ? "Unnamed Perch" : name;
        }

        // ------------------------------------------------------------ inbox storage

        public static List<Delivery> ReadInbox(ZDO zdo)
        {
            var list = new List<Delivery>();
            byte[] data = zdo.GetByteArray(InboxKey);
            if (data == null || data.Length == 0)
            {
                return list;
            }

            var pkg = new ZPackage(data);
            int version = pkg.ReadInt();
            if (version < 1 || version > InboxVersion)
            {
                Plugin.Log.LogWarning($"Unknown inbox version {version}; ignoring.");
                return list;
            }
            int count = pkg.ReadInt();
            for (int i = 0; i < count; i++)
            {
                list.Add(ReadDelivery(pkg, version));
            }
            return list;
        }

        public static void WriteInbox(ZDO zdo, List<Delivery> inbox)
        {
            var pkg = new ZPackage();
            pkg.Write(InboxVersion);
            pkg.Write(inbox.Count);
            foreach (var d in inbox)
            {
                WriteDelivery(pkg, d);
            }
            zdo.Set(InboxKey, pkg.GetArray());
        }

        /// <summary>Adds one delivery to a perch's inbox. Callers make sure they may write this ZDO.</summary>
        public static void AppendToInbox(ZDO zdo, Delivery d)
        {
            var inbox = ReadInbox(zdo);
            inbox.Add(d);
            WriteInbox(zdo, inbox);
        }

        private static Delivery ReadDelivery(ZPackage pkg, int version)
        {
            var d = new Delivery
            {
                ArriveAt = pkg.ReadDouble(),
                From = pkg.ReadString(),
                Notified = pkg.ReadBool(),
                Items = pkg.ReadByteArray(),
            };
            if (version >= 2)
            {
                d.FromPos = pkg.ReadVector3();
            }
            if (version >= 3)
            {
                d.Note = pkg.ReadString();
                d.FromPlayer = pkg.ReadString();
                d.SentAt = pkg.ReadDouble();
            }
            return d;
        }

        private static void WriteDelivery(ZPackage pkg, Delivery d)
        {
            pkg.Write(d.ArriveAt);
            pkg.Write(d.From ?? "");
            pkg.Write(d.Notified);
            pkg.Write(d.Items ?? new byte[0]);
            pkg.Write(d.FromPos);
            pkg.Write(d.Note ?? "");
            pkg.Write(d.FromPlayer ?? "");
            pkg.Write(d.SentAt);
        }

        /// <summary>One delivery as a network package.</summary>
        public static ZPackage Serialize(Delivery d)
        {
            var pkg = new ZPackage();
            pkg.Write(InboxVersion);
            WriteDelivery(pkg, d);
            return pkg;
        }

        public static Delivery Deserialize(ZPackage pkg)
        {
            pkg.SetPos(0);
            int version = pkg.ReadInt();
            return ReadDelivery(pkg, version);
        }

        // ------------------------------------------------------------ cargo

        public static byte[] PackItems(List<ItemDrop.ItemData> items)
        {
            var cargo = NewCargoInventory();
            foreach (var item in items)
            {
                cargo.AddItem(item.Clone());
            }
            var pkg = new ZPackage();
            cargo.Save(pkg);
            return pkg.GetArray();
        }

        public static Inventory UnpackItems(byte[] data)
        {
            var cargo = NewCargoInventory();
            if (data != null && data.Length > 0)
            {
                cargo.Load(new ZPackage(data));
            }
            return cargo;
        }

        // Large enough for the biggest perch tier (8x10).
        private static Inventory NewCargoInventory() => new Inventory("RavenCargo", null, 8, 10);

        // ------------------------------------------------------------ flight time and text

        public static float FlightSeconds(float distance, float weight)
        {
            float seconds = distance / 100f * Plugin.SecondsPer100m.Value;
            seconds *= 1f + weight / 100f * Plugin.WeightSlowdown.Value;
            return Mathf.Max(Plugin.MinFlightSeconds.Value, seconds);
        }

        public static string FormatDuration(double seconds)
        {
            if (seconds < 0)
            {
                seconds = 0;
            }
            int total = Mathf.CeilToInt((float)seconds);
            return total >= 3600
                ? $"{total / 3600}h {total % 3600 / 60:00}m"
                : $"{total / 60}:{total % 60:00}";
        }

        public static string FormatDistance(float metres) =>
            metres >= 1000f ? $"{metres / 1000f:0.0} km" : $"{Mathf.RoundToInt(metres)} m";

        public static string ArrivalMessage(Delivery d, string at)
        {
            bool cargo = d.HasCargo;
            if (d.HasNote && !cargo)
            {
                return $"Letter from {d.From} has landed at {at}";
            }
            return d.HasNote
                ? $"Raven from {d.From} has landed at {at} with a letter"
                : $"Raven from {d.From} has landed at {at}";
        }
    }
}
