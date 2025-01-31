using System;
using System.Numerics;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using Lumina.Excel.Sheets;
using ECommons;
using System.Threading.Tasks;
using ECommons.Automation;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;
using System.ComponentModel.DataAnnotations;
using AdRunner.Utils;
using System.Reflection.Emit;

namespace AdRunner.Windows;

public class MainWindow : Window, IDisposable
{
    private Plugin Plugin;
    private string status = "N/A";
    private IChatGui _chat;

    // We give this window a hidden ID using ##
    // So that the user will see "My Amazing Window" as window title,
    // but for ImGui the ID is "My Amazing Window##With a hidden ID"
    public MainWindow(Plugin plugin)
        : base("AdRunner##With a hidden ID", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 330),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        Plugin = plugin;
        _chat = plugin.GetChatGui();
    }

    public void Dispose() { }

    public override void Draw()
    {
        ImGui.Text($"Status => {status}");

        if (ImGui.Button("Teleport"))
        {
            try
            {
                unsafe
                {
                    _ = TeleportAsync();                    
                }
            }
            catch (Exception ex)
            {
                _chat.PrintError($"Some error occurred: {ex.Message}");
            }
        }
    }

    public async Task TeleportAsync()
    {
        
        
        int tries = 0;

        status = $"Get TP Pointer. Try {tries}";

        IntPtr addonPtr = ForceTeleportAddonPtr(tries);

        while (addonPtr == IntPtr.Zero && tries < 3)
        {
            await Task.Delay(500);
            tries++;
            status = $"Get TP Pointer. Try {tries}";
            addonPtr = ForceTeleportAddonPtr(tries);
        }

        if (addonPtr == IntPtr.Zero)
            return;

        status = "Get Items in 3 Seconds";
        await Task.Delay(3000);
        unsafe
        {
            AddonTeleport* addon = (AddonTeleport*)addonPtr;

            var list = addon->TeleportTreeList;
            int itemCount = 0;
            if (list == null)
                return;

            itemCount = list->AtkComponentList.GetItemCount();

            status = "Get Text Nodes";

            int maxCount = Math.Min(itemCount, 10);
            for (int i = 0; i < maxCount; i++)
            {
                // 1) Renderer holen
                AtkComponentListItemRenderer* renderer = list->AtkComponentList.GetItemRenderer(i);
                if (renderer == null)
                    continue;

                var textNode = (AtkTextNode*)renderer->AtkComponentButton.ButtonTextNode;

                if (textNode == null)
                    continue;

                string nodeText = textNode->NodeText.ExtractText(); ;

                status = $"Last Text Node: {nodeText}";
            }
        }
    }

    private IntPtr ForceTeleportAddonPtr(int tries)
    {
        IntPtr addonPtr = Svc.GameGui.GetAddonByName("Teleport");
        if (addonPtr == IntPtr.Zero && tries == 0)
        {
            Chat.Instance.SendMessage("/teleport");
        }
        return addonPtr;
    }

    public unsafe void ForceCallback(AddonTeleport* addon, int buttonValue)
    {
        const int paramCount = 2;
        var values = stackalloc AtkValue[paramCount];

        values[0] = new AtkValue
        {
            Type = ValueType.Int,
            Int = 0 // ID für "Teleportation"
        };
        values[1] = new AtkValue
        {
            Type = ValueType.Int,
            Int = buttonValue
        };

        addon->FireCallback((uint)paramCount, values, false);
    }



}
