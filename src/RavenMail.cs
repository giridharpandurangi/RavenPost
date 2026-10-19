using System.Collections.Generic;
using UnityEngine;

namespace RavenPost
{
    /// <summary>A letter in a perch's mailbox.</summary>
    internal class Letter
    {
        public double SentAt;
        public string From = "";       // sending perch's name
        public string FromPlayer = "";
        public Vector3 FromPos;        // to find the sender again for Reply
        public string Text = "";
        public bool Read;
    }

    /// <summary>
    /// Mailboxes live on each perch's saved data, next to its delivery inbox.
    /// The note being written ("draft") is stored on the sending perch until it is sent.
    /// </summary>
    internal static class RavenMail
    {
        public const string MailKey = "ravenpost_mail";
        public const string DraftKey = "ravenpost_draft";
        public const int MaxNoteLength = 200;
        private const int MaxLetters = 50;
        private const int MailVersion = 1;

        public static List<Letter> Read(ZDO zdo)
        {
            var list = new List<Letter>();
            byte[] data = zdo.GetByteArray(MailKey);
            if (data == null || data.Length == 0)
            {
                return list;
            }
            var pkg = new ZPackage(data);
            int version = pkg.ReadInt();
            if (version != MailVersion)
            {
                Plugin.Log.LogWarning($"Unknown mailbox version {version}; ignoring.");
                return list;
            }
            int count = pkg.ReadInt();
            for (int i = 0; i < count; i++)
            {
                list.Add(new Letter
                {
                    SentAt = pkg.ReadDouble(),
                    From = pkg.ReadString(),
                    FromPlayer = pkg.ReadString(),
                    FromPos = pkg.ReadVector3(),
                    Text = pkg.ReadString(),
                    Read = pkg.ReadBool(),
                });
            }
            return list;
        }

        public static void Write(ZDO zdo, List<Letter> letters)
        {
            // Full mailbox: drop the oldest read letters first, then the oldest unread.
            while (letters.Count > MaxLetters)
            {
                int oldestRead = letters.FindIndex(l => l.Read);
                letters.RemoveAt(oldestRead >= 0 ? oldestRead : 0);
            }

            var pkg = new ZPackage();
            pkg.Write(MailVersion);
            pkg.Write(letters.Count);
            foreach (var l in letters)
            {
                pkg.Write(l.SentAt);
                pkg.Write(l.From ?? "");
                pkg.Write(l.FromPlayer ?? "");
                pkg.Write(l.FromPos);
                pkg.Write(l.Text ?? "");
                pkg.Write(l.Read);
            }
            zdo.Set(MailKey, pkg.GetArray());
        }

        public static void Deliver(ZDO zdo, Delivery d)
        {
            var letters = Read(zdo);
            letters.Add(new Letter
            {
                SentAt = d.SentAt,
                From = d.From,
                FromPlayer = d.FromPlayer,
                FromPos = d.FromPos,
                Text = d.Note,
                Read = false,
            });
            Write(zdo, letters);
        }

        public static int UnreadCount(ZDO zdo)
        {
            int n = 0;
            foreach (var l in Read(zdo))
            {
                if (!l.Read)
                {
                    n++;
                }
            }
            return n;
        }

        public static int Day(double time) => EnvMan.instance ? EnvMan.instance.GetDay(time) : 0;

        /// <summary>One-line preview, shortened to fit a list row.</summary>
        public static string Preview(string text, int max = 28)
        {
            text = (text ?? "").Replace('\n', ' ');
            return text.Length <= max ? text : text.Substring(0, max - 1) + "…";
        }

        /// <summary>
        /// Finds the perch a letter came from: same name, nearest to where it was sent from.
        /// </summary>
        public static PerchInfo FindSender(Letter letter)
        {
            PerchInfo best = null;
            float bestDistance = float.MaxValue;
            foreach (var perch in PostOffice.GetPerches())
            {
                if (perch.Name != letter.From)
                {
                    continue;
                }
                float distance = Vector3.Distance(perch.Position, letter.FromPos);
                if (distance < bestDistance)
                {
                    best = perch;
                    bestDistance = distance;
                }
            }
            return best;
        }
    }

    /// <summary>
    /// Opens Valheim's text box to write the note for a perch. The inventory window closes keys like E,
    /// so the perch is closed while typing and opened again when the note is entered.
    /// </summary>
    internal class NoteWriter : TextReceiver
    {
        private readonly RavenPerch _perch;

        private NoteWriter(RavenPerch perch)
        {
            _perch = perch;
        }

        public static void Open(RavenPerch perch, string topic)
        {
            if (!perch || !TextInput.instance)
            {
                return;
            }
            if (InventoryGui.instance)
            {
                InventoryGui.instance.Hide();
            }
            TextInput.instance.RequestText(new NoteWriter(perch), topic, MaxNoteLength());
        }

        private static int MaxNoteLength() => RavenMail.MaxNoteLength;

        public string GetText() => _perch ? _perch.Draft : "";

        public void SetText(string text)
        {
            if (!_perch)
            {
                return;
            }
            _perch.Draft = text;
            var player = Player.m_localPlayer;
            var container = _perch.GetComponent<Container>();
            if (player && container)
            {
                SendWindow.OpenOnSendTabNext();
                container.Interact(player, false, false);
            }
        }
    }
}
