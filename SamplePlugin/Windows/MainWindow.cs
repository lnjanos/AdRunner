using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using ECommons.Automation;
using ECommons.DalamudServices;
using Dalamud.Game.ClientState.Conditions;
using System.ComponentModel;
using Microsoft.VisualBasic;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using ECommons.GameFunctions;
using System.Drawing;

namespace AdRunner.Windows
{
    public class MainWindow : Window, IDisposable
    {
        private Plugin Plugin;
        private IChatGui _chat;
        private IClientState _clientState;

        // Beispiel-Liste an AetherNames (Route)
        private readonly List<string> AetherNames = new()
        {
            "New Gridania",
            "Limsa Lominsa Lower Decks",
            "Ul'dah - Steps of Nald"
        };

        // Aetheryte-Name → AetheryteID
        private Dictionary<string, uint> Aetherytes;

        // Aetheryte-Name → TerritoryID
        // Nur wenn du wirklich z.B. "New Gridania" = 133, ...
        private Dictionary<string, ushort> Territories;

        private string message = "";

        private int choosenChat = 0;
        private string[] chatOptions = { "Shout", "Yell", "Say" };
        private Vector4[] chatColors = { new Vector4(1f, 0.65f, 0.4f, 1f), new Vector4(1f, 1f, 0f, 1f), new Vector4(0.97f, 0.97f, 0.97f, 1f) };


        private bool autoDetect = false;

        private string lastKnownServer = "";
        private string startServer = "";

        private int spentGil = 0;

        private bool isRunning = false;
        private CancellationTokenSource? cts;

        public MainWindow(Plugin plugin)
            : base("AdRunner##With a hidden ID", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
        {
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(375, 330),
                MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
            };

            this.Size = new Vector2(400, 400);

            this.Plugin = plugin;
            this._chat = plugin.GetChatGui();
            this._clientState = plugin.GetClientState();
            this.Aetherytes = plugin.Aetherytes;

            Svc.Condition.ConditionChange += ConChange;

            // Beispiel: Falls du Territorien mit IDs kennst
            this.Territories = new Dictionary<string, ushort>()
            {
                { "new gridania",               132 }, // Bsp: Gridania = 133
                { "limsa lominsa lower decks",  129 },
                { "ul'dah - steps of nald",     130 },
            };
        }

        private void ConChange(ConditionFlag flag, bool value)
        {
            if (!autoDetect) return;

            if (flag == ConditionFlag.BetweenAreas && value == false)
            {
                string currentWorld = Svc.ClientState.LocalPlayer!.CurrentWorld.Value.Name.ExtractText();
                if (currentWorld != lastKnownServer)
                {
                    lastKnownServer = currentWorld;
                    if (currentWorld == startServer)
                    {
                        autoDetect = false;
                        return;
                    }
                    StartSequence();
                }
            }
        }

        public override void Draw()
        {
            ImGui.Text($"Status => {(isRunning ? "Running" : autoDetect ? "Waiting for Server Change" : "Idle")}");

            //ImGui.Spacing();

            //ImGui.Text($"Spent {spentGil} Gil this session.");

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.Text("Message");
            if (ImGui.InputText("##Message", ref message, 1024))
            {
                // ...
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.Text("Chat");
            ImGui.Combo("##Chat", ref choosenChat, chatOptions, chatOptions.Length);

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.Text("Preview");

            // Setze den Text in der gewünschten Farbe:
            ImGui.PushStyleColor(ImGuiCol.Text, chatColors[choosenChat]);

            // Erhalte den noch verfügbaren Platz:
            var availSize = ImGui.GetContentRegionAvail();
            availSize.Y -= 75;

            // Erstelle ein Child-Fenster, das den gesamten verfügbaren Raum einnimmt:
            ImGui.BeginChild("PreviewChild", availSize, false, ImGuiWindowFlags.None);

            // Zeichne den Inhalt:
            ImGui.TextWrapped(message);

            // Schließe das Child-Fenster:
            ImGui.EndChild();

            ImGui.PopStyleColor();

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (ImGui.Checkbox("Auto Detect Server Change", ref autoDetect))
            {
                // ...
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (ImGui.Button("Start"))
            {
                if (!isRunning)
                {
                    string currentServer = Svc.ClientState.LocalPlayer!.CurrentWorld.Value.Name.ExtractText();
                    startServer = currentServer;
                    lastKnownServer = currentServer;
                    StartSequence();
                }
                else
                {
                    _chat.PrintError("Already running!");
                }
            }

            ImGui.SameLine();


            if (ImGui.Button("Stop"))
            {
                cts?.Cancel();
            }

        }

        private void StartSequence()
        {
            cts = new CancellationTokenSource();
            _ = RunSequenceAsync(cts.Token);
        }

        private async Task RunSequenceAsync(CancellationToken ct)
        {
            isRunning = true;
            try
            {
                // Kopie der Route, damit wir ggf. Einträge entfernen können
                var route = new List<string>(AetherNames);

                // 1) Prüfen, ob wir bereits in einem der Territorien stehen
                string? currentName = GetTerritoryNameWeAreIn(route);
                if (currentName != null)
                {
                    // => Nachricht sofort absenden, ohne Teleport
                    _chat.Print($"Already in {currentName}, sending immediate message...");
                    await WaitForChatReady(ct);
                    await Task.Delay(1000, ct);
                    SendPartyMessage(message);

                    // Aus der Route entfernen, damit wir es nicht nochmal machen
                    route.Remove(currentName);
                }

                // 2) Jetzt normal durch die restliche Route laufen
                foreach (var name in route)
                {
                    // Falls wir nochmal abgebrochen haben
                    ct.ThrowIfCancellationRequested();

                    // Prüfen, ob wir das Territorium evtl. bereits haben
                    if (IsInTerritory(name))
                    {
                        // Dann kein Teleport nötig
                        _chat.Print($"Already in {name}, skip teleport");
                    }
                    else
                    {
                        TP tp = await TryingTeleport(ct, name);
                        if (!tp.tped)
                        {
                            _chat.Print("Error when trying to teleport.");
                            break;
                        }
                        spentGil += tp.cost;
                    }

                    await WaitWhileLoading(ct);

                    // Nun warten, bis Chat ready
                    await WaitForChatReady(ct);

                    // Noch 1 Sekunde Pause
                    await Task.Delay(1000, ct);

                    // Party-Nachricht
                    SendPartyMessage(message);
                }
            }
            catch (TaskCanceledException)
            {
                _chat.Print("Sequence canceled.");
            }
            catch (Exception ex)
            {
                _chat.PrintError($"Sequence error: {ex.Message}");
            }
            finally
            {
                isRunning = false;
            }
        }

        // Gibt den *ersten* AetherName zurück, in dessen Territory wir uns gerade befinden
        // oder null, wenn wir in keinem der Route-Gebiete sind.
        private string? GetTerritoryNameWeAreIn(List<string> route)
        {
            foreach (var name in route)
            {
                if (IsInTerritory(name))
                {
                    return name;
                }
            }
            return null;
        }

        private async Task WaitWhileLoading(CancellationToken ct)
        {
            // Solange wir noch in "BetweenAreas" sind, warten wir
            while (Svc.Condition[ConditionFlag.BetweenAreas] && !ct.IsCancellationRequested)
            {
                await Task.Delay(250, ct);
            }
        }

        private class TP
        {
            public int cost = 0;
            public bool tped = false;
        }

        private async Task<TP> TryingTeleport(CancellationToken ct, string name)
        {
            bool changed = false;

            TP x = new TP
            {
                tped = false,
                cost = 0
            };

            for (int i = 0; i < 3; i++)
            {
                TP tp = TeleportTo(name);
                if (!tp.tped)
                {
                   continue;
                }

                // Warte, ob wir in 5 Sekunden das Event bekommen
                changed = await WaitForTerritoryChangedOrTimeout(ct, 7000);
                if (changed) return tp;
            }

            return x;

        }

        // Prüft, ob der aktuelle TerritoryType = Territories[name.ToLower()]
        private bool IsInTerritory(string name)
        {
            if (Territories.TryGetValue(name.ToLower(), out ushort terrId))
            {
                return _clientState.TerritoryType == terrId;
            }
            // Falls du keine TerrID hast, kannst du hier false zurückgeben
            return false;
        }

        // Teleport
        private unsafe TP TeleportTo(string name)
        {
            TP tp = new TP
            {
                cost = 0,
                tped = false
            };
            if (!Aetherytes.TryGetValue(name.ToLower(), out uint aethId))
                return tp;

            Telepo* tel = Telepo.Instance();
            if (tel == null)
                return tp;

            // 2 => confirm
            var list = tel->TeleportList;
            tp.tped = tel->Teleport(aethId, 2);
            if (!tp.tped) return tp;
            foreach (var t in list)
            {
                if (tp.tped && t.AetheryteId == aethId)
                {
                    tp.cost = (int)t.GilCost;
                }
            }
            return tp;
        }

        private async Task<bool> WaitForTerritoryChangedOrTimeout(CancellationToken ct, int timeoutMs)
        {
            // Wir behalten die aktuelle Territory-ID für den Fall, 
            // dass wir prüfen wollen, ob sich *wirklich* was ändert.
            ushort startTerr = _clientState.TerritoryType;

            var tcs = new TaskCompletionSource<bool>();

            void Handler(ushort newTerr)
            {
                // nur wenn es sich auch wirklich geändert hat
                if (newTerr != startTerr)
                {
                    tcs.TrySetResult(true);
                }
            }

            _clientState.TerritoryChanged += Handler;

            // Wir haben zwei Tasks: 
            //  (1) tcs.Task (wenn TerritoryChanged wirklich eintritt)
            //  (2) Task.Delay(...) als Timeout
            Task delayTask = Task.Delay(timeoutMs, ct);
            Task territoryTask = tcs.Task;

            try
            {
                // Warten auf whichever finishes first
                var finished = await Task.WhenAny(territoryTask, delayTask);

                // Wenn das territoryTask zuerst fertig wurde => Erfolg
                if (finished == territoryTask && territoryTask.IsCompletedSuccessfully)
                {
                    return true;
                }

                // Sonst => Timeout oder Cancel
                return false;
            }
            finally
            {
                // Aufräumen
                _clientState.TerritoryChanged -= Handler;
            }
        }


        // Warte, bis Chat.Instance != null (oder was immer du als Chat-Bereitschaft definierst)
        private async Task WaitForChatReady(CancellationToken ct)
        {
            while (!IsChatReady() && !ct.IsCancellationRequested)
            {
                await Task.Delay(250, ct);
            }
        }
        private bool IsChatReady()
        {
            return Chat.Instance != null;
        }

        // Chat senden
        private void SendPartyMessage(string txt)
        {
            Svc.Framework.RunOnTick(() =>
            {
                Chat.Instance.SendMessage($"/{chatOptions[choosenChat].ToLower()} {txt}");
            });
        }

        public void Dispose()
        {
            cts?.Cancel();
            cts?.Dispose();

            Svc.Condition.ConditionChange -= ConChange;
        }
    }
}
