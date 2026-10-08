using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using BepInEx;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace DrakesTelemarker
{
    [BepInPlugin(GUID, ModName, Version)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [BepInDependency("server_devcommands", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class DrakesTelemarkerPlugin : BaseUnityPlugin
    {
        public const string ModName = "DrakesTelemarker";
        public const string Version = "0.1.0";
        public const string GUID = "com.drakemods." + ModName;

        internal const int SlotCount = 10;

        private void Awake()
        {
            TelemarkerClearDialog.RegisterForGuiRebuild();
            CommandManager.Instance.AddConsoleCommand(new TelemarkCommand(this));
            CommandManager.Instance.AddConsoleCommand(new SetMarkCommand(this));
            CommandManager.Instance.AddConsoleCommand(new RecallMarkCommand(this));
            TelemarkerMinimap.Init(this);
            Logger.LogInfo($"{ModName} v{Version} loaded — type 'telemark' in console for commands.");
        }

        internal static string GetMarksFilePath()
        {
            string dir = Path.Combine(Paths.ConfigPath, ModName);
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "TelemarkerMarks.json");
        }

        internal static string GetWorldKey()
        {
            if (ZNet.instance != null)
            {
                string name = ZNet.instance.GetWorldName();
                if (!string.IsNullOrEmpty(name))
                    return name;
            }

            return "noworld";
        }

        internal static string SanitizeWorldKey(string worldName)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                worldName = worldName.Replace(c, '_');
            return string.IsNullOrEmpty(worldName) ? "world" : worldName;
        }

        internal static Player? GetLocalPlayerOrNotify()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
                Notify("No local player (join a world first).");
            return player;
        }

        internal static void Notify(string message)
        {
            PrintToDeveloperConsole($"[{ModName}] {message}");
        }

        private static void PrintToDeveloperConsole(string line)
        {
            try
            {
                object? inst = TryGetValheimConsoleInstance();
                if (inst == null)
                {
                    Debug.Log(line);
                    return;
                }

                MethodInfo? print = inst.GetType().GetMethod("Print",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                    new[] { typeof(string) }, null);
                if (print == null)
                {
                    Debug.Log(line);
                    return;
                }

                print.Invoke(inst, new object[] { line });
            }
            catch
            {
                Debug.Log(line);
            }
        }

        private static object? TryGetValheimConsoleInstance()
        {
            try
            {
                Type? consoleType = typeof(Player).Assembly.GetType("Console");
                if (consoleType == null)
                    return null;

                PropertyInfo? pi =
                    consoleType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                if (pi != null)
                    return pi.GetValue(null);

                FieldInfo? fi = consoleType.GetField("instance", BindingFlags.Public | BindingFlags.Static);
                if (fi != null)
                    return fi.GetValue(null);

                fi = consoleType.GetField("m_instance", BindingFlags.NonPublic | BindingFlags.Static);
                return fi?.GetValue(null);
            }
            catch
            {
                return null;
            }
        }

        private static void PrintMultilineToDeveloperConsole(string text)
        {
            foreach (string raw in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                if (raw.Length > 0)
                    PrintToDeveloperConsole(raw);
            }
        }

        internal bool TrySetSlot(int slot, Vector3 position, out string error)
        {
            error = "";
            string path = GetMarksFilePath();
            string worldKey = SanitizeWorldKey(GetWorldKey());
            TelemarkerMarksDocument doc = TelemarkerMarksDocument.Load(path);
            doc.UpsertSlot(worldKey, slot, position);
            TelemarkerMarksDocument.Save(path, doc);
            TelemarkerMinimap.RequestSync();
            return true;
        }

        internal bool TryRecallSlot(int slot, out Vector3 position, out string error)
        {
            position = default;
            error = "";
            string path = GetMarksFilePath();
            string worldKey = SanitizeWorldKey(GetWorldKey());
            TelemarkerMarksDocument doc = TelemarkerMarksDocument.Load(path);
            return doc.TryGetSlot(worldKey, slot, out position, out error);
        }

        internal bool TryClearSlot(int slot, out string error)
        {
            error = "";
            string path = GetMarksFilePath();
            string worldKey = SanitizeWorldKey(GetWorldKey());
            TelemarkerMarksDocument doc = TelemarkerMarksDocument.Load(path);
            if (!doc.ClearSlot(worldKey, slot))
            {
                error = $"Mark {slot} was already empty.";
                return false;
            }

            TelemarkerMarksDocument.Save(path, doc);
            TelemarkerMinimap.RequestSync();
            return true;
        }

        internal bool TryClearAllSlots(out int clearedCount, out string error)
        {
            clearedCount = 0;
            error = "";
            string path = GetMarksFilePath();
            string worldKey = SanitizeWorldKey(GetWorldKey());
            TelemarkerMarksDocument doc = TelemarkerMarksDocument.Load(path);
            clearedCount = doc.ClearAllSlotsForWorld(worldKey);
            if (clearedCount == 0)
            {
                error = "No marks to clear.";
                return false;
            }

            TelemarkerMarksDocument.Save(path, doc);
            TelemarkerMinimap.RequestSync();
            return true;
        }

        /// <summary>
        /// Devcommands client flag and/or server admin (adminlist / mods like Server Devcommands — see Jotunn admin sync).
        /// </summary>
        internal static bool CanUseTelemarkerCheats()
        {
            if (IsDevcommandsClientFlagOn())
                return true;

            try
            {
                SynchronizationManager? sync = SynchronizationManager.Instance;
                if (sync != null && sync.PlayerIsAdmin)
                    return true;
            }
            catch
            {
                // ignore sync errors during startup
            }

            return false;
        }

        /// <summary>
        /// Client cheat flag as the game exposes it after other mods' patches (e.g. Server Devcommands adjusts
        /// <see cref="Terminal.IsCheatsEnabled"/>). Part of <see cref="CanUseTelemarkerCheats"/> (with Jotunn admin sync).
        /// </summary>
        internal static bool IsDevcommandsClientFlagOn() => ReadTerminalCheatsEnabledWithFallback();

        /// <summary>
        /// Prefer <c>Terminal.IsCheatsEnabled</c> so Server Devcommands and similar mods stay in sync; fall back to
        /// <c>m_cheat</c> via reflection if needed (no direct field access — publicize/IL may differ).
        /// </summary>
        private static bool ReadTerminalCheatsEnabledWithFallback()
        {
            try
            {
                const BindingFlags bf = BindingFlags.Public | BindingFlags.NonPublic |
                                        BindingFlags.Static | BindingFlags.Instance;
                foreach (MethodInfo m in typeof(Terminal).GetMethods(bf))
                {
                    if (m.Name != "IsCheatsEnabled" || m.ReturnType != typeof(bool) ||
                        m.GetParameters().Length != 0)
                        continue;

                    if (m.IsStatic)
                    {
                        if (m.Invoke(null, null) is bool bs)
                            return bs;
                    }
                    else
                    {
                        object? inst = TryGetValheimConsoleInstance();
                        if (inst != null && m.Invoke(inst, null) is bool bi)
                            return bi;
                    }
                }
            }
            catch
            {
                // ignore
            }

            return ReadTerminalCheatFlagViaReflection();
        }

        /// <summary>
        /// Do not read <see cref="Terminal"/> cheat fields directly — vanilla assembly may not be publicized at runtime (FieldAccessException).
        /// </summary>
        private static bool ReadTerminalCheatFlagViaReflection()
        {
            try
            {
                const BindingFlags vis = BindingFlags.Public | BindingFlags.NonPublic;
                object? inst = TryGetValheimConsoleInstance();

                FieldInfo? fi = typeof(Terminal).GetField("m_cheat", vis | BindingFlags.Instance);
                if (fi != null && inst != null && fi.GetValue(inst) is bool bi)
                    return bi;

                fi = typeof(Terminal).GetField("m_cheat", vis | BindingFlags.Static);
                if (fi != null && fi.IsStatic && fi.GetValue(null) is bool bs)
                    return bs;
            }
            catch
            {
                // ignore
            }

            return false;
        }

        internal static string[] ArgsTail(string[] args, int start)
        {
            int len = args.Length - start;
            if (len <= 0)
                return Array.Empty<string>();
            var tail = new string[len];
            Array.Copy(args, start, tail, 0, len);
            return tail;
        }

        internal static string FormatVec(Vector3 p)
        {
            return string.Format(CultureInfo.InvariantCulture, "({0:F1}, {1:F1}, {2:F1})", p.x, p.y, p.z);
        }

        /// <summary>Jotunn passes this to vanilla tab-completion — include every token users might Tab-complete after the command.</summary>
        internal static List<string> BuildTelemarkTabCompletions()
        {
            var list = new List<string>
            {
                "help", "?", "list", "set", "recall", "clear", "mark", "all",
            };
            for (int i = 1; i <= SlotCount; i++)
                list.Add(i.ToString(CultureInfo.InvariantCulture));
            return list;
        }

        internal static List<string> BuildMarkAliasTabCompletions()
        {
            var list = new List<string> { "mark" };
            for (int i = 1; i <= SlotCount; i++)
                list.Add(i.ToString(CultureInfo.InvariantCulture));
            return list;
        }

        /// <summary>
        /// Parsed args exclude the command word. Requires <c>mark</c> then slot (e.g. set mark 3).
        /// Allows an extra <c>mark</c> token for symmetry (set mark mark 3).
        /// </summary>
        internal static bool TryParseMarkSlotArgs(string[] args, out int slot, out string usageError)
        {
            slot = 0;
            usageError = "";

            if (args == null || args.Length < 2 || !args[0].Equals("mark", StringComparison.OrdinalIgnoreCase))
            {
                usageError =
                    $"Usage: telemark set mark <1-{SlotCount}> | telemark recall mark <1-{SlotCount}> (aliases: set mark …, recall mark …)";
                return false;
            }

            int idx = 1;
            if (idx < args.Length && args[idx].Equals("mark", StringComparison.OrdinalIgnoreCase))
                idx++;

            if (idx >= args.Length ||
                !int.TryParse(args[idx], NumberStyles.Integer, CultureInfo.InvariantCulture, out slot) ||
                slot < 1 || slot > SlotCount)
            {
                usageError = $"Slot must be 1-{SlotCount}.";
                return false;
            }

            return true;
        }

        private static void PrintTelemarkUsage()
        {
            string body =
                $"[{ModName}] Cheat vs help:\r\n" +
                $"  • Running telemark alone (or telemark help) is NOT a cheat — it only prints this guide.\r\n" +
                $"  • telemark list / set / recall / clear ARE cheats — need devcommands or server admin.\r\n" +
                $"  • Same for aliases: set mark … and recall mark … (cheat access required).\r\n" +
                "\r\n" +
                $"Commands:\r\n" +
                $"  telemark\r\n" +
                $"      Show this help (always allowed).\r\n" +
                $"  telemark list\r\n" +
                $"      List every slot that has a saved position for this world (cheat).\r\n" +
                $"  telemark set mark <1-{SlotCount}>\r\n" +
                $"      Save your current position into a slot (cheat).\r\n" +
                $"  telemark recall mark <1-{SlotCount}>\r\n" +
                $"      Warp to a saved slot (cheat).\r\n" +
                $"  telemark clear <1-{SlotCount}>\r\n" +
                $"      Opens a confirmation dialog — click Yes to erase that slot, No to cancel.\r\n" +
                $"  telemark clear all\r\n" +
                $"      Same dialog for wiping every mark in this world.\r\n" +
                $"Aliases:\r\n" +
                $"  set mark <n>        Same as telemark set mark … (cheat).\r\n" +
                $"  recall mark <n>    Same as telemark recall mark … (cheat).\r\n" +
                "\r\n" +
                "Cheat access = devcommands OR server admin (adminlist; Server Devcommands works well here).\r\n" +
                "Jotunn syncs admin status for PlayerIsAdmin. Open console (F5): devcommands\r\n" +
                "\r\n" +
                $"Minimap:\r\n" +
                $"  Numbered pins labeled Mark 1–Mark {SlotCount}. Right-click (or Ctrl+left-click) a pin to recall when cheat access matches console (devcommands / Server Devcommands admin, same as telemark recall).\r\n";

            PrintMultilineToDeveloperConsole(body);
        }

        private static void RunTelemarkList()
        {
            string path = GetMarksFilePath();
            string worldKey = SanitizeWorldKey(GetWorldKey());
            TelemarkerMarksDocument doc = TelemarkerMarksDocument.Load(path);

            var sb = new StringBuilder();
            int count = 0;
            for (int i = 1; i <= SlotCount; i++)
            {
                if (!doc.TryGetSlot(worldKey, i, out Vector3 p, out _))
                    continue;
                count++;
                sb.AppendLine($"  slot {i}: {FormatVec(p)}");
            }

            if (count == 0)
            {
                Notify("No active marks for this world.");
                return;
            }

            PrintMultilineToDeveloperConsole(
                $"[{ModName}] Active marks ({count}) for world \"{worldKey}\":\n{sb.ToString().TrimEnd()}");
        }

        /// <summary>
        /// Only empty/help routes skip cheat checks. All action subcommands require <see cref="CanUseTelemarkerCheats"/>.
        /// </summary>
        private static void RouteTelemark(DrakesTelemarkerPlugin plugin, string[] args)
        {
            if (args == null || args.Length == 0)
            {
                PrintTelemarkUsage();
                return;
            }

            string head = args[0].ToLowerInvariant();
            if (head is "help" or "?")
            {
                PrintTelemarkUsage();
                return;
            }

            if (!CanUseTelemarkerCheats())
            {
                Notify(
                    "That needs cheat access (devcommands or server admin). telemark alone only prints help — run it for the full list.");
                return;
            }

            switch (head)
            {
                case "list":
                    RunTelemarkList();
                    return;

                case "set":
                    if (!TryParseMarkSlotArgs(ArgsTail(args, 1), out int setSlot, out string setErr))
                    {
                        Notify(setErr);
                        return;
                    }

                    if (GetLocalPlayerOrNotify() is not Player setPlayer)
                        return;

                    Vector3 setPos = setPlayer.transform.position;
                    plugin.TrySetSlot(setSlot, setPos, out _);
                    Notify($"Saved mark {setSlot} at {FormatVec(setPos)}.");
                    return;

                case "recall":
                    if (!TryParseMarkSlotArgs(ArgsTail(args, 1), out int recallSlot, out string recallErr))
                    {
                        Notify(recallErr);
                        return;
                    }

                    if (GetLocalPlayerOrNotify() is not Player recallPlayer)
                        return;

                    if (!plugin.TryRecallSlot(recallSlot, out Vector3 recallPos, out string recallFail))
                    {
                        Notify(string.IsNullOrEmpty(recallFail) ? $"Mark {recallSlot} is empty." : recallFail);
                        return;
                    }

                    recallPlayer.transform.position = recallPos;
                    Physics.SyncTransforms();
                    Notify($"Recalled mark {recallSlot} -> {FormatVec(recallPos)}.");
                    return;

                case "clear":
                    if (args.Length < 2)
                    {
                        Notify($"Usage: telemark clear <1-{SlotCount}>  |  telemark clear all");
                        return;
                    }

                    string clearTarget = args[1];
                    if (clearTarget.Equals("all", StringComparison.OrdinalIgnoreCase))
                    {
                        if (args.Length > 2)
                        {
                            Notify("Usage: telemark clear all");
                            return;
                        }

                        TelemarkerClearDialog.RequestConfirmation(plugin, clearAll: true, slot1Based: 0);
                        return;
                    }

                    if (!int.TryParse(clearTarget, NumberStyles.Integer, CultureInfo.InvariantCulture,
                            out int clearSlot) ||
                        clearSlot < 1 || clearSlot > SlotCount)
                    {
                        Notify($"Usage: telemark clear <1-{SlotCount}>  |  telemark clear all");
                        return;
                    }

                    if (args.Length > 2)
                    {
                        Notify($"Usage: telemark clear <1-{SlotCount}>");
                        return;
                    }

                    TelemarkerClearDialog.RequestConfirmation(plugin, clearAll: false, slot1Based: clearSlot);
                    return;

                default:
                    Notify($"Unknown telemark subcommand '{args[0]}'. Check console log for full usage.");
                    PrintTelemarkUsage();
                    return;
            }
        }

        private sealed class TelemarkCommand : ConsoleCommand
        {
            private readonly DrakesTelemarkerPlugin _plugin;

            public TelemarkCommand(DrakesTelemarkerPlugin plugin)
            {
                _plugin = plugin;
            }

            public override string Name => "telemark";

            public override string Help =>
                "Not a cheat: prints usage. Subcommands list/set/recall/clear require cheat access — run with no args.";

            public override bool IsCheat => false;

            public override List<string> CommandOptionList() => BuildTelemarkTabCompletions();

            public override void Run(string[] args)
            {
                RouteTelemark(_plugin, args);
            }
        }

        private sealed class SetMarkCommand : ConsoleCommand
        {
            private readonly DrakesTelemarkerPlugin _plugin;

            public SetMarkCommand(DrakesTelemarkerPlugin plugin)
            {
                _plugin = plugin;
            }

            public override string Name => "set";

            public override string Help =>
                $"Shortcut for telemark set mark <1-{SlotCount}>";

            public override bool IsCheat => false;

            public override List<string> CommandOptionList() => BuildMarkAliasTabCompletions();

            public override void Run(string[] args)
            {
                if (!CanUseTelemarkerCheats())
                {
                    Notify("Need cheat access (devcommands or server admin). Run telemark for help.");
                    return;
                }

                if (!TryParseMarkSlotArgs(args, out int slot, out string parseErr))
                {
                    Notify(parseErr);
                    return;
                }

                if (GetLocalPlayerOrNotify() is not Player player)
                    return;

                Vector3 pos = player.transform.position;
                _plugin.TrySetSlot(slot, pos, out _);
                Notify($"Saved mark {slot} at {DrakesTelemarkerPlugin.FormatVec(pos)}.");
            }
        }

        private sealed class RecallMarkCommand : ConsoleCommand
        {
            private readonly DrakesTelemarkerPlugin _plugin;

            public RecallMarkCommand(DrakesTelemarkerPlugin plugin)
            {
                _plugin = plugin;
            }

            public override string Name => "recall";

            public override string Help =>
                $"Shortcut for telemark recall mark <1-{SlotCount}>";

            public override bool IsCheat => false;

            public override List<string> CommandOptionList() => BuildMarkAliasTabCompletions();

            public override void Run(string[] args)
            {
                if (!CanUseTelemarkerCheats())
                {
                    Notify("Need cheat access (devcommands or server admin). Run telemark for help.");
                    return;
                }

                if (!TryParseMarkSlotArgs(args, out int slot, out string parseErr))
                {
                    Notify(parseErr);
                    return;
                }

                if (GetLocalPlayerOrNotify() is not Player player)
                    return;

                if (!_plugin.TryRecallSlot(slot, out Vector3 pos, out string err))
                {
                    Notify(string.IsNullOrEmpty(err) ? $"Mark {slot} is empty." : err);
                    return;
                }

                player.transform.position = pos;
                Physics.SyncTransforms();
                Notify($"Recalled mark {slot} -> {DrakesTelemarkerPlugin.FormatVec(pos)}.");
            }
        }
    }

    [DataContract]
    internal sealed class TelemarkerMarksDocument
    {
        [DataMember(Name = "schemaVersion")]
        public int SchemaVersion { get; set; } = 1;

        /// <summary>Sanitized world key -> slots for that world.</summary>
        [DataMember(Name = "worlds")]
        public Dictionary<string, WorldMarkSlots> Worlds { get; set; } = new Dictionary<string, WorldMarkSlots>();

        internal static TelemarkerMarksDocument Load(string path)
        {
            if (!File.Exists(path))
                return new TelemarkerMarksDocument();

            try
            {
                using FileStream stream = File.OpenRead(path);
                var serializer = new DataContractJsonSerializer(typeof(TelemarkerMarksDocument));
                if (serializer.ReadObject(stream) is TelemarkerMarksDocument doc)
                {
                    doc.Worlds ??= new Dictionary<string, WorldMarkSlots>();
                    foreach (WorldMarkSlots world in doc.Worlds.Values)
                        world.Slots ??= new Dictionary<string, MarkCoordinates>();

                    return doc;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DrakesTelemarker] Could not read {path}, starting fresh: {ex.Message}");
            }

            return new TelemarkerMarksDocument();
        }

        internal static void Save(string path, TelemarkerMarksDocument doc)
        {
            doc.Worlds ??= new Dictionary<string, WorldMarkSlots>();
            string tmp = path + ".tmp";
            using (FileStream stream = File.Create(tmp))
            {
                var serializer = new DataContractJsonSerializer(typeof(TelemarkerMarksDocument));
                serializer.WriteObject(stream, doc);
            }

            if (File.Exists(path))
                File.Delete(path);
            File.Move(tmp, path);
        }

        internal void UpsertSlot(string worldKey, int slot1Based, Vector3 position)
        {
            Worlds.TryGetValue(worldKey, out WorldMarkSlots? world);
            if (world == null)
            {
                world = new WorldMarkSlots();
                Worlds[worldKey] = world;
            }

            world.Slots ??= new Dictionary<string, MarkCoordinates>();
            string slotKey = slot1Based.ToString(CultureInfo.InvariantCulture);
            world.Slots[slotKey] = new MarkCoordinates { X = position.x, Y = position.y, Z = position.z };
        }

        internal bool TryGetSlot(string worldKey, int slot1Based, out Vector3 position, out string error)
        {
            position = default;
            error = "";
            if (!Worlds.TryGetValue(worldKey, out WorldMarkSlots? world) || world?.Slots == null)
            {
                error = $"Mark {slot1Based} is empty.";
                return false;
            }

            string slotKey = slot1Based.ToString(CultureInfo.InvariantCulture);
            if (!world.Slots.TryGetValue(slotKey, out MarkCoordinates? c))
            {
                error = $"Mark {slot1Based} is empty.";
                return false;
            }

            position = new Vector3(c!.X, c!.Y, c!.Z);
            return true;
        }

        internal bool ClearSlot(string worldKey, int slot1Based)
        {
            if (!Worlds.TryGetValue(worldKey, out WorldMarkSlots? world) || world.Slots == null)
                return false;

            string slotKey = slot1Based.ToString(CultureInfo.InvariantCulture);
            return world.Slots.Remove(slotKey);
        }

        /// <returns>Number of slots cleared.</returns>
        internal int ClearAllSlotsForWorld(string worldKey)
        {
            if (!Worlds.TryGetValue(worldKey, out WorldMarkSlots? world) || world.Slots == null || world.Slots.Count == 0)
                return 0;

            int n = world.Slots.Count;
            world.Slots.Clear();
            Worlds.Remove(worldKey);
            return n;
        }
    }

    [DataContract]
    internal sealed class WorldMarkSlots
    {
        /// <summary>Slot index string ("1".."10") -> coordinates.</summary>
        [DataMember(Name = "slots")]
        public Dictionary<string, MarkCoordinates>? Slots { get; set; }
    }

    [DataContract]
    internal sealed class MarkCoordinates
    {
        [DataMember(Name = "x")] public float X { get; set; }
        [DataMember(Name = "y")] public float Y { get; set; }
        [DataMember(Name = "z")] public float Z { get; set; }
    }
}
