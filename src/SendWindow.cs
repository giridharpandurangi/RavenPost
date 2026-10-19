using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace RavenPost
{
    /// <summary>
    /// A wood panel shown to the right of the cargo slots while a perch is open, with two tabs:
    /// Send (destination list, note, feed, send) and Mail (letters received at this perch).
    /// </summary>
    internal static class SendWindow
    {
        private const int RowsPerPage = 6;
        private const float Width = 320f;
        private const float Height = 560f;
        private const float RowHeight = 34f;
        private const float ListTop = -112f;

        private enum Tab { Send, Mail, Perch }

        private static readonly FieldInfo s_currentContainer = AccessTools.Field(typeof(InventoryGui), "m_currentContainer");

        private static GameObject s_panel;
        private static Text s_title;
        private static Button s_sendTab;
        private static Button s_mailTab;
        private static Button s_perchTab;

        // Perch tab: tier, meals, upgrade
        private const int CostLines = 4;
        private static GameObject s_perchView;
        private static Text s_tierHeader;
        private static Text s_tierStats;
        private static Text s_perchMeals;
        private static Button s_perchFeed;
        private static Text s_nextHeader;
        private static readonly List<Text> s_costLines = new List<Text>();
        private static Button s_upgrade;

        // Send tab
        private static GameObject s_sendView;
        private static readonly List<Button> s_destRows = new List<Button>();
        private static readonly List<Text> s_destTexts = new List<Text>();
        private static readonly ZDOID[] s_destTargets = new ZDOID[RowsPerPage];
        private static Button s_destPrev;
        private static Button s_destNext;
        private static Text s_destPage;
        private static Text s_info;
        private static Text s_noteLine;
        private static Button s_writeNote;
        private static Button s_clearNote;
        private static Button s_feed;
        private static Button s_send;

        // Mail tab: list
        private static GameObject s_mailView;
        private static readonly List<Button> s_mailRows = new List<Button>();
        private static readonly List<Text> s_mailTexts = new List<Text>();
        private static readonly int[] s_mailTargets = new int[RowsPerPage];
        private static Button s_mailPrev;
        private static Button s_mailNext;
        private static Text s_mailPage;
        private static Text s_mailHint;

        // Mail tab: reading one letter
        private static GameObject s_readView;
        private static Text s_readHeader;
        private static Text s_readBody;

        private static RavenPerch s_perch;
        private static Tab s_tab = Tab.Send;
        private static int s_destPageIndex;
        private static int s_mailPageIndex;
        private static int s_reading = -1; // saved index of the open letter, -1 = list
        private static float s_nextRefresh;
        private static bool s_openOnSendTab;

        /// <summary>The next time a perch opens, show the Send tab (used after writing a note).</summary>
        public static void OpenOnSendTabNext() => s_openOnSendTab = true;

        private static readonly Color Gold = new Color(1f, 0.83f, 0.35f);
        private static readonly Color Plain = new Color(0.9f, 0.9f, 0.85f);
        private static readonly Color Faded = new Color(0.65f, 0.63f, 0.6f);

        public static void Tick()
        {
            var perch = OpenPerch();
            if (!perch)
            {
                if (s_panel && s_panel.activeSelf)
                {
                    s_panel.SetActive(false);
                }
                s_perch = null;
                return;
            }

            if (!s_panel && !Build())
            {
                return;
            }

            if (perch != s_perch)
            {
                s_perch = perch;
                s_destPageIndex = 0;
                s_mailPageIndex = 0;
                s_reading = -1;
                // Open straight on the mail tab when letters are waiting.
                s_tab = !s_openOnSendTab && perch.UnreadLetters > 0 ? Tab.Mail : Tab.Send;
                s_openOnSendTab = false;
                s_nextRefresh = 0f;
            }
            if (!s_panel.activeSelf)
            {
                s_panel.SetActive(true);
            }
            if (Time.time >= s_nextRefresh)
            {
                Refresh();
            }
        }

        private static RavenPerch OpenPerch()
        {
            var gui = InventoryGui.instance;
            if (!gui || !InventoryGui.IsVisible() || !PostOffice.IsActive)
            {
                return null;
            }
            var container = s_currentContainer.GetValue(gui) as Container;
            return container ? container.GetComponent<RavenPerch>() : null;
        }

        // ---------------------------------------------------------------- building

        private static bool Build()
        {
            var gui = InventoryGui.instance;
            if (!gui || !gui.m_container || GUIManager.Instance == null || !GUIManager.Instance.AveriaSerifBold)
            {
                return false;
            }

            // Logging out destroys the game's UI along with our old panel; drop references to its controls.
            s_destRows.Clear();
            s_destTexts.Clear();
            s_mailRows.Clear();
            s_mailTexts.Clear();
            s_costLines.Clear();
            s_perch = null;

            s_panel = GUIManager.Instance.CreateWoodpanel(gui.m_container, new Vector2(1f, 1f), new Vector2(1f, 1f),
                Vector2.zero, Width, Height, false);
            s_panel.name = "RavenPost_Window";
            var rect = (RectTransform)s_panel.transform;
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(12f, 0f);

            var root = s_panel.transform;
            s_title = AddText(root, "Raven Perch", -30f, 22, Gold, 36f);
            s_sendTab = AddButton(root, "Send", new Vector2(-98f, -70f), 90f, () => SwitchTab(Tab.Send));
            s_mailTab = AddButton(root, "Mail", new Vector2(0f, -70f), 90f, () => SwitchTab(Tab.Mail));
            s_perchTab = AddButton(root, "Perch", new Vector2(98f, -70f), 90f, () => SwitchTab(Tab.Perch));

            BuildSendView();
            BuildMailView();
            BuildReadView();
            BuildPerchView();

            s_panel.SetActive(false);
            return true;
        }

        private static GameObject AddView(string name)
        {
            var view = new GameObject(name, typeof(RectTransform));
            view.transform.SetParent(s_panel.transform, false);
            var r = (RectTransform)view.transform;
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
            return view;
        }

        private static void BuildSendView()
        {
            s_sendView = AddView("SendView");
            var t = s_sendView.transform;
            float y = ListTop;
            for (int i = 0; i < RowsPerPage; i++)
            {
                int index = i;
                var row = AddButton(t, "", new Vector2(0f, y), Width - 40f, () => OnDestinationClicked(index), RowHeight - 4f);
                var text = row.GetComponentInChildren<Text>();
                text.fontSize = 16;
                s_destRows.Add(row);
                s_destTexts.Add(text);
                y -= RowHeight;
            }
            y -= 6f;
            s_destPrev = AddButton(t, "<", new Vector2(-(Width / 2f - 50f), y), 50f, () => { s_destPageIndex--; Refresh(); });
            s_destNext = AddButton(t, ">", new Vector2(Width / 2f - 50f, y), 50f, () => { s_destPageIndex++; Refresh(); });
            s_destPage = AddText(t, "", y, 16, Plain, 30f);
            y -= 52f;

            s_info = AddText(t, "", y, 16, Plain, 66f);
            y -= 52f;
            s_noteLine = AddText(t, "", y, 14, Faded, 34f);
            y -= 40f;

            s_writeNote = AddButton(t, "Write note", new Vector2(-72f, y), 130f, OnWriteNoteClicked);
            s_clearNote = AddButton(t, "Clear note", new Vector2(72f, y), 130f, () => { if (s_perch) { s_perch.Draft = ""; Refresh(); } });
            y -= 42f;
            s_feed = AddButton(t, "Feed", new Vector2(-72f, y), 130f, OnFeedClicked);
            s_send = AddButton(t, "Send raven", new Vector2(72f, y), 130f, OnSendClicked);
        }

        private static void BuildMailView()
        {
            s_mailView = AddView("MailView");
            var t = s_mailView.transform;
            float y = ListTop;
            for (int i = 0; i < RowsPerPage; i++)
            {
                int index = i;
                var row = AddButton(t, "", new Vector2(0f, y), Width - 40f, () => OnLetterClicked(index), RowHeight - 4f);
                var text = row.GetComponentInChildren<Text>();
                text.fontSize = 14;
                text.alignment = TextAnchor.MiddleLeft;
                var tr = (RectTransform)text.transform;
                tr.offsetMin = new Vector2(10f, tr.offsetMin.y);
                s_mailRows.Add(row);
                s_mailTexts.Add(text);
                y -= RowHeight;
            }
            y -= 6f;
            s_mailPrev = AddButton(t, "<", new Vector2(-(Width / 2f - 50f), y), 50f, () => { s_mailPageIndex--; Refresh(); });
            s_mailNext = AddButton(t, ">", new Vector2(Width / 2f - 50f, y), 50f, () => { s_mailPageIndex++; Refresh(); });
            s_mailPage = AddText(t, "", y, 16, Plain, 30f);
            y -= 60f;
            s_mailHint = AddText(t, "", y, 15, Faded, 60f);
        }

        private static void BuildReadView()
        {
            s_readView = AddView("ReadView");
            var t = s_readView.transform;
            s_readHeader = AddText(t, "", -130f, 17, Gold, 50f);
            s_readBody = AddText(t, "", -290f, 17, Plain, 260f);
            s_readBody.alignment = TextAnchor.UpperLeft;
            s_readBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            s_readBody.verticalOverflow = VerticalWrapMode.Truncate;

            float y = -470f;
            AddButton(t, "Back", new Vector2(-98f, y), 90f, () => { s_reading = -1; Refresh(); });
            AddButton(t, "Reply", new Vector2(0f, y), 90f, OnReplyClicked);
            AddButton(t, "Delete", new Vector2(98f, y), 90f, OnDeleteClicked);
        }

        private static void BuildPerchView()
        {
            s_perchView = AddView("PerchView");
            var t = s_perchView.transform;
            s_tierHeader = AddText(t, "", -122f, 20, Gold, 30f);
            s_tierStats = AddText(t, "", -152f, 15, Plain, 26f);
            s_perchMeals = AddText(t, "", -184f, 14, Plain, 26f);
            s_perchFeed = AddButton(t, "Feed", new Vector2(0f, -220f), 130f, OnFeedClicked);

            s_nextHeader = AddText(t, "", -282f, 16, Gold, 50f);
            float y = -322f;
            for (int i = 0; i < CostLines; i++)
            {
                s_costLines.Add(AddText(t, "", y, 15, Plain, 24f));
                y -= 24f;
            }
            s_upgrade = AddButton(t, "Upgrade", new Vector2(0f, -470f), 160f, OnUpgradeClicked);
        }

        private static Text AddText(Transform parent, string value, float y, int size, Color color, float height)
        {
            var go = GUIManager.Instance.CreateText(value, parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, y), GUIManager.Instance.AveriaSerifBold, size, color, true, Color.black, Width - 40f, height, false);
            var text = go.GetComponent<Text>();
            text.alignment = TextAnchor.MiddleCenter;
            return text;
        }

        private static Button AddButton(Transform parent, string label, Vector2 pos, float width, UnityAction onClick, float height = 34f)
        {
            var go = GUIManager.Instance.CreateButton(label, parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                pos, width, height);
            var button = go.GetComponent<Button>();
            button.onClick.AddListener(onClick);
            return button;
        }

        // ---------------------------------------------------------------- refreshing

        private static void Refresh()
        {
            s_nextRefresh = Time.time + 0.5f;
            if (!s_perch)
            {
                return;
            }

            s_title.text = s_perch.PerchName;
            int unread = s_perch.UnreadLetters;
            SetLabel(s_sendTab, s_tab == Tab.Send ? "> Send" : "Send");
            SetLabel(s_mailTab, (s_tab == Tab.Mail ? "> " : "") + (unread > 0 ? $"Mail ({unread})" : "Mail"));
            SetLabel(s_perchTab, s_tab == Tab.Perch ? "> Perch" : "Perch");
            s_perchView.SetActive(s_tab == Tab.Perch);

            s_sendView.SetActive(s_tab == Tab.Send);
            s_mailView.SetActive(s_tab == Tab.Mail && s_reading < 0);
            s_readView.SetActive(s_tab == Tab.Mail && s_reading >= 0);

            if (s_tab == Tab.Send)
            {
                RefreshSend();
            }
            else if (s_tab == Tab.Perch)
            {
                RefreshPerch();
            }
            else if (s_reading >= 0)
            {
                RefreshRead();
            }
            else
            {
                RefreshMail();
            }
        }

        private static void SetLabel(Button button, string label)
        {
            var text = button.GetComponentInChildren<Text>();
            if (text)
            {
                text.text = label;
            }
        }

        private static void RefreshSend()
        {
            var destinations = s_perch.Destinations();
            var selected = s_perch.SelectedDestination(destinations);
            int pages = Mathf.Max(1, (destinations.Count + RowsPerPage - 1) / RowsPerPage);
            s_destPageIndex = Mathf.Clamp(s_destPageIndex, 0, pages - 1);

            for (int i = 0; i < RowsPerPage; i++)
            {
                int index = s_destPageIndex * RowsPerPage + i;
                bool used = index < destinations.Count;
                s_destRows[i].gameObject.SetActive(used);
                if (!used)
                {
                    s_destTargets[i] = ZDOID.None;
                    continue;
                }
                var zdo = destinations[index];
                bool isSelected = selected != null && zdo.m_uid == selected.m_uid;
                s_destTargets[i] = zdo.m_uid;
                s_destTexts[i].text = $"{(isSelected ? "> " : "")}{PostOffice.GetName(zdo)}  " +
                                      $"<size=13>({PostOffice.FormatDistance(s_perch.DistanceTo(zdo))})</size>";
                s_destTexts[i].color = isSelected ? Gold : Plain;
            }

            bool paged = pages > 1;
            s_destPrev.gameObject.SetActive(paged);
            s_destNext.gameObject.SetActive(paged);
            s_destPage.text = paged ? $"{s_destPageIndex + 1} / {pages}" : "";

            string draft = s_perch.Draft;
            bool hasNote = !string.IsNullOrEmpty(draft);
            bool hasCargo = HasCargo();
            s_noteLine.text = hasNote ? $"Note: \"{RavenMail.Preview(draft, 34)}\"" : "No note attached";
            s_noteLine.color = hasNote ? Plain : Faded;
            SetLabel(s_writeNote, hasNote ? "Edit note" : "Write note");
            s_clearNote.gameObject.SetActive(hasNote);

            float weight = s_perch.CargoWeight;
            s_feed.gameObject.SetActive(Plugin.UseFood.Value);
            string meals = Plugin.UseFood.Value && hasCargo ? $"\n<size=14>{s_perch.MealsStatus}</size>" : "";

            if (selected == null)
            {
                s_info.text = "Build another Raven Perch\nto send to.";
                s_send.interactable = false;
                return;
            }

            if (!hasCargo && hasNote)
            {
                float seconds = s_perch.LetterSecondsTo(selected);
                s_info.text = $"Letter to {PostOffice.GetName(selected)}: {PostOffice.FormatDuration(seconds)}\n" +
                              "<size=13>A letter alone needs no meal</size>";
                s_send.interactable = true;
                return;
            }

            float flight = s_perch.FlightSecondsTo(selected);
            s_info.text = $"To {PostOffice.GetName(selected)}: {PostOffice.FormatDuration(flight)}\n" +
                          $"<size=13>Cargo weight {weight:0.#}</size>" + meals;
            s_send.interactable = (hasCargo || hasNote) && !(hasCargo && s_perch.RefusesToFly);
        }

        /// <summary>Letters newest first.</summary>
        private static List<int> MailOrder(List<Letter> letters)
        {
            var order = new List<int>();
            for (int i = letters.Count - 1; i >= 0; i--)
            {
                order.Add(i);
            }
            return order;
        }

        private static void RefreshMail()
        {
            var letters = s_perch.Mailbox;
            var order = MailOrder(letters);
            int pages = Mathf.Max(1, (order.Count + RowsPerPage - 1) / RowsPerPage);
            s_mailPageIndex = Mathf.Clamp(s_mailPageIndex, 0, pages - 1);

            for (int i = 0; i < RowsPerPage; i++)
            {
                int index = s_mailPageIndex * RowsPerPage + i;
                bool used = index < order.Count;
                s_mailRows[i].gameObject.SetActive(used);
                if (!used)
                {
                    s_mailTargets[i] = -1;
                    continue;
                }
                var letter = letters[order[index]];
                s_mailTargets[i] = order[index];
                string mark = letter.Read ? "   " : "<color=#ffd45a>●</color> ";
                s_mailTexts[i].text = $"{mark}{letter.From} <size=12>Day {RavenMail.Day(letter.SentAt)}</size>  " +
                                      $"<size=13>{RavenMail.Preview(letter.Text, 18)}</size>";
                s_mailTexts[i].color = letter.Read ? Faded : Plain;
            }

            bool paged = pages > 1;
            s_mailPrev.gameObject.SetActive(paged);
            s_mailNext.gameObject.SetActive(paged);
            s_mailPage.text = paged ? $"{s_mailPageIndex + 1} / {pages}" : "";
            s_mailHint.text = order.Count == 0
                ? "No letters yet.\nWrite one from the Send tab."
                : "Click a letter to read it.";
        }

        private static void RefreshRead()
        {
            var letters = s_perch.Mailbox;
            if (s_reading >= letters.Count)
            {
                s_reading = -1;
                Refresh();
                return;
            }
            var letter = letters[s_reading];
            string who = string.IsNullOrEmpty(letter.FromPlayer) ? letter.From : $"{letter.From} ({letter.FromPlayer})";
            s_readHeader.text = $"From {who}\n<size=14>Day {RavenMail.Day(letter.SentAt)}</size>";
            s_readBody.text = letter.Text;
        }

        private static void RefreshPerch()
        {
            var tier = s_perch.Tier;
            s_tierHeader.text = $"{tier.Name}";
            s_tierStats.text = $"{tier.Biome}  -  cargo {tier.CargoWidth}x{tier.CargoHeight}, speed {RavenFood.FormatSpeed(tier.SpeedMultiplier)}";
            bool food = Plugin.UseFood.Value;
            s_perchMeals.text = food ? s_perch.MealsStatus : "";
            s_perchFeed.gameObject.SetActive(food);

            var next = s_perch.NextTier;
            if (next == null)
            {
                s_nextHeader.text = "Fully upgraded";
                foreach (var line in s_costLines)
                {
                    line.text = "";
                }
                s_upgrade.gameObject.SetActive(false);
                return;
            }

            s_nextHeader.text = $"Next: {next.Name}\n<size=13>{next.Biome} - cargo {next.CargoWidth}x{next.CargoHeight}, " +
                                $"speed {RavenFood.FormatSpeed(next.SpeedMultiplier)}</size>";
            var reqs = next.Requirements();
            var inventory = Player.m_localPlayer ? Player.m_localPlayer.GetInventory() : null;
            bool affordable = reqs.Count > 0 && inventory != null;
            for (int i = 0; i < CostLines; i++)
            {
                if (i >= reqs.Count)
                {
                    s_costLines[i].text = "";
                    continue;
                }
                var data = PerchTiers.ItemData(reqs[i].Key);
                if (data == null)
                {
                    s_costLines[i].text = $"<color=#ff6060>{reqs[i].Key} (not in this game)</color>";
                    affordable = false;
                    continue;
                }
                int have = inventory != null ? inventory.CountItems(data.m_shared.m_name) : 0;
                bool enough = have >= reqs[i].Value;
                affordable &= enough;
                string colour = enough ? "#9be07a" : "#ff8060";
                s_costLines[i].text = $"<color={colour}>{PerchTiers.DisplayName(reqs[i].Key)}  {have}/{reqs[i].Value}</color>";
            }
            s_upgrade.gameObject.SetActive(true);
            s_upgrade.interactable = affordable;
        }

        private static void OnUpgradeClicked()
        {
            if (s_perch && Player.m_localPlayer)
            {
                s_perch.Upgrade(Player.m_localPlayer);
                Refresh();
            }
        }

        // ---------------------------------------------------------------- actions

        private static void SwitchTab(Tab tab)
        {
            s_tab = tab;
            s_reading = -1;
            Refresh();
        }

        private static bool HasCargo()
        {
            var container = s_perch ? s_perch.GetComponent<Container>() : null;
            return container && container.GetInventory() != null && container.GetInventory().NrOfItems() > 0;
        }

        private static void OnDestinationClicked(int row)
        {
            if (s_perch && s_destTargets[row] != ZDOID.None)
            {
                s_perch.Select(s_destTargets[row]);
                Refresh();
            }
        }

        private static void OnWriteNoteClicked()
        {
            if (!s_perch)
            {
                return;
            }
            var destination = s_perch.SelectedDestination(s_perch.Destinations());
            string topic = destination != null ? $"Note to {PostOffice.GetName(destination)}" : "Write a note";
            NoteWriter.Open(s_perch, topic);
        }

        private static void OnFeedClicked()
        {
            if (s_perch && Player.m_localPlayer)
            {
                s_perch.FeedBest(Player.m_localPlayer);
                Refresh();
            }
        }

        private static void OnSendClicked()
        {
            if (s_perch && Player.m_localPlayer)
            {
                s_perch.Send(Player.m_localPlayer);
                Refresh();
            }
        }

        private static void OnLetterClicked(int row)
        {
            if (s_perch && s_mailTargets[row] >= 0)
            {
                s_reading = s_mailTargets[row];
                s_perch.OpenLetter(s_reading);
                Refresh();
            }
        }

        private static void OnReplyClicked()
        {
            if (!s_perch || s_reading < 0)
            {
                return;
            }
            var letters = s_perch.Mailbox;
            if (s_reading >= letters.Count)
            {
                return;
            }
            var letter = letters[s_reading];
            var sender = RavenMail.FindSender(letter);
            if (sender == null)
            {
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center, $"Can't find the perch \"{letter.From}\" any more");
                return;
            }
            s_perch.Select(sender.m_uid);
            s_tab = Tab.Send;
            s_reading = -1;
            NoteWriter.Open(s_perch, $"Reply to {letter.From}");
        }

        private static void OnDeleteClicked()
        {
            if (s_perch && s_reading >= 0)
            {
                s_perch.DeleteLetter(s_reading);
                s_reading = -1;
                Refresh();
            }
        }
    }
}
