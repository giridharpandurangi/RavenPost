using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace RavenPost
{
    /// <summary>
    /// A wood panel shown to the right of the cargo slots while a perch is open:
    /// pick a destination from the list and press Send.
    /// </summary>
    internal static class SendWindow
    {
        private const int RowsPerPage = 6;
        private const float Width = 300f;
        private const float RowHeight = 34f;

        private static readonly FieldInfo s_currentContainer = AccessTools.Field(typeof(InventoryGui), "m_currentContainer");

        private static GameObject s_panel;
        private static Text s_title;
        private static Text s_info;
        private static Text s_pageLabel;
        private static readonly List<Button> s_rows = new List<Button>();
        private static readonly List<Text> s_rowTexts = new List<Text>();
        private static readonly ZDOID[] s_rowTargets = new ZDOID[RowsPerPage];
        private static Button s_prev;
        private static Button s_next;
        private static Button s_send;

        private static RavenPerch s_perch;
        private static int s_page;
        private static float s_nextRefresh;

        private static readonly Color Gold = new Color(1f, 0.83f, 0.35f);
        private static readonly Color Plain = new Color(0.9f, 0.9f, 0.85f);

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
                s_page = 0;
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

        private static bool Build()
        {
            var gui = InventoryGui.instance;
            if (!gui || !gui.m_container || GUIManager.Instance == null || !GUIManager.Instance.AveriaSerifBold)
            {
                return false;
            }

            var rows = RowsPerPage;
            float height = 70f + rows * RowHeight + 150f;

            s_panel = GUIManager.Instance.CreateWoodpanel(gui.m_container, new Vector2(1f, 1f), new Vector2(1f, 1f),
                Vector2.zero, Width, height, false);
            s_panel.name = "RavenPost_SendWindow";
            var rect = (RectTransform)s_panel.transform;
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(12f, 0f);

            float y = -32f;
            s_title = AddText("Raven Perch", y, 22, Gold, 40f);
            y -= 40f;

            for (int i = 0; i < rows; i++)
            {
                int index = i;
                var row = GUIManager.Instance.CreateButton("", s_panel.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, y), Width - 40f, RowHeight - 4f);
                var button = row.GetComponent<Button>();
                button.onClick.AddListener(() => OnRowClicked(index));
                var text = row.GetComponentInChildren<Text>();
                text.fontSize = 16;
                s_rows.Add(button);
                s_rowTexts.Add(text);
                y -= RowHeight;
            }

            y -= 6f;
            s_prev = AddButton("<", new Vector2(-(Width / 2f - 50f), y), 50f, () => ChangePage(-1));
            s_next = AddButton(">", new Vector2(Width / 2f - 50f, y), 50f, () => ChangePage(1));
            s_pageLabel = AddText("", y, 16, Plain, 30f);
            y -= 46f;

            s_info = AddText("", y, 16, Plain, 56f);
            y -= 62f;

            s_send = AddButton("Send raven", new Vector2(0f, y), 180f, OnSendClicked);
            s_panel.SetActive(false);
            return true;
        }

        private static Text AddText(string value, float y, int size, Color color, float height)
        {
            var go = GUIManager.Instance.CreateText(value, s_panel.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, y), GUIManager.Instance.AveriaSerifBold, size, color, true, Color.black, Width - 40f, height, false);
            var text = go.GetComponent<Text>();
            text.alignment = TextAnchor.MiddleCenter;
            return text;
        }

        private static Button AddButton(string label, Vector2 pos, float width, UnityEngine.Events.UnityAction onClick)
        {
            var go = GUIManager.Instance.CreateButton(label, s_panel.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                pos, width, 34f);
            var button = go.GetComponent<Button>();
            button.onClick.AddListener(onClick);
            return button;
        }

        private static void Refresh()
        {
            s_nextRefresh = Time.time + 0.5f;
            if (!s_perch)
            {
                return;
            }

            s_title.text = s_perch.PerchName;

            var destinations = s_perch.Destinations();
            var selected = s_perch.SelectedDestination(destinations);
            int pages = Mathf.Max(1, (destinations.Count + RowsPerPage - 1) / RowsPerPage);
            s_page = Mathf.Clamp(s_page, 0, pages - 1);

            for (int i = 0; i < RowsPerPage; i++)
            {
                int index = s_page * RowsPerPage + i;
                bool used = index < destinations.Count;
                s_rows[i].gameObject.SetActive(used);
                if (!used)
                {
                    s_rowTargets[i] = ZDOID.None;
                    continue;
                }
                var zdo = destinations[index];
                bool isSelected = selected != null && zdo.m_uid == selected.m_uid;
                s_rowTargets[i] = zdo.m_uid;
                s_rowTexts[i].text = $"{(isSelected ? "> " : "")}{PostOffice.GetName(zdo)}  " +
                                     $"<size=13>({PostOffice.FormatDistance(s_perch.DistanceTo(zdo))})</size>";
                s_rowTexts[i].color = isSelected ? Gold : Plain;
            }

            bool paged = pages > 1;
            s_prev.gameObject.SetActive(paged);
            s_next.gameObject.SetActive(paged);
            s_pageLabel.text = paged ? $"{s_page + 1} / {pages}" : "";

            float weight = s_perch.CargoWeight;
            if (selected == null)
            {
                s_info.text = "Build another Raven Perch\nto send to.";
                s_send.interactable = false;
            }
            else
            {
                float seconds = PostOffice.FlightSeconds(s_perch.DistanceTo(selected), weight);
                s_info.text = $"To {PostOffice.GetName(selected)}: {PostOffice.FormatDuration(seconds)}\n" +
                              $"<size=13>Cargo weight {weight:0.#}</size>";
                s_send.interactable = weight > 0f || HasCargo();
            }
        }

        private static bool HasCargo()
        {
            var container = s_perch ? s_perch.GetComponent<Container>() : null;
            return container && container.GetInventory() != null && container.GetInventory().NrOfItems() > 0;
        }

        private static void OnRowClicked(int row)
        {
            if (s_perch && s_rowTargets[row] != ZDOID.None)
            {
                s_perch.Select(s_rowTargets[row]);
                Refresh();
            }
        }

        private static void ChangePage(int delta)
        {
            s_page += delta;
            Refresh();
        }

        private static void OnSendClicked()
        {
            if (s_perch && Player.m_localPlayer)
            {
                s_perch.Send(Player.m_localPlayer);
                Refresh();
            }
        }
    }
}
