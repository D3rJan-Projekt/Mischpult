using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Mischpult.Native
{
    /// <summary>
    /// 4.4.1: Windows-Firewall fuer die Fernbedienung – nachsehen (ohne Admin-Rechte) und auf Wunsch eine Regel anlegen
    /// (mit Admin-Rechten, nur eingehend, nur dieser Port, nur aus dem eigenen Teilnetz). Ueber die Firewall-Schnittstelle von Windows.
    /// </summary>
    internal static class Firewall440
    {
        public enum State { Unknown, Off, Allowed, Blocked, NoRule }

        public const string RuleName = "Mischpult Fernbedienung";
        private const int InDir = 1, ActBlock = 0, ActAllow = 1, ProtoTcp = 6, ProtoAny = 256;
        private const BindingFlags Get = BindingFlags.GetProperty, Set = BindingFlags.SetProperty, Call = BindingFlags.InvokeMethod;

        private static object P(object o, string name) => o.GetType().InvokeMember(name, Get, null, o, null);
        private static object P(object o, string name, object arg) => o.GetType().InvokeMember(name, Get, null, o, new[] { arg });
        private static void S(object o, string name, object v) => o.GetType().InvokeMember(name, Set, null, o, new[] { v });

        /// <summary>Netzwerk-Profil jetzt: "Privat", "Öffentlich", "Domäne" (oder null).</summary>
        public static string ProfileName(int profiles) =>
            (profiles & 2) != 0 ? "Privat" : (profiles & 4) != 0 ? "Öffentlich" : (profiles & 1) != 0 ? "Domäne" : null;

        /// <summary>Darf das Pult auf diesem Port von aussen angesprochen werden? profile = aktuelles Profil (Bitmaske).</summary>
        public static State Check(string exe, int port, out int profile)
        {
            profile = 0;
            if (!Win32.IsWindows) return State.Unknown;
            try
            {
                var t = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
                if (t == null) return State.Unknown;
                object pol = Activator.CreateInstance(t);
                profile = (int)P(pol, "CurrentProfileTypes");
                bool anyOn = false;
                foreach (int bit in new[] { 1, 2, 4 })
                    if ((profile & bit) != 0 && (bool)P(pol, "FirewallEnabled", bit)) anyOn = true;
                if (!anyOn) return State.Off;
                bool allow = false, block = false;
                foreach (object r in (IEnumerable)P(pol, "Rules"))
                {
                    try
                    {
                        if ((int)P(r, "Direction") != InDir || !(bool)P(r, "Enabled")) continue;
                        if (((int)P(r, "Profiles") & profile) == 0) continue;
                        int proto = (int)P(r, "Protocol");
                        if (proto != ProtoTcp && proto != ProtoAny) continue;
                        string app = P(r, "ApplicationName") as string;
                        bool forUs = !string.IsNullOrEmpty(app) && string.Equals(Environment.ExpandEnvironmentVariables(app), exe, StringComparison.OrdinalIgnoreCase);
                        bool anyApp = string.IsNullOrEmpty(app);
                        if (!forUs && !anyApp) continue;
                        if (!PortMatches(P(r, "LocalPorts") as string, port, proto)) continue;
                        int act = (int)P(r, "Action");
                        if (act == ActBlock && forUs) block = true;
                        else if (act == ActAllow && (forUs || (anyApp && !string.IsNullOrEmpty(P(r, "LocalPorts") as string)))) allow = true;
                    }
                    catch { }
                }
                return block ? State.Blocked : allow ? State.Allowed : State.NoRule;
            }
            catch { return State.Unknown; }
        }

        /// <summary>"*" oder leer = alle Ports, sonst Liste wie "80,8000-9000".</summary>
        public static bool PortMatches(string ports, int port, int proto = ProtoTcp)
        {
            if (string.IsNullOrEmpty(ports) || ports.Trim() == "*") return true;
            foreach (var part in ports.Split(','))
            {
                var p = part.Trim();
                int dash = p.IndexOf('-');
                if (dash > 0)
                {
                    if (int.TryParse(p.Substring(0, dash), out int a) && int.TryParse(p.Substring(dash + 1), out int b) && port >= a && port <= b) return true;
                }
                else if (int.TryParse(p, out int one) && one == port) return true;
            }
            return false;
        }

        /// <summary>
        /// Regel anlegen (braucht Admin-Rechte): eingehend, TCP, nur dieser Port, nur aus dem eigenen Teilnetz, alle Profile.
        /// Sperr-Regeln, die Windows beim Abbrechen der Firewall-Frage fuer diese EXE angelegt hat, kommen weg. null = geklappt.
        /// </summary>
        public static string Allow(string exe, int port)
        {
            if (!Win32.IsWindows) return "Geht nur unter Windows.";
            try
            {
                var t = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
                object pol = Activator.CreateInstance(t);
                object rules = P(pol, "Rules");
                var remove = new List<string>();
                foreach (object r in (IEnumerable)rules)
                {
                    try
                    {
                        string name = P(r, "Name") as string;
                        string app = P(r, "ApplicationName") as string;
                        bool forUs = !string.IsNullOrEmpty(app) && string.Equals(Environment.ExpandEnvironmentVariables(app), exe, StringComparison.OrdinalIgnoreCase);
                        if (name == RuleName || (forUs && (int)P(r, "Direction") == InDir && (int)P(r, "Action") == ActBlock)) remove.Add(name);
                    }
                    catch { }
                }
                foreach (var n in remove)
                    try { rules.GetType().InvokeMember("Remove", Call, null, rules, new object[] { n }); } catch { }
                var rt = Type.GetTypeFromProgID("HNetCfg.FWRule");
                object rule = Activator.CreateInstance(rt);
                S(rule, "Name", RuleName);
                S(rule, "Description", "Fernbedienung des Mischpults im Browser – nur aus dem eigenen Heimnetz.");
                S(rule, "ApplicationName", exe);
                S(rule, "Protocol", ProtoTcp);
                S(rule, "LocalPorts", port.ToString(System.Globalization.CultureInfo.InvariantCulture));
                S(rule, "RemoteAddresses", "LocalSubnet");
                S(rule, "Direction", InDir);
                S(rule, "Action", ActAllow);
                S(rule, "Profiles", 7);
                S(rule, "Enabled", true);
                rules.GetType().InvokeMember("Add", Call, null, rules, new[] { rule });
                return null;
            }
            catch (Exception ex) { return "Die Firewall-Regel ließ sich nicht anlegen: " + ex.Message; }
        }
    }
}
