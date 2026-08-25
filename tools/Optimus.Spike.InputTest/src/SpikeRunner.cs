using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace Optimus.Spike
{
    /// <summary>Résultat d'un test du plan.</summary>
    public sealed class TestResult
    {
        public string Id;
        public string Name;
        public string Detail;
        /// <summary>PASS · FAIL · INFO · WARN · OBSERVE (verdict à fournir par l'utilisateur).</summary>
        public string Verdict;
        /// <summary>Question posée à l'utilisateur en mode « game ».</summary>
        public string Question;
        /// <summary>Réponse de l'utilisateur (mode « game »).</summary>
        public string Observation;

        public TestResult(string id, string name)
        {
            Id = id;
            Name = name;
            Detail = "";
            Verdict = "INFO";
            Observation = "";
        }
    }

    /// <summary>Options de ligne de commande.</summary>
    public sealed class Options
    {
        public string Mode = "probe";
        public string Target = "StarCitizen";
        public string Key = "L";
        public string HoldKey = "SPACE";
        public string DoubleTapKey = "";
        public string Modifier = "LALT";
        public string ProbeKey = "F13";
        public string ProbeKey2 = "F14";
        public bool IncludeMouseRight;
        public int Countdown = 5;
        public int GapMs = 2500;
        public string ReportPath;
        public bool ListKeys;
        public bool ShowHelp;
        public bool NoQuestions;

        // Mode "voice" (spike S0-3)
        // Défaut RCTRL/INSERT : mesuré sur defaults-4.9.json, ces touches ne sont utilisées par
        // aucune action de Star Citizen. F10 en revanche l'est (v_power_throttle_up/max).
        public string PushToTalk = "INSERT";
        public int Utterances = 5;
        public int MicDevice = -1;
        public string AudioDir;
        public bool ListMics;

        public static Options Parse(string[] args)
        {
            Options o = new Options();
            if (args == null) return o;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                string next = (i + 1 < args.Length) ? args[i + 1] : null;

                if (a == "--help" || a == "-h" || a == "/?") o.ShowHelp = true;
                else if (a == "--list-keys") o.ListKeys = true;
                else if (a == "--include-mouse-right") o.IncludeMouseRight = true;
                else if (a == "--no-questions") o.NoQuestions = true;
                else if (a == "--mode" && next != null) { o.Mode = next.ToLowerInvariant(); i++; }
                else if (a == "--target" && next != null) { o.Target = next; i++; }
                else if (a == "--key" && next != null) { o.Key = next; i++; }
                else if (a == "--hold-key" && next != null) { o.HoldKey = next; i++; }
                else if (a == "--doubletap-key" && next != null) { o.DoubleTapKey = next; i++; }
                else if (a == "--modifier" && next != null) { o.Modifier = next; i++; }
                else if (a == "--probe-key" && next != null) { o.ProbeKey = next; i++; }
                else if (a == "--report" && next != null) { o.ReportPath = next; i++; }
                else if (a == "--countdown" && next != null) { o.Countdown = ParseInt(next, 5); i++; }
                else if (a == "--gap" && next != null) { o.GapMs = ParseInt(next, 2500); i++; }
                else if (a == "--list-mics") o.ListMics = true;
                else if (a == "--ptt" && next != null) { o.PushToTalk = next; i++; }
                else if (a == "--utterances" && next != null) { o.Utterances = ParseInt(next, 5); i++; }
                else if (a == "--mic" && next != null) { o.MicDevice = ParseInt(next, -1); i++; }
                else if (a == "--audio-dir" && next != null) { o.AudioDir = next; i++; }
            }
            return o;
        }

        private static int ParseInt(string s, int fallback)
        {
            int v;
            return int.TryParse(s, out v) ? v : fallback;
        }
    }

    /// <summary>
    /// SPIKE S0-1 — « Star Citizen accepte-t-il une injection SendInput en scancode ? »
    ///
    /// Deux modes :
    ///   • probe : vérification automatique, sans le jeu. Répond aux questions vérifiables par
    ///             la machine (le scancode part-il correctement ? est-il marqué injecté ? quelle
    ///             est la granularité réelle des maintiens ? la disposition clavier pose-t-elle
    ///             problème ?).
    ///   • game  : plan d'observation en jeu. Le programme injecte, l'utilisateur observe et
    ///             répond ; un rapport Markdown est produit.
    /// </summary>
    public static class SpikeRunner
    {
        private const string Version = "1.0.0";

        public static int Run(string[] args)
        {
            Options options = Options.Parse(args);

            if (options.ShowHelp) { PrintHelp(); return 0; }
            if (options.ListKeys) { PrintKeys(); return 0; }
            if (options.ListMics) { PrintMics(); return 0; }

            PrintBanner(options);

            List<TestResult> results;
            EnvironmentReport env;

            using (new InputSender.HighResolutionTimerScope())
            using (InputProbe probe = new InputProbe())
            {
                // Le raccourci global doit être enregistré sur le thread de la boucle de
                // messages : on le configure AVANT Start().
                if (options.Mode == "voice")
                {
                    KeySpec ptt = ScanCodes.Get(options.PushToTalk);
                    if (ptt != null) probe.HotkeyVirtualKey = ptt.VirtualKey;
                }

                probe.Start();
                env = CollectEnvironment(probe, options);
                PrintEnvironment(env);

                if (!probe.HookInstalled && !probe.RawInputRegistered)
                {
                    Console.WriteLine();
                    Console.WriteLine("ERREUR : aucune sonde n'a pu être installée. Résultats non exploitables.");
                    return 2;
                }

                if (options.Mode == "game")
                {
                    results = RunGameMode(probe, options, env);
                    if (results == null) return 3;
                }
                else if (options.Mode == "voice")
                {
                    results = RunVoiceMode(probe, options);
                    if (results == null) return 4;
                }
                else
                {
                    results = RunProbeMode(probe, options);
                }
            }

            PrintResults(results);
            string path = WriteReport(results, env, options);
            Console.WriteLine();
            Console.WriteLine("Rapport écrit : " + path);
            return 0;
        }

        // ============================================================== MODE PROBE

        private static List<TestResult> RunProbeMode(InputProbe probe, Options options)
        {
            Console.WriteLine();
            Console.WriteLine("=== MODE PROBE — vérification automatique (aucun jeu requis) ===");
            Console.WriteLine("Les touches injectées sont " + options.ProbeKey + " / " + options.ProbeKey2 +
                              " : sans effet sur un clavier standard.");
            Console.WriteLine();

            List<TestResult> results = new List<TestResult>();

            KeySpec probeKey = ScanCodes.Get(options.ProbeKey);
            KeySpec probeKey2 = ScanCodes.Get(options.ProbeKey2);
            if (probeKey == null || probeKey2 == null)
            {
                TestResult bad = new TestResult("T0", "Touches d'essai");
                bad.Verdict = "FAIL";
                bad.Detail = "Touche d'essai inconnue.";
                results.Add(bad);
                return results;
            }

            results.Add(TestScanCodeInjection(probe, probeKey));
            results.Add(TestVirtualKeyOnly(probe, probeKey2));
            results.Add(TestExtendedKey(probe));
            results.Add(TestHoldLadder(probe, probeKey));
            results.Add(TestDoubleTap(probe, probeKey));
            results.Add(TestCombo(probe, probeKey, options.Modifier));
            results.Add(TestKeyboardLayout());
            results.Add(TestMouse(options));

            return results;
        }

        /// <summary>T1 — l'injection scancode atteint-elle le Raw Input avec le bon make code ?</summary>
        private static TestResult TestScanCodeInjection(InputProbe probe, KeySpec key)
        {
            TestResult r = new TestResult("T1", "Injection scancode → Raw Input");
            Announce(r);

            List<ObservedEvent> events = Capture(probe, delegate { InputSender.Tap(key, 45); });

            List<ObservedEvent> raw = Filter(events, "rawinput");
            List<ObservedEvent> hook = Filter(events, "hook");

            bool rawOk = CountWithScanCode(raw, key.ScanCode) >= 2;
            bool hookOk = CountWithScanCode(hook, key.ScanCode) >= 2;
            bool injectedFlag = AnyInjected(hook);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Injection : scancode 0x" + key.ScanCode.ToString("X2") + " (" + key.Name + "), maintien 45 ms.");
            sb.AppendLine("Raw Input : " + raw.Count + " évènement(s), make code correct : " + (rawOk ? "OUI" : "NON"));
            sb.AppendLine("Hook LL   : " + hook.Count + " évènement(s), scancode correct : " + (hookOk ? "OUI" : "NON"));
            sb.AppendLine("Drapeau LLKHF_INJECTED présent : " + (injectedFlag ? "OUI" : "NON") +
                          "  (c'est ce drapeau qu'inspectent les anti-triches)");
            sb.Append(DumpEvents(events));

            r.Detail = sb.ToString();
            r.Verdict = (rawOk && hookOk) ? "PASS" : "FAIL";
            Report(r);
            return r;
        }

        /// <summary>T2 — que voit le Raw Input quand on injecte un code de touche virtuelle seul ?</summary>
        private static TestResult TestVirtualKeyOnly(InputProbe probe, KeySpec key)
        {
            TestResult r = new TestResult("T2", "Injection virtual-key seule (sans scancode)");
            Announce(r);

            List<ObservedEvent> events = Capture(probe, delegate { InputSender.TapVirtualKey(key.VirtualKey, 45); });
            List<ObservedEvent> raw = Filter(events, "rawinput");

            ushort observed = raw.Count > 0 ? raw[0].ScanCode : (ushort)0xFFFF;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Injection : vk=0x" + key.VirtualKey.ToString("X2") + " (" + key.Name + "), wScan=0.");
            sb.AppendLine("Raw Input : " + raw.Count + " évènement(s), make code observé : " +
                          (raw.Count > 0 ? "0x" + observed.ToString("X2") : "(aucun)"));
            if (raw.Count > 0 && observed == 0)
            {
                sb.AppendLine("→ Make code NUL : un moteur lisant le scancode physique ne reconnaîtra pas la touche.");
                sb.AppendLine("→ Conclusion : Optimus DOIT injecter en KEYEVENTF_SCANCODE.");
            }
            else if (raw.Count > 0)
            {
                sb.AppendLine("→ Windows a complété le make code depuis la disposition clavier active,");
                sb.AppendLine("  ce qui rend le résultat dépendant du layout (cf. T7). Le scancode reste préférable.");
            }
            sb.Append(DumpEvents(events));

            r.Detail = sb.ToString();
            r.Verdict = "INFO";
            Report(r);
            return r;
        }

        /// <summary>T3 — les touches étendues (préfixe E0) sont-elles correctement signalées ?</summary>
        private static TestResult TestExtendedKey(InputProbe probe)
        {
            TestResult r = new TestResult("T3", "Touche étendue (préfixe E0)");
            Announce(r);

            KeySpec rctrl = ScanCodes.Get("RCTRL");
            List<ObservedEvent> events = Capture(probe, delegate { InputSender.Tap(rctrl, 45); });
            List<ObservedEvent> raw = Filter(events, "rawinput");

            bool e0Ok = false;
            for (int i = 0; i < raw.Count; i++)
            {
                if (raw[i].ScanCode == rctrl.ScanCode && raw[i].Extended) { e0Ok = true; break; }
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Injection : RCTRL (scancode 0x1D + E0), maintien 45 ms.");
            sb.AppendLine("Drapeau E0 vu par Raw Input : " + (e0Ok ? "OUI" : "NON"));
            sb.Append(DumpEvents(events));

            r.Detail = sb.ToString();
            r.Verdict = e0Ok ? "PASS" : "FAIL";
            Report(r);
            return r;
        }

        /// <summary>T4 — quelle durée de maintien est réellement produite ? (choix du hold par défaut)</summary>
        private static TestResult TestHoldLadder(InputProbe probe, KeySpec key)
        {
            TestResult r = new TestResult("T4", "Précision des durées de maintien");
            Announce(r);

            int[] durations = { 8, 16, 32, 48, 80, 120 };
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("| demandé (ms) | mesuré (ms) | écart |");
            sb.AppendLine("|---|---|---|");

            bool allMeasured = true;

            for (int i = 0; i < durations.Length; i++)
            {
                int d = durations[i];
                List<ObservedEvent> events = Capture(probe, delegate { InputSender.Tap(key, d); });
                List<ObservedEvent> hook = Filter(events, "hook");

                double measured = MeasureDownUpMs(hook, key.ScanCode);
                if (measured < 0)
                {
                    allMeasured = false;
                    sb.AppendLine("| " + d + " | non mesuré | — |");
                }
                else
                {
                    sb.AppendLine("| " + d + " | " + measured.ToString("F1") + " | " +
                                  (measured - d).ToString("+0.0;-0.0") + " |");
                }
            }

            sb.AppendLine();
            sb.AppendLine("Lecture : c'est la durée réellement produite par le couple SendInput + attente.");
            sb.AppendLine("Elle détermine le `hold_ms` par défaut du moteur (cf. docs/07 §7.6).");

            r.Detail = sb.ToString();
            r.Verdict = allMeasured ? "PASS" : "WARN";
            Report(r);
            return r;
        }

        /// <summary>T5 — le double appui produit-il bien 4 évènements avec l'écart demandé ?</summary>
        private static TestResult TestDoubleTap(InputProbe probe, KeySpec key)
        {
            TestResult r = new TestResult("T5", "Double appui");
            Announce(r);

            List<ObservedEvent> events = Capture(probe, delegate { InputSender.DoubleTap(key, 45, 60); }, 250);
            List<ObservedEvent> hook = Filter(events, "hook");

            int count = CountWithScanCode(hook, key.ScanCode);
            double gap = MeasureGapBetweenTapsMs(hook, key.ScanCode);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Injection : deux appuis de 45 ms séparés de 60 ms.");
            sb.AppendLine("Évènements observés : " + count + " (attendu 4)");
            sb.AppendLine("Écart mesuré entre relâchement et appui suivant : " +
                          (gap < 0 ? "non mesuré" : gap.ToString("F1") + " ms"));
            sb.Append(DumpEvents(events));

            r.Detail = sb.ToString();
            r.Verdict = count >= 4 ? "PASS" : "FAIL";
            Report(r);
            return r;
        }

        /// <summary>T6 — l'ordre des évènements d'une combinaison est-il correct ?</summary>
        private static TestResult TestCombo(InputProbe probe, KeySpec key, string modifierName)
        {
            TestResult r = new TestResult("T6", "Combinaison modificateur + touche");
            Announce(r);

            KeySpec modifier = ScanCodes.Get(modifierName);
            if (modifier == null)
            {
                r.Verdict = "FAIL";
                r.Detail = "Modificateur inconnu : " + modifierName;
                Report(r);
                return r;
            }

            List<KeySpec> mods = new List<KeySpec>();
            mods.Add(modifier);

            List<ObservedEvent> events = Capture(probe, delegate { InputSender.Combo(mods, key, 45); }, 250);
            List<ObservedEvent> hook = Filter(events, "hook");

            // Ordre attendu : MOD down, KEY down, KEY up, MOD up
            bool orderOk = hook.Count >= 4
                && hook[0].ScanCode == modifier.ScanCode && !hook[0].KeyUp
                && hook[1].ScanCode == key.ScanCode && !hook[1].KeyUp
                && hook[2].ScanCode == key.ScanCode && hook[2].KeyUp
                && hook[3].ScanCode == modifier.ScanCode && hook[3].KeyUp;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Injection : " + modifier.Name + " + " + key.Name);
            sb.AppendLine("Ordre attendu (MOD↓ KEY↓ KEY↑ MOD↑) respecté : " + (orderOk ? "OUI" : "NON"));
            sb.Append(DumpEvents(events));

            r.Detail = sb.ToString();
            r.Verdict = orderOk ? "PASS" : "FAIL";
            Report(r);
            return r;
        }

        /// <summary>
        /// T7 — la disposition clavier Windows fausse-t-elle la correspondance touche → scancode ?
        /// Point critique pour un utilisateur francophone (AZERTY).
        /// </summary>
        private static TestResult TestKeyboardLayout()
        {
            TestResult r = new TestResult("T7", "Disposition clavier et scancodes");
            Announce(r);

            string[] samples = { "A", "Q", "W", "Z", "M", "L" };
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("| touche SC | scancode position US | scancode via MapVirtualKey | identique |");
            sb.AppendLine("|---|---|---|---|");

            int mismatches = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                KeySpec spec = ScanCodes.Get(samples[i]);
                bool ext;
                ushort layoutScan = ScanCodes.ScanCodeFromLayout(spec.VirtualKey, out ext);
                bool same = layoutScan == spec.ScanCode;
                if (!same) mismatches++;

                sb.AppendLine("| " + spec.Name +
                              " | 0x" + spec.ScanCode.ToString("X2") +
                              " | 0x" + layoutScan.ToString("X2") +
                              " | " + (same ? "oui" : "**NON**") + " |");
            }

            sb.AppendLine();
            if (mismatches > 0)
            {
                sb.AppendLine("→ " + mismatches + " divergence(s) : la disposition Windows active n'est pas QWERTY US.");
                sb.AppendLine("→ CONCLUSION : Optimus doit utiliser une table de scancodes FIXE en positions US");
                sb.AppendLine("  (comme le fait Star Citizen avec `kb1_x`), et surtout PAS MapVirtualKey.");
            }
            else
            {
                sb.AppendLine("→ Aucune divergence sur cette machine (disposition compatible QWERTY US).");
                sb.AppendLine("→ La table fixe reste néanmoins obligatoire : un autre utilisateur en AZERTY divergerait.");
            }

            r.Detail = sb.ToString();
            r.Verdict = "INFO";
            Report(r);
            return r;
        }

        /// <summary>T8 — injection souris (pas de sonde : on vérifie l'acceptation par SendInput).</summary>
        private static TestResult TestMouse(Options options)
        {
            TestResult r = new TestResult("T8", "Injection souris");
            Announce(r);

            bool x2 = InputSender.SendMouseButton(InputSender.MouseButton.X2, false);
            InputSender.PreciseSleep(45);
            x2 = InputSender.SendMouseButton(InputSender.MouseButton.X2, true) && x2;

            InputSender.PreciseSleep(120);
            bool wheel = InputSender.SendWheel(1);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Bouton X2 (latéral, généralement non assigné) accepté par SendInput : " + (x2 ? "OUI" : "NON"));
            sb.AppendLine("Molette (+1 cran) acceptée par SendInput : " + (wheel ? "OUI" : "NON"));
            sb.AppendLine("Remarque : la sonde Raw Input de ce spike n'écoute que le clavier ;");
            sb.AppendLine("la validation souris se fait en mode `game` par observation.");

            r.Detail = sb.ToString();
            r.Verdict = (x2 && wheel) ? "PASS" : "FAIL";
            Report(r);
            return r;
        }

        // =============================================================== MODE GAME

        private static List<TestResult> RunGameMode(InputProbe probe, Options options, EnvironmentReport env)
        {
            Console.WriteLine();
            Console.WriteLine("=== MODE GAME — plan d'observation dans " + options.Target + " ===");

            Process target = TargetLocator.FindProcess(options.Target);
            if (target == null)
            {
                Console.WriteLine();
                Console.WriteLine("Processus « " + options.Target + " » introuvable.");
                Console.WriteLine("Lance le jeu, puis relance ce spike. (--target permet de viser un autre processus)");
                return null;
            }

            bool? targetElevated = TargetLocator.IsProcessElevated(target);
            if (targetElevated == true && !env.CurrentElevated)
            {
                Console.WriteLine();
                Console.WriteLine("AVERTISSEMENT : la cible est élevée et pas ce spike.");
                Console.WriteLine("UIPI bloquera l'injection. Relance en administrateur pour un test valide.");
            }

            List<TestResult> results = new List<TestResult>();
            List<PlannedTest> plan = BuildGamePlan(options);

            Console.WriteLine();
            Console.WriteLine("Plan (" + plan.Count + " tests, ~" + ((plan.Count * options.GapMs) / 1000 + options.Countdown) + " s) :");
            for (int i = 0; i < plan.Count; i++)
            {
                Console.WriteLine("  " + plan[i].Id + "  " + plan[i].Name);
            }

            Console.WriteLine();
            Console.WriteLine("PROTOCOLE :");
            Console.WriteLine("  1. Mets-toi en sécurité dans le jeu (posé, moteurs coupés, pas en combat).");
            Console.WriteLine("  2. Bascule sur la fenêtre du jeu : le compte à rebours démarre tout seul.");
            Console.WriteLine("  3. Observe. Chaque test est annoncé par un bip.");
            Console.WriteLine("  4. Reviens ici à la fin pour répondre aux questions.");
            Console.WriteLine("  ARRÊT D'URGENCE : appuie sur Échap à tout moment.");
            Console.WriteLine();
            Console.Write("Prêt ? Appuie sur Entrée pour armer le test… ");
            Console.ReadLine();

            Console.WriteLine("En attente du passage de « " + options.Target + " » au premier plan…");
            if (!WaitForForeground(target, probe, 120))
            {
                Console.WriteLine("La cible n'est pas passée au premier plan (ou arrêt demandé). Test annulé.");
                return null;
            }

            for (int s = options.Countdown; s > 0; s--)
            {
                Beep(600, 80);
                Thread.Sleep(920);
                if (probe.AbortRequested) { Console.WriteLine("Arrêt demandé."); return null; }
            }

            for (int i = 0; i < plan.Count; i++)
            {
                PlannedTest test = plan[i];
                TestResult r = new TestResult(test.Id, test.Name);
                r.Question = test.Question;
                r.Verdict = "OBSERVE";

                if (probe.AbortRequested)
                {
                    r.Verdict = "WARN";
                    r.Detail = "Test non exécuté : arrêt d'urgence demandé.";
                    results.Add(r);
                    continue;
                }

                if (!TargetLocator.IsForeground(target))
                {
                    r.Verdict = "WARN";
                    r.Detail = "Test non exécuté : la cible n'était pas au premier plan.";
                    results.Add(r);
                    continue;
                }

                // Signature sonore : n bips courts = test n.
                for (int b = 0; b <= i && b < 6; b++) { Beep(1200, 40); Thread.Sleep(60); }

                probe.Clear();
                Stopwatch sw = Stopwatch.StartNew();
                test.Action();
                sw.Stop();

                // Les sondes sont asynchrones : sans ce délai, on compte les évènements avant
                // que la boucle de messages ait fini de les livrer (d'où des comptages partiels
                // et variables d'un test à l'autre).
                Thread.Sleep(150);

                List<ObservedEvent> ours = probe.OurEvents(null);
                int hookCount = Filter(ours, "hook").Count;
                int rawCount = Filter(ours, "rawinput").Count;

                r.Detail = test.Detail + Environment.NewLine +
                           "Injection exécutée en " + sw.Elapsed.TotalMilliseconds.ToString("F1") + " ms. " +
                           "Évènements confirmés : " + hookCount + " par le hook, " + rawCount + " par le Raw Input." +
                           (hookCount == 0 && rawCount == 0
                                ? " (normal pour un test souris : les sondes n'écoutent que le clavier)"
                                : "");

                results.Add(r);
                Thread.Sleep(options.GapMs);
            }

            if (!options.NoQuestions) AskObservations(results);
            return results;
        }

        private sealed class PlannedTest
        {
            public string Id;
            public string Name;
            public string Question;
            public string Detail;
            public Action Action;
        }

        private static List<PlannedTest> BuildGamePlan(Options options)
        {
            List<PlannedTest> plan = new List<PlannedTest>();

            KeySpec key = ScanCodes.Get(options.Key);
            KeySpec holdKey = ScanCodes.Get(options.HoldKey);
            KeySpec modifier = ScanCodes.Get(options.Modifier);
            KeySpec doubleTapKey = string.IsNullOrEmpty(options.DoubleTapKey) ? null : ScanCodes.Get(options.DoubleTapKey);

            if (key != null)
            {
                PlannedTest g1 = new PlannedTest();
                g1.Id = "G1";
                g1.Name = "Appui scancode sur " + key.Name;
                g1.Detail = "Tap scancode 0x" + key.ScanCode.ToString("X2") + ", maintien 45 ms.";
                g1.Question = "Le jeu a-t-il réagi à la touche " + key.Name + " ?";
                g1.Action = delegate { InputSender.Tap(key, 45); };
                plan.Add(g1);

                PlannedTest g2 = new PlannedTest();
                g2.Id = "G2";
                g2.Name = "Appui virtual-key seul sur " + key.Name;
                g2.Detail = "Tap vk=0x" + key.VirtualKey.ToString("X2") + " sans scancode.";
                g2.Question = "Le jeu a-t-il réagi CETTE FOIS (méthode virtual-key) ?";
                g2.Action = delegate { InputSender.TapVirtualKey(key.VirtualKey, 45); };
                plan.Add(g2);

                PlannedTest g3 = new PlannedTest();
                g3.Id = "G3";
                g3.Name = "Appui très court (16 ms) sur " + key.Name;
                g3.Detail = "Tap scancode, maintien 16 ms — cherche la limite basse acceptée par le jeu.";
                g3.Question = "Le jeu a-t-il réagi à l'appui de 16 ms ?";
                g3.Action = delegate { InputSender.Tap(key, 16); };
                plan.Add(g3);
            }

            if (holdKey != null)
            {
                PlannedTest g4 = new PlannedTest();
                g4.Id = "G4";
                g4.Name = "Maintien 800 ms sur " + holdKey.Name;
                g4.Detail = "Down, 800 ms, up.";
                g4.Question = "Le maintien a-t-il été pris en compte pendant toute sa durée ?";
                g4.Action = delegate { InputSender.Tap(holdKey, 800); };
                plan.Add(g4);
            }

            if (modifier != null && key != null)
            {
                PlannedTest g5 = new PlannedTest();
                g5.Id = "G5";
                g5.Name = "Combinaison " + modifier.Name + " + " + key.Name;
                g5.Detail = "Modificateur maintenu, touche tapée, modificateur relâché.";
                g5.Question = "La combinaison a-t-elle déclenché l'action attendue ?";
                g5.Action = delegate
                {
                    List<KeySpec> mods = new List<KeySpec>();
                    mods.Add(modifier);
                    InputSender.Combo(mods, key, 45);
                };
                plan.Add(g5);
            }

            if (doubleTapKey != null)
            {
                PlannedTest g6 = new PlannedTest();
                g6.Id = "G6";
                g6.Name = "Double appui sur " + doubleTapKey.Name;
                g6.Detail = "Deux taps de 45 ms séparés de 80 ms.";
                g6.Question = "Le jeu a-t-il reconnu un DOUBLE appui (et non deux appuis simples) ?";
                g6.Action = delegate { InputSender.DoubleTap(doubleTapKey, 45, 80); };
                plan.Add(g6);
            }

            PlannedTest g7 = new PlannedTest();
            g7.Id = "G7";
            g7.Name = "Bouton souris latéral X2";
            g7.Detail = "Down/up sur le bouton X2.";
            g7.Question = "Le jeu a-t-il réagi au bouton latéral (s'il est assigné) ?";
            g7.Action = delegate { InputSender.TapMouse(InputSender.MouseButton.X2, 45); };
            plan.Add(g7);

            if (options.IncludeMouseRight)
            {
                PlannedTest g8 = new PlannedTest();
                g8.Id = "G8";
                g8.Name = "Clic droit (ATTENTION : peut déclencher une action de combat)";
                g8.Detail = "Down/up sur le bouton droit.";
                g8.Question = "Le jeu a-t-il réagi au clic droit ?";
                g8.Action = delegate { InputSender.TapMouse(InputSender.MouseButton.Right, 45); };
                plan.Add(g8);
            }

            return plan;
        }

        private static bool WaitForForeground(Process target, InputProbe probe, int timeoutSeconds)
        {
            for (int i = 0; i < timeoutSeconds * 4; i++)
            {
                if (probe.AbortRequested) return false;
                if (TargetLocator.IsForeground(target)) return true;
                Thread.Sleep(250);
            }
            return false;
        }

        private static void AskObservations(List<TestResult> results)
        {
            Console.WriteLine();
            Console.WriteLine("=== OBSERVATIONS ===");
            Console.WriteLine("Réponds par o (oui), n (non), ? (incertain) ou un commentaire libre.");
            Console.WriteLine();

            for (int i = 0; i < results.Count; i++)
            {
                TestResult r = results[i];
                if (r.Question == null) continue;

                Console.WriteLine("[" + r.Id + "] " + r.Name);
                Console.Write("      " + r.Question + " ");
                string answer = Console.ReadLine();
                if (answer == null) answer = "";
                answer = answer.Trim();

                r.Observation = answer;
                string lower = answer.ToLowerInvariant();
                if (lower == "o" || lower == "oui" || lower == "y") r.Verdict = "PASS";
                else if (lower == "n" || lower == "non") r.Verdict = "FAIL";
                else if (lower == "?" || answer.Length == 0) r.Verdict = "WARN";
                else r.Verdict = "INFO";
            }
        }

        // =============================================================== MODE VOICE

        /// <summary>
        /// Spike S0-3 : push-to-talk et capture microphone, pendant que le jeu tourne.
        ///
        /// Répond à trois questions :
        ///   1. RegisterHotKey reçoit-il la touche quand Star Citizen a le focus ?
        ///   2. Le hook bas niveau la voit-il (lui seul donne l'appui ET le relâchement,
        ///      indispensable au push-to-talk) ?
        ///   3. Peut-on capturer le micro pendant que le jeu tourne, et en combien de temps
        ///      la capture démarre-t-elle ?
        ///
        /// Les WAV produits alimentent ensuite le spike S0-2 (latence Whisper) : ce sont de
        /// vrais énoncés, avec la vraie voix, le vrai micro et le vrai bruit de fond - bien
        /// plus représentatifs que des échantillons de synthèse.
        /// </summary>
        private static List<TestResult> RunVoiceMode(InputProbe probe, Options options)
        {
            List<TestResult> results = new List<TestResult>();

            KeySpec ptt = ScanCodes.Get(options.PushToTalk);
            if (ptt == null)
            {
                Console.WriteLine("Touche push-to-talk inconnue : " + options.PushToTalk);
                return null;
            }

            Console.WriteLine();
            Console.WriteLine("=== MODE VOICE - push-to-talk et capture micro ===");

            // --- périphériques
            List<string> devices = MicRecorder.ListDevices();
            Console.WriteLine();
            Console.WriteLine("Micros détectés :");
            if (devices.Count == 0) Console.WriteLine("  (aucun)");
            for (int i = 0; i < devices.Count; i++)
            {
                string marker = (options.MicDevice == i || (options.MicDevice < 0 && i == 0)) ? " <-" : "";
                Console.WriteLine("  [" + i + "] " + devices[i] + marker);
            }
            if (devices.Count == 0)
            {
                TestResult noMic = new TestResult("V0", "Périphériques d'entrée");
                noMic.Verdict = "FAIL";
                noMic.Detail = "Aucun périphérique de capture détecté.";
                results.Add(noMic);
                return results;
            }

            // --- raccourci global
            TestResult hotkeyResult = new TestResult("V1", "RegisterHotKey (raccourci global)");
            if (probe.HotkeyRegistered)
            {
                hotkeyResult.Verdict = "PASS";
                hotkeyResult.Detail = "Raccourci " + ptt.Name + " (vk=0x" + ptt.VirtualKey.ToString("X2") +
                                      ") enregistré avec succès.";
            }
            else
            {
                hotkeyResult.Verdict = "FAIL";
                hotkeyResult.Detail = "Échec de l'enregistrement : " + (probe.HotkeyError ?? "raison inconnue");
            }
            Announce(hotkeyResult); Report(hotkeyResult);
            results.Add(hotkeyResult);

            // --- cible éventuelle
            Process target = TargetLocator.FindProcess(options.Target);
            if (target != null)
            {
                Console.WriteLine();
                Console.WriteLine(options.Target + " détecté : place-toi dans le jeu pendant le test.");
                Console.WriteLine("Le test est plus concluant si le jeu est au premier plan quand tu parles.");
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine(options.Target + " n'est pas lancé : le test reste valable, mais ne dira");
                Console.WriteLine("rien du comportement en plein écran. Relance-le avec le jeu ouvert.");
            }

            string audioDir = options.AudioDir;
            if (string.IsNullOrEmpty(audioDir))
            {
                audioDir = Path.Combine(Environment.CurrentDirectory, Path.Combine("docs", Path.Combine("spikes", "audio")));
            }
            if (!Directory.Exists(audioDir)) Directory.CreateDirectory(audioDir);

            string[] suggestions = new string[] {
                "Optimus, ouvre les portes",
                "Optimus, prépare le saut quantique",
                "Optimus, mets les boucliers sur l'avant",
                "Optimus, active le scan",
                "Optimus, rapport système",
                "Optimus, passe en mode combat",
                "Optimus, allume les moteurs",
                "Optimus, tu penses qu'on devrait se poser ?"
            };

            Console.WriteLine();
            Console.WriteLine("PROTOCOLE : maintiens " + ptt.Name + ", prononce la phrase, relâche.");
            Console.WriteLine("Échap à tout moment pour arrêter.");
            Console.WriteLine();

            List<double> startLatencies = new List<double>();
            List<string> files = new List<string>();
            int inGameCount = 0;
            int hotkeyBefore = probe.HotkeyHitCount;

            for (int i = 0; i < options.Utterances; i++)
            {
                string phrase = suggestions[i % suggestions.Length];
                Console.WriteLine("[" + (i + 1) + "/" + options.Utterances + "] Dis : \"" + phrase + "\"");
                Console.Write("      en attente de " + ptt.Name + "… ");

                long downTicks = WaitForRealKey(probe, ptt.ScanCode, false, 120000);
                if (downTicks == 0)
                {
                    Console.WriteLine("délai dépassé ou arrêt demandé.");
                    break;
                }

                bool foreground = target != null && TargetLocator.IsForeground(target);
                if (foreground) inGameCount++;

                MicRecorder recorder = new MicRecorder();
                if (!recorder.Start(options.MicDevice))
                {
                    Console.WriteLine("ÉCHEC : " + recorder.LastError);
                    TestResult micFail = new TestResult("V2", "Capture microphone");
                    micFail.Verdict = "FAIL";
                    micFail.Detail = recorder.LastError;
                    results.Add(micFail);
                    return results;
                }

                Console.Write("enregistrement… ");
                long upTicks = WaitForRealKey(probe, ptt.ScanCode, true, 60000);
                byte[] pcm = recorder.Stop();

                double heldMs = upTicks > 0 ? TicksToMs(upTicks - downTicks) : 0;
                double startLatency = recorder.FirstSampleTicks > 0
                    ? TicksToMs(recorder.FirstSampleTicks - downTicks) : -1;
                if (startLatency >= 0) startLatencies.Add(startLatency);

                string file = Path.Combine(audioDir,
                    string.Format("utt{0:D2}-{1}.wav", i + 1, Environment.MachineName));
                MicRecorder.WriteWav(file, pcm);
                files.Add(file);

                // Sans la phrase attendue, la « précision » de la transcription ne serait
                // qu'une impression. On écrit la vérité terrain à côté de l'audio pour que
                // le spike S0-2 puisse comparer automatiquement.
                AppendExpected(audioDir, Path.GetFileName(file), phrase);

                Console.WriteLine(string.Format(
                    "{0:F0} ms audio · maintien {1:F0} ms · démarrage capture {2} · crête {3:P0} · {4}",
                    MicRecorder.DurationMs(pcm), heldMs,
                    startLatency >= 0 ? string.Format("{0:F0} ms", startLatency) : "n/a",
                    recorder.PeakLevel,
                    foreground ? "JEU au premier plan" : "hors jeu"));

                recorder.Dispose();

                if (probe.AbortRequested) { Console.WriteLine("Arrêt demandé."); break; }
                Thread.Sleep(400);
            }

            int hotkeyHits = probe.HotkeyHitCount - hotkeyBefore;

            // --- V2 : capture
            TestResult capture = new TestResult("V2", "Capture microphone pendant le jeu");
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Périphérique : " + devices[options.MicDevice < 0 ? 0 : options.MicDevice]);
            sb.AppendLine("Format : PCM " + MicRecorder.SampleRate + " Hz mono " + MicRecorder.BitsPerSample + " bits");
            sb.AppendLine("Énoncés enregistrés : " + files.Count + " (dont " + inGameCount + " avec le jeu au premier plan)");
            if (startLatencies.Count > 0)
            {
                startLatencies.Sort();
                sb.AppendLine("Latence de démarrage de capture (appui -> premier échantillon) :");
                sb.AppendLine("  min " + startLatencies[0].ToString("F0") + " ms · " +
                              "médiane " + startLatencies[startLatencies.Count / 2].ToString("F0") + " ms · " +
                              "max " + startLatencies[startLatencies.Count - 1].ToString("F0") + " ms");
                sb.AppendLine("  Cette latence est celle d'une ouverture de périphérique À LA DEMANDE.");
                sb.AppendLine("  Elle justifie le tampon de pré-roll de docs/09 : en production le flux");
                sb.AppendLine("  reste ouvert en permanence et le début de phrase n'est jamais perdu.");
            }
            sb.AppendLine();
            foreach (string f in files) sb.AppendLine("  " + f);
            capture.Detail = sb.ToString();
            capture.Verdict = files.Count > 0 ? "PASS" : "FAIL";
            Announce(capture); Report(capture);
            results.Add(capture);

            // --- V3 : quel mécanisme reçoit la touche
            TestResult mechanism = new TestResult("V3", "Mécanisme de détection du push-to-talk");
            StringBuilder mb = new StringBuilder();
            mb.AppendLine("Appuis vus par le hook bas niveau : " + files.Count + " (appui ET relâchement)");
            mb.AppendLine("Messages WM_HOTKEY reçus         : " + hotkeyHits +
                          (probe.HotkeyRegistered ? " (raccourci pourtant enregistré avec succès)" : ""));
            mb.AppendLine();
            mb.AppendLine("RegisterHotKey ne signale de toute façon QUE l'appui : il n'existe aucun");
            mb.AppendLine("message de relâchement. Il conviendrait pour une bascule (couper le micro,");
            mb.AppendLine("kill switch) mais PAS pour un push-to-talk, qui exige de connaître la durée");
            mb.AppendLine("du maintien. Le hook bas niveau est le seul mécanisme qui couvre les deux.");
            if (hotkeyHits == 0 && probe.HotkeyRegistered && files.Count > 0)
            {
                mb.AppendLine();
                mb.AppendLine("À NOTER : aucun WM_HOTKEY n'a été reçu alors que l'enregistrement a réussi et");
                mb.AppendLine("que le hook a bien vu chaque appui. La cause reste à établir (le message est");
                mb.AppendLine("posté dans la file du thread appelant ; hook et pompe partagent ce thread).");
                mb.AppendLine("Sans conséquence sur la décision - le hook s'impose de toute façon - mais à");
                mb.AppendLine("élucider avant d'utiliser RegisterHotKey pour le kill switch.");
            }
            if (inGameCount > 0)
            {
                mb.AppendLine();
                mb.AppendLine("Confirmé avec Star Citizen au premier plan sur " + inGameCount + " énoncé(s).");
            }
            mechanism.Detail = mb.ToString();
            mechanism.Verdict = files.Count > 0 ? "PASS" : "WARN";
            Announce(mechanism); Report(mechanism);
            results.Add(mechanism);

            return results;
        }

        /// <summary>
        /// Consigne la phrase attendue en regard du fichier audio, dans un TSV cumulatif.
        /// Format volontairement trivial : une ligne « fichier[TAB]phrase », lisible à l'œil
        /// et par n'importe quel script.
        /// </summary>
        private static void AppendExpected(string audioDir, string fileName, string phrase)
        {
            try
            {
                string manifest = Path.Combine(audioDir, "expected.tsv");
                string line = fileName + "\t" + phrase + Environment.NewLine;
                File.AppendAllText(manifest, line, new UTF8Encoding(false));
            }
            catch (Exception)
            {
                // Le manifeste est un confort de mesure : son échec ne doit pas perdre l'audio.
            }
        }

        /// <summary>
        /// Attend un appui ou un relâchement RÉEL (non injecté) de la touche donnée.
        /// Retourne l'horodatage haute résolution, ou 0 en cas de délai dépassé.
        /// </summary>
        private static long WaitForRealKey(InputProbe probe, ushort scanCode, bool keyUp, int timeoutMs)
        {
            long start = Stopwatch.GetTimestamp();
            probe.Clear();

            while (TicksToMs(Stopwatch.GetTimestamp() - start) < timeoutMs)
            {
                if (probe.AbortRequested) return 0;

                List<ObservedEvent> events = probe.Snapshot();
                for (int i = 0; i < events.Count; i++)
                {
                    ObservedEvent e = events[i];
                    if (e.Source != "hook") continue;
                    if (e.Injected) continue;
                    if (e.ScanCode != scanCode) continue;
                    if (e.KeyUp != keyUp) continue;
                    return e.Ticks;
                }

                Thread.Sleep(5);
            }

            return 0;
        }

        private static double TicksToMs(long ticks)
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }

        private static void PrintMics()
        {
            List<string> devices = MicRecorder.ListDevices();
            Console.WriteLine("Périphériques de capture :");
            if (devices.Count == 0) Console.WriteLine("  (aucun)");
            for (int i = 0; i < devices.Count; i++) Console.WriteLine("  [" + i + "] " + devices[i]);
        }

        // ================================================================ Outils

        private static List<ObservedEvent> Capture(InputProbe probe, Action action)
        {
            return Capture(probe, action, 150);
        }

        private static List<ObservedEvent> Capture(InputProbe probe, Action action, int settleMs)
        {
            probe.Clear();
            action();
            Thread.Sleep(settleMs);
            return probe.OurEvents(null);
        }

        private static List<ObservedEvent> Filter(List<ObservedEvent> events, string source)
        {
            List<ObservedEvent> result = new List<ObservedEvent>();
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i].Source == source) result.Add(events[i]);
            }
            return result;
        }

        private static int CountWithScanCode(List<ObservedEvent> events, ushort scanCode)
        {
            int n = 0;
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i].ScanCode == scanCode) n++;
            }
            return n;
        }

        private static bool AnyInjected(List<ObservedEvent> events)
        {
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i].Injected) return true;
            }
            return false;
        }

        /// <summary>Durée mesurée entre le premier down et le premier up d'un scancode donné.</summary>
        private static double MeasureDownUpMs(List<ObservedEvent> events, ushort scanCode)
        {
            long down = -1;
            for (int i = 0; i < events.Count; i++)
            {
                ObservedEvent e = events[i];
                if (e.ScanCode != scanCode) continue;
                if (!e.KeyUp && down < 0) down = e.Ticks;
                else if (e.KeyUp && down >= 0)
                {
                    return (e.Ticks - down) * 1000.0 / Stopwatch.Frequency;
                }
            }
            return -1;
        }

        /// <summary>Écart entre le relâchement du 1er appui et l'appui du 2e.</summary>
        private static double MeasureGapBetweenTapsMs(List<ObservedEvent> events, ushort scanCode)
        {
            long firstUp = -1;
            for (int i = 0; i < events.Count; i++)
            {
                ObservedEvent e = events[i];
                if (e.ScanCode != scanCode) continue;
                if (e.KeyUp && firstUp < 0) firstUp = e.Ticks;
                else if (!e.KeyUp && firstUp >= 0)
                {
                    return (e.Ticks - firstUp) * 1000.0 / Stopwatch.Frequency;
                }
            }
            return -1;
        }

        private static string DumpEvents(List<ObservedEvent> events)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("```");
            if (events.Count == 0) sb.AppendLine("(aucun évènement observé)");
            for (int i = 0; i < events.Count && i < 24; i++)
            {
                sb.AppendLine(events[i].ToString());
            }
            sb.AppendLine("```");
            return sb.ToString();
        }

        private static void Announce(TestResult r)
        {
            Console.WriteLine("[" + r.Id + "] " + r.Name + " …");
        }

        private static void Report(TestResult r)
        {
            Console.WriteLine("      → " + r.Verdict);
        }

        private static void Beep(int frequency, int duration)
        {
            try { Console.Beep(frequency, duration); }
            catch (Exception) { }
        }

        // =========================================================== Environnement

        public sealed class EnvironmentReport
        {
            public string OsVersion;
            public string Runtime;
            public bool Is64Bit;
            public bool CurrentElevated;
            public bool HookInstalled;
            public bool RawInputRegistered;
            public string HookError;
            public string RawInputError;
            public string StarCitizenStatus;
            public string Foreground;
            public string MachineName;
            public DateTime StartedUtc;
        }

        private static EnvironmentReport CollectEnvironment(InputProbe probe, Options options)
        {
            EnvironmentReport env = new EnvironmentReport();
            env.StartedUtc = DateTime.UtcNow;
            env.OsVersion = Environment.OSVersion.ToString();
            env.Runtime = Environment.Version.ToString();
            env.Is64Bit = IntPtr.Size == 8;
            env.MachineName = Environment.MachineName;
            env.CurrentElevated = TargetLocator.IsCurrentProcessElevated();
            env.HookInstalled = probe.HookInstalled;
            env.RawInputRegistered = probe.RawInputRegistered;
            env.HookError = probe.HookError;
            env.RawInputError = probe.RawInputError;

            Process sc = TargetLocator.FindStarCitizen();
            if (sc == null)
            {
                env.StarCitizenStatus = "non détecté";
            }
            else
            {
                bool? elevated = TargetLocator.IsProcessElevated(sc);
                env.StarCitizenStatus = sc.ProcessName + " (pid " + sc.Id + "), élévation : " +
                    (elevated == null ? "inconnue (probablement élevé)" : (elevated.Value ? "OUI" : "non"));
            }

            env.Foreground = TargetLocator.GetForeground().ToString();
            return env;
        }

        private static void PrintEnvironment(EnvironmentReport env)
        {
            Console.WriteLine("Machine        : " + env.MachineName + " · " + env.OsVersion + " · " +
                              (env.Is64Bit ? "x64" : "x86"));
            Console.WriteLine("Runtime        : " + env.Runtime);
            Console.WriteLine("Élévation      : " + (env.CurrentElevated ? "administrateur" : "utilisateur standard"));
            Console.WriteLine("Sonde hook     : " + (env.HookInstalled ? "active" : "INDISPONIBLE — " + env.HookError));
            Console.WriteLine("Sonde rawinput : " + (env.RawInputRegistered ? "active" : "INDISPONIBLE — " + env.RawInputError));
            Console.WriteLine("Star Citizen   : " + env.StarCitizenStatus);
            Console.WriteLine("Premier plan   : " + env.Foreground);
        }

        // ================================================================= Sortie

        private static void PrintResults(List<TestResult> results)
        {
            Console.WriteLine();
            Console.WriteLine("=== RÉSULTATS ===");
            for (int i = 0; i < results.Count; i++)
            {
                Console.WriteLine(string.Format("{0,-4} {1,-8} {2}",
                    results[i].Id, results[i].Verdict, results[i].Name));
            }
        }

        private static string WriteReport(List<TestResult> results, EnvironmentReport env, Options options)
        {
            string path = options.ReportPath;
            if (string.IsNullOrEmpty(path))
            {
                string dir = Path.Combine(Environment.CurrentDirectory, Path.Combine("docs", "spikes"));
                string name = "S0-1-" + options.Mode + "-" +
                              DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".md";
                path = Path.Combine(dir, name);
            }

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# Spike S0-1 — injection d'entrées (" + options.Mode + ")");
            sb.AppendLine();
            sb.AppendLine("*Généré par Optimus.Spike.InputTest " + Version + " le " +
                          DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "*");
            sb.AppendLine();
            sb.AppendLine("## Environnement");
            sb.AppendLine();
            sb.AppendLine("| | |");
            sb.AppendLine("|---|---|");
            sb.AppendLine("| Machine | " + env.MachineName + " |");
            sb.AppendLine("| Système | " + env.OsVersion + " (" + (env.Is64Bit ? "x64" : "x86") + ") |");
            sb.AppendLine("| Runtime | " + env.Runtime + " |");
            sb.AppendLine("| Élévation du spike | " + (env.CurrentElevated ? "administrateur" : "utilisateur standard") + " |");
            sb.AppendLine("| Sonde hook (WH_KEYBOARD_LL) | " + (env.HookInstalled ? "active" : "indisponible : " + env.HookError) + " |");
            sb.AppendLine("| Sonde Raw Input (WM_INPUT) | " + (env.RawInputRegistered ? "active" : "indisponible : " + env.RawInputError) + " |");
            sb.AppendLine("| Star Citizen | " + env.StarCitizenStatus + " |");
            sb.AppendLine("| Fenêtre au premier plan au démarrage | " + env.Foreground + " |");
            sb.AppendLine();
            sb.AppendLine("## Synthèse");
            sb.AppendLine();
            sb.AppendLine("| Test | Verdict | Intitulé |");
            sb.AppendLine("|---|---|---|");
            for (int i = 0; i < results.Count; i++)
            {
                sb.AppendLine("| " + results[i].Id + " | **" + results[i].Verdict + "** | " + results[i].Name + " |");
            }
            sb.AppendLine();
            sb.AppendLine("## Détail");
            sb.AppendLine();
            for (int i = 0; i < results.Count; i++)
            {
                TestResult r = results[i];
                sb.AppendLine("### " + r.Id + " — " + r.Name);
                sb.AppendLine();
                sb.AppendLine("**Verdict : " + r.Verdict + "**");
                sb.AppendLine();
                if (!string.IsNullOrEmpty(r.Question))
                {
                    sb.AppendLine("Question : " + r.Question);
                    sb.AppendLine();
                    sb.AppendLine("Observation : " + (string.IsNullOrEmpty(r.Observation) ? "(non renseignée)" : r.Observation));
                    sb.AppendLine();
                }
                sb.AppendLine(r.Detail);
                sb.AppendLine();
            }

            sb.AppendLine("## Conclusion à tirer");
            sb.AppendLine();
            if (options.Mode == "voice")
            {
                sb.AppendLine("- **V2 en PASS** → le micro est capturable pendant que le jeu tourne.");
                sb.AppendLine("- **Latence de démarrage de capture** → dimensionne le tampon de pré-roll ; en production");
                sb.AppendLine("  le flux reste ouvert en permanence (D24).");
                sb.AppendLine("- **WM_HOTKEY à 0 alors que le hook voit tout** → `RegisterHotKey` ne peut pas servir de");
                sb.AppendLine("  push-to-talk. Le hook bas niveau est le seul mécanisme retenu.");
                sb.AppendLine("- Les WAV produits alimentent le spike S0-2 (`bench-stt.ps1`), avec `expected.tsv`");
                sb.AppendLine("  comme vérité terrain pour mesurer le taux d'erreur de mots.");
            }
            else
            {
                sb.AppendLine("- **T1/G1 en PASS** → l'injection scancode est la bonne approche : le plan A d'`docs/05` (D10) est validé.");
                sb.AppendLine("- **G1 en FAIL** → risque R1 confirmé : passer au plan B (pilote Interception) avant de continuer.");
                sb.AppendLine("- **G2 en FAIL alors que G1 est PASS** → confirme que `KEYEVENTF_SCANCODE` est obligatoire.");
                sb.AppendLine("- **T7 avec divergences** → interdiction formelle d'utiliser MapVirtualKey dans le moteur.");
                sb.AppendLine("- **T4** → fixe la valeur par défaut de `hold_ms` dans le `SequenceRunner`.");
            }
            sb.AppendLine();

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            return path;
        }

        private static void PrintBanner(Options options)
        {
            Console.WriteLine("+----------------------------------------------------------+");
            Console.WriteLine("|  OPTIMUS — SPIKE S0-1 : injection clavier / souris        |");
            Console.WriteLine("|  Star Citizen accepte-t-il SendInput en scancode ?        |");
            Console.WriteLine("+----------------------------------------------------------+");
            Console.WriteLine("mode = " + options.Mode + "   cible = " + options.Target);
        }

        private static void PrintHelp()
        {
            Console.WriteLine("Optimus.Spike.InputTest " + Version);
            Console.WriteLine();
            Console.WriteLine("  --mode probe|game|voice  probe = vérification automatique (défaut)");
            Console.WriteLine("                           game  = plan d'observation dans le jeu (S0-1)");
            Console.WriteLine("                           voice = push-to-talk + capture micro (S0-3)");
            Console.WriteLine("  --ptt <TOUCHE>           touche push-to-talk (défaut : INSERT, libre dans SC)");
            Console.WriteLine("  --utterances <n>         nombre d'énoncés à enregistrer (défaut : 5)");
            Console.WriteLine("  --mic <index>            périphérique de capture (défaut : celui du système)");
            Console.WriteLine("  --audio-dir <chemin>     destination des WAV (défaut : docs\\spikes\\audio)");
            Console.WriteLine("  --list-mics              liste les périphériques de capture");
            Console.WriteLine("  --target <processus>     nom du processus cible (défaut : StarCitizen)");
            Console.WriteLine("  --key <TOUCHE>           touche testée en jeu (défaut : L)");
            Console.WriteLine("  --hold-key <TOUCHE>      touche du test de maintien (défaut : SPACE)");
            Console.WriteLine("  --doubletap-key <TOUCHE> touche du test de double appui (désactivé par défaut)");
            Console.WriteLine("  --modifier <TOUCHE>      modificateur testé (défaut : LALT)");
            Console.WriteLine("  --probe-key <TOUCHE>     touche d'essai en mode probe (défaut : F13, sans effet)");
            Console.WriteLine("  --include-mouse-right    ajoute un test de clic droit (peut tirer en jeu !)");
            Console.WriteLine("  --countdown <s>          compte à rebours avant le plan (défaut : 5)");
            Console.WriteLine("  --gap <ms>               délai entre deux tests (défaut : 2500)");
            Console.WriteLine("  --report <chemin>        chemin du rapport Markdown");
            Console.WriteLine("  --no-questions           n'interroge pas l'utilisateur en fin de plan");
            Console.WriteLine("  --list-keys              liste les noms de touches reconnus");
            Console.WriteLine();
            Console.WriteLine("Arrêt d'urgence : touche Échap.");
        }

        private static void PrintKeys()
        {
            List<string> names = ScanCodes.KnownNames();
            StringBuilder line = new StringBuilder();
            for (int i = 0; i < names.Count; i++)
            {
                line.Append(names[i].PadRight(14));
                if ((i + 1) % 6 == 0) { Console.WriteLine(line.ToString()); line.Length = 0; }
            }
            if (line.Length > 0) Console.WriteLine(line.ToString());
        }
    }
}
