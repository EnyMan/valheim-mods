using System;
using System.Collections.Generic;
using System.Linq;
using Jotunn.Entities;
using UnityEngine;

namespace SensibleHunting
{
    /// Diagnostic: `sh_probe` prints every creature in range and why it is or is not sensed, plus every
    /// ambient bird the registry knows about. With a name filter it also dumps the component list of any
    /// loaded object matching it - which is the only way to find out what a species actually *is*
    /// (`sh_probe seagal`), since prefab composition lives in asset bundles, not in the DLLs.
    internal class ProbeCommand : ConsoleCommand
    {
        public override string Name => "sh_probe";
        public override string Help => "Sensible Hunting diagnostics: sh_probe [name filter]";
        public override bool IsSecret => true;

        private static void Print(string line)
        {
            if (Console.instance != null) Console.instance.Print(line);
            SensibleHuntingPlugin.Log?.LogInfo(line);
        }

        public override void Run(string[] args)
        {
            var player = Player.m_localPlayer;
            var plugin = SensibleHuntingPlugin.Instance;
            if (player == null || plugin == null) { Print("sh_probe: no local player"); return; }

            var eye = player.transform.position;
            var range = plugin.CurrentRange(player);
            Print($"sh_probe: skill {player.GetSkillLevel(SensibleHuntingPlugin.Hunting):0} range {range:0} m " +
                  $"sensing {plugin.IsSensing(player)} game list [{plugin.GameAnimals.Value}]");

            var chars = new List<Character>();
            Character.GetCharactersInRange(eye, range, chars);
            Print($"characters in range: {chars.Count}");
            foreach (var c in chars)
            {
                var ai = c.GetBaseAI();
                Print($"  {Utils.GetPrefabName(c.gameObject),-18} {Vector3.Distance(c.transform.position, eye),5:0} m " +
                      $"ai={(ai == null ? "none" : ai.GetType().Name),-10} game={SensibleHuntingPlugin.IsGame(c),-5} " +
                      $"sensed={SensibleHuntingPlugin.CanSense(c),-5} flying={c.IsFlying(),-5} onGround={c.IsOnGround(),-5} " +
                      $"tamed={c.IsTamed(),-5} dead={c.IsDead()}");
            }

            Print($"ambient birds loaded: {RandomFlyingBird.Instances.Count}");
            foreach (var updater in RandomFlyingBird.Instances)
            {
                if (!(updater is RandomFlyingBird bird)) continue;
                var nview = bird.GetComponent<ZNetView>();
                var landed = nview != null && nview.IsValid() && nview.GetZDO().GetBool(ZDOVars.s_landed);
                Print($"  {Utils.GetPrefabName(bird.gameObject),-18} {Vector3.Distance(bird.transform.position, eye),5:0} m " +
                      $"listed={SensibleHuntingPlugin.IsGameName(bird.gameObject),-5} " +
                      $"zdo={(nview != null && nview.IsValid()),-5} landed={landed}");
            }

            var filter = args != null && args.Length > 0 ? args[0] : null;
            if (string.IsNullOrEmpty(filter)) { Print("pass a name filter to dump components, e.g. sh_probe seagal"); return; }

            var hits = 0;
            foreach (var nview in UnityEngine.Object.FindObjectsOfType<ZNetView>())
            {
                var name = Utils.GetPrefabName(nview.gameObject);
                if (name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (Vector3.Distance(nview.transform.position, eye) > 300f) continue;
                hits++;
                var comps = string.Join(", ", nview.GetComponents<Component>().Select(c => c.GetType().Name));
                Print($"  {name} {Vector3.Distance(nview.transform.position, eye):0} m [{comps}]");
            }
            Print($"objects matching \"{filter}\": {hits}");
        }
    }
}
