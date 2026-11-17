using Mono.Cecil;
using Mono.Cecil.Cil;
using BepInEx;
using BepInEx.Configuration;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Goose.Monsterpatch.GTSAllInOnePatcher;

namespace Goose.Monsterpatch.OfficialServer
{
    internal static class OfficialServerSaveSelectNativePatch
    {
        public static int Patch(AssemblyDefinition assembly)
        {
            int hooks = 0;
            ModuleDefinition module = assembly.MainModule;
            TypeDefinition menuScript = module.Types.FirstOrDefault(t => t.Name == "MenuScript");
            if (menuScript == null)
            {
                Console.WriteLine("[MMOnsterpatch Official Server] MenuScript not found; native save-select hooks were not injected.");
                return 0;
            }

            hooks += PatchMenuScriptStartSilentLauncherReturn(module, menuScript);
            hooks += PatchRefreshSaveFiles(module, menuScript);
            hooks += PatchHoverSaveFile(module, menuScript);
            hooks += PatchUpdate(module, menuScript);
            hooks += PatchPlay(module, menuScript);
            hooks += PatchDeleteASaveFileForLauncher(module, menuScript);
            hooks += PatchDiscordForLauncher(module, menuScript);
            hooks += PatchSelectSaveFile(module, menuScript);
            hooks += PatchSaveSystemDeleteSaveBlock(module);
            hooks += PatchCancel(module, menuScript);
            hooks += PatchSaveSystemSaveGameBlock(module);
            hooks += PatchGameScriptReturnToTitle(module);
            hooks += PatchGameScriptActuallyReturnToTitle(module);
            hooks += PatchGameScriptReturnToTitleMenuText(module);
            hooks += PatchGameScriptUpdateLauncherInGameUi(module);
            hooks += PatchGameScriptShowDialogueReturnToTitleText(module);
            hooks += PatchDialogueManagerShowDialogueListForOfficialReturnToTitle(module);
            hooks += PatchDialogueManagerShowDialogueBoolForOfficialReturnToTitle(module);
            hooks += PatchMonSummaryRefreshLeftPanelsOnlineHouseIcon(module);
            return hooks;
        }

        private static int PatchMonSummaryRefreshLeftPanelsOnlineHouseIcon(ModuleDefinition module)
        {
            TypeDefinition monSummaryScript = module.Types.FirstOrDefault(t => t.Name == "MonSummaryScript");
            if (monSummaryScript == null)
            {
                Console.WriteLine("[MMOnsterpatch Official Server] MonSummaryScript not found for OnlinehouseIcon1 summary override.");
                return 0;
            }

            MethodDefinition m = monSummaryScript.Methods.FirstOrDefault(x => x.Name == "RefreshLeftPanels" && x.HasBody && !x.IsStatic && x.Parameters.Count == 1);
            if (m == null)
            {
                Console.WriteLine("[MMOnsterpatch Official Server] MonSummaryScript.RefreshLeftPanels(Mon) not found for OnlinehouseIcon1 summary override.");
                return 0;
            }

            MethodReference call = module.ImportReference(typeof(OfficialServerSaveSelectNativeRuntime).GetMethod(nameof(OfficialServerSaveSelectNativeRuntime.ApplyOnlineHouseIcon1ToMonSummary), BindingFlags.Public | BindingFlags.Static));
            ILProcessor il = m.Body.GetILProcessor();
            int count = 0;
            foreach (Instruction ret in m.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToArray())
            {
                il.InsertBefore(ret, il.Create(OpCodes.Ldarg_0));
                il.InsertBefore(ret, il.Create(OpCodes.Call, call));
                count++;
            }
            Console.WriteLine("[MMOnsterpatch Official Server] MonSummaryScript.RefreshLeftPanels OnlinehouseIcon1 summary override hook(s)=" + count);
            return count;
        }

        private static int PatchMenuScriptStartSilentLauncherReturn(ModuleDefinition module, TypeDefinition menuScript)
        {
            MethodDefinition m = menuScript.Methods.FirstOrDefault(x => x.Name == "Start" && x.HasBody && !x.IsStatic && x.Parameters.Count == 0);
            if (m == null)
            {
                Console.WriteLine("[MMOnsterpatch Official Server] MenuScript.Start not found for silent launcher Log Out return hook.");
                return 0;
            }

            MethodReference call = module.ImportReference(typeof(OfficialServerSaveSelectNativeRuntime).GetMethod(nameof(OfficialServerSaveSelectNativeRuntime.TryHandleLauncherSilentMenuStart), BindingFlags.Public | BindingFlags.Static));
            ILProcessor il = m.Body.GetILProcessor();
            Instruction first = m.Body.Instructions.FirstOrDefault();
            if (first == null)
                return 0;

            Instruction continueNative = first;
            il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
            il.InsertBefore(first, il.Create(OpCodes.Call, call));
            il.InsertBefore(first, il.Create(OpCodes.Brfalse, continueNative));
            il.InsertBefore(first, il.Create(OpCodes.Ret));

            Console.WriteLine("[MMOnsterpatch Official Server] MenuScript.Start silent launcher Log Out return hook(s)=1");
            return 1;
        }

        private static int PatchRefreshSaveFiles(ModuleDefinition module, TypeDefinition menuScript)
        {
            MethodDefinition m = menuScript.Methods.FirstOrDefault(x => x.Name == "RefreshSaveFiles" && x.HasBody && !x.IsStatic);
            if (m == null)
            {
                Console.WriteLine("[MMOnsterpatch Official Server] MenuScript.RefreshSaveFiles not found.");
                return 0;
            }
            MethodReference call = module.ImportReference(typeof(OfficialServerSaveSelectNativeRuntime).GetMethod(nameof(OfficialServerSaveSelectNativeRuntime.EnsureForMenuObject), BindingFlags.Public | BindingFlags.Static));
            ILProcessor il = m.Body.GetILProcessor();
            int count = 0;
            foreach (Instruction ret in m.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToArray())
            {
                il.InsertBefore(ret, il.Create(OpCodes.Ldarg_0));
                il.InsertBefore(ret, il.Create(OpCodes.Call, call));
                count++;
            }
            Console.WriteLine("[MMOnsterpatch Official Server] MenuScript.RefreshSaveFiles native save-select hook(s)=" + count);
            return count;
        }

        private static int PatchHoverSaveFile(ModuleDefinition module, TypeDefinition menuScript)
        {
            MethodDefinition m = menuScript.Methods.FirstOrDefault(x => x.Name == "HoverSaveFile" && x.HasBody && !x.IsStatic && x.Parameters.Count == 1);
            if (m == null)
            {
                Console.WriteLine("[MMOnsterpatch Official Server] MenuScript.HoverSaveFile(int) not found.");
                return 0;
            }

            ILProcessor il = m.Body.GetILProcessor();
            Instruction first = m.Body.Instructions.FirstOrDefault();
            int count = 0;

            // Pre-hook: while Official Server online slot mode is active, stop vanilla HoverSaveFile
            // before it shows local/offline save previews. This prevents the local-save flashing that
            // happened when moving between online slots.
            if (first != null)
            {
                MethodReference blockCall = module.ImportReference(typeof(OfficialServerSaveSelectNativeRuntime).GetMethod(nameof(OfficialServerSaveSelectNativeRuntime.TryBlockOnlineHoverSaveFile), BindingFlags.Public | BindingFlags.Static));
                il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
                il.InsertBefore(first, il.Create(OpCodes.Ldarg_1));
                il.InsertBefore(first, il.Create(OpCodes.Call, blockCall));
                il.InsertBefore(first, il.Create(OpCodes.Brfalse, first));
                il.InsertBefore(first, il.Create(OpCodes.Ret));
                count++;
            }

            MethodReference call = module.ImportReference(typeof(OfficialServerSaveSelectNativeRuntime).GetMethod(nameof(OfficialServerSaveSelectNativeRuntime.OnHoverSaveFile), BindingFlags.Public | BindingFlags.Static));
            foreach (Instruction ret in m.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToArray())
            {
                il.InsertBefore(ret, il.Create(OpCodes.Ldarg_0));
                il.InsertBefore(ret, il.Create(OpCodes.Ldarg_1));
                il.InsertBefore(ret, il.Create(OpCodes.Call, call));
                count++;
            }
            Console.WriteLine("[MMOnsterpatch Official Server] MenuScript.HoverSaveFile native save-select hook(s)=" + count + " (includes online pre-block).");
            return count;
        }

        private static int PatchUpdate(ModuleDefinition module, TypeDefinition menuScript)
        {
            MethodDefinition m = menuScript.Methods.FirstOrDefault(x => x.Name == "Update" && x.HasBody && !x.IsStatic && x.Parameters.Count == 0);
            if (m == null)
            {
                Console.WriteLine("[MMOnsterpatch Official Server] MenuScript.Update not found.");
                return 0;
            }
            MethodReference call = module.ImportReference(typeof(OfficialServerSaveSelectNativeRuntime).GetMethod(nameof(OfficialServerSaveSelectNativeRuntime.TickMenuObject), BindingFlags.Public | BindingFlags.Static));
            ILProcessor il = m.Body.GetILProcessor();
            Instruction first = m.Body.Instructions.FirstOrDefault();
            if (first == null)
                return 0;
            il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
            il.InsertBefore(first, il.Create(OpCodes.Call, call));
            Console.WriteLine("[MMOnsterpatch Official Server] MenuScript.Update native save-select tick hook(s)=1");
            return 1;
        }

        private static int PatchPlay(ModuleDefinition module, TypeDefinition menuScript)
        {
            MethodDefinition m = menuScript.Methods.FirstOrDefault(x => x.Name == "Play" && x.HasBody && !x.IsStatic && x.Parameters.Count == 0);
            if (m == null)
            {
                Console.WriteLine("[MMOnsterpatch Official Server] MenuScript.Play not found.");
                return 0;
            }
            MethodReference call = module.ImportReference(typeof(OfficialServerSaveSelectNativeRuntime).GetMethod(nameof(OfficialServerSaveSelectNativeRuntime.TryHandleLauncherLogInFromTitle), BindingFlags.Public | BindingFlags.Static));
            ILProcessor il = m.Body.GetILProcessor();
            Instruction first = m.Body.Instructions.FirstOrDefault();
            if (first == null)
                return 0;
            il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
            il.InsertBefore(first, il.Create(OpCodes.Call, call));
            il.InsertBefore(first, il.Create(OpCodes.Brfalse, first));
            il.InsertBefore(first, il.Create(OpCodes.Ret));
            Console.WriteLine("[MMOnsterpatch Official Server] MenuScript.Play launcher pre-auth hook(s)=1");
            return 1;
        }


        private static int PatchDeleteASaveFileForLauncher(ModuleDefinition module, TypeDefinition menuScript)
        {
            MethodDefinition m = menuScript.Methods.FirstOrDefault(x => x.Name == "DeleteASaveFile" && x.HasBody && !x.IsStatic && x.Parameters.Count == 0);
            if (m == null)
            {
                Console.WriteLine("[MMOnsterpatch Official Server] MenuScript.DeleteASaveFile not found for launcher online delete hook.");
                return 0;
            }
            MethodReference call = module.ImportReference(typeof(OfficialServerSaveSelectNativeRuntime).GetMethod(nameof(OfficialServerSaveSelectNativeRuntime.TryHandleLauncherDeleteASaveFile), BindingFlags.Public | BindingFlags.Static));
            ILProcessor il = m.Body.GetILProcessor();
            Instruction first = m.Body.Instructions.FirstOrDefault();
            if (first == null)
                return 0;
            il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
            il.InsertBefore(first, il.Create(OpCodes.Call, call));
            il.InsertBefore(first, il.Create(OpCodes.Brfalse, first));
            il.InsertBefore(first, il.Create(OpCodes.Ret));
            Console.WriteLine("[MMOnsterpatch Official Server] MenuScript.DeleteASaveFile launcher online-delete hook(s)=1");
            return 1;
        }

        private static int PatchDiscordForLauncher(ModuleDefinition module, TypeDefinition menuScript)
        {
            MethodDefinition m = menuScript.Methods.FirstOrDefault(x => x.Name == "Discord" && x.HasBody && !x.IsStatic && x.Parameters.Count == 0);
            if (m == null)
            {
                Console.WriteLine("[MMOnsterpatch Official Server] MenuScript.Discord not found for launcher GitHub hook.");
                return 0;
            }
            MethodReference call = module.ImportReference(typeof(OfficialServerSaveSelectNativeRuntime).GetMethod(nameof(OfficialServerSaveSelectNativeRuntime.TryOpenLauncherGithub), BindingFlags.Public | BindingFlags.Static));
            ILProcessor il = m.Body.GetILProcessor();
            Instruction first = m.Body.Instructions.FirstOrDefault();
            if (first == null)
                return 0;
            il.InsertBefore(first, il.Create(OpCodes.Call, call));
            il.InsertBefore(first, il.Create(OpCodes.Brfalse, first));
            il.InsertBefore(first, il.Create(OpCodes.Ret));
            Console.WriteLine("[MMOnsterpatch Official Server] MenuScript.Discord launcher GitHub hook(s)=1");
            return 1;
        }

        private static int PatchSelectSaveFile(ModuleDefinition module, TypeDefinition menuScript)
        {
            MethodDefinition m = menuScript.Methods.FirstOrDefault(x => x.Name == "SelectSaveFile" && x.HasBody && !x.IsStatic && x.Parameters.Count == 1);
            if (m == null)
            {
                Console.WriteLine("[MMOnsterpatch Official Server] MenuScript.SelectSaveFile(int) not found for online save-select intercept.");
                return 0;
            }

            MethodReference call = module.ImportReference(typeof(OfficialServerSaveSelectNativeRuntime).GetMethod(nameof(OfficialServerSaveSelectNativeRuntime.TryHandleSelectSaveFile), BindingFlags.Public | BindingFlags.Static));
            ILProcessor il = m.Body.GetILProcessor();
            Instruction first = m.Body.Instructions.FirstOrDefault();
            if (first == null)
                return 0;

            il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
            il.InsertBefore(first, il.Create(OpCodes.Ldarg_1));
            il.InsertBefore(first, il.Create(OpCodes.Call, call));
            il.InsertBefore(first, il.Create(OpCodes.Brfalse, first));
            il.InsertBefore(first, il.Create(OpCodes.Ret));
            Console.WriteLine("[MMOnsterpatch Official Server] MenuScript.SelectSaveFile online save-select intercept hook(s)=1");
            return 1;
        }

        private static int PatchSaveSystemDeleteSaveBlock(ModuleDefinition module)
        {
            TypeDefinition saveSystem = module.Types.FirstOrDefault(t => t.Name == "SaveSystem");
            if (saveSystem == null)
            {
                Console.WriteLine("[MMOnsterpatch Official Server] SaveSystem not found for online delete redirect.");
                return 0;
            }

            MethodDefinition m = saveSystem.Methods.FirstOrDefault(x => x.Name == "DeleteSave" && x.HasBody && x.Parameters.Count == 1);
            if (m == null)
            {
                Console.WriteLine("[MMOnsterpatch Official Server] SaveSystem.DeleteSave(int) not found for online delete redirect.");
                return 0;
            }

            MethodReference call = module.ImportReference(typeof(OfficialServerSaveSelectNativeRuntime).GetMethod(nameof(OfficialServerSaveSelectNativeRuntime.TryHandleOnlineDeleteSaveAndBlockLocal), BindingFlags.Public | BindingFlags.Static));
            ILProcessor il = m.Body.GetILProcessor();
            Instruction first = m.Body.Instructions.FirstOrDefault();
            if (first == null)
                return 0;

            // This keeps the official MenuScript delete flow intact:
            // DeleteASaveFile -> SelectSaveFile -> deleteSaveFile dialogue -> ActuallyDeleteSaveFile.
            // Only the disk-level SaveSystem.DeleteSave call is redirected while Official Server Online Mode is active.
            il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
            il.InsertBefore(first, il.Create(OpCodes.Call, call));
            il.InsertBefore(first, il.Create(OpCodes.Brfalse, first));
            il.InsertBefore(first, il.Create(OpCodes.Ret));

            Console.WriteLine("[MMOnsterpatch Official Server] SaveSystem.DeleteSave online server-delete/local-disk block hook(s)=1");
            return 1;
        }

        private static int PatchCancel(ModuleDefinition module, TypeDefinition menuScript)
        {
            MethodDefinition m = menuScript.Methods.FirstOrDefault(x => x.Name == "Cancel" && x.HasBody && !x.IsStatic && x.Parameters.Count == 0);
            if (m == null)
            {
                Console.WriteLine("[MMOnsterpatch Official Server] MenuScript.Cancel not found for online disconnect hook.");
                return 0;
            }
            MethodReference call = module.ImportReference(typeof(OfficialServerSaveSelectNativeRuntime).GetMethod(nameof(OfficialServerSaveSelectNativeRuntime.OnMenuCancelInvoked), BindingFlags.Public | BindingFlags.Static));
            ILProcessor il = m.Body.GetILProcessor();
            Instruction first = m.Body.Instructions.FirstOrDefault();
            if (first == null)
                return 0;
            il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
            il.InsertBefore(first, il.Create(OpCodes.Call, call));
            Console.WriteLine("[MMOnsterpatch Official Server] MenuScript.Cancel online disconnect hook(s)=1");
            return 1;
        }

        private static int PatchSaveSystemSaveGameBlock(ModuleDefinition module)
        {
            TypeDefinition saveSystem = module.Types.FirstOrDefault(t => t.Name == "SaveSystem");
            if (saveSystem == null)
            {
                Console.WriteLine("[MMOnsterpatch Official Server] SaveSystem not found for online local-save safety block.");
                return 0;
            }
            MethodDefinition m = saveSystem.Methods.FirstOrDefault(x => x.Name == "SaveGame" && x.HasBody && x.Parameters.Count == 2);
            if (m == null)
            {
                Console.WriteLine("[MMOnsterpatch Official Server] SaveSystem.SaveGame(SaveData,int) not found for online local-save safety block.");
                return 0;
            }
            MethodReference call = module.ImportReference(typeof(OfficialServerSaveSelectNativeRuntime).GetMethod(nameof(OfficialServerSaveSelectNativeRuntime.ShouldHandleOnlineSaveAndBlockLocalWrite), BindingFlags.Public | BindingFlags.Static));
            ILProcessor il = m.Body.GetILProcessor();
            Instruction first = m.Body.Instructions.FirstOrDefault();
            if (first == null)
                return 0;
            il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
            il.InsertBefore(first, il.Create(OpCodes.Ldarg_1));
            il.InsertBefore(first, il.Create(OpCodes.Call, call));
            il.InsertBefore(first, il.Create(OpCodes.Brfalse, first));
            il.InsertBefore(first, il.Create(OpCodes.Ret));
            Console.WriteLine("[MMOnsterpatch Official Server] SaveSystem.SaveGame online server-save/local-write block hook(s)=1");
            return 1;
        }

        private static int PatchGameScriptReturnToTitleMenuText(ModuleDefinition module)
        {
            TypeDefinition gameScript = module.Types.FirstOrDefault(t => t.Name == "GameScript");
            if (gameScript == null)
            {
                Console.WriteLine("[MMOnsterpatch Official Server] GameScript not found for launcher Disconnect pause text hook.");
                return 0;
            }
            MethodDefinition m = gameScript.Methods.FirstOrDefault(x => x.Name == "RefreshQuickSpells" && x.HasBody && !x.IsStatic && x.Parameters.Count == 0);
            if (m == null)
            {
                Console.WriteLine("[MMOnsterpatch Official Server] GameScript.RefreshQuickSpells not found for launcher Disconnect pause text hook.");
                return 0;
            }
            MethodReference call = module.ImportReference(typeof(OfficialServerSaveSelectNativeRuntime).GetMethod(nameof(OfficialServerSaveSelectNativeRuntime.TickLauncherInGameUi), BindingFlags.Public | BindingFlags.Static));
            ILProcessor il = m.Body.GetILProcessor();
            foreach (Instruction ret in m.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToArray())
            {
                il.InsertBefore(ret, il.Create(OpCodes.Call, call));
            }
            Console.WriteLine("[MMOnsterpatch Official Server] GameScript.RefreshQuickSpells launcher Disconnect pause text hook(s)=1");
            return 1;
        }

        private static int PatchGameScriptUpdateLauncherInGameUi(ModuleDefinition module)
        {
            TypeDefinition gameScript = module.Types.FirstOrDefault(t => t.Name == "GameScript");
            if (gameScript == null)
            {
                Console.WriteLine("[MMOnsterpatch Official Server] GameScript not found for launcher in-game UI tick hook.");
                return 0;
            }
            MethodDefinition m = gameScript.Methods.FirstOrDefault(x => x.Name == "Update" && x.HasBody && !x.IsStatic && x.Parameters.Count == 0);
            if (m == null)
            {
                Console.WriteLine("[MMOnsterpatch Official Server] GameScript.Update not found for launcher in-game UI tick hook.");
                return 0;
            }
            MethodReference call = module.ImportReference(typeof(OfficialServerSaveSelectNativeRuntime).GetMethod(nameof(OfficialServerSaveSelectNativeRuntime.TickLauncherInGameUi), BindingFlags.Public | BindingFlags.Static));
            ILProcessor il = m.Body.GetILProcessor();
            Instruction first = m.Body.Instructions.FirstOrDefault();
            if (first == null)
                return 0;
            il.InsertBefore(first, il.Create(OpCodes.Call, call));
            Console.WriteLine("[MMOnsterpatch Official Server] GameScript.Update launcher in-game UI tick hook(s)=1");
            return 1;
        }

        private static int PatchGameScriptShowDialogueReturnToTitleText(ModuleDefinition module)
        {
            TypeDefinition gameScript = module.Types.FirstOrDefault(t => t.Name == "GameScript");
            if (gameScript == null)
            {
                Console.WriteLine("[MMOnsterpatch Official Server] GameScript not found for Return-to-Title dialogue text override.");
                return 0;
            }
            MethodDefinition m = gameScript.Methods.FirstOrDefault(x => x.Name == "ShowDialogue" && x.HasBody && !x.IsStatic && x.Parameters.Count == 1 && x.Parameters[0].ParameterType.FullName == module.TypeSystem.String.FullName);
            if (m == null)
            {
                Console.WriteLine("[MMOnsterpatch Official Server] GameScript.ShowDialogue(string) not found for Return-to-Title dialogue text override.");
                return 0;
            }
            MethodReference call = module.ImportReference(typeof(OfficialServerSaveSelectNativeRuntime).GetMethod(nameof(OfficialServerSaveSelectNativeRuntime.ApplyOfficialReturnToTitleDialogueIfNeeded), BindingFlags.Public | BindingFlags.Static));
            ILProcessor il = m.Body.GetILProcessor();
            Instruction first = m.Body.Instructions.FirstOrDefault();
            if (first == null)
                return 0;
            il.InsertBefore(first, il.Create(OpCodes.Ldarg_1));
            il.InsertBefore(first, il.Create(OpCodes.Call, call));
            Console.WriteLine("[MMOnsterpatch Official Server] GameScript.ShowDialogue confirmExitToTitle server-text override hook(s)=1");
            return 1;
        }


        private static int PatchDialogueManagerShowDialogueListForOfficialReturnToTitle(ModuleDefinition module)
        {
            TypeDefinition dialogueManager = module.Types.FirstOrDefault(t => t.Name == "DialogueManager");
            if (dialogueManager == null)
            {
                Console.WriteLine("[MMOnsterpatch Official Server] DialogueManager not found for Return-to-Title dialogue list override.");
                return 0;
            }
            MethodDefinition m = dialogueManager.Methods.FirstOrDefault(x => x.Name == "ShowDialogue" && x.HasBody && !x.IsStatic && x.Parameters.Count == 2 && x.Parameters[0].ParameterType.FullName.IndexOf("System.Collections.Generic.List`1<System.String>", StringComparison.OrdinalIgnoreCase) >= 0 && x.Parameters[1].ParameterType.FullName == module.TypeSystem.Boolean.FullName);
            if (m == null)
            {
                Console.WriteLine("[MMOnsterpatch Official Server] DialogueManager.ShowDialogue(List<string>, bool) not found for Return-to-Title dialogue list override.");
                return 0;
            }
            MethodReference call = module.ImportReference(typeof(OfficialServerSaveSelectNativeRuntime).GetMethod(nameof(OfficialServerSaveSelectNativeRuntime.ApplyOfficialReturnToTitleDialogueList), BindingFlags.Public | BindingFlags.Static));
            ILProcessor il = m.Body.GetILProcessor();
            Instruction first = m.Body.Instructions.FirstOrDefault();
            if (first == null)
                return 0;
            il.InsertBefore(first, il.Create(OpCodes.Ldarg_1));
            il.InsertBefore(first, il.Create(OpCodes.Call, call));
            Console.WriteLine("[MMOnsterpatch Official Server] DialogueManager.ShowDialogue(List<string>, bool) connected Return-to-Title text override hook(s)=1");
            return 1;
        }

        private static int PatchDialogueManagerShowDialogueBoolForOfficialReturnToTitle(ModuleDefinition module)
        {
            TypeDefinition dialogueManager = module.Types.FirstOrDefault(t => t.Name == "DialogueManager");
            if (dialogueManager == null)
            {
                Console.WriteLine("[MMOnsterpatch Official Server] DialogueManager not found for Return-to-Title dialogue bool override.");
                return 0;
            }
            MethodDefinition m = dialogueManager.Methods.FirstOrDefault(x => x.Name == "ShowDialogue" && x.HasBody && !x.IsStatic && x.Parameters.Count == 1 && x.Parameters[0].ParameterType.FullName == module.TypeSystem.Boolean.FullName);
            if (m == null)
            {
                Console.WriteLine("[MMOnsterpatch Official Server] DialogueManager.ShowDialogue(bool) not found for Return-to-Title dialogue bool override.");
                return 0;
            }

            MethodReference call = module.ImportReference(typeof(OfficialServerSaveSelectNativeRuntime).GetMethod(nameof(OfficialServerSaveSelectNativeRuntime.ApplyOfficialReturnToTitleDialogueList), BindingFlags.Public | BindingFlags.Static));
            ILProcessor il = m.Body.GetILProcessor();
            int count = 0;
            foreach (Instruction inst in m.Body.Instructions.ToArray())
            {
                if (inst.OpCode != OpCodes.Call && inst.OpCode != OpCodes.Callvirt)
                    continue;
                MethodReference mr = inst.Operand as MethodReference;
                if (mr == null || mr.Name != "ShowDialogue" || mr.Parameters.Count != 2)
                    continue;
                if (mr.Parameters[0].ParameterType.FullName.IndexOf("System.Collections.Generic.List`1", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                // The vanilla IL pushes this, list, playSFX immediately before calling
                // DialogueManager.ShowDialogue(List<string>, bool). Insert before those loads
                // so the localized confirmExitToTitle list is replaced before the text box reads it.
                Instruction insertAt = inst;
                for (int back = 0; back < 3 && insertAt.Previous != null; back++)
                    insertAt = insertAt.Previous;
                il.InsertBefore(insertAt, il.Create(OpCodes.Ldloc_0));
                il.InsertBefore(insertAt, il.Create(OpCodes.Call, call));
                count++;
            }

            Console.WriteLine("[MMOnsterpatch Official Server] DialogueManager.ShowDialogue(bool) connected Return-to-Title text override hook(s)=" + count);
            return count;
        }

        private static int PatchGameScriptReturnToTitle(ModuleDefinition module)
        {
            TypeDefinition gameScript = module.Types.FirstOrDefault(t => t.Name == "GameScript");
            if (gameScript == null)
            {
                Console.WriteLine("[MMOnsterpatch Official Server] GameScript not found for ReturnToTitle confirmation-text hook.");
                return 0;
            }
            MethodDefinition m = gameScript.Methods.FirstOrDefault(x => x.Name == "ReturnToTitle" && x.HasBody && !x.IsStatic && x.Parameters.Count == 0);
            if (m == null)
            {
                Console.WriteLine("[MMOnsterpatch Official Server] GameScript.ReturnToTitle not found for confirmation-text hook.");
                return 0;
            }
            MethodReference call = module.ImportReference(typeof(OfficialServerSaveSelectNativeRuntime).GetMethod(nameof(OfficialServerSaveSelectNativeRuntime.OnReturnToTitleInvoked), BindingFlags.Public | BindingFlags.Static));
            ILProcessor il = m.Body.GetILProcessor();
            Instruction first = m.Body.Instructions.FirstOrDefault();
            if (first == null)
                return 0;
            il.InsertBefore(first, il.Create(OpCodes.Call, call));
            Console.WriteLine("[MMOnsterpatch Official Server] GameScript.ReturnToTitle connected confirmation-text hook(s)=1");
            return 1;
        }

        private static int PatchGameScriptActuallyReturnToTitle(ModuleDefinition module)
        {
            TypeDefinition gameScript = module.Types.FirstOrDefault(t => t.Name == "GameScript");
            if (gameScript == null)
            {
                Console.WriteLine("[MMOnsterpatch Official Server] GameScript not found for ActuallyReturnToTitle server logout hook.");
                return 0;
            }
            MethodDefinition m = gameScript.Methods.FirstOrDefault(x => x.Name == "ActuallyReturnToTitle" && x.HasBody && !x.IsStatic && x.Parameters.Count == 0);
            if (m == null)
            {
                Console.WriteLine("[MMOnsterpatch Official Server] GameScript.ActuallyReturnToTitle not found for server logout hook.");
                return 0;
            }
            MethodReference call = module.ImportReference(typeof(OfficialServerSaveSelectNativeRuntime).GetMethod(nameof(OfficialServerSaveSelectNativeRuntime.OnActuallyReturnToTitleInvoked), BindingFlags.Public | BindingFlags.Static));
            ILProcessor il = m.Body.GetILProcessor();
            Instruction first = m.Body.Instructions.FirstOrDefault();
            if (first == null)
                return 0;
            il.InsertBefore(first, il.Create(OpCodes.Call, call));
            Console.WriteLine("[MMOnsterpatch Official Server] GameScript.ActuallyReturnToTitle server save/disconnect hook(s)=1");
            return 1;
        }
    }

    public sealed class OfficialServerSaveSelectCoroutineHost : MonoBehaviour
    {
        private void OnApplicationQuit()
        {
            OfficialServerSaveSelectNativeRuntime.OnApplicationQuitSaveAndDisconnect();
        }
    }

    public sealed class OfficialServerSaveSelectNativeRelay : MonoBehaviour, IPointerEnterHandler, ISelectHandler, ISubmitHandler, IPointerClickHandler
    {
        public void OnPointerEnter(PointerEventData eventData)
        {
            OfficialServerSaveSelectNativeRuntime.OnModeButtonHovered();
        }

        public void OnSelect(BaseEventData eventData)
        {
            OfficialServerSaveSelectNativeRuntime.OnModeButtonHovered();
        }

        public void OnSubmit(BaseEventData eventData)
        {
            OfficialServerSaveSelectNativeRuntime.OnModeButtonClicked();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            OfficialServerSaveSelectNativeRuntime.OnModeButtonClicked();
        }
    }

    public sealed class OfficialServerSaveSelectDeleteRelay : MonoBehaviour, IPointerEnterHandler, ISelectHandler, ISubmitHandler, IPointerClickHandler
    {
        public void OnPointerEnter(PointerEventData eventData)
        {
            OfficialServerSaveSelectNativeRuntime.OnDeleteButtonHovered();
        }

        public void OnSelect(BaseEventData eventData)
        {
            OfficialServerSaveSelectNativeRuntime.OnDeleteButtonHovered();
        }

        public void OnSubmit(BaseEventData eventData)
        {
            OfficialServerSaveSelectNativeRuntime.OnDeleteButtonClicked();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            OfficialServerSaveSelectNativeRuntime.OnDeleteButtonClicked();
        }
    }

    public static class OfficialServerSaveSelectNativeRuntime
    {
        private static object _menuObj;
        private static GameObject _menuSaveFiles;
        private static GameObject _root;
        private static GameObject _modeButton;
        private static TMP_Text _modeButtonText;
        private static GameObject _deleteButton;
        private static TMP_Text _deleteButtonText;
        private static TMP_Text _statusText;
        private static bool _onlineMode;
        private static bool _authBusy;
        private static string _statusOverride;
        private static bool _officialOnlineSaveSessionActive;
        private static int _officialOnlineSaveSessionSlot = -1;
        private static OfficialServerSaveSelectCoroutineHost _host;
        private static bool _loggedMissingMenu;
        private static bool _loggedHidden;
        private static float _nextTick;
        private static int _lastHoverSlot = -1;
        private static bool _layoutLogged;
        private static Transform _visibleSaveSlotParent;
        private static bool _onlineHoverBlockLogged;
        private static bool _localSaveBlockLogged;
        private static float _modeClickGuardUntil;
        private static float _deleteClickGuardUntil;
        private static bool _deleteMode;
        private static int _pendingLocalDeleteSlot = -1;
        private static float _pendingLocalDeleteCheckUntil;
        private static float _pendingSelectFirstSlotAfter;
        private static float _pendingSelectFirstSlotUntil;
        private static bool _pendingSelectFirstSlotLoggedDeferral;
        private static bool _onlineSaveDataClearedLogged;
        private static bool _onlineSlotPayloadsLoaded;
        private static readonly object[] _onlineSaveSlotCache = new object[6];
        private static readonly bool[] _onlineSaveSlotOccupied = new bool[6];
        private static readonly bool[] _onlineSaveSlotPayloadLoaded = new bool[6];
        private static readonly string[] _onlineSaveSlotDisplayNames = new string[6];
        private static readonly int[] _onlineSaveSlotUpdatedAt = new int[6];
        private static readonly int[] _onlineSaveSlotPlayerDesign = new int[6];
        private static readonly int[] _onlineSaveSlotPlayerColor1 = new int[6];
        private static readonly int[] _onlineSaveSlotPlayerColor2 = new int[6];
        private static readonly string[] _onlineSaveSlotHandles = new string[6];
        private static readonly bool[] _onlineSaveSlotPvpRegistered = new bool[6];
        private static readonly int[] _onlineSaveSlotPvpRp = new int[6];
        private static readonly string[] _onlineSaveSlotPvpRank = new string[6];
        private static readonly int[] _onlineSaveSlotPvpWins = new int[6];
        private static readonly int[] _onlineSaveSlotPvpLosses = new int[6];
        private static readonly int[] _onlineSaveSlotPvpDraws = new int[6];
        private static readonly TMP_Text[] _onlineSaveSlotPvpSummaryTexts = new TMP_Text[6];
        private static TMP_Text _onlinePreviewPvpSummaryText;
        private static bool _refreshingOnlineSlotsFromCache;
        private static bool _returnToTitleFromOfficialDisconnect;
        private static bool _launcherPendingLogOutToSaveSelect;
        private static float _launcherPendingLogOutToSaveSelectAfter;
        private static bool _launcherLogOutToSaveSelectBusy;
        private static bool _launcherSilentLogOutReturnToSaveSelect;
        private static float _lastOfficialSaveUploadAt;
        private static double _lastOfficialSaveUploadCompletedAt;
        private static float _lastWorldRatesRefreshAt = -9999f;
        private static readonly object _officialSaveUploadLock = new object();
        private static bool _officialSaveUploadInFlight;
        private static bool _officialSaveUploadPending;
        private static int _officialSaveUploadPendingSlot = -1;
        private static string _officialSaveUploadPendingToken = string.Empty;
        private static string _officialSaveUploadPendingJson = string.Empty;
        private static string _officialSaveUploadPendingHost = string.Empty;
        private static int _officialSaveUploadPendingPort;
        private static int _officialSaveUploadPendingTimeoutMs;
        private static int _officialSaveUploadGeneration;
        private static int _officialSaveUploadLastCompletedGeneration;
        private static bool _officialSaveUploadLastSuccess = true;
        private static string _officialSaveUploadLastError = string.Empty;
        private static Image _onlineWallpaperTargetImage;
        private static Sprite _originalSaveSelectBackgroundSprite;
        private static Color _originalSaveSelectBackgroundColor = Color.white;
        private static Sprite _embeddedOnlineWallpaperSprite;
        private static Texture2D _embeddedOnlineWallpaperTexture;
        private static string _onlineWallpaperTargetPath;
        private static bool _onlineWallpaperSwapLogged;
        private static bool _returnToTitleDialogueOverrideApplied;
        private static string[] _returnToTitleDialogueOriginalLines;
        private static float _officialReturnToTitleDialogueOverridePendingUntil;
        private static bool _launcherSessionChecked;
        private static bool _launcherSessionActive;
        private static bool _launcherAutoAuthStarted;
        private static bool _launcherOpenDeleteAfterOnlineSlots;
        private static bool _launcherPreAuthBusy;
        private static bool _launcherAllowNativePlayOnce;
        private static bool _launcherOnlineVersionSelectReturnGuard;
        private static float _launcherOnlineVersionSelectReturnGuardUntil;
        private static int _launcherOnlineVersionSelectReturnSlot = -1;
        private static float _lastLauncherTitleTakeoverLog;
        private static bool _applicationQuitSaveStarted;
        private static float _nextLauncherInGameUiTextTick;
        private static float _lastLauncherCursorRepairLog;
        private static Sprite _embeddedOnlineTitleLogoSprite;
        private static Texture2D _embeddedOnlineTitleLogoTexture;
        private static Sprite _embeddedOnlineSavePlusSprite;
        private static Texture2D _embeddedOnlineSavePlusTexture;
        private static Sprite _embeddedOnlineLegendaryBackgroundSprite;
        private static Texture2D _embeddedOnlineLegendaryBackgroundTexture;
        private static Sprite _embeddedOnlineHouseIcon1Sprite;
        private static Texture2D _embeddedOnlineHouseIcon1Texture;
        private static bool _onlineHouseIcon1SummaryLogged;
        private static bool _onlineSavePreviewHouseIcon1Logged;
        private static readonly Dictionary<Image, Sprite> _originalSavePreviewHouseSprites = new Dictionary<Image, Sprite>();
        private static readonly Dictionary<string, Sprite> _onlineHouseIcon1SliceSprites = new Dictionary<string, Sprite>();
        private static Sprite _originalTitleLogoSprite;
        private static Sprite _originalTitleBackgroundSprite;
        private static bool _onlineTitleBackgroundSwapLogged;
        private static GameObject _launcherLegendaryMon1Object;
        private static RectTransform _legendaryMon0OffsetRoot;
        private static RectTransform _legendaryMon1OffsetRoot;
        private static Vector3? _legendaryMon0BaseLocalPosition;
        private static Vector3? _legendaryMon0BaseLocalScale;
        private static Vector2? _legendaryMon0BaseSizeDelta;
        private static Vector3? _legendaryMon1BaseLocalPosition;
        private static Vector3? _legendaryMon1BaseLocalScale;
        private static Vector2? _legendaryMon1BaseSizeDelta;
        private static readonly Dictionary<Image, Sprite> _originalSavePlusSprites = new Dictionary<Image, Sprite>();

        private const string ConfigFileName = "goose.monsterpatch.officialserver.cfg";
        private static ConfigFile _config;
        private static ConfigEntry<float> ButtonX;
        private static ConfigEntry<float> ButtonY;
        private static ConfigEntry<float> ButtonWidth;
        private static ConfigEntry<float> ButtonHeight;
        private static ConfigEntry<float> ButtonFontSize;
        private static ConfigEntry<float> DeleteButtonX;
        private static ConfigEntry<float> DeleteButtonY;
        private static ConfigEntry<float> DeleteButtonWidth;
        private static ConfigEntry<float> DeleteButtonHeight;
        private static ConfigEntry<float> DeleteButtonFontSize;
        private static ConfigEntry<float> DeleteButtonCursorX;
        private static ConfigEntry<float> DeleteButtonCursorY;
        private static ConfigEntry<float> StatusX;
        private static ConfigEntry<float> StatusY;
        private static ConfigEntry<float> StatusWidth;
        private static ConfigEntry<float> StatusHeight;
        private static ConfigEntry<float> StatusFontSize;
        private static ConfigEntry<string> ServerHost;
        private static ConfigEntry<int> SocialPort;
        private static ConfigEntry<int> NetworkTimeoutMs;
        private static ConfigEntry<bool> AsyncOfficialSaveUploads;
        private static ConfigEntry<int> FinalSaveFlushTimeoutMs;
        private static ConfigEntry<bool> LazyLoadOnlineSaveSlotPayloads;
        private static ConfigEntry<float> SkipFinalDisconnectSaveIfLastUploadWithinSeconds;
        private static ConfigEntry<bool> OnlineSaveCacheEnabled;
        private static ConfigEntry<bool> LauncherUploadsOnlineSaveCache;
        private static ConfigEntry<float> WorldRatesRefreshMinIntervalSeconds;
        private static ConfigEntry<bool> OnlineWallpaperTintEnabled;
        private static ConfigEntry<string> OnlineWallpaperTintColor;
        private static ConfigEntry<float> OnlineWallpaperMinWidth;
        private static ConfigEntry<float> OnlineWallpaperMinHeight;
        private static ConfigEntry<bool> OnlineTitleBackgroundEnabled;
        private static ConfigEntry<bool> OnlineTitleLegendaryMon0Enabled;
        private static ConfigEntry<float> OnlineTitleLegendaryMon0OffsetX;
        private static ConfigEntry<float> OnlineTitleLegendaryMon0OffsetY;
        private static ConfigEntry<float> OnlineTitleLegendaryMon0Scale;
        private static ConfigEntry<float> OnlineTitleLegendaryMon0Alpha;
        private static ConfigEntry<bool> OnlineTitleLegendaryMon1Enabled;
        private static ConfigEntry<float> OnlineTitleLegendaryMon1OffsetX;
        private static ConfigEntry<float> OnlineTitleLegendaryMon1OffsetY;
        private static ConfigEntry<float> OnlineTitleLegendaryMon1Scale;
        private static ConfigEntry<float> OnlineTitleLegendaryMon1Alpha;
        private static ConfigEntry<float> SaveSelectPvpPreviewOffsetX;
        private static ConfigEntry<float> SaveSelectPvpPreviewOffsetY;
        private static ConfigEntry<float> SaveSelectPvpPreviewWidth;
        private static ConfigEntry<float> SaveSelectPvpPreviewHeight;
        private static ConfigEntry<float> SaveSelectPvpPreviewFontSize;

        [Serializable]
        private sealed class OfficialOnlineSaveSlotInfo
        {
            public int slot;
            public int occupied;
            public string save_json;
            public string display_name;
            public long updated_at;
            public string public_handle;
            public int pvp_registered;
            public int pvp_rp;
            public string pvp_rank;
            public int pvp_wins;
            public int pvp_losses;
            public int pvp_draws;
        }

        [Serializable]
        private sealed class OfficialOnlineSaveSlotsResponse
        {
            public OfficialOnlineSaveSlotInfo[] slots;
        }

        public static bool TryHandleLauncherLogInFromTitle(object menuObj)
        {
            try
            {
                _menuObj = menuObj;

                if (!IsLauncherSessionActive())
                {
                    Log("MenuScript.Play observed; vanilla/offline save-select flow allowed.");
                    return false;
                }

                if (_launcherAllowNativePlayOnce)
                {
                    _launcherAllowNativePlayOnce = false;
                    Log("Launcher pre-auth complete; allowing one native Play2 save-select transition.");
                    return false;
                }

                if (_launcherPreAuthBusy || _authBusy)
                {
                    Log("Launcher Log In ignored because authentication is already running.");
                    return true;
                }

                EnsureTitleCoroutineHost(menuObj);
                if (_host == null)
                {
                    Log("Launcher Log In could not create a coroutine host on the title MenuScript; allowing vanilla Play as fallback.");
                    return false;
                }

                _launcherOpenDeleteAfterOnlineSlots = false;
                _launcherAutoAuthStarted = true;
                _host.StartCoroutine(AuthenticateThenOpenLauncherSaveSelect(false));
                Log("Launcher Log In selected. Authenticating before opening Online save-select.");
                return true;
            }
            catch (Exception ex)
            {
                Log("TryHandleLauncherLogInFromTitle failed: " + ex.Message);
                return false;
            }
        }

        public static void TickMenuObject(object menuObj)
        {
            try
            {
                if (Time.unscaledTime < _nextTick)
                    return;
                _nextTick = Time.unscaledTime + 0.20f;

                if (menuObj == null)
                    return;

                _menuObj = menuObj;
                TryStartPendingLauncherLogOutSaveSelect(menuObj);
                ApplyLauncherTitleAndExtrasTakeover(menuObj);
                GameObject saveMenu = GetGameObjectField(menuObj, "menuSaveFiles");
                if (saveMenu == null)
                {
                    if (!_loggedMissingMenu)
                    {
                        _loggedMissingMenu = true;
                        Log("MenuScript.Update tick could not read field menuSaveFiles. Save-select UI cannot attach.");
                    }
                    return;
                }

                bool visible = saveMenu.activeInHierarchy || saveMenu.activeSelf;
                if (!visible)
                {
                    if (_root != null && _root.activeSelf)
                        _root.SetActive(false);
                    if (!_loggedHidden)
                    {
                        _loggedHidden = true;
                        Log("menuSaveFiles found but not visible yet; native save-select UI remains hidden.");
                    }
                    return;
                }

                EnsureForMenuObject(menuObj);
                RepairLauncherOnlineSaveSelectAfterVersionBack(menuObj);
                UpdateVisualState();
                HandlePendingSaveSlotSelection();
                HandlePendingLocalDeleteCompletion();
            }
            catch (Exception ex)
            {
                Log("TickMenuObject failed: " + ex.Message);
            }
        }

        public static void EnsureForMenuObject(object menuObj)
        {
            try
            {
                if (menuObj == null)
                    return;

                _menuObj = menuObj;
                GameObject saveMenu = GetGameObjectField(menuObj, "menuSaveFiles");
                if (saveMenu == null)
                {
                    if (!_loggedMissingMenu)
                    {
                        _loggedMissingMenu = true;
                        Log("EnsureForMenuObject could not read MenuScript.menuSaveFiles.");
                    }
                    return;
                }

                _menuSaveFiles = saveMenu;
                bool visible = saveMenu.activeInHierarchy || saveMenu.activeSelf;
                if (_root == null)
                {
                    CreateNativeRootAndControls(menuObj, saveMenu);
                }

                if (_root != null)
                {
                    _root.transform.SetAsLastSibling();
                    _root.SetActive(visible);
                    UpdateVisualState();
                }

                TryStartLauncherAutoOnlineSlots();
            }
            catch (Exception ex)
            {
                Log("EnsureForMenuObject failed: " + ex);
            }
        }

        public static void OnHoverSaveFile(object menuObj, int slot)
        {
            try
            {
                _lastHoverSlot = slot;
                EnsureForMenuObject(menuObj);
                UpdateVisualState();
                LogOncePerSlot(slot, "HoverSaveFile observed on native save slot " + slot + "; official preview panel is active path.");
            }
            catch (Exception ex)
            {
                Log("OnHoverSaveFile failed: " + ex.Message);
            }
        }

        public static void OnModeButtonHovered()
        {
            try
            {
                if (_menuObj != null)
                {
                    MethodInfo hover = _menuObj.GetType().GetMethod("HoverButtonLarge", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (hover != null && _modeButton != null)
                        hover.Invoke(_menuObj, new object[] { _modeButton });
                }
            }
            catch { }
        }

        public static void OnModeButtonClicked()
        {
            try
            {
                if (Time.unscaledTime < _modeClickGuardUntil)
                {
                    Log("Mode button click ignored during short input guard.");
                    return;
                }
                _modeClickGuardUntil = Time.unscaledTime + 0.35f;

                EnsureHostComponent();

                if (_onlineMode)
                {
                    SwitchToOfflineSaveSelect();
                    return;
                }

                if (_authBusy)
                {
                    Log("Switch to Online Mode ignored because launcher account authentication is already in progress.");
                    return;
                }

                // If the GTS/AIO auth host is carrying a stale busy flag from a prior
                // title transition or closed game session, clear it before starting the
                // Official Server login. This prevents the save-select button from
                // sitting on Connecting forever without opening Steam auth.
                try
                {
                    if (GTSRuntimeHost.IsAuthBusyForAio() && !GTSRuntimeHost.IsLoggedInForAio())
                    {
                        Log("Detected stale launcher/AIO auth busy state before Official Server login; resetting auth state.");
                        GTSRuntimeHost.ResetStaleOfficialServerAuthForAio();
                    }
                }
                catch { }

                if (_host != null)
                    _host.StartCoroutine(AuthenticateThenShowOnlineSlots());
                else
                    Log("Switch to Online Mode could not start authentication coroutine because the save-select host component is missing.");
            }
            catch (Exception ex)
            {
                Log("OnModeButtonClicked failed: " + ex.Message);
            }
        }

        public static void OnDeleteButtonHovered()
        {
            try
            {
                if (_menuObj != null)
                {
                    MethodInfo hover = _menuObj.GetType().GetMethod("HoverButtonLarge", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (hover != null && _deleteButton != null)
                    {
                        hover.Invoke(_menuObj, new object[] { _deleteButton });
                        PositionDeleteButtonCursor();
                    }
                }
            }
            catch { }
        }

        public static void OnDeleteButtonClicked()
        {
            try
            {
                if (Time.unscaledTime < _deleteClickGuardUntil)
                {
                    Log("Delete button click ignored during short input guard.");
                    return;
                }
                _deleteClickGuardUntil = Time.unscaledTime + 0.30f;

                if (_authBusy || !_onlineMode)
                {
                    Log("Delete button ignored because delete is only available on the Official Server Online Mode save-select screen.");
                    return;
                }

                if (IsNativeDeleteModeActive(_menuObj))
                {
                    SetDeleteMode(false, "Cancel button");
                    return;
                }

                // Use the game's official Extras-menu delete entry point. It sets deletingSaveFile
                // and runs MenuScript.Play()/Play2(), which gives the native red delete screen and
                // confirmation dialogue path. Online save data is re-applied from the server cache
                // after Play2 reloads local saveData from disk.
                bool invokedOfficial = TryInvokeMenuMethod(_menuObj, "DeleteASaveFile");
                if (invokedOfficial)
                {
                    Log("Delete button invoked official MenuScript.DeleteASaveFile flow for Online Mode.");
                    ApplyOnlineSaveSlotVisuals();
                    RequestSelectFirstSaveSlot();
                    UpdateVisualState();
                }
                else
                {
                    SetDeleteMode(true, "Delete button fallback");
                }
            }
            catch (Exception ex)
            {
                Log("OnDeleteButtonClicked failed: " + ex.Message);
            }
        }

        private static IEnumerator AuthenticateThenOpenLauncherSaveSelect(bool deleteMode)
        {
            _launcherPreAuthBusy = true;
            _authBusy = true;
            _statusOverride = "Server Status: Connecting";
            UpdateLauncherTitleLogInText();
            Log("Launcher pre-auth flow started from title screen. Native save-select will remain closed until online slots are ready.");

            bool authenticated = false;
            bool authRoutineFailed = false;
            IEnumerator authRoutine = null;

            try
            {
                authenticated = GTSRuntimeHost.TryUseLauncherAccountSessionForAio();
                if (authenticated)
                    Log("Launcher account handoff found. Skipping old Steam/socket pre-auth and loading online save slots.");
            }
            catch (Exception ex)
            {
                Log("Launcher account handoff check failed: " + ex.Message);
            }

            if (!authenticated)
            {
                try
                {
                    if (GTSRuntimeHost.IsAuthBusyForAio() && !GTSRuntimeHost.IsLoggedInForAio())
                    {
                        Log("Launcher pre-auth found stale auth busy state; resetting before login.");
                        GTSRuntimeHost.ResetStaleOfficialServerAuthForAio();
                    }
                }
                catch { }

                try
                {
                    authRoutine = GTSRuntimeHost.EnsureSteamAuthForAioCoroutine();
                }
                catch (Exception ex)
                {
                    authRoutineFailed = true;
                    Log("Launcher pre-auth routine could not be created: " + ex.Message);
                }

                if (authRoutine != null)
                {
                    float authStart = Time.realtimeSinceStartup;
                    const float watchdogSeconds = 90f;
                    while (true)
                    {
                        if (Time.realtimeSinceStartup - authStart > watchdogSeconds)
                        {
                            authRoutineFailed = true;
                            Log("Launcher pre-auth timed out before completion; clearing stale auth state.");
                            try { GTSRuntimeHost.ResetStaleOfficialServerAuthForAio(); } catch { }
                            break;
                        }

                        object current = null;
                        bool moved = false;
                        try
                        {
                            moved = authRoutine.MoveNext();
                            if (moved)
                                current = authRoutine.Current;
                        }
                        catch (Exception ex)
                        {
                            authRoutineFailed = true;
                            Log("Launcher pre-auth failed: " + ex.Message);
                            break;
                        }

                        if (!moved)
                            break;

                        yield return current;
                    }
                }

                if (!authRoutineFailed)
                {
                    try
                    {
                        authenticated = GTSRuntimeHost.TryUseLauncherAccountSessionForAio() || GTSRuntimeHost.IsLoggedInForAio();
                    }
                    catch (Exception ex)
                    {
                        Log("Launcher pre-auth status check failed: " + ex.Message);
                    }
                }
            }

            if (!authenticated)
            {
                _onlineMode = false;
                _authBusy = false;
                _launcherPreAuthBusy = false;
                _launcherAutoAuthStarted = false;
                _statusOverride = "Server Status: Auth Failed";
                SetObjectField(_menuObj, "pressingButton", false);
                UpdateLauncherTitleLogInText();
                Log("Launcher pre-auth did not complete. Staying on the title screen.");
                yield break;
            }

            _onlineMode = true;
            _statusOverride = "Server Status: Loading Saves";
            Log("Launcher pre-auth complete. Loading online save slots before native save-select opens.");

            bool loaded = FetchOnlineSaveSlotsFromServer();
            if (loaded)
                RefreshOfficialWorldRatesFromServer();

            _authBusy = false;
            _launcherPreAuthBusy = false;
            _statusOverride = loaded ? null : "Server Status: Save Load Failed";

            if (!loaded)
            {
                _onlineMode = false;
                _launcherAutoAuthStarted = false;
                SetObjectField(_menuObj, "pressingButton", false);
                UpdateLauncherTitleLogInText();
                Log("Launcher pre-auth succeeded, but online save slots failed to load. Staying on the title screen.");
                yield break;
            }

            SetObjectField(_menuObj, "pressingButton", false);
            _launcherAllowNativePlayOnce = true;
            bool started = TryStartMenuCoroutine(_menuObj, "Play2");
            if (started)
                _launcherAllowNativePlayOnce = false;
            if (!started)
            {
                Log("Launcher pre-auth could not start MenuScript.Play2 directly; trying MenuScript.Play fallback.");
                bool invokedPlay = TryInvokeMenuMethod(_menuObj, "Play");
                if (!invokedPlay)
                {
                    _launcherAllowNativePlayOnce = false;
                    _launcherAutoAuthStarted = false;
                    UpdateLauncherTitleLogInText();
                    Log("Launcher pre-auth could not open native save-select after authentication.");
                    yield break;
                }
            }

            float waitUntil = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < waitUntil)
            {
                GameObject saveMenu = GetGameObjectField(_menuObj, "menuSaveFiles");
                if (saveMenu != null && (saveMenu.activeInHierarchy || saveMenu.activeSelf))
                    break;
                yield return null;
            }

            EnsureForMenuObject(_menuObj);
            InvokeMenuMethod(_menuObj, "RefreshSaveFiles");
            ApplyOnlineSaveSlotVisuals();

            if (deleteMode)
                SetDeleteMode(true, "Launcher pre-auth delete flow");
            else
                RequestSelectFirstSaveSlot();

            UpdateVisualState();
            UpdateLauncherTitleLogInText();
            Log("Launcher pre-auth save-select flow is ready. Online slot data was loaded before the save-select screen opened.");
        }

        private static IEnumerator AuthenticateThenShowOnlineSlots()
        {
            _authBusy = true;
            _statusOverride = "Server Status: Connecting";
            UpdateVisualState();
            Log("Switch to Online Mode clicked. Starting launcher account authentication from the native save-select screen.");

            bool authenticated = false;
            bool authRoutineFailed = false;
            IEnumerator authRoutine = null;

            try
            {
                authenticated = GTSRuntimeHost.TryUseLauncherAccountSessionForAio();
                if (authenticated)
                    Log("Launcher account handoff found from save-select. Skipping old Steam/socket pre-auth.");
            }
            catch (Exception ex)
            {
                Log("launcher account handoff check from save-select failed: " + ex.Message);
            }

            if (!authenticated)
            {
                try
                {
                    authRoutine = GTSRuntimeHost.EnsureSteamAuthForAioCoroutine();
                }
                catch (Exception ex)
                {
                    authRoutineFailed = true;
                    Log("launcher account authentication routine could not be created from save-select: " + ex.Message);
                }

                if (authRoutine != null)
                {
                    float authStart = Time.realtimeSinceStartup;
                    const float officialAuthWatchdogSeconds = 90f;
                    while (true)
                    {
                        if (Time.realtimeSinceStartup - authStart > officialAuthWatchdogSeconds)
                        {
                            authRoutineFailed = true;
                            Log("Official Server launcher account authentication timed out before completion; clearing stale auth state.");
                            try { GTSRuntimeHost.ResetStaleOfficialServerAuthForAio(); } catch { }
                            break;
                        }
                        object current = null;
                        bool moved = false;

                        try
                        {
                            moved = authRoutine.MoveNext();
                            if (moved)
                                current = authRoutine.Current;
                        }
                        catch (Exception ex)
                        {
                            authRoutineFailed = true;
                            Log("launcher account authentication from save-select failed: " + ex.Message);
                            break;
                        }

                        if (!moved)
                            break;

                        yield return current;
                    }
                }

                if (!authRoutineFailed)
                {
                    try
                    {
                        authenticated = GTSRuntimeHost.TryUseLauncherAccountSessionForAio() || GTSRuntimeHost.IsLoggedInForAio();
                    }
                    catch (Exception ex)
                    {
                        Log("launcher account authentication status check from save-select failed: " + ex.Message);
                    }
                }
            }

            _authBusy = false;
            if (authenticated)
            {
                _onlineMode = true;
                _statusOverride = "Server Status: Loading Saves";
                UpdateVisualState();
                Log("launcher account authentication complete. Loading Official Server online save slots.");
                bool loaded = FetchOnlineSaveSlotsFromServer();
                if (loaded)
                    RefreshOfficialWorldRatesFromServer();
                _statusOverride = loaded ? null : "Server Status: Save Load Failed";
                InvokeMenuMethod(_menuObj, "RefreshSaveFiles");
                ApplyOnlineSaveSlotVisuals();
                if (_launcherOpenDeleteAfterOnlineSlots)
                {
                    _launcherOpenDeleteAfterOnlineSlots = false;
                    TryInvokeMenuMethod(_menuObj, "DeleteASaveFile");
                    ApplyOnlineSaveSlotVisuals();
                }
                RequestSelectFirstSaveSlot();
            }
            else
            {
                _onlineMode = false;
                _statusOverride = "Server Status: Auth Failed";
                Log("launcher account authentication did not complete. Staying on local/offline save slots.");
            }

            UpdateVisualState();
        }

        private static void SwitchToOfflineSaveSelect()
        {
            _modeClickGuardUntil = Time.unscaledTime + 0.75f;
            DisconnectServerSession("Switch to Offline Mode");
            _onlineMode = false;
            _authBusy = false;
            _statusOverride = null;
            _onlineSaveDataClearedLogged = false;
            ClearOfficialOnlineSaveSession("Switch to Offline Mode");
            OfficialServerWorldRatesRuntime.ResetToOfflineDefaults("Switch to Offline Mode");
            ClearOnlineSaveSlotCache();
            SetDeleteMode(false, "Switch to Offline Mode");
            RestoreOnlineWallpaperColors();
            RestoreOnlinePreviewPlayerSprites(_menuObj);
            Log("Switch to Offline Mode clicked. Restoring vanilla local save-slot screen.");
            RestoreLocalSaveDataFromDisk();
            InvokeMenuMethod(_menuObj, "RefreshSaveFiles");
            RepairLocalSaveSlotVisuals();
            RequestSelectFirstSaveSlot();
            UpdateVisualState();
        }

        private static void SetDeleteMode(bool active, string reason)
        {
            try
            {
                _deleteMode = active;
                if (!active)
                {
                    _pendingLocalDeleteSlot = -1;
                    _pendingLocalDeleteCheckUntil = 0f;
                }

                if (_menuObj != null)
                    SetObjectField(_menuObj, "deletingSaveFile", active);

                GameObject panelTopDelete = GetGameObjectField(_menuObj, "panelTopDeleteSave");
                if (panelTopDelete != null)
                    panelTopDelete.SetActive(active);

                GameObject preview = GetGameObjectField(_menuObj, "panelSaveDataPreview");
                if (preview != null)
                {
                    Image previewImg = preview.GetComponent<Image>();
                    Sprite sprite = GetObjectField(_menuObj, active ? "sliceMenuRed" : "sliceMenu") as Sprite;
                    if (previewImg != null && sprite != null)
                        previewImg.sprite = sprite;
                    preview.SetActive(false);
                }

                if (!active && _lastOfficialSaveUploadAt <= 0f)
                    _statusOverride = null;

                InvokeMenuMethod(_menuObj, "RefreshSaveFiles");
                if (_onlineMode)
                    ApplyOnlineSaveSlotVisuals();

                RequestSelectFirstSaveSlot();
                UpdateVisualState();
                Log((active ? "Delete Save mode enabled" : "Delete Save mode canceled") + " from " + reason + ".");
            }
            catch (Exception ex)
            {
                Log("SetDeleteMode failed: " + ex.Message);
            }
        }

        private static bool IsNativeDeleteModeActive(object menuObj)
        {
            try
            {
                object value = GetObjectField(menuObj, "deletingSaveFile");
                return value is bool b && b;
            }
            catch { return _deleteMode; }
        }

        private static void CreateNativeRootAndControls(object menuObj, GameObject saveMenu)
        {
            EnsureConfig();

            GameObject slot0 = GetGameObjectFromArrayField(menuObj, "saveFileObj", 0);
            GameObject slot5 = GetGameObjectFromArrayField(menuObj, "saveFileObj", 5);
            GameObject preview = GetGameObjectField(menuObj, "panelSaveDataPreview");

            // Important: do NOT parent to the save-slot Panel. The previous build used the visible slot
            // parent as the custom root and that disturbed the native save-slot layout. The real save
            // screen owns a top-level menuSaveSelect transform; vanilla slot panel and preview panel are
            // siblings under it. Custom controls belong there as siblings, not inside the slot grid.
            Transform parent = null;
            GameObject menuSaveSelect = GetGameObjectField(menuObj, "menuSaveSelect");
            if (menuSaveSelect != null && menuSaveSelect.transform != null)
                parent = menuSaveSelect.transform;
            else if (preview != null && preview.transform != null && preview.transform.parent != null)
                parent = preview.transform.parent;
            else if (saveMenu != null && saveMenu.transform != null)
                parent = saveMenu.transform;

            _visibleSaveSlotParent = parent;
            Log("Creating native save-select controls as siblings under '" + (parent != null ? parent.name : "null") + "' so vanilla slot layout is not moved.");

            if (_root == null)
            {
                _root = new GameObject("MMOnsterpatchOfficialServer_SaveSelectNativeControls", typeof(RectTransform), typeof(CanvasGroup));
                RectTransform rootRt = _root.GetComponent<RectTransform>();
                rootRt.SetParent(parent, false);
                rootRt.anchorMin = new Vector2(0.5f, 0.5f);
                rootRt.anchorMax = new Vector2(0.5f, 0.5f);
                rootRt.pivot = new Vector2(0.5f, 0.5f);
                rootRt.anchoredPosition = Vector2.zero;
                rootRt.localPosition = Vector3.zero;
                rootRt.localScale = Vector3.one;
                rootRt.sizeDelta = new Vector2(1f, 1f);
                CanvasGroup cg = _root.GetComponent<CanvasGroup>();
                _host = _root.GetComponent<OfficialServerSaveSelectCoroutineHost>();
                if (_host == null) _host = _root.AddComponent<OfficialServerSaveSelectCoroutineHost>();
                cg.alpha = 1f;
                cg.interactable = true;
                cg.blocksRaycasts = false;
                _root.transform.SetAsLastSibling();
                _root.SetActive(true);
            }
            else if (_root.transform.parent != parent && parent != null)
            {
                _root.transform.SetParent(parent, false);
                _root.transform.SetAsLastSibling();
            }

            LogNativeLayout(menuObj, slot0, slot5, preview, parent);
            CreateStatusText(menuObj);
            // Launcher-gated Online Mode: the old save-select Switch Online/Offline button is removed.
            // Launcher sessions use title-screen Log In -> auto online slots instead.
            // Delete is moved back to the Extras screen online-delete flow.
            ForceVisibleRecursive(_root);
            UpdateVisualState();
        }

        private static void CreateStatusText(object menuObj)
        {
            if (_statusText != null)
                return;

            Transform parent = _root != null ? _root.transform : _visibleSaveSlotParent;
            if (parent == null)
                return;

            GameObject go = new GameObject("MMOnsterpatchOfficialServer_ServerStatusText_NATIVE", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            go.transform.SetAsLastSibling();
            _statusText = go.GetComponent<TextMeshProUGUI>();

            TMP_Text txtPreview = GetObjectField(menuObj, "txtPreview") as TMP_Text;
            if (txtPreview != null)
            {
                _statusText.font = txtPreview.font;
                _statusText.fontMaterial = txtPreview.fontMaterial;
                _statusText.fontSharedMaterial = txtPreview.fontSharedMaterial;
            }

            RectTransform rt = _statusText.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
            rt.sizeDelta = new Vector2(Cfg(StatusWidth, 120f), Cfg(StatusHeight, 18f));
            // menuSaveSelect native coordinates from decompile/log: preview panel lives at x=44,y=0;
            // save-slot Panel lives at x=-74,y=0. Config lets this be tuned in-game-style
            // native coordinates without rebuilding.
            rt.anchoredPosition = new Vector2(Cfg(StatusX, -150f), Cfg(StatusY, 82f));
            rt.localPosition = new Vector3(rt.localPosition.x, rt.localPosition.y, 0f);

            _statusText.enableWordWrapping = false;
            _statusText.overflowMode = TextOverflowModes.Overflow;
            _statusText.alignment = TextAlignmentOptions.Left;
            _statusText.fontSize = Cfg(StatusFontSize, 4f);
            _statusText.raycastTarget = false;
            _statusText.color = new Color(0.45f, 0.10f, 0.10f, 1f);
            go.SetActive(true);
            Log("Native status text created under " + parent.name + "; local=" + rt.localPosition + "; anchored=" + rt.anchoredPosition + "; fontCopied=" + (txtPreview != null) + ".");
        }

        private static void CreateModeButton(object menuObj)
        {
            if (_modeButton != null)
                return;

            Transform parent = _root != null ? _root.transform : _visibleSaveSlotParent;
            if (parent == null)
                return;

            _modeButton = new GameObject("MMOnsterpatchOfficialServer_SwitchOnlineModeButton_NATIVE", typeof(RectTransform), typeof(Image), typeof(Button), typeof(OfficialServerSaveSelectNativeRelay));
            _modeButton.transform.SetParent(parent, false);
            _modeButton.transform.SetAsLastSibling();
            _modeButton.SetActive(true);

            RectTransform brt = _modeButton.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0.5f, 0.5f);
            brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.pivot = new Vector2(0.5f, 0.5f);
            brt.localScale = Vector3.one;
            brt.localRotation = Quaternion.identity;
            brt.sizeDelta = new Vector2(Cfg(ButtonWidth, 110f), Cfg(ButtonHeight, 18f));
            // Under the native preview panel (panelSaveDataPreview local x=44,y=0,height=124).
            // Config values use the same menuSaveSelect native coordinate space.
            brt.anchoredPosition = new Vector2(Cfg(ButtonX, 44f), Cfg(ButtonY, -76f));
            brt.localPosition = new Vector3(brt.localPosition.x, brt.localPosition.y, 0f);

            Image img = _modeButton.GetComponent<Image>();
            img.raycastTarget = true;
            img.color = new Color(1f, 1f, 1f, 1f);
            Sprite buttonSprite = GetImageSprite(GetGameObjectField(menuObj, "buttonAlphaOk"));
            if (buttonSprite == null)
                buttonSprite = GetImageSprite(GetGameObjectField(menuObj, "panelSaveDataPreview"));
            if (buttonSprite != null)
            {
                img.sprite = buttonSprite;
                img.type = Image.Type.Sliced;
                img.pixelsPerUnitMultiplier = 1f;
            }

            Button btn = _modeButton.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(OnModeButtonClicked);

            GameObject textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(_modeButton.transform, false);
            RectTransform trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(2f, 0f);
            trt.offsetMax = new Vector2(-2f, 0f);
            trt.localScale = Vector3.one;
            trt.localRotation = Quaternion.identity;

            _modeButtonText = textGo.GetComponent<TextMeshProUGUI>();
            TMP_Text txtPreview = GetObjectField(menuObj, "txtPreview") as TMP_Text;
            if (txtPreview != null)
            {
                _modeButtonText.font = txtPreview.font;
                _modeButtonText.fontMaterial = txtPreview.fontMaterial;
                _modeButtonText.fontSharedMaterial = txtPreview.fontSharedMaterial;
            }
            _modeButtonText.alignment = TextAlignmentOptions.Center;
            _modeButtonText.enableWordWrapping = false;
            _modeButtonText.overflowMode = TextOverflowModes.Overflow;
            _modeButtonText.fontSize = Cfg(ButtonFontSize, 5.5f);
            _modeButtonText.raycastTarget = false;
            _modeButtonText.color = new Color(0.17f, 0.10f, 0.10f, 1f);

            Log("Native switch button created under " + parent.name + "; local=" + brt.localPosition + "; anchored=" + brt.anchoredPosition + "; size=" + brt.sizeDelta + "; sprite=" + (buttonSprite != null ? buttonSprite.name : "none") + "; fontCopied=" + (txtPreview != null) + ".");
        }

        private static void CreateDeleteButton(object menuObj)
        {
            if (_deleteButton != null)
                return;

            Transform parent = _root != null ? _root.transform : _visibleSaveSlotParent;
            if (parent == null)
                return;

            _deleteButton = new GameObject("MMOnsterpatchOfficialServer_DeleteSaveButton_NATIVE", typeof(RectTransform), typeof(Image), typeof(Button), typeof(OfficialServerSaveSelectDeleteRelay));
            _deleteButton.transform.SetParent(parent, false);
            _deleteButton.transform.SetAsLastSibling();
            _deleteButton.SetActive(true);

            RectTransform brt = _deleteButton.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0.5f, 0.5f);
            brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.pivot = new Vector2(0.5f, 0.5f);
            brt.localScale = Vector3.one;
            brt.localRotation = Quaternion.identity;
            brt.sizeDelta = new Vector2(Cfg(DeleteButtonWidth, 72f), Cfg(DeleteButtonHeight, 18f));
            // Centered under the native 2x3 save-slot grid. The save-slot Panel lives at about x=-74,y=0 in menuSaveSelect space.
            brt.anchoredPosition = new Vector2(Cfg(DeleteButtonX, -74f), Cfg(DeleteButtonY, -76f));
            brt.localPosition = new Vector3(brt.localPosition.x, brt.localPosition.y, 0f);

            Image img = _deleteButton.GetComponent<Image>();
            img.raycastTarget = true;
            img.color = new Color(1f, 1f, 1f, 1f);
            Sprite buttonSprite = GetImageSprite(GetGameObjectField(menuObj, "buttonAlphaOk"));
            if (buttonSprite == null)
                buttonSprite = GetImageSprite(GetGameObjectField(menuObj, "panelSaveDataPreview"));
            if (buttonSprite != null)
            {
                img.sprite = buttonSprite;
                img.type = Image.Type.Sliced;
                img.pixelsPerUnitMultiplier = 1f;
            }

            Button btn = _deleteButton.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(OnDeleteButtonClicked);

            GameObject textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(_deleteButton.transform, false);
            RectTransform trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(2f, 0f);
            trt.offsetMax = new Vector2(-2f, 0f);
            trt.localScale = Vector3.one;
            trt.localRotation = Quaternion.identity;

            _deleteButtonText = textGo.GetComponent<TextMeshProUGUI>();
            TMP_Text txtPreview = GetObjectField(menuObj, "txtPreview") as TMP_Text;
            if (txtPreview != null)
            {
                _deleteButtonText.font = txtPreview.font;
                _deleteButtonText.fontMaterial = txtPreview.fontMaterial;
                _deleteButtonText.fontSharedMaterial = txtPreview.fontSharedMaterial;
            }
            _deleteButtonText.alignment = TextAlignmentOptions.Center;
            _deleteButtonText.enableWordWrapping = false;
            _deleteButtonText.overflowMode = TextOverflowModes.Overflow;
            _deleteButtonText.fontSize = Cfg(DeleteButtonFontSize, 5.5f);
            _deleteButtonText.raycastTarget = false;
            _deleteButtonText.color = new Color(0.17f, 0.10f, 0.10f, 1f);

            Log("Native Delete Save button created under " + parent.name + "; local=" + brt.localPosition + "; anchored=" + brt.anchoredPosition + "; size=" + brt.sizeDelta + "; sprite=" + (buttonSprite != null ? buttonSprite.name : "none") + "; fontCopied=" + (txtPreview != null) + ".");
        }

        private static void EnsureConfig()
        {
            try
            {
                if (_config != null)
                    return;

                string configPath = Path.Combine(Paths.ConfigPath, ConfigFileName);
                _config = new ConfigFile(configPath, true);

                ButtonX = _config.Bind("Save Select Online Button", "ButtonX", 44f, "Native menuSaveSelect X position for the Switch to Online/Offline Mode button.");
                ButtonY = _config.Bind("Save Select Online Button", "ButtonY", -76f, "Native menuSaveSelect Y position for the Switch to Online/Offline Mode button.");
                ButtonWidth = _config.Bind("Save Select Online Button", "ButtonWidth", 110f, "Width of the Switch to Online/Offline Mode button.");
                ButtonHeight = _config.Bind("Save Select Online Button", "ButtonHeight", 18f, "Height of the Switch to Online/Offline Mode button.");
                ButtonFontSize = _config.Bind("Save Select Online Button", "ButtonFontSize", 5.5f, "Font size for the Switch to Online/Offline Mode button text.");

                DeleteButtonX = _config.Bind("Save Select Delete Button", "DeleteButtonX", -74f, "Native menuSaveSelect X position for the Delete/Cancel button centered under the save-slot grid.");
                DeleteButtonY = _config.Bind("Save Select Delete Button", "DeleteButtonY", -76f, "Native menuSaveSelect Y position for the Delete/Cancel button centered under the save-slot grid.");
                DeleteButtonWidth = _config.Bind("Save Select Delete Button", "DeleteButtonWidth", 72f, "Width of the Delete/Cancel button.");
                DeleteButtonHeight = _config.Bind("Save Select Delete Button", "DeleteButtonHeight", 18f, "Height of the Delete/Cancel button.");
                DeleteButtonFontSize = _config.Bind("Save Select Delete Button", "DeleteButtonFontSize", 5.5f, "Font size for the Delete/Cancel button text.");
                DeleteButtonCursorX = _config.Bind("Save Select Delete Button", "DeleteButtonCursorX", -42f, "Local X position for the native arrow cursor while it is parented to the Delete/Cancel button. More negative moves the cursor left; less negative moves it closer to the button.");
                DeleteButtonCursorY = _config.Bind("Save Select Delete Button", "DeleteButtonCursorY", 0f, "Local Y position for the native arrow cursor while it is parented to the Delete/Cancel button.");

                StatusX = _config.Bind("Save Select Server Status", "StatusX", -150f, "Native menuSaveSelect X position for the Server Status text.");
                StatusY = _config.Bind("Save Select Server Status", "StatusY", 82f, "Native menuSaveSelect Y position for the Server Status text.");
                StatusWidth = _config.Bind("Save Select Server Status", "StatusWidth", 120f, "Width of the Server Status text area.");
                StatusHeight = _config.Bind("Save Select Server Status", "StatusHeight", 18f, "Height of the Server Status text area.");
                StatusFontSize = _config.Bind("Save Select Server Status", "StatusFontSize", 4f, "Font size for the Server Status text.");

                ServerHost = _config.Bind("Official Server Network", "ServerHost", "mmo.gamingwithgoose.com", "Official Server social/save TCP host. This should match the Social server host.");
                SocialPort = _config.Bind("Official Server Network", "SocialPort", 61529, "Official Server social/save TCP port. This should match the Social server port.");
                NetworkTimeoutMs = _config.Bind("Official Server Network", "NetworkTimeoutMs", 5000, "Timeout in milliseconds for save-select online slot requests and server save uploads.");
                AsyncOfficialSaveUploads = _config.Bind("Official Server Network", "AsyncOfficialSaveUploads", true, "When true, normal online autosaves queue the server upload on a background thread so the game does not freeze on every SaveSystem.SaveGame. Final disconnect/quit still waits briefly for the latest queued save.");
                FinalSaveFlushTimeoutMs = _config.Bind("Official Server Network", "FinalSaveFlushTimeoutMs", 3500, "Maximum milliseconds to wait for the latest queued online save when disconnecting/returning to title/quitting. WoW-like mode prefers a reliable final save over instantly dropping the session.");
                LazyLoadOnlineSaveSlotPayloads = _config.Bind("Official Server Network", "LazyLoadOnlineSaveSlotPayloads", true, "When true, title Log In fetches only lightweight online save-slot metadata. The full save payload is fetched only for the selected slot, reducing the title Log In freeze.");
                OnlineSaveCacheEnabled = _config.Bind("Official Server Network", "OnlineSaveCacheEnabled", true, "Keep using the local launcher/game cache for fast title previews and hover materialization. This cache is not the online source of truth.");
                LauncherUploadsOnlineSaveCache = _config.Bind("Official Server Network", "LauncherUploadsOnlineSaveCache", false, "When false, the game client streams online saves directly to the Official Server in the background, WoW-style. When true, saves are staged for launcher cleanup upload after exit.");
                SkipFinalDisconnectSaveIfLastUploadWithinSeconds = _config.Bind("Official Server Network", "SkipFinalDisconnectSaveIfLastUploadWithinSeconds", 0f, "Set 0 for WoW-like behavior: always request a final save on disconnect/quit. A positive value skips only after a recent successful server upload.");
                WorldRatesRefreshMinIntervalSeconds = _config.Bind("Official Server Network", "WorldRatesRefreshMinIntervalSeconds", 30f, "Minimum seconds between synchronous Official Server world-rate refreshes. Prevents duplicate network stalls when title login already refreshed rates before slot load.");

                OnlineWallpaperTintEnabled = _config.Bind("Save Select Online Background", "OnlineWallpaperTintEnabled", true, "When true, the embedded Online Mode save-select wallpaper is swapped onto the native save-menu background while Online Mode is active and restored when Offline Mode is active.");
                OnlineWallpaperTintColor = _config.Bind("Save Select Online Background", "OnlineWallpaperTintColor", "#E886B8", "Legacy setting retained for compatibility. The embedded Online Mode wallpaper is now used instead of a tint swap.");
                OnlineWallpaperMinWidth = _config.Bind("Save Select Online Background", "OnlineWallpaperMinWidth", 180f, "Minimum RectTransform width for an Image to be considered the native save-select wallpaper/background target for Online Mode wallpaper swapping.");
                OnlineWallpaperMinHeight = _config.Bind("Save Select Online Background", "OnlineWallpaperMinHeight", 95f, "Minimum RectTransform height for an Image to be considered the native save-select wallpaper/background target for Online Mode wallpaper swapping.");

                OnlineTitleBackgroundEnabled = _config.Bind("Launcher Title Cosmetics", "OnlineTitleBackgroundEnabled", true, "When true, launcher Online Mode swaps the native title-screen MainTitleRotation.mainBG sprite to embedded Source/Assets/OnlinelegendaryBG.png. This does not affect OnlinemenuBG.png/save-select wallpaper.");
                OnlineTitleLegendaryMon0Enabled = _config.Bind("Launcher Title Cosmetics", "LegendaryMon0Enabled", false, "Legacy/unused in v0.11.3 calibration. Launcher Online Mode now hides the native legendaryMons 1_0 title image entirely.");
                OnlineTitleLegendaryMon0OffsetX = _config.Bind("Launcher Title Cosmetics", "LegendaryMon0OffsetX", -100f, "Local X offset added to the existing title position for legendaryMons 1_0.");
                OnlineTitleLegendaryMon0OffsetY = _config.Bind("Launcher Title Cosmetics", "LegendaryMon0OffsetY", 0f, "Local Y offset added to the existing title position for legendaryMons 1_0.");
                OnlineTitleLegendaryMon0Scale = _config.Bind("Launcher Title Cosmetics", "LegendaryMon0Scale", 1f, "Scale multiplier for legendaryMons 1_0 on the launcher Online Mode title screen.");
                OnlineTitleLegendaryMon0Alpha = _config.Bind("Launcher Title Cosmetics", "LegendaryMon0Alpha", 1f, "Alpha/transparency for legendaryMons 1_0 on the launcher Online Mode title screen. 1 is fully visible, 0 is hidden.");
                OnlineTitleLegendaryMon1Enabled = _config.Bind("Launcher Title Cosmetics", "LegendaryMon1Enabled", false, "Legacy/unused in v0.11.3 calibration. Launcher Online Mode no longer adds legendaryMons 1_1.");
                OnlineTitleLegendaryMon1OffsetX = _config.Bind("Launcher Title Cosmetics", "LegendaryMon1OffsetX", 180f, "Local X offset from the existing legendary title image position for legendaryMons 1_1.");
                OnlineTitleLegendaryMon1OffsetY = _config.Bind("Launcher Title Cosmetics", "LegendaryMon1OffsetY", 0f, "Local Y offset from the existing legendary title image position for legendaryMons 1_1.");
                OnlineTitleLegendaryMon1Scale = _config.Bind("Launcher Title Cosmetics", "LegendaryMon1Scale", 1f, "Scale multiplier for legendaryMons 1_1 on the launcher Online Mode title screen.");
                OnlineTitleLegendaryMon1Alpha = _config.Bind("Launcher Title Cosmetics", "LegendaryMon1Alpha", 1f, "Alpha/transparency for legendaryMons 1_1 on the launcher Online Mode title screen. 1 is fully visible, 0 is hidden.");

                SaveSelectPvpPreviewOffsetX = _config.Bind("Save Select PVP Preview", "PvpPreviewOffsetX", -15f, "X offset for the PVP preview text in the right-side online save-select preview panel. Positive moves right; negative moves left.");
                SaveSelectPvpPreviewOffsetY = _config.Bind("Save Select PVP Preview", "PvpPreviewOffsetY", 5f, "Y offset for the PVP preview text in the right-side online save-select preview panel. Positive moves up; negative moves down.");
                SaveSelectPvpPreviewWidth = _config.Bind("Save Select PVP Preview", "PvpPreviewWidth", 150f, "Width of the PVP preview text area in the right-side online save-select preview panel.");
                SaveSelectPvpPreviewHeight = _config.Bind("Save Select PVP Preview", "PvpPreviewHeight", 86f, "Height of the PVP preview text area in the right-side online save-select preview panel.");
                SaveSelectPvpPreviewFontSize = _config.Bind("Save Select PVP Preview", "PvpPreviewFontSize", 7f, "Font size for the PVP preview text in the right-side online save-select preview panel.");

                Log("Official Server save-select config loaded from " + configPath + ".");
            }
            catch (Exception ex)
            {
                Log("Official Server save-select config load failed; using built-in defaults. " + ex.Message);
            }
        }

        private static float Cfg(ConfigEntry<float> entry, float fallback)
        {
            try { return entry != null ? entry.Value : fallback; } catch { return fallback; }
        }

        private static Sprite GetImageSprite(GameObject go)
        {
            if (go == null)
                return null;
            Image img = go.GetComponent<Image>();
            return img != null ? img.sprite : null;
        }

        private static void CopyRectBasis(RectTransform from, RectTransform to)
        {
            if (from == null || to == null)
                return;
            to.anchorMin = from.anchorMin;
            to.anchorMax = from.anchorMax;
            to.pivot = from.pivot;
            to.sizeDelta = from.sizeDelta;
            to.localRotation = from.localRotation;
            to.localScale = from.localScale;
        }

        private static void ForceVisibleRecursive(GameObject go)
        {
            if (go == null)
                return;
            go.SetActive(true);
            foreach (Transform child in go.GetComponentsInChildren<Transform>(true))
            {
                if (child != null)
                    child.gameObject.SetActive(true);
            }
            foreach (CanvasGroup cg in go.GetComponentsInChildren<CanvasGroup>(true))
            {
                cg.alpha = 1f;
                cg.interactable = true;
                // The MMOnsterpatch root is only a visual/host container. It must never sit on top
                // of native save-slot buttons as a raycast blocker.
                cg.blocksRaycasts = cg.gameObject != go;
            }
            foreach (Graphic g in go.GetComponentsInChildren<Graphic>(true))
            {
                Color c = g.color;
                if (c.a < 0.95f)
                    g.color = new Color(c.r, c.g, c.b, 1f);
                g.raycastTarget = g is Image;
            }
        }

        private static void LogNativeLayout(object menuObj, GameObject slot0, GameObject slot5, GameObject preview, Transform parent)
        {
            if (_layoutLogged)
                return;
            _layoutLogged = true;
            try
            {
                Log("Native layout anchor parent=" + (parent != null ? parent.name : "null") + "; parentType=" + (parent != null ? parent.GetType().Name : "null") + "; parentLocal=" + (parent != null ? parent.localPosition.ToString() : "null") + "; parentScale=" + (parent != null ? parent.localScale.ToString() : "null") + ".");
                LogObjectLayout("slot0/saveFileObj[0]", slot0);
                LogObjectLayout("slot5/saveFileObj[5]", slot5);
                LogObjectLayout("panelSaveDataPreview", preview);
                TMP_Text txtPreview = GetObjectField(menuObj, "txtPreview") as TMP_Text;
                if (txtPreview != null)
                    LogObjectLayout("txtPreview", txtPreview.gameObject);
            }
            catch (Exception ex)
            {
                Log("Native layout logging failed: " + ex.Message);
            }
        }

        private static void LogObjectLayout(string label, GameObject go)
        {
            if (go == null)
            {
                Log(label + "=null");
                return;
            }
            RectTransform rt = go.GetComponent<RectTransform>();
            if (rt != null)
                Log(label + " name=" + go.name + "; activeSelf=" + go.activeSelf + "; activeHierarchy=" + go.activeInHierarchy + "; parent=" + (go.transform.parent != null ? go.transform.parent.name : "null") + "; local=" + rt.localPosition + "; anchored=" + rt.anchoredPosition + "; size=" + rt.sizeDelta + "; rect=" + rt.rect + "; scale=" + rt.localScale + ".");
            else
                Log(label + " name=" + go.name + "; activeSelf=" + go.activeSelf + "; activeHierarchy=" + go.activeInHierarchy + "; parent=" + (go.transform.parent != null ? go.transform.parent.name : "null") + "; local=" + go.transform.localPosition + "; scale=" + go.transform.localScale + ".");
        }

        private static void UpdateVisualState()
        {
            ApplyConfiguredLayout();

            if (_onlineMode)
                ApplyOnlineSaveSlotVisuals();
            else
                RestoreOnlinePreviewPlayerSprites(_menuObj);

            if (_statusText != null)
            {
                _statusText.text = !string.IsNullOrEmpty(_statusOverride) ? _statusOverride : (_onlineMode ? "Server Status: Connected" : "Server Status: Disconnected");
                bool warning = !string.IsNullOrEmpty(_statusOverride) && _statusOverride.IndexOf("Failed", StringComparison.OrdinalIgnoreCase) >= 0;
                _statusText.color = (_onlineMode && !warning) ? new Color(0.22f, 0.42f, 0.24f, 1f) : new Color(0.45f, 0.10f, 0.10f, 1f);
            }

            _deleteMode = IsNativeDeleteModeActive(_menuObj);
            if (_modeButton != null)
                _modeButton.SetActive(false);
            if (_deleteButton != null)
                _deleteButton.SetActive(false);

            bool showDeleteButton = _onlineMode && !_authBusy;
            if (_deleteButton != null && _deleteButton.activeSelf != showDeleteButton)
                _deleteButton.SetActive(showDeleteButton);
            if (!showDeleteButton && _deleteMode)
                SetDeleteMode(false, "left Online Mode");
            if (_deleteButtonText != null)
                _deleteButtonText.text = _deleteMode ? "Cancel" : "Delete";

            ApplyModeButtonNavigation();
            ApplyDeleteButtonNavigation();
            if (showDeleteButton && EventSystem.current != null && EventSystem.current.currentSelectedGameObject == _deleteButton)
                PositionDeleteButtonCursor();
            ApplyOnlineWallpaperTheme();
        }

        private static void ApplyOnlineWallpaperTheme()
        {
            try
            {
                EnsureConfig();
                bool enabled = OnlineWallpaperTintEnabled == null || OnlineWallpaperTintEnabled.Value;
                if (!_onlineMode || !enabled)
                {
                    RestoreOnlineWallpaperColors();
                    return;
                }

                Image target = FindBestSaveSelectBackgroundImage();
                if (target == null)
                    return;

                if (_onlineWallpaperTargetImage != target)
                {
                    RestoreOnlineWallpaperColors();
                    _onlineWallpaperTargetImage = target;
                    _originalSaveSelectBackgroundSprite = target.sprite;
                    _originalSaveSelectBackgroundColor = target.color;
                    _onlineWallpaperTargetPath = GetTransformPath(target.transform);
                }

                Sprite onlineSprite = GetEmbeddedOnlineWallpaperSprite();
                if (onlineSprite == null)
                    return;

                if (_onlineWallpaperTargetImage.sprite != onlineSprite)
                    _onlineWallpaperTargetImage.sprite = onlineSprite;

                float alpha = _originalSaveSelectBackgroundColor.a > 0f ? _originalSaveSelectBackgroundColor.a : 1f;
                _onlineWallpaperTargetImage.color = new Color(1f, 1f, 1f, alpha);

                if (!_onlineWallpaperSwapLogged)
                {
                    _onlineWallpaperSwapLogged = true;
                    Log("Online Mode save-select wallpaper swap applied to native Image path='" + (_onlineWallpaperTargetPath ?? "unknown") + "' using embedded resource OnlinemenuBG.png. Offline Mode restores the original native sprite.");
                }
            }
            catch (Exception ex)
            {
                Log("ApplyOnlineWallpaperTheme failed: " + ex.Message);
            }
        }

        private static void RestoreOnlineWallpaperColors()
        {
            try
            {
                if (_onlineWallpaperTargetImage != null)
                {
                    _onlineWallpaperTargetImage.sprite = _originalSaveSelectBackgroundSprite;
                    _onlineWallpaperTargetImage.color = _originalSaveSelectBackgroundColor;
                }
            }
            catch { }
            finally
            {
                _onlineWallpaperTargetImage = null;
                _originalSaveSelectBackgroundSprite = null;
                _originalSaveSelectBackgroundColor = Color.white;
                _onlineWallpaperTargetPath = null;
                _onlineWallpaperSwapLogged = false;
            }
        }

        private static Image FindBestSaveSelectBackgroundImage()
        {
            Image best = null;
            float bestScore = float.MinValue;
            foreach (Image img in EnumerateSaveSelectBackgroundImages())
            {
                if (img == null)
                    continue;
                float score = ScoreSaveSelectBackgroundImage(img);
                if (best == null || score > bestScore)
                {
                    best = img;
                    bestScore = score;
                }
            }
            return best;
        }

        private static float ScoreSaveSelectBackgroundImage(Image img)
        {
            if (img == null)
                return float.MinValue;

            string path = GetTransformPath(img.transform).ToLowerInvariant();
            string spriteName = img.sprite != null ? (img.sprite.name ?? string.Empty).ToLowerInvariant() : string.Empty;

            float score = 0f;
            if (spriteName.Contains("menubg")) score += 1000f;
            if (spriteName.Contains("menu")) score += 250f;
            if (spriteName.Contains("bg")) score += 150f;
            if (path.Contains("background")) score += 400f;
            if (path.Contains("wallpaper")) score += 350f;
            if (path.Contains("back")) score += 150f;
            if (path.Contains("menu")) score += 75f;
            if (img.gameObject.activeInHierarchy) score += 10f;

            RectTransform rt = img.GetComponent<RectTransform>();
            if (rt != null)
                score += Mathf.Abs(rt.rect.width * rt.rect.height) / 100f;

            return score;
        }

        private static bool LoadPngIntoTexture(Texture2D texture, byte[] bytes)
        {
            try
            {
                if (texture == null || bytes == null || bytes.Length == 0)
                    return false;

                Type imageConversionType = null;
                try
                {
                    imageConversionType = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
                }
                catch { imageConversionType = null; }

                if (imageConversionType == null)
                {
                    foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        try
                        {
                            imageConversionType = asm.GetType("UnityEngine.ImageConversion", false);
                            if (imageConversionType != null)
                                break;
                        }
                        catch { }
                    }
                }

                if (imageConversionType == null)
                {
                    Log("UnityEngine.ImageConversion was not available, so the embedded Online Mode wallpaper PNG could not be decoded.");
                    return false;
                }

                MethodInfo loadImage = imageConversionType.GetMethod("LoadImage", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(Texture2D), typeof(byte[]) }, null);
                if (loadImage != null)
                    return (bool)loadImage.Invoke(null, new object[] { texture, bytes });

                loadImage = imageConversionType.GetMethod("LoadImage", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(Texture2D), typeof(byte[]), typeof(bool) }, null);
                if (loadImage != null)
                    return (bool)loadImage.Invoke(null, new object[] { texture, bytes, false });

                Log("UnityEngine.ImageConversion.LoadImage overload was not found, so the embedded Online Mode wallpaper PNG could not be decoded.");
                return false;
            }
            catch (Exception ex)
            {
                Log("LoadPngIntoTexture failed: " + ex.Message);
                return false;
            }
        }

        private static Sprite GetEmbeddedOnlineWallpaperSprite()
        {
            try
            {
                if (_embeddedOnlineWallpaperSprite != null)
                    return _embeddedOnlineWallpaperSprite;

                Assembly asm = typeof(OfficialServerSaveSelectNativeRuntime).Assembly;
                string resourceName = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("OnlinemenuBG.png", StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrEmpty(resourceName))
                {
                    Log("Embedded Online Mode wallpaper resource OnlinemenuBG.png was not found in the patcher assembly.");
                    return null;
                }

                using (Stream stream = asm.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        Log("Embedded Online Mode wallpaper resource stream could not be opened: " + resourceName);
                        return null;
                    }

                    byte[] bytes = new byte[stream.Length];
                    int read = stream.Read(bytes, 0, bytes.Length);
                    if (read <= 0)
                    {
                        Log("Embedded Online Mode wallpaper resource was empty: " + resourceName);
                        return null;
                    }

                    Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    tex.name = "MMOnsterpatchOfficialServerOnlineMenuBG";
                    tex.filterMode = FilterMode.Point;
                    tex.wrapMode = TextureWrapMode.Clamp;
                    if (!LoadPngIntoTexture(tex, bytes))
                    {
                        UnityEngine.Object.Destroy(tex);
                        Log("Embedded Online Mode wallpaper PNG failed to load from assembly resource: " + resourceName);
                        return null;
                    }

                    _embeddedOnlineWallpaperTexture = tex;
                    _embeddedOnlineWallpaperSprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                    _embeddedOnlineWallpaperSprite.name = "MMOnsterpatchOfficialServerOnlineMenuBG";
                    Log("Loaded embedded Online Mode save-select wallpaper from assembly resource " + resourceName + " (" + tex.width + "x" + tex.height + ").");
                    return _embeddedOnlineWallpaperSprite;
                }
            }
            catch (Exception ex)
            {
                Log("GetEmbeddedOnlineWallpaperSprite failed: " + ex.Message);
                return null;
            }
        }

        private static IEnumerable<Image> EnumerateSaveSelectBackgroundImages()
        {
            HashSet<Image> yielded = new HashSet<Image>();
            foreach (GameObject root in EnumerateSaveSelectRoots())
            {
                if (root == null)
                    continue;
                foreach (Image img in root.GetComponentsInChildren<Image>(true))
                {
                    if (img == null || yielded.Contains(img))
                        continue;
                    if (!IsSaveSelectWallpaperCandidate(img))
                        continue;
                    yielded.Add(img);
                    yield return img;
                }
            }
        }

        private static IEnumerable<GameObject> EnumerateSaveSelectRoots()
        {
            GameObject menuSaveSelect = GetGameObjectField(_menuObj, "menuSaveSelect");
            GameObject menuSaveFiles = GetGameObjectField(_menuObj, "menuSaveFiles");
            if (menuSaveSelect != null) yield return menuSaveSelect;
            if (menuSaveFiles != null && menuSaveFiles != menuSaveSelect) yield return menuSaveFiles;
        }

        private static bool IsSaveSelectWallpaperCandidate(Image img)
        {
            if (img == null || img.gameObject == null)
                return false;
            if (_root != null && img.transform.IsChildOf(_root.transform))
                return false;
            string path = GetTransformPath(img.transform).ToLowerInvariant();
            if (path.Contains("savefile") || path.Contains("saveplus") || path.Contains("playerpreview") || path.Contains("preview") || path.Contains("button") || path.Contains("version") || path.Contains("arrow") || path.Contains("text"))
                return false;
            if (img.GetComponent<Button>() != null || img.GetComponent<Selectable>() != null)
                return false;
            RectTransform rt = img.GetComponent<RectTransform>();
            if (rt == null)
                return false;
            float w = Mathf.Abs(rt.rect.width);
            float h = Mathf.Abs(rt.rect.height);
            bool bigEnough = w >= Cfg(OnlineWallpaperMinWidth, 180f) && h >= Cfg(OnlineWallpaperMinHeight, 95f);
            bool namedLikeBackground = path.Contains("bg") || path.Contains("back") || path.Contains("wall") || path.Contains("paper") || path.Contains("menu") || path.Contains("background");
            bool greenTinted = img.color.g > img.color.r + 0.05f && img.color.g > img.color.b + 0.05f;
            bool hasSprite = img.sprite != null;
            return bigEnough && (namedLikeBackground || greenTinted || hasSprite);
        }

        private static string GetTransformPath(Transform t)
        {
            try
            {
                if (t == null)
                    return string.Empty;
                List<string> parts = new List<string>();
                Transform cur = t;
                while (cur != null)
                {
                    parts.Add(cur.name ?? string.Empty);
                    cur = cur.parent;
                }
                parts.Reverse();
                return string.Join("/", parts.ToArray());
            }
            catch { return string.Empty; }
        }

        private static void ApplyConfiguredLayout()
        {
            EnsureConfig();

            try
            {
                if (_statusText != null)
                {
                    RectTransform rt = _statusText.GetComponent<RectTransform>();
                    if (rt != null)
                    {
                        rt.sizeDelta = new Vector2(Cfg(StatusWidth, 120f), Cfg(StatusHeight, 18f));
                        rt.anchoredPosition = new Vector2(Cfg(StatusX, -150f), Cfg(StatusY, 82f));
                    }
                    _statusText.fontSize = Cfg(StatusFontSize, 4f);
                }

                if (_modeButton != null)
                {
                    RectTransform brt = _modeButton.GetComponent<RectTransform>();
                    if (brt != null)
                    {
                        brt.sizeDelta = new Vector2(Cfg(ButtonWidth, 110f), Cfg(ButtonHeight, 18f));
                        brt.anchoredPosition = new Vector2(Cfg(ButtonX, 44f), Cfg(ButtonY, -76f));
                    }
                }

                if (_modeButtonText != null)
                    _modeButtonText.fontSize = Cfg(ButtonFontSize, 5.5f);

                if (_deleteButton != null)
                {
                    RectTransform drt = _deleteButton.GetComponent<RectTransform>();
                    if (drt != null)
                    {
                        drt.sizeDelta = new Vector2(Cfg(DeleteButtonWidth, 72f), Cfg(DeleteButtonHeight, 18f));
                        drt.anchoredPosition = new Vector2(Cfg(DeleteButtonX, -74f), Cfg(DeleteButtonY, -76f));
                    }
                }

                if (_deleteButtonText != null)
                    _deleteButtonText.fontSize = Cfg(DeleteButtonFontSize, 5.5f);
            }
            catch (Exception ex)
            {
                Log("ApplyConfiguredLayout failed: " + ex.Message);
            }
        }

        public static bool TryHandleSelectSaveFile(object menuObj, int slot)
        {
            try
            {
                _menuObj = menuObj;
                int safeSlot = Mathf.Clamp(slot, 0, 5);
                if (!_onlineMode && IsLauncherOnlineSaveSelectReturnContext(safeSlot))
                {
                    _onlineMode = true;
                    _authBusy = false;
                    _statusOverride = null;
                    ApplyOnlineSaveSlotCacheToMenuData();
                    Log("Launcher online save-select state re-armed before selecting slot" + safeSlot + " after returning from version select.");
                }

                if (!_onlineMode)
                    return false;

                if (_authBusy)
                {
                    Log("Online save slot " + slot + " ignored because authentication is still in progress.");
                    return true;
                }

                if (IsLauncherSessionActive())
                {
                    if (TryRunLauncherOnlineSaveSlotFlow(menuObj, safeSlot))
                        return true;
                    Log("Launcher online save slot " + safeSlot + " direct flow failed; falling back to vanilla SelectSaveFile.");
                }

                if (IsNativeDeleteModeActive(menuObj))
                {
                    SetObjectField(menuObj, "curSaveSlot", safeSlot);
                    SetObjectField(menuObj, "lastButtonObj", GetGameObjectFromArrayField(menuObj, "saveFileObj", safeSlot));
                    ApplyOnlineSaveSlotVisuals();
                    UpdateVisualState();
                    Log("Online save slot " + safeSlot + " selected while Delete Save mode is active. Native confirmation dialogue is allowed; the confirmed delete is redirected to the Official Server.");
                    return false;
                }

                // Empty online slots intentionally use the game's native character-creation flow.
                // The moment an online slot is selected, arm a gameplay-session guard that survives the
                // title-to-game scene transition. This is critical for new-character creation, because the
                // first GameScript.SaveGame can happen after MenuScript is gone; the guard still redirects
                // SaveSystem.SaveGame to the Official Server and blocks local disk writes.
                SetObjectField(menuObj, "curSaveSlot", safeSlot);
                SetObjectField(menuObj, "lastButtonObj", GetGameObjectFromArrayField(menuObj, "saveFileObj", safeSlot));
                if (!EnsureOnlineSaveSlotPayloadLoaded(safeSlot))
                {
                    Log("Online save slot " + safeSlot + " selection blocked because the server payload could not be loaded.");
                    return true;
                }
                object existing = GetSaveDataForSlot(safeSlot);
                BeginOfficialOnlineSaveSession(safeSlot, existing == null ? "creating online save slot" : "loading online save slot");
                _statusOverride = existing == null ? "Server Status: Creating Online Slot" : null;
                ApplyOnlineSaveSlotVisuals();
                UpdateVisualState();
                Log((existing == null ? "Online empty save slot " : "Online occupied save slot ") + safeSlot + " selected. Native save flow is allowed; SaveSystem writes are redirected to the server while Official Online Save Session guard is active.");
                return false;
            }
            catch (Exception ex)
            {
                Log("TryHandleSelectSaveFile failed: " + ex.Message);
                return true;
            }
        }

        private static bool TryRunLauncherOnlineSaveSlotFlow(object menuObj, int safeSlot)
        {
            try
            {
                if (menuObj == null || !_onlineMode || _authBusy)
                    return false;

                GameObject slotObj = GetBestSaveSlotSelectable(menuObj, safeSlot);
                SetObjectField(menuObj, "curSaveSlot", safeSlot);
                if (slotObj != null)
                    SetObjectField(menuObj, "lastButtonObj", slotObj);

                if (IsNativeDeleteModeActive(menuObj))
                {
                    ApplyOnlineSaveSlotVisuals();
                    UpdateVisualState();
                    bool shown = TryInvokeMenuMethodWithString(menuObj, "ShowDialogue", "deleteSaveFile");
                    Log("Launcher online delete slot " + safeSlot + " selected; native delete confirmation " + (shown ? "opened" : "could not be opened") + ".");
                    return shown;
                }

                if (!EnsureOnlineSaveSlotPayloadLoaded(safeSlot))
                {
                    Log("Launcher online save slot " + safeSlot + " selection blocked because the server payload could not be loaded.");
                    return true;
                }
                object existing = GetSaveDataForSlot(safeSlot);
                bool empty = existing == null;
                BeginOfficialOnlineSaveSession(safeSlot, empty ? "creating launcher online save slot" : "loading launcher online save slot");
                _statusOverride = empty ? "Server Status: Creating Online Slot" : null;
                ApplyOnlineSaveSlotVisuals();
                UpdateVisualState();

                if (empty)
                    ArmLauncherOnlineVersionSelectReturnGuard(safeSlot);

                bool invoked = empty
                    ? TryStartMenuCoroutine(menuObj, "PickVersion")
                    : TryInvokeMenuMethodWithInt(menuObj, "PlaySaveFile", safeSlot);

                Log("Launcher online " + (empty ? "empty" : "occupied") + " save slot " + safeSlot + " selected; direct native " + (empty ? "character creation" : "load") + " flow " + (invoked ? "started" : "failed") + ".");
                return invoked;
            }
            catch (Exception ex)
            {
                Log("TryRunLauncherOnlineSaveSlotFlow failed: " + ex.Message);
                return false;
            }
        }

        public static bool TryHandleOnlineDeleteSaveAndBlockLocal(int slot)
        {
            try
            {
                if (!IsOfficialOnlineSaveProtectionActive())
                    return false;

                int safeSlot = (_officialOnlineSaveSessionActive && _officialOnlineSaveSessionSlot >= 0) ? Mathf.Clamp(_officialOnlineSaveSessionSlot, 0, 5) : Mathf.Clamp(slot, 0, 5);
                bool deleted = DeleteOnlineSaveSlotOnServer(safeSlot);
                if (deleted)
                {
                    SetSaveDataForSlot(safeSlot, null);
                    _onlineSaveSlotCache[safeSlot] = null;
                    _statusOverride = "Server Status: Save Deleted";
                    Log("Official MenuScript delete confirmed for Online Mode slot" + safeSlot + ". SaveSystem.DeleteSave was redirected to the server and the local disk delete was blocked.");
                }
                else
                {
                    _statusOverride = "Server Status: Delete Failed";
                    Log("Official MenuScript delete for Online Mode slot" + safeSlot + " failed. Local disk delete was blocked.");
                }

                // Let MenuScript.ActuallyDeleteSaveFile continue after this blocked disk call. The official
                // method will clear MenuScript.saveData[curSaveSlot], refresh the slot grid, hide preview,
                // play the click SFX, and restore menu state exactly like vanilla.
                return true;
            }
            catch (Exception ex)
            {
                Log("TryHandleOnlineDeleteSaveAndBlockLocal failed: " + ex.Message + ". Blocking local disk delete while Online Mode is active.");
                _statusOverride = "Server Status: Delete Failed";
                return true;
            }
        }

        public static void OnActuallyDeleteSaveFileCompleted(object menuObj)
        {
            // Intentionally unused. MenuScript.ActuallyDeleteSaveFile is no longer patched.
            // Online deletion is redirected at SaveSystem.DeleteSave so the official MenuScript
            // delete confirmation/cleanup flow remains untouched.
        }

        private static int GetCurrentSaveSlot(object menuObj)
        {
            try
            {
                object value = GetObjectField(menuObj, "curSaveSlot");
                if (value is int i)
                    return Mathf.Clamp(i, 0, 5);
            }
            catch { }
            return Mathf.Clamp(_lastHoverSlot, 0, 5);
        }

        private static void BeginOfficialOnlineSaveSession(int slot, string reason)
        {
            try
            {
                int safeSlot = Mathf.Clamp(slot, 0, 5);
                _officialOnlineSaveSessionActive = true;
                _officialOnlineSaveSessionSlot = safeSlot;
                _onlineMode = true;
                Log("Official Online Save Session armed for slot" + safeSlot + " (" + reason + "). Local SaveSystem writes are now server-redirected until return-to-title/disconnect.");
                RefreshOfficialWorldRatesFromServer();
            }
            catch { }
        }

        private static void ClearOfficialOnlineSaveSession(string reason)
        {
            try
            {
                if (_officialOnlineSaveSessionActive || _officialOnlineSaveSessionSlot >= 0)
                    Log("Official Online Save Session cleared from " + reason + ".");
            }
            catch { }
            _officialOnlineSaveSessionActive = false;
            _officialOnlineSaveSessionSlot = -1;
            OfficialServerWorldRatesRuntime.ResetToOfflineDefaults(reason);
        }

        private static bool IsOfficialOnlineSaveProtectionActive()
        {
            try { return _onlineMode || _officialOnlineSaveSessionActive; } catch { return false; }
        }


        private static bool DeleteOnlineSaveSlotOnServer(int slot)
        {
            try
            {
                string token = GTSRuntimeHost.GetAioSessionTokenForSocial();
                if (string.IsNullOrEmpty(token))
                {
                    Log("Official Server save delete failed for slot" + slot + ": launcher session token is empty.");
                    return false;
                }

                string line = SendOfficialServerRequest("OFFICIAL_SAVE_DELETE|" + B64(token) + "|" + Mathf.Clamp(slot, 0, 5).ToString(), "OFFICIAL_SAVE_DELETE_OK", "OFFICIAL_SAVE_ERROR");
                if (!string.IsNullOrEmpty(line) && line.StartsWith("OFFICIAL_SAVE_DELETE_OK|", StringComparison.OrdinalIgnoreCase))
                    return true;

                Log("Official Server save delete failed for slot" + slot + ": " + DecodeServerError(line));
                return false;
            }
            catch (Exception ex)
            {
                Log("DeleteOnlineSaveSlotOnServer failed: " + ex.Message);
                return false;
            }
        }

        public static bool ShouldHandleOnlineSaveAndBlockLocalWrite(object saveDataObj, int slot)
        {
            try
            {
                if (!IsOfficialOnlineSaveProtectionActive())
                    return false;

                int safeSlot = (_officialOnlineSaveSessionActive && _officialOnlineSaveSessionSlot >= 0) ? Mathf.Clamp(_officialOnlineSaveSessionSlot, 0, 5) : Mathf.Clamp(slot, 0, 5);
                // v0.15.6 cache-first save path: write the online cache immediately and let the launcher
                // upload dirty saves during Online Mode cleanup after the game exits. This removes the
                // disconnect/title-return freeze caused by waiting on TCP/MariaDB from the Unity thread.
                bool uploadedOrQueued = false;
                if (ShouldUseLauncherOnlineSaveCacheUpload())
                {
                    uploadedOrQueued = WriteOnlineSaveSlotCacheAndMarkDirty(saveDataObj, safeSlot);
                    if (uploadedOrQueued)
                    {
                        SanitizeSaveDataForMenuPreview(saveDataObj, "post-cache-write menu preview slot" + safeSlot);
                        SetSaveDataForSlot(safeSlot, saveDataObj);
                        _statusOverride = null;
                        _lastOfficialSaveUploadAt = Time.unscaledTime;
                        Log("Cached SaveSystem.SaveGame(slot" + safeSlot + ") to launcher online cache and marked it dirty for cleanup upload. Local vanilla disk write was blocked.");
                    }
                }
                else
                {
                    // Upload the game's raw SaveData first, before preview-only placeholder cleanup.
                    // Normal autosaves are queued to a background thread so the Unity main thread does
                    // not hang on TCP/MariaDB latency. Final disconnect/quit paths explicitly flush.
                    uploadedOrQueued = ShouldQueueAsyncOfficialSaveUploads()
                        ? QueueOnlineSaveSlotUploadToServer(saveDataObj, safeSlot)
                        : UploadOnlineSaveSlotToServer(saveDataObj, safeSlot);
                    if (uploadedOrQueued)
                    {
                        SanitizeSaveDataForMenuPreview(saveDataObj, "post-upload menu preview slot" + safeSlot);
                        SetSaveDataForSlot(safeSlot, saveDataObj);
                        _statusOverride = ShouldQueueAsyncOfficialSaveUploads() ? "Server Status: Saving..." : null;
                        _lastOfficialSaveUploadAt = Time.unscaledTime;
                        Log((ShouldQueueAsyncOfficialSaveUploads() ? "Queued" : "Redirected") + " SaveSystem.SaveGame(slot" + safeSlot + ") to Official Server and blocked local disk write.");
                    }
                }
                if (!uploadedOrQueued)
                {
                    _statusOverride = "Server Status: Save Failed";
                    Log("Blocked local SaveSystem.SaveGame(slot" + safeSlot + ") but server upload failed/was not queued. Local save file was not touched.");
                }

                if (!_localSaveBlockLogged)
                {
                    _localSaveBlockLogged = true;
                    Log("Official Server Online Mode is redirecting SaveSystem.SaveGame to the server. Local disk writes are blocked while online.");
                }
                return true;
            }
            catch (Exception ex)
            {
                Log("Online save redirect failed: " + ex.Message + ". Blocking local write to protect local saves.");
                return true;
            }
        }

        public static bool TryBlockOnlineHoverSaveFile(object menuObj, int slot)
        {
            try
            {
                _menuObj = menuObj;
                if (!_onlineMode)
                    return false;

                _lastHoverSlot = slot;

                // v0.15.7 SaveSlotHoverFix:
                // The v0.15.6 launcher cache intentionally made title Log In fast by first
                // loading metadata-only SaveData placeholders.  That fixed the hitch, but
                // vanilla HoverSaveFile then only had placeholder data, so the right preview
                // panel showed a generic icon / empty team instead of the real character.
                // Before vanilla hover renders the preview, materialize the full payload from
                // the local launcher-prefetched cache only.  This avoids a server round-trip
                // on hover while restoring native appearance/team preview details.
                int safeSlot = Mathf.Clamp(slot, 0, 5);
                if (_onlineSaveSlotOccupied[safeSlot] && !_onlineSaveSlotPayloadLoaded[safeSlot])
                {
                    if (ShouldUseOnlineSaveCache())
                    {
                        bool loadedFromCache = LoadOnlineSaveSlotPayloadFromCache(safeSlot);
                        if (loadedFromCache)
                            LogOncePerSlot(safeSlot, "Materialized online save slot " + safeSlot + " from launcher cache before hover preview.");
                    }
                }

                if (_lastOfficialSaveUploadAt <= 0f || Time.unscaledTime - _lastOfficialSaveUploadAt > 2.0f)
                    _statusOverride = null;
                ApplyOnlineSaveSlotVisuals();
                UpdateVisualState();
                if (!_onlineHoverBlockLogged)
                {
                    _onlineHoverBlockLogged = true;
                    Log("Online save-select hover now materializes launcher-cache payloads before vanilla preview rendering, so native appearance/team details stay intact.");
                }
                return false;
            }
            catch (Exception ex)
            {
                Log("TryBlockOnlineHoverSaveFile failed: " + ex.Message);
                return true;
            }
        }

        public static void OnMenuCancelInvoked(object menuObj)
        {
            try
            {
                _menuObj = menuObj;

                // Backing out of the native game-version picker while creating an online character
                // should return to the online save-slot screen, not disconnect from the server.
                if (IsLauncherSessionActive() && IsLauncherOnlineVersionSelectBackContext(menuObj))
                {
                    _onlineMode = true;
                    _statusOverride = null;
                    _launcherPreAuthBusy = false;
                    _authBusy = false;
                    _launcherOnlineVersionSelectReturnGuard = true;
                    _launcherOnlineVersionSelectReturnGuardUntil = Time.unscaledTime + 2.0f;
                    if (_officialOnlineSaveSessionSlot >= 0)
                        _launcherOnlineVersionSelectReturnSlot = Mathf.Clamp(_officialOnlineSaveSessionSlot, 0, 5);
                    Log("MenuScript.Cancel observed from launcher Online version select/create flow; keeping server session active and returning to online save slots.");
                    return;
                }

                if (_onlineMode || _authBusy || IsAnyServerSessionActive())
                    DisconnectServerSession("MenuScript.Cancel/title navigation");
            }
            catch (Exception ex)
            {
                Log("OnMenuCancelInvoked failed: " + ex.Message);
            }
        }

        public static void OnReturnToTitleInvoked()
        {
            try
            {
                if (_returnToTitleFromOfficialDisconnect)
                {
                    HideOfficialChatWindow();
                    return;
                }

                bool serverActive = _onlineMode || _officialOnlineSaveSessionActive || _authBusy || IsAnyServerSessionActive();
                try { if (OfficialServerWorldRatesRuntime.IsOnlineRatesActive) serverActive = true; } catch { }
                if (serverActive)
                    _officialReturnToTitleDialogueOverridePendingUntil = Time.unscaledTime + 10f;
                SetOfficialReturnToTitleDialogueOverride(serverActive || IsOfficialReturnToTitleDialogueOverridePending());
            }
            catch (Exception ex)
            {
                Log("OnReturnToTitleInvoked failed: " + ex.Message);
            }
        }

        public static void OnActuallyReturnToTitleInvoked()
        {
            try
            {
                if (_returnToTitleFromOfficialDisconnect)
                {
                    HideOfficialChatWindow();
                    return;
                }

                bool serverActive = _onlineMode || _officialOnlineSaveSessionActive || _authBusy || IsAnyServerSessionActive();
                try { if (OfficialServerWorldRatesRuntime.IsOnlineRatesActive) serverActive = true; } catch { }

                if (!serverActive)
                    return;

                if (IsLauncherSessionActive())
                {
                    BeginLauncherLogOutToSaveSelect("GameScript.ActuallyReturnToTitle");
                    return;
                }

                ForceSaveAndDisconnectOnly("GameScript.ActuallyReturnToTitle");
            }
            catch (Exception ex)
            {
                Log("OnActuallyReturnToTitleInvoked failed: " + ex.Message);
            }
        }

        public static void ApplyOfficialReturnToTitleDialogueIfNeeded(string dialogueName)
        {
            try
            {
                if (!string.Equals(dialogueName, "confirmExitToTitle", StringComparison.OrdinalIgnoreCase))
                    return;

                bool serverActive = _onlineMode || _officialOnlineSaveSessionActive || _authBusy || IsAnyServerSessionActive() || IsOfficialReturnToTitleDialogueOverridePending();
                try { if (OfficialServerWorldRatesRuntime.IsOnlineRatesActive) serverActive = true; } catch { }
                SetOfficialReturnToTitleDialogueOverride(serverActive);
            }
            catch (Exception ex)
            {
                Log("ApplyOfficialReturnToTitleDialogueIfNeeded failed: " + ex.Message);
            }
        }


        public static void ApplyOfficialReturnToTitleDialogueList(List<string> lines)
        {
            try
            {
                if (lines == null || lines.Count == 0)
                    return;

                bool looksLikeExitToTitle = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    string line = lines[i] ?? string.Empty;
                    if (line.IndexOf("Exit to Title Screen", StringComparison.OrdinalIgnoreCase) >= 0 && line.IndexOf("unsaved progress", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        looksLikeExitToTitle = true;
                        break;
                    }
                }
                if (!looksLikeExitToTitle)
                    return;

                bool serverActive = _onlineMode || _officialOnlineSaveSessionActive || _authBusy || IsAnyServerSessionActive() || IsOfficialReturnToTitleDialogueOverridePending();
                try { if (OfficialServerWorldRatesRuntime.IsOnlineRatesActive) serverActive = true; } catch { }
                if (!serverActive)
                    return;

                lines.Clear();
                if (IsLauncherSessionActive())
                {
                    lines.Add("Log out and return to character select?");
                    lines.Add("Current character will be saved to server.");
                    lines.Add("#yn");
                }
                else
                {
                    lines.Add("Disconnect from MMOnsterpatch? Character will be saved to the server.");
                    lines.Add("#yn");
                }
                _officialReturnToTitleDialogueOverridePendingUntil = Time.unscaledTime + 10f;
                Log("Official Server Return-to-Title confirmation list replaced for connected server logout.");
            }
            catch (Exception ex)
            {
                Log("ApplyOfficialReturnToTitleDialogueList failed: " + ex.Message);
            }
        }

        private static bool IsOfficialReturnToTitleDialogueOverridePending()
        {
            try { return _officialReturnToTitleDialogueOverridePendingUntil > 0f && Time.unscaledTime <= _officialReturnToTitleDialogueOverridePendingUntil; } catch { return false; }
        }

        private static void SetOfficialReturnToTitleDialogueOverride(bool serverActive)
        {
            try
            {
                object dialogue = FindDialogueScriptableObject("confirmExitToTitle");
                if (dialogue == null)
                    return;

                object[] chunks = GetObjectArrayField(dialogue, "dialogueChunk");
                if (chunks == null || chunks.Length == 0 || chunks[0] == null)
                    return;

                FieldInfo linesField = chunks[0].GetType().GetField("lines", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (linesField == null)
                    return;

                string[] lines = linesField.GetValue(chunks[0]) as string[];
                if (lines == null || lines.Length == 0)
                    return;

                if (_returnToTitleDialogueOriginalLines == null)
                    _returnToTitleDialogueOriginalLines = (string[])lines.Clone();

                if (serverActive)
                {
                    string[] wanted = IsLauncherSessionActive()
                        ? new string[]
                        {
                            "Log out and return to character select?",
                            "Current character will be saved to server.",
                            "#yn"
                        }
                        : new string[]
                        {
                            "Disconnect from MMOnsterpatch? Character will be saved to the server.",
                            "#yn"
                        };
                    if (!StringArraysEqual(lines, wanted))
                    {
                        linesField.SetValue(chunks[0], wanted);
                        _returnToTitleDialogueOverrideApplied = true;
                        Log("Official Server Return-to-Title confirmation text applied for connected server logout.");
                    }
                }
                else if (_returnToTitleDialogueOverrideApplied && _returnToTitleDialogueOriginalLines != null)
                {
                    linesField.SetValue(chunks[0], (string[])_returnToTitleDialogueOriginalLines.Clone());
                    _returnToTitleDialogueOverrideApplied = false;
                    Log("Official Server Return-to-Title confirmation text restored to vanilla/offline text.");
                }
            }
            catch (Exception ex)
            {
                Log("SetOfficialReturnToTitleDialogueOverride failed: " + ex.Message);
            }
        }

        private static object FindDialogueScriptableObject(string dialogueName)
        {
            try
            {
                Type dmType = FindGameType("DialogueManager");
                if (dmType == null)
                    return null;

                object dm = null;
                try
                {
                    PropertyInfo instProp = dmType.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (instProp != null)
                        dm = instProp.GetValue(null, null);
                }
                catch { }
                if (dm == null)
                    dm = UnityEngine.Object.FindObjectOfType(dmType);
                if (dm == null)
                    return null;

                object[] all = GetObjectArrayField(dm, "dialogueScriptableObjects");
                if (all == null)
                    return null;

                foreach (object d in all)
                {
                    if (d == null)
                        continue;
                    UnityEngine.Object unityObj = d as UnityEngine.Object;
                    string n = unityObj != null ? unityObj.name : null;
                    if (string.Equals(n, dialogueName, StringComparison.OrdinalIgnoreCase))
                        return d;
                }
            }
            catch { }
            return null;
        }

        private static object[] GetObjectArrayField(object obj, string fieldName)
        {
            try
            {
                if (obj == null)
                    return null;
                FieldInfo f = obj.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                if (f == null)
                    return null;
                Array arr = f.GetValue(obj) as Array;
                if (arr == null)
                    return null;
                object[] result = new object[arr.Length];
                for (int i = 0; i < arr.Length; i++)
                    result[i] = arr.GetValue(i);
                return result;
            }
            catch { return null; }
        }

        private static bool StringArraysEqual(string[] a, string[] b)
        {
            if (ReferenceEquals(a, b))
                return true;
            if (a == null || b == null || a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        private static bool IsAnyServerSessionActive()
        {
            try { if (GTSRuntimeHost.IsLoggedInForAio() || GTSRuntimeHost.IsAuthBusyForAio()) return true; } catch { }
            try { if (global::MMOnsterpatchAIOBootstrap.IsMMOBusy() || global::MMOnsterpatchAIOBootstrap.IsMMOConnected()) return true; } catch { }
            return false;
        }

        private static void DisconnectServerSession(string reason)
        {
            try
            {
                Log("Disconnecting Official Server session from " + reason + ".");
                _authBusy = false;
                _launcherPendingLogOutToSaveSelect = false;
                _launcherLogOutToSaveSelectBusy = false;
                _onlineMode = false;
                _statusOverride = null;
                _onlineHoverBlockLogged = false;
                ClearOfficialOnlineSaveSession(reason);

                try { global::MMOnsterpatchAIOBootstrap.DisconnectMMO(); } catch (Exception ex) { Log("MMO disconnect failed: " + ex.Message); }
                // Keep the Steam/AIO session token in memory for this game process so switching
                // Online/Offline during testing can restore with SESSION_LOGIN instead of opening Steam auth again.
                // GTSRuntimeHost.OnApplicationQuit still revokes/clears this memory-only token when the game closes.
                try { GTSRuntimeHost.DisconnectNetworkOnlyForAio(); } catch (Exception ex) { Log("launcher/GTS network disconnect failed: " + ex.Message); }

                ClearOnlineSaveSlotCache();
                RestoreOnlinePreviewPlayerSprites(_menuObj);
                RestoreOnlineWallpaperColors();
                RestoreLocalSaveDataFromDisk();
                if (_menuObj != null)
                {
                    try { InvokeMenuMethod(_menuObj, "RefreshSaveFiles"); } catch { }
                    RepairLocalSaveSlotVisuals();
                }

                UpdateVisualState();
            }
            catch (Exception ex)
            {
                Log("DisconnectServerSession failed: " + ex.Message);
            }
        }

        private static void ApplyOnlineSaveSlotVisuals()
        {
            try
            {
                object menuObj = _menuObj;
                if (menuObj == null || !_onlineMode)
                    return;

                // Keep the vanilla save-slot controls intact. The only data source we replace is
                // MenuScript.saveData, which is loaded from the server while Online Mode is active.
                // Official MenuScript.DeleteASaveFile calls Play2(), which reloads local saveData from disk;
                // re-apply the server cache so Online Mode never displays/deletes local saves.
                bool cacheChanged = ApplyOnlineSaveSlotCacheToMenuData();
                if (cacheChanged && !_refreshingOnlineSlotsFromCache)
                {
                    try
                    {
                        _refreshingOnlineSlotsFromCache = true;
                        InvokeMenuMethod(menuObj, "RefreshSaveFiles");
                    }
                    finally
                    {
                        _refreshingOnlineSlotsFromCache = false;
                    }
                }

                for (int i = 0; i < 6; i++)
                {
                    EnsureSaveSlotSelectableReady(GetBestSaveSlotSelectable(menuObj, i));
                    object save = GetSaveDataForSlot(i);
                    GameObject plus = GetGameObjectFromArrayField(menuObj, "savePlusObj", i);
                    ApplyOnlineSavePlusSprite(plus);
                    GameObject preview = GetGameObjectFromArrayField(menuObj, "savePlayerPreviewObj", i);
                    GameObject secondary = GetGameObjectFromArrayField(menuObj, "savePlayerPreviewSecondaryColor", i);
                    if (save == null)
                    {
                        if (plus != null) plus.SetActive(true);
                        if (preview != null) preview.SetActive(false);
                    }
                    else
                    {
                        if (plus != null) plus.SetActive(false);
                        // Keep the small save-slot character icon intact. PVP data belongs in the large preview panel on the right.
                        if (preview != null) preview.SetActive(true);
                        if (secondary != null) secondary.SetActive(true);
                    }
                    SetOnlineSaveSlotPvpSummaryVisible(menuObj, i, false);
                }

                ApplyOnlinePreviewPanelPvpSummary(menuObj);
            }
            catch (Exception ex)
            {
                Log("ApplyOnlineSaveSlotVisuals failed: " + ex.Message);
            }
        }

        private static void RestoreOnlinePreviewPlayerSprites(object menuObj)
        {
            try
            {
                RestoreOnlineHouseIcon1ToSavePreviewPanel();
                object[] imgs = GetObjectArrayField(menuObj, "previewSprite");
                if (imgs != null)
                {
                    for (int i = 0; i < imgs.Length; i++)
                    {
                        Image img = imgs[i] as Image;
                        if (img != null) img.enabled = true;
                    }
                }
                if (_onlinePreviewPvpSummaryText != null)
                    _onlinePreviewPvpSummaryText.gameObject.SetActive(false);
            }
            catch { }
        }

        private static void ApplyOnlinePreviewPanelPvpSummary(object menuObj)
        {
            try
            {
                if (menuObj == null || !_onlineMode)
                {
                    RestoreOnlinePreviewPlayerSprites(menuObj);
                    return;
                }

                int slot = _lastHoverSlot;
                if (slot < 0 || slot >= 6)
                    slot = GetCurrentSaveSlot(menuObj);
                if (slot < 0 || slot >= 6)
                    slot = 0;

                GameObject panel = GetGameObjectField(menuObj, "panelSaveDataPreview");
                object save = GetSaveDataForSlot(slot);
                if (panel == null || !panel.activeInHierarchy || save == null)
                {
                    RestoreOnlinePreviewPlayerSprites(menuObj);
                    return;
                }

                ApplyOnlineHouseIcon1ToSavePreviewPanel(menuObj, panel);

                Image anchor = null;
                object[] previewImages = GetObjectArrayField(menuObj, "previewSprite");
                if (previewImages != null)
                {
                    for (int i = 0; i < previewImages.Length; i++)
                    {
                        Image img = previewImages[i] as Image;
                        if (img == null) continue;
                        if (anchor == null) anchor = img;

                        // v0.15.8 SaveSlotPvpPreviewIconHideFix:
                        // Vanilla HoverSaveFile uses previewSprite[0]/[1] for the large player
                        // appearance preview inside the right summary panel.  That sits directly
                        // under the MMOnsterpatch PVP/RP/rank text and creates the duplicate icon
                        // shown over the PVP block.  Keep the small player icon on the left save
                        // slot untouched, and keep saveTeamMonImg team icons visible, but hide only
                        // the right-panel player preview layers while the online PVP overlay is active.
                        if (i <= 1)
                            img.enabled = false;
                        else
                            img.enabled = true;
                    }
                }

                TMP_Text txt = EnsureOnlinePreviewPvpSummaryText(menuObj, panel, anchor);
                if (txt == null)
                    return;
                PositionOnlinePreviewPvpSummaryText(txt, anchor);

                bool registered = _onlineSaveSlotPvpRegistered[slot];
                if (registered)
                {
                    string rank = string.IsNullOrEmpty(_onlineSaveSlotPvpRank[slot]) ? "UNRANKED" : _onlineSaveSlotPvpRank[slot].ToUpperInvariant();
                    txt.text = "PVP\nRP " + Math.Max(0, _onlineSaveSlotPvpRp[slot]).ToString() + "\n" +
                               Math.Max(0, _onlineSaveSlotPvpWins[slot]).ToString() + "W " +
                               Math.Max(0, _onlineSaveSlotPvpLosses[slot]).ToString() + "L " +
                               Math.Max(0, _onlineSaveSlotPvpDraws[slot]).ToString() + "D\nRANK: " + rank;
                }
                else
                {
                    txt.text = "PVP\nNOT REGISTERED";
                }

                txt.gameObject.SetActive(true);
            }
            catch (Exception ex)
            {
                Log("ApplyOnlinePreviewPanelPvpSummary failed: " + ex.Message);
            }
        }

        private static TMP_Text EnsureOnlinePreviewPvpSummaryText(object menuObj, GameObject panel, Image anchor)
        {
            try
            {
                if (_onlinePreviewPvpSummaryText != null)
                    return _onlinePreviewPvpSummaryText;

                Transform parent = anchor != null && anchor.transform.parent != null ? anchor.transform.parent : panel.transform;
                GameObject go = new GameObject("MMOnsterpatchOfficialServer_PreviewPanelPvpSummary", typeof(RectTransform), typeof(TextMeshProUGUI));
                go.transform.SetParent(parent, false);
                go.transform.SetAsLastSibling();

                TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
                TMP_Text txtPreview = GetObjectField(menuObj, "txtPreview") as TMP_Text;
                if (txtPreview != null)
                {
                    text.font = txtPreview.font;
                    text.fontMaterial = txtPreview.fontMaterial;
                    text.fontSharedMaterial = txtPreview.fontSharedMaterial;
                }

                RectTransform rt = text.GetComponent<RectTransform>();
                RectTransform anchorRt = anchor != null ? anchor.GetComponent<RectTransform>() : null;
                float offsetX = Cfg(SaveSelectPvpPreviewOffsetX, 0f);
                float offsetY = Cfg(SaveSelectPvpPreviewOffsetY, 0f);
                float width = Mathf.Max(40f, Cfg(SaveSelectPvpPreviewWidth, 150f));
                float height = Mathf.Max(30f, Cfg(SaveSelectPvpPreviewHeight, 86f));

                if (anchorRt != null)
                {
                    rt.anchorMin = anchorRt.anchorMin;
                    rt.anchorMax = anchorRt.anchorMax;
                    rt.pivot = anchorRt.pivot;
                    rt.anchoredPosition = anchorRt.anchoredPosition + new Vector2(offsetX, offsetY);
                    rt.sizeDelta = new Vector2(width, height);
                }
                else
                {
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = new Vector2(82f + offsetX, 58f + offsetY);
                    rt.sizeDelta = new Vector2(width, height);
                }
                rt.localScale = Vector3.one;
                rt.localRotation = Quaternion.identity;
                rt.localPosition = new Vector3(rt.localPosition.x, rt.localPosition.y, 0f);
                text.fontSize = Cfg(SaveSelectPvpPreviewFontSize, text.fontSize);

                text.alignment = TextAlignmentOptions.Center;
                text.enableWordWrapping = false;
                text.overflowMode = TextOverflowModes.Overflow;
                text.fontSize = Cfg(SaveSelectPvpPreviewFontSize, txtPreview != null ? Mathf.Max(6.8f, txtPreview.fontSize) : 7.0f);
                text.raycastTarget = false;
                text.color = new Color(0.17f, 0.10f, 0.10f, 1f);
                _onlinePreviewPvpSummaryText = text;
                return text;
            }
            catch (Exception ex)
            {
                Log("EnsureOnlinePreviewPvpSummaryText failed: " + ex.Message);
                return null;
            }
        }

        private static void PositionOnlinePreviewPvpSummaryText(TMP_Text text, Image anchor)
        {
            try
            {
                if (text == null) return;
                RectTransform rt = text.GetComponent<RectTransform>();
                RectTransform anchorRt = anchor != null ? anchor.GetComponent<RectTransform>() : null;
                if (rt == null) return;

                float offsetX = Cfg(SaveSelectPvpPreviewOffsetX, 0f);
                float offsetY = Cfg(SaveSelectPvpPreviewOffsetY, 0f);
                float width = Mathf.Max(40f, Cfg(SaveSelectPvpPreviewWidth, 150f));
                float height = Mathf.Max(30f, Cfg(SaveSelectPvpPreviewHeight, 86f));

                if (anchorRt != null)
                {
                    rt.anchorMin = anchorRt.anchorMin;
                    rt.anchorMax = anchorRt.anchorMax;
                    rt.pivot = anchorRt.pivot;
                    rt.anchoredPosition = anchorRt.anchoredPosition + new Vector2(offsetX, offsetY);
                    rt.sizeDelta = new Vector2(width, height);
                }
                else
                {
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = new Vector2(82f + offsetX, 58f + offsetY);
                    rt.sizeDelta = new Vector2(width, height);
                }
                rt.localScale = Vector3.one;
                rt.localRotation = Quaternion.identity;
                rt.localPosition = new Vector3(rt.localPosition.x, rt.localPosition.y, 0f);
                text.fontSize = Cfg(SaveSelectPvpPreviewFontSize, text.fontSize);
            }
            catch { }
        }


        private static void SetOnlineSaveSlotPvpSummaryVisible(object menuObj, int slot, bool visible)
        {
            try
            {
                if (slot < 0 || slot >= _onlineSaveSlotPvpSummaryTexts.Length)
                    return;
                TMP_Text txt = _onlineSaveSlotPvpSummaryTexts[slot];
                if (txt != null)
                    txt.gameObject.SetActive(visible);
            }
            catch { }
        }

        private static void UpdateOnlineSaveSlotPvpSummary(object menuObj, int slot, GameObject previewAnchor)
        {
            try
            {
                if (slot < 0 || slot >= _onlineSaveSlotPvpSummaryTexts.Length)
                    return;

                TMP_Text txt = EnsureOnlineSaveSlotPvpSummaryText(menuObj, slot, previewAnchor);
                if (txt == null)
                    return;

                bool registered = _onlineSaveSlotPvpRegistered[slot];
                if (registered)
                {
                    string rank = string.IsNullOrEmpty(_onlineSaveSlotPvpRank[slot]) ? "UNRANKED" : _onlineSaveSlotPvpRank[slot].ToUpperInvariant();
                    txt.text = "PVP\nRP " + Math.Max(0, _onlineSaveSlotPvpRp[slot]).ToString() + "\n" +
                               Math.Max(0, _onlineSaveSlotPvpWins[slot]).ToString() + "W " +
                               Math.Max(0, _onlineSaveSlotPvpLosses[slot]).ToString() + "L " +
                               Math.Max(0, _onlineSaveSlotPvpDraws[slot]).ToString() + "D\nRANK: " + rank;
                }
                else
                {
                    txt.text = "PVP\nNOT REGISTERED";
                }

                txt.gameObject.SetActive(true);
            }
            catch (Exception ex)
            {
                Log("UpdateOnlineSaveSlotPvpSummary failed for slot" + slot + ": " + ex.Message);
            }
        }

        private static TMP_Text EnsureOnlineSaveSlotPvpSummaryText(object menuObj, int slot, GameObject previewAnchor)
        {
            try
            {
                TMP_Text existing = _onlineSaveSlotPvpSummaryTexts[slot];
                if (existing != null)
                    return existing;

                Transform parent = null;
                RectTransform anchorRt = null;
                if (previewAnchor != null)
                {
                    parent = previewAnchor.transform.parent;
                    anchorRt = previewAnchor.GetComponent<RectTransform>();
                }
                if (parent == null)
                    parent = _visibleSaveSlotParent != null ? _visibleSaveSlotParent : (_root != null ? _root.transform : null);
                if (parent == null)
                    return null;

                GameObject go = new GameObject("MMOnsterpatchOfficialServer_SaveSlotPvpSummary_" + slot.ToString(), typeof(RectTransform), typeof(TextMeshProUGUI));
                go.transform.SetParent(parent, false);
                go.transform.SetAsLastSibling();

                TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
                TMP_Text txtPreview = GetObjectField(menuObj, "txtPreview") as TMP_Text;
                if (txtPreview != null)
                {
                    text.font = txtPreview.font;
                    text.fontMaterial = txtPreview.fontMaterial;
                    text.fontSharedMaterial = txtPreview.fontSharedMaterial;
                }

                RectTransform rt = text.GetComponent<RectTransform>();
                rt.anchorMin = anchorRt != null ? anchorRt.anchorMin : new Vector2(0.5f, 0.5f);
                rt.anchorMax = anchorRt != null ? anchorRt.anchorMax : new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.localScale = Vector3.one;
                rt.localRotation = Quaternion.identity;
                rt.sizeDelta = anchorRt != null ? new Vector2(Mathf.Max(72f, anchorRt.sizeDelta.x + 35f), Mathf.Max(42f, anchorRt.sizeDelta.y)) : new Vector2(92f, 48f);
                if (anchorRt != null)
                    rt.anchoredPosition = anchorRt.anchoredPosition;
                else
                    rt.anchoredPosition = new Vector2(80f, 28f);
                rt.localPosition = new Vector3(rt.localPosition.x, rt.localPosition.y, 0f);

                text.alignment = TextAlignmentOptions.Center;
                text.enableWordWrapping = false;
                text.overflowMode = TextOverflowModes.Overflow;
                text.fontSize = 5.2f;
                text.raycastTarget = false;
                text.color = new Color(0.17f, 0.10f, 0.10f, 1f);
                _onlineSaveSlotPvpSummaryTexts[slot] = text;
                return text;
            }
            catch (Exception ex)
            {
                Log("EnsureOnlineSaveSlotPvpSummaryText failed for slot" + slot + ": " + ex.Message);
                return null;
            }
        }

        private static bool ApplyOnlineSaveSlotCacheToMenuData()
        {
            try
            {
                if (!_onlineMode)
                    return false;

                Type menuType = FindGameType("MenuScript");
                FieldInfo f = menuType != null ? menuType.GetField("saveData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static) : null;
                Array arr = f != null ? f.GetValue(null) as Array : null;
                if (arr == null)
                    return false;

                bool changed = false;
                int count = Math.Min(6, arr.Length);
                for (int i = 0; i < count; i++)
                {
                    object cached = _onlineSaveSlotCache[i];
                    if (cached != null)
                        ApplyOnlineSlotServerHandleToSaveData(cached, _onlineSaveSlotHandles[i], null);
                    object current = arr.GetValue(i);
                    if (!object.ReferenceEquals(current, cached))
                    {
                        arr.SetValue(cached, i);
                        changed = true;
                    }
                }
                return changed;
            }
            catch
            {
                return false;
            }
        }

        private static void ClearOnlineSaveSlotCache()
        {
            try
            {
                for (int i = 0; i < _onlineSaveSlotCache.Length; i++)
                {
                    _onlineSaveSlotCache[i] = null;
                    _onlineSaveSlotOccupied[i] = false;
                    _onlineSaveSlotPayloadLoaded[i] = false;
                    _onlineSaveSlotDisplayNames[i] = string.Empty;
                    _onlineSaveSlotUpdatedAt[i] = 0;
                    _onlineSaveSlotPlayerDesign[i] = 0;
                    _onlineSaveSlotPlayerColor1[i] = 0;
                    _onlineSaveSlotPlayerColor2[i] = 0;
                    SetOnlineSlotProfile(i, string.Empty, false, 0, string.Empty, 0, 0, 0);
                }
            }
            catch { }
        }

        private static void ForceSelectOnlineSaveSlot(object menuObj, int slot)
        {
            try
            {
                if (menuObj == null)
                    return;
                int safeSlot = Mathf.Clamp(slot, 0, 5);
                GameObject target = GetBestSaveSlotSelectable(menuObj, safeSlot);
                if (target == null)
                    return;

                SetObjectField(menuObj, "curSaveSlot", safeSlot);
                SetObjectField(menuObj, "lastButtonObj", target);
                EventSystem es = GetMenuEventSystem(menuObj);
                if (es != null && !IsEventSystemAlreadySelecting(es))
                {
                    // Clear first so Unity fires a fresh OnSelect even if the same object
                    // survived the scene/menu transition in EventSystem state.
                    es.SetSelectedGameObject(null);
                    es.SetSelectedGameObject(target);
                }

                TryInvokeMenuMethodWithInt(menuObj, "HoverSaveFile", safeSlot);
                OnHoverSaveFile(menuObj, safeSlot);
                ApplyOnlineSaveSlotVisuals();
                Log("Forced online save slot " + safeSlot + " focus after Log Out return so the preview and cursor are live without requiring movement.");
            }
            catch (Exception ex)
            {
                Log("ForceSelectOnlineSaveSlot failed: " + ex.Message);
            }
        }

        private static void RequestSelectFirstSaveSlot()
        {
            _pendingSelectFirstSlotAfter = Time.unscaledTime + 0.08f;
            _pendingSelectFirstSlotUntil = Time.unscaledTime + 1.00f;
            _pendingSelectFirstSlotLoggedDeferral = false;
        }

        private static void HandlePendingLocalDeleteCompletion()
        {
            try
            {
                if (_pendingLocalDeleteSlot < 0)
                    return;

                if (_onlineMode || _menuObj == null || !IsNativeDeleteModeActive(_menuObj))
                {
                    _pendingLocalDeleteSlot = -1;
                    _pendingLocalDeleteCheckUntil = 0f;
                    return;
                }

                if (Time.unscaledTime > _pendingLocalDeleteCheckUntil)
                {
                    _pendingLocalDeleteSlot = -1;
                    _pendingLocalDeleteCheckUntil = 0f;
                    return;
                }

                int slot = Mathf.Clamp(_pendingLocalDeleteSlot, 0, 5);
                if (GetSaveDataForSlot(slot) == null)
                {
                    SetDeleteMode(false, "confirmed local delete");
                    Log("Delete Save mode auto-canceled after confirmed local save deletion for slot" + slot + ".");
                }
            }
            catch (Exception ex)
            {
                Log("HandlePendingLocalDeleteCompletion failed: " + ex.Message);
                _pendingLocalDeleteSlot = -1;
                _pendingLocalDeleteCheckUntil = 0f;
            }
        }

        private static void HandlePendingSaveSlotSelection()
        {
            try
            {
                if (_pendingSelectFirstSlotUntil <= 0f)
                    return;
                if (Time.unscaledTime < _pendingSelectFirstSlotAfter)
                    return;
                if (Time.unscaledTime > _pendingSelectFirstSlotUntil)
                {
                    _pendingSelectFirstSlotUntil = 0f;
                    return;
                }
                GameObject first = GetBestSaveSlotSelectable(_menuObj, 0);
                EventSystem es = GetMenuEventSystem(_menuObj);
                if (es != null && first != null)
                {
                    if (IsEventSystemAlreadySelecting(es))
                    {
                        if (!_pendingSelectFirstSlotLoggedDeferral)
                        {
                            _pendingSelectFirstSlotLoggedDeferral = true;
                            Log("Save slot focus handoff deferred because Unity EventSystem is already inside a selection event.");
                        }
                        _pendingSelectFirstSlotAfter = Time.unscaledTime + 0.05f;
                        return;
                    }
                    if (es.currentSelectedGameObject == first)
                    {
                        SetObjectField(_menuObj, "lastButtonObj", first);
                        _pendingSelectFirstSlotUntil = 0f;
                        return;
                    }
                    es.SetSelectedGameObject(first);
                    SetObjectField(_menuObj, "lastButtonObj", first);
                    _pendingSelectFirstSlotUntil = 0f;
                    _pendingSelectFirstSlotLoggedDeferral = false;
                    Log("Selected first save slot after mode switch so controller/keyboard focus leaves the Online/Offline button.");
                }
            }
            catch { }
        }


        private static bool IsEventSystemAlreadySelecting(EventSystem es)
        {
            try
            {
                if (es == null)
                    return false;
                PropertyInfo prop = typeof(EventSystem).GetProperty("alreadySelecting", BindingFlags.Public | BindingFlags.Instance);
                if (prop != null)
                {
                    object value = prop.GetValue(es, null);
                    if (value is bool b)
                        return b;
                }
            }
            catch { }
            return false;
        }

        private static EventSystem GetMenuEventSystem(object menuObj)
        {
            try
            {
                object esObj = GetObjectField(menuObj, "eventSystem");
                if (esObj is EventSystem es)
                    return es;
            }
            catch { }
            try { return EventSystem.current; } catch { return null; }
        }

        private static void EnsureSaveSlotSelectableReady(GameObject go)
        {
            try
            {
                if (go == null)
                    return;
                if (!go.activeSelf)
                    go.SetActive(true);
                Selectable selectable = go.GetComponent<Selectable>();
                if (selectable != null && !selectable.interactable)
                    selectable.interactable = true;
                Image img = go.GetComponent<Image>();
                if (img != null)
                    img.raycastTarget = true;
                foreach (CanvasGroup cg in go.GetComponentsInChildren<CanvasGroup>(true))
                {
                    cg.alpha = 1f;
                    cg.interactable = true;
                    cg.blocksRaycasts = true;
                }
            }
            catch { }
        }

        private static GameObject GetBestSaveSlotSelectable(object menuObj, int slot)
        {
            GameObject go = GetGameObjectFromArrayField(menuObj, "saveFileObj", slot);
            if (go != null && go.GetComponent<Selectable>() != null)
                return go;
            go = GetGameObjectFromArrayField(menuObj, "bSaveFile", slot);
            if (go != null && go.GetComponent<Selectable>() != null)
                return go;
            go = GetGameObjectFromArrayField(menuObj, "saveFileObj", slot);
            if (go != null)
                return go;
            return GetGameObjectFromArrayField(menuObj, "bSaveFile", slot);
        }

        private static Selectable GetSaveSlotSelectable(object menuObj, int slot)
        {
            GameObject go = GetBestSaveSlotSelectable(menuObj, slot);
            return go != null ? go.GetComponent<Selectable>() : null;
        }

        private static void ApplyModeButtonNavigation()
        {
            try
            {
                if (_modeButton == null || _menuObj == null)
                    return;
                Button btn = _modeButton.GetComponent<Button>();
                if (btn == null)
                    return;
                Selectable slot0 = GetSaveSlotSelectable(_menuObj, 0);
                Selectable slot4 = GetSaveSlotSelectable(_menuObj, 4);
                Selectable slot5 = GetSaveSlotSelectable(_menuObj, 5);
                Selectable target = slot5 ?? slot4 ?? slot0;
                Navigation nav = btn.navigation;
                nav.mode = Navigation.Mode.Explicit;
                nav.selectOnUp = target;
                nav.selectOnDown = target;
                nav.selectOnLeft = target;
                nav.selectOnRight = target;
                btn.navigation = nav;
            }
            catch { }
        }


        private static void PositionDeleteButtonCursor()
        {
            try
            {
                if (_menuObj == null || _deleteButton == null)
                    return;

                GameObject arrow = GetGameObjectField(_menuObj, "versionArrowObj");
                if (arrow == null || arrow.transform == null)
                    return;

                if (arrow.transform.parent != _deleteButton.transform)
                    return;

                arrow.transform.localPosition = new Vector3(Cfg(DeleteButtonCursorX, -42f), Cfg(DeleteButtonCursorY, 0f), 0f);
            }
            catch { }
        }

        private static void ApplyDeleteButtonNavigation()
        {
            try
            {
                if (_deleteButton == null || _menuObj == null)
                    return;
                Button btn = _deleteButton.GetComponent<Button>();
                if (btn == null)
                    return;
                Selectable slot0 = GetSaveSlotSelectable(_menuObj, 0);
                Selectable slot4 = GetSaveSlotSelectable(_menuObj, 4);
                Selectable slot5 = GetSaveSlotSelectable(_menuObj, 5);
                Selectable mode = _modeButton != null ? _modeButton.GetComponent<Selectable>() : null;
                Selectable target = slot5 ?? slot4 ?? slot0;
                Navigation nav = btn.navigation;
                nav.mode = Navigation.Mode.Explicit;
                nav.selectOnUp = target;
                nav.selectOnDown = target;
                nav.selectOnLeft = target;
                nav.selectOnRight = mode ?? target;
                btn.navigation = nav;
            }
            catch { }
        }


        public static bool IsOfficialOnlineModeActive()
        {
            try { return _onlineMode || _authBusy || _officialOnlineSaveSessionActive; } catch { return false; }
        }

        public static void RefreshOfficialWorldRatesFromServer()
        {
            try
            {
                EnsureConfig();
                float minInterval = WorldRatesRefreshMinIntervalSeconds != null ? Mathf.Clamp(WorldRatesRefreshMinIntervalSeconds.Value, 0f, 300f) : 30f;
                if (_lastWorldRatesRefreshAt > -1000f && minInterval > 0f && Time.realtimeSinceStartup - _lastWorldRatesRefreshAt < minInterval)
                    return;
                _lastWorldRatesRefreshAt = Time.realtimeSinceStartup;

                string token = GTSRuntimeHost.GetAioSessionTokenForSocial();
                if (string.IsNullOrEmpty(token))
                {
                    OfficialServerWorldRatesRuntime.ResetToOfflineDefaults("missing launcher session token");
                    return;
                }

                string line = SendOfficialServerRequest("OFFICIAL_WORLD_RATES_REQ|" + B64(token), "OFFICIAL_WORLD_RATES", "OFFICIAL_SAVE_ERROR");
                if (!string.IsNullOrEmpty(line) && line.StartsWith("OFFICIAL_WORLD_RATES|", StringComparison.OrdinalIgnoreCase))
                {
                    OfficialServerWorldRatesRuntime.ApplyServerLine(line);
                    return;
                }

                Log("Official Server world-rates request failed: " + DecodeServerError(line));
                OfficialServerWorldRatesRuntime.ResetToOfflineDefaults("world-rates request failed");
            }
            catch (Exception ex)
            {
                Log("RefreshOfficialWorldRatesFromServer failed: " + ex.Message);
                OfficialServerWorldRatesRuntime.ResetToOfflineDefaults("world-rates exception");
            }
        }

        public static void TickLauncherInGameUi()
        {
            try
            {
                if (!IsLauncherSessionActive())
                    return;
                if (Time.unscaledTime < _nextLauncherInGameUiTextTick)
                    return;
                _nextLauncherInGameUiTextTick = Time.unscaledTime + 2.0f;

                foreach (TMP_Text t in Resources.FindObjectsOfTypeAll<TMP_Text>())
                {
                    if (t == null)
                        continue;
                    string cur = (t.text ?? string.Empty).Trim();
                    if (cur.Equals("Title Screen", StringComparison.OrdinalIgnoreCase) || cur.Equals("Title Menu", StringComparison.OrdinalIgnoreCase) || cur.Equals("Return to Title", StringComparison.OrdinalIgnoreCase) || cur.Equals("Disconnect", StringComparison.OrdinalIgnoreCase))
                    {
                        t.text = "Log Out";
                        t.enableWordWrapping = false;
                        t.overflowMode = TextOverflowModes.Overflow;
                    }
                }
            }
            catch (Exception ex)
            {
                Log("TickLauncherInGameUi failed: " + ex.Message);
            }
        }

        private static void BeginLauncherLogOutToSaveSelect(string reason)
        {
            try
            {
                if (_launcherLogOutToSaveSelectBusy)
                    return;

                _launcherLogOutToSaveSelectBusy = true;
                Log("Launcher Log Out selected from " + reason + "; saving current character and returning to online character select.");

                if (_onlineMode || _officialOnlineSaveSessionActive)
                    ForceCurrentGameSaveToOfficialServer("Log Out");

                ClearOfficialOnlineSaveSession("Log Out return to character select");
                _onlineMode = true;
                _authBusy = false;
                _launcherPreAuthBusy = false;
                _launcherAutoAuthStarted = true;
                _statusOverride = "Server Status: Loading Saves";
                // The confirmed Log Out button still lets the game load the title/menu scene,
                // but the next MenuScript.Start is intercepted so the alpha/copyright/title flow
                // is skipped and the Online character-select screen is rebuilt immediately.
                _launcherSilentLogOutReturnToSaveSelect = true;
                _launcherPendingLogOutToSaveSelect = true;
                _launcherPendingLogOutToSaveSelectAfter = Time.unscaledTime + 0.05f;
                _officialReturnToTitleDialogueOverridePendingUntil = 0f;
                HideOfficialChatWindow();
            }
            catch (Exception ex)
            {
                _launcherLogOutToSaveSelectBusy = false;
                Log("BeginLauncherLogOutToSaveSelect failed: " + ex.Message);
            }
        }

        public static bool TryHandleLauncherSilentMenuStart(object menuObj)
        {
            try
            {
                if (!_launcherSilentLogOutReturnToSaveSelect || !IsLauncherSessionActive())
                    return false;

                _launcherSilentLogOutReturnToSaveSelect = false;
                _launcherPendingLogOutToSaveSelect = false;
                _menuObj = menuObj;
                _onlineMode = true;
                _authBusy = true;
                _launcherPreAuthBusy = true;
                _launcherAutoAuthStarted = true;
                _launcherOpenDeleteAfterOnlineSlots = false;
                _statusOverride = "Server Status: Loading Saves";
                _loggedHidden = false;

                SetMenuStaticStateForLauncherMenu();

                // Hide all startup/title/create panels before their first rendered frame.
                // This avoids the copyright/title flash that happened when Log Out used the
                // game's normal return-to-title scene path.
                SetMenuGameObjectActive(menuObj, "menuAlphWarning", false);
                SetMenuGameObjectActive(menuObj, "menuStartup", false);
                SetMenuGameObjectActive(menuObj, "menuIntroText", false);
                SetMenuGameObjectActive(menuObj, "menuMainTitle", false);
                SetMenuGameObjectActive(menuObj, "menuExtras", false);
                SetMenuGameObjectActive(menuObj, "menuOptions", false);
                SetMenuGameObjectActive(menuObj, "menuBenIntro", false);
                SetMenuGameObjectActive(menuObj, "menuCreateNewSave", false);
                SetMenuGameObjectActive(menuObj, "menuVersionSelect", false);
                SetMenuGameObjectActive(menuObj, "panelTopDeleteSave", false);
                SetMenuGameObjectActive(menuObj, "panelSaveDataPreview", false);
                SetMenuGameObjectActive(menuObj, "menuSaveSelect", true);
                SetMenuGameObjectActive(menuObj, "menuSaveFiles", false);

                SetObjectField(menuObj, "deletingSaveFile", false);
                SetObjectField(menuObj, "pressingButton", false);
                HideOfficialChatWindow();
                StartPrivateMenuCoroutine(menuObj, "SetLocalizationToEnglish");
                EnsureTitleCoroutineHost(menuObj);

                if (_host != null && _host.gameObject != null && _host.gameObject.activeInHierarchy)
                {
                    _host.StartCoroutine(LauncherLogOutReloadSaveSelect());
                    Log("Launcher Log Out silent menu start intercepted; opening online character select without alpha/title playback.");
                }
                else
                {
                    _launcherPendingLogOutToSaveSelect = true;
                    _launcherPendingLogOutToSaveSelectAfter = Time.unscaledTime + 0.05f;
                    Log("Launcher Log Out silent menu start intercepted; coroutine host not ready, queued online character select reopen.");
                }

                // Native Start would begin IntroText/copyright/alpha. We handled the menu state.
                return true;
            }
            catch (Exception ex)
            {
                _launcherSilentLogOutReturnToSaveSelect = false;
                Log("TryHandleLauncherSilentMenuStart failed: " + ex.Message);
                return false;
            }
        }

        private static void TryStartPendingLauncherLogOutSaveSelect(object menuObj)
        {
            try
            {
                if (!_launcherPendingLogOutToSaveSelect || !IsLauncherSessionActive())
                    return;
                if (_launcherPreAuthBusy || _authBusy)
                    return;
                if (Time.unscaledTime < _launcherPendingLogOutToSaveSelectAfter)
                    return;

                _menuObj = menuObj;
                EnsureTitleCoroutineHost(menuObj);
                if (_host == null || !_host.gameObject.activeInHierarchy)
                {
                    EnsureHostComponent();
                    if (_host == null || !_host.gameObject.activeInHierarchy)
                        return;
                }

                _launcherPendingLogOutToSaveSelect = false;
                _launcherOpenDeleteAfterOnlineSlots = false;
                _host.StartCoroutine(LauncherLogOutReloadSaveSelect());
                Log("Launcher Log Out title flow ready; reopening online character select.");
            }
            catch (Exception ex)
            {
                _launcherLogOutToSaveSelectBusy = false;
                Log("TryStartPendingLauncherLogOutSaveSelect failed: " + ex.Message);
            }
        }

        private static IEnumerator LauncherLogOutReloadSaveSelect()
        {
            _launcherPreAuthBusy = true;
            _authBusy = true;
            _statusOverride = "Server Status: Loading Saves";
            UpdateLauncherTitleLogInText();

            bool authenticated = false;
            try { authenticated = GTSRuntimeHost.TryUseLauncherAccountSessionForAio() || GTSRuntimeHost.IsLoggedInForAio(); } catch { }
            if (!authenticated)
            {
                IEnumerator authRoutine = null;
                bool authFailed = false;
                try { authRoutine = GTSRuntimeHost.EnsureSteamAuthForAioCoroutine(); }
                catch (Exception ex) { authFailed = true; Log("Launcher Log Out re-auth routine could not be created: " + ex.Message); }

                if (authRoutine != null)
                {
                    float authStart = Time.realtimeSinceStartup;
                    const float watchdogSeconds = 90f;
                    while (true)
                    {
                        if (Time.realtimeSinceStartup - authStart > watchdogSeconds)
                        {
                            authFailed = true;
                            try { GTSRuntimeHost.ResetStaleOfficialServerAuthForAio(); } catch { }
                            Log("Launcher Log Out re-auth timed out before online save-select could reopen.");
                            break;
                        }

                        object current = null;
                        bool moved = false;
                        try
                        {
                            moved = authRoutine.MoveNext();
                            if (moved)
                                current = authRoutine.Current;
                        }
                        catch (Exception ex)
                        {
                            authFailed = true;
                            Log("Launcher Log Out re-auth failed: " + ex.Message);
                            break;
                        }

                        if (!moved)
                            break;

                        yield return current;
                    }
                }

                if (!authFailed)
                {
                    try { authenticated = GTSRuntimeHost.TryUseLauncherAccountSessionForAio() || GTSRuntimeHost.IsLoggedInForAio(); } catch { }
                }
            }

            if (!authenticated)
            {
                _onlineMode = false;
                _authBusy = false;
                _launcherPreAuthBusy = false;
                _launcherLogOutToSaveSelectBusy = false;
                _statusOverride = "Server Status: Auth Failed";
                SetObjectField(_menuObj, "pressingButton", false);
                UpdateLauncherTitleLogInText();
                Log("Launcher Log Out could not keep the online account session active. Staying on title screen.");
                yield break;
            }

            _onlineMode = true;
            bool loaded = FetchOnlineSaveSlotsFromServer();
            if (loaded)
                RefreshOfficialWorldRatesFromServer();

            _authBusy = false;
            _launcherPreAuthBusy = false;
            _launcherLogOutToSaveSelectBusy = false;
            _statusOverride = loaded ? null : "Server Status: Save Load Failed";

            if (!loaded)
            {
                _onlineMode = false;
                SetObjectField(_menuObj, "pressingButton", false);
                UpdateLauncherTitleLogInText();
                Log("Launcher Log Out saved the character, but online save slots could not reload. Staying on title screen.");
                yield break;
            }

            SetObjectField(_menuObj, "pressingButton", false);

            // An earlier Log Out implementation re-opened save-select through
            // MenuScript.Play2(). That worked, but Play2 intentionally fades through
            // the title/alpha path and reloads local save slots before our online cache
            // takes over, causing visible flicker. Rebuild the same post-login
            // save-select state directly instead: hide title/alpha panels, show the
            // native save-select panels, then apply the already-loaded server slot cache.
            bool openedDirect = DirectOpenLauncherOnlineSaveSelectWithoutTitleFlow(_menuObj);
            if (!openedDirect)
            {
                Log("Launcher Log Out direct save-select reopen failed; falling back to the standard Log In button flow.");
                _launcherLogOutToSaveSelectBusy = false;
                _launcherPreAuthBusy = false;
                _authBusy = false;
                EnsureTitleCoroutineHost(_menuObj);
                if (_host != null && _host.gameObject.activeInHierarchy)
                {
                    _host.StartCoroutine(AuthenticateThenOpenLauncherSaveSelect(false));
                    yield break;
                }

                UpdateLauncherTitleLogInText();
                Log("Launcher Log Out could not reopen native online save-select after saving.");
                yield break;
            }

            _launcherLogOutToSaveSelectBusy = false;
            UpdateLauncherTitleLogInText();
            Log("Launcher Log Out completed. Current character saved and online character select reopened without replaying the title/alpha flow.");
        }

        private static bool DirectOpenLauncherOnlineSaveSelectWithoutTitleFlow(object menuObj)
        {
            try
            {
                if (menuObj == null)
                    return false;

                _menuObj = menuObj;
                _onlineMode = true;
                _authBusy = false;
                _launcherPreAuthBusy = false;
                _launcherAllowNativePlayOnce = false;
                _launcherOpenDeleteAfterOnlineSlots = false;
                _statusOverride = null;

                // Hide every title/create/version panel that can briefly show during the
                // vanilla return-to-title transition, including the typoed native
                // menuAlphWarning field name. Keep menuSaveSelect/menuSaveFiles alive.
                SetMenuGameObjectActive(menuObj, "menuAlphWarning", false);
                SetMenuGameObjectActive(menuObj, "menuStartup", false);
                SetMenuGameObjectActive(menuObj, "menuIntroText", false);
                SetMenuGameObjectActive(menuObj, "menuMainTitle", false);
                SetMenuGameObjectActive(menuObj, "menuExtras", false);
                SetMenuGameObjectActive(menuObj, "menuOptions", false);
                SetMenuGameObjectActive(menuObj, "menuBenIntro", false);
                SetMenuGameObjectActive(menuObj, "menuCreateNewSave", false);
                SetMenuGameObjectActive(menuObj, "menuVersionSelect", false);
                SetMenuGameObjectActive(menuObj, "menuSaveSelect", true);
                SetMenuGameObjectActive(menuObj, "menuSaveFiles", true);
                SetMenuGameObjectActive(menuObj, "panelTopDeleteSave", false);
                SetMenuGameObjectActive(menuObj, "panelSaveDataPreview", false);

                SetObjectField(menuObj, "deletingSaveFile", false);
                SetObjectField(menuObj, "pressingButton", false);

                EnsureForMenuObject(menuObj);
                ApplyOnlineSaveSlotCacheToMenuData();
                InvokeMenuMethod(menuObj, "RefreshSaveFiles");
                ApplyOnlineSaveSlotVisuals();
                ForceSelectOnlineSaveSlot(menuObj, 0);
                RequestSelectFirstSaveSlot();
                UpdateVisualState();

                return IsMenuSaveFilesVisible(menuObj);
            }
            catch (Exception ex)
            {
                Log("DirectOpenLauncherOnlineSaveSelectWithoutTitleFlow failed: " + ex.Message);
                return false;
            }
        }

        private static void SetMenuStaticStateForLauncherMenu()
        {
            try
            {
                Type menuType = FindGameType("MenuScript");
                if (menuType == null)
                    return;

                FieldInfo began = menuType.GetField("beganWithScene0", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (began != null)
                    began.SetValue(null, true);

                FieldInfo state = menuType.GetField("gameState", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (state != null)
                    state.SetValue(null, Enum.ToObject(state.FieldType, 3));
            }
            catch (Exception ex)
            {
                Log("SetMenuStaticStateForLauncherMenu failed: " + ex.Message);
            }
        }

        private static void StartPrivateMenuCoroutine(object menuObj, string methodName)
        {
            try
            {
                MonoBehaviour mb = menuObj as MonoBehaviour;
                if (mb == null || mb.gameObject == null || !mb.gameObject.activeInHierarchy)
                    return;
                MethodInfo method = menuObj.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                IEnumerator routine = method != null ? method.Invoke(menuObj, null) as IEnumerator : null;
                if (routine != null)
                    mb.StartCoroutine(routine);
            }
            catch (Exception ex)
            {
                Log("StartPrivateMenuCoroutine " + methodName + " failed: " + ex.Message);
            }
        }

        private static void SetMenuGameObjectActive(object menuObj, string fieldName, bool active)
        {
            try
            {
                GameObject go = GetGameObjectField(menuObj, fieldName);
                if (go != null && go.activeSelf != active)
                    go.SetActive(active);
            }
            catch { }
        }

        public static void OnApplicationQuitSaveAndDisconnect()
        {
            try
            {
                if (_applicationQuitSaveStarted)
                    return;
                _applicationQuitSaveStarted = true;

                bool serverActive = _onlineMode || _officialOnlineSaveSessionActive || _authBusy || IsAnyServerSessionActive();
                try { if (OfficialServerWorldRatesRuntime.IsOnlineRatesActive) serverActive = true; } catch { }
                if (!serverActive)
                    return;

                Log("Application quit detected while Official Server session is active; attempting final server save before network teardown.");
                if (!ShouldSkipDuplicateFinalDisconnectSave("application quit"))
                {
                    ForceCurrentGameSaveToOfficialServer("application quit");
                    WaitForQueuedOfficialSaveUpload(GetFinalSaveFlushTimeoutMs(), "application quit");
                }
                else
                {
                    WaitForQueuedOfficialSaveUpload(Math.Min(GetFinalSaveFlushTimeoutMs(), 350), "application quit recent-save quick flush");
                }
                DisconnectServerSession("application quit");
                HideOfficialChatWindow();
            }
            catch (Exception ex)
            {
                Log("OnApplicationQuitSaveAndDisconnect failed: " + ex.Message);
            }
        }

        public static void ForceSaveDisconnectAndReturnToTitleFromChat()
        {
            try
            {
                ForceSaveAndDisconnectOnly("chat Disconnect button");
                _returnToTitleFromOfficialDisconnect = true;
                try
                {
                    Type gsType = FindGameType("GameScript");
                    UnityEngine.Object gsObj = gsType != null ? UnityEngine.Object.FindObjectOfType(gsType) : null;
                    MethodInfo rt = gsType != null ? gsType.GetMethod("ReturnToTitle", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static) : null;
                    if (rt != null)
                    {
                        rt.Invoke(rt.IsStatic ? null : gsObj, null);
                        Log("Chat Disconnect force-saved, disconnected, and invoked GameScript.ReturnToTitle.");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Log("GameScript.ReturnToTitle invoke failed after chat disconnect: " + ex.Message);
                }
                try { UnityEngine.SceneManagement.SceneManager.LoadScene(0); } catch { }
            }
            finally
            {
                _returnToTitleFromOfficialDisconnect = false;
            }
        }

        private static void ForceSaveAndDisconnectOnly(string reason)
        {
            try
            {
                if (_onlineMode || _officialOnlineSaveSessionActive)
                {
                    if (!ShouldSkipDuplicateFinalDisconnectSave(reason))
                    {
                        ForceCurrentGameSaveToOfficialServer(reason);
                        WaitForQueuedOfficialSaveUpload(GetFinalSaveFlushTimeoutMs(), reason);
                    }
                    else
                    {
                        WaitForQueuedOfficialSaveUpload(Math.Min(GetFinalSaveFlushTimeoutMs(), 350), reason + " recent-save quick flush");
                    }
                }
                DisconnectServerSession(reason);
                HideOfficialChatWindow();
                if (IsLauncherSessionActive())
                {
                    Log("Launcher mode disconnect completed; closing game instead of returning to title.");
                    try { Application.Quit(); } catch { }
                }
            }
            catch (Exception ex)
            {
                Log("ForceSaveAndDisconnectOnly failed: " + ex.Message);
            }
        }

        private static void ForceCurrentGameSaveToOfficialServer(string reason)
        {
            try
            {
                Type gsType = FindGameType("GameScript");
                UnityEngine.Object gsObj = gsType != null ? UnityEngine.Object.FindObjectOfType(gsType) : null;
                MethodInfo save = gsType != null ? gsType.GetMethod("ActuallySaveGame", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static) : null;
                if (save == null)
                    save = gsType != null ? gsType.GetMethod("SaveGame", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static) : null;
                if (save == null || (!save.IsStatic && gsObj == null))
                {
                    Log("No active GameScript save method found for Official Server force-save from " + reason + ".");
                    return;
                }
                save.Invoke(save.IsStatic ? null : gsObj, null);
                Log("Requested GameScript." + save.Name + " for Official Server before disconnect from " + reason + ". SaveSystem.SaveGame remains redirected to the server while Official Online Save Session is active.");
            }
            catch (Exception ex)
            {
                Log("Official Server force-save failed from " + reason + ": " + ex.Message);
            }
        }

        private static void HideOfficialChatWindow()
        {
            try
            {
                Goose.Monsterpatch.SocialPatcher.SocialNativePatcher.SocialRuntimeHost.HideChatForOfficialServerTitle();
            }
            catch { }
        }


        private static bool IsLauncherSessionActive()
        {
            try
            {
                if (_launcherSessionChecked)
                    return _launcherSessionActive;
                _launcherSessionChecked = true;
                _launcherSessionActive = false;
                string env = Environment.GetEnvironmentVariable("MMONSTERPATCH_LAUNCHER") ?? string.Empty;
                if (env == "1" || env.Equals("true", StringComparison.OrdinalIgnoreCase) || env.Equals("online", StringComparison.OrdinalIgnoreCase))
                {
                    _launcherSessionActive = true;
                    Log("Launcher session detected from MMONSTERPATCH_LAUNCHER environment variable.");
                    return true;
                }
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string sessionPath = Path.Combine(local ?? string.Empty, "MMOnsterpatch", "launcher_session.json");
                if (!string.IsNullOrEmpty(local) && File.Exists(sessionPath))
                {
                    _launcherSessionActive = true;
                    Log("Launcher session detected from " + sessionPath + ".");
                    return true;
                }
            }
            catch (Exception ex) { Log("Launcher session detection failed: " + ex.Message); }
            return _launcherSessionActive;
        }

        private static void TryStartLauncherAutoOnlineSlots()
        {
            try
            {
                if (!IsLauncherSessionActive() || _launcherAutoAuthStarted || _onlineMode || _authBusy)
                    return;
                EnsureHostComponent();
                if (_host == null) return;
                _launcherAutoAuthStarted = true;
                _host.StartCoroutine(AuthenticateThenShowOnlineSlots());
                Log("Launcher save-select fallback auth started because native save-select was already open.");
            }
            catch (Exception ex) { Log("TryStartLauncherAutoOnlineSlots failed: " + ex.Message); }
        }

        public static bool TryHandleLauncherDeleteASaveFile(object menuObj)
        {
            try
            {
                if (!IsLauncherSessionActive()) return false;
                _menuObj = menuObj;
                EnsureTitleCoroutineHost(menuObj);
                _launcherOpenDeleteAfterOnlineSlots = true;
                _launcherAutoAuthStarted = true;
                if (_host != null)
                {
                    _host.StartCoroutine(AuthenticateThenOpenLauncherSaveSelect(true));
                    Log("Launcher Extras Delete selected. Authenticating before opening online save slots in delete mode.");
                    return true;
                }
            }
            catch (Exception ex) { Log("TryHandleLauncherDeleteASaveFile failed: " + ex.Message); }
            return false;
        }

        public static bool TryOpenLauncherGithub()
        {
            try
            {
                if (!IsLauncherSessionActive()) return false;
                Application.OpenURL("https://github.com/maver1gam1ng/MMOnsterpatch");
                Log("Launcher Extras GitHub link opened.");
                return true;
            }
            catch (Exception ex) { Log("TryOpenLauncherGithub failed: " + ex.Message); return false; }
        }

        private static void ApplyLauncherTitleAndExtrasTakeover(object menuObj)
        {
            try
            {
                if (menuObj == null || !IsLauncherSessionActive()) return;

                if (IsLauncherVersionSelectVisible(menuObj))
                {
                    ApplyLauncherVersionSelectOnlineTheme(menuObj);
                    EnsureLauncherVersionSelectControlsVisible(menuObj);
                    EnsureLauncherNativeMenuCursorVisible(menuObj);
                    return;
                }

                GameObject options = GetGameObjectField(menuObj, "bOptions");
                if (options != null && options.activeSelf) options.SetActive(false);
                GameObject play = GetGameObjectField(menuObj, "bPlay");
                SetTextRecursive(play, (_launcherPreAuthBusy || _authBusy) ? "Logging In..." : "Log In", true);
                GameObject extras = GetGameObjectField(menuObj, "bExtras");
                if (extras != null && !extras.activeSelf) extras.SetActive(true);
                ApplyOnlineTitleLogoSprite(menuObj);
                ApplyOnlineTitleBackgroundSprite(menuObj);
                ApplyLauncherOnlineVersionTitle(menuObj);
                HideLauncherSwitchVersionButton(menuObj);
                EnsureLauncherNativeMenuCursorVisible(menuObj);
                ApplyLauncherTitleLegendaryMons(menuObj);
                ApplyLauncherExtrasText(menuObj);
                if (Time.unscaledTime - _lastLauncherTitleTakeoverLog > 15f)
                {
                    _lastLauncherTitleTakeoverLog = Time.unscaledTime;
                    Log("Launcher title takeover active: Play=Log In, Options hidden, Extras retained.");
                }
            }
            catch (Exception ex) { Log("ApplyLauncherTitleAndExtrasTakeover failed: " + ex.Message); }
        }

        private const string LauncherExtrasInfoText = "MMOnsterpatch is an unofficial Online Server for Monsterpatch.\nYou can find more information by clicking the GitHub link below.";

        private static void ApplyLauncherExtrasText(object menuObj)
        {
            try
            {
                GameObject extras = GetGameObjectField(menuObj, "menuExtras");
                if (extras == null || !extras.activeInHierarchy) return;

                foreach (TMP_Text t in extras.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (t == null) continue;
                    string cur = t.text ?? string.Empty;
                    string trimmed = cur.Trim();
                    string path = GetTransformPath(t.transform);
                    bool insideButton = t.GetComponentInParent<Button>() != null || path.IndexOf("button", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool insideDelete = path.IndexOf("delete", StringComparison.OrdinalIgnoreCase) >= 0 || (cur.IndexOf("Delete", StringComparison.OrdinalIgnoreCase) >= 0 && cur.IndexOf("Save", StringComparison.OrdinalIgnoreCase) >= 0);
                    bool insideDiscord = path.IndexOf("discord", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool isBack = string.Equals(trimmed, "Back", StringComparison.OrdinalIgnoreCase);

                    if (insideDelete)
                    {
                        t.text = "Delete Online Save File";
                        t.enableWordWrapping = false;
                        t.overflowMode = TextOverflowModes.Overflow;
                        t.alignment = TextAlignmentOptions.Center;
                        if (t.fontSize > 5.25f) t.fontSize = 5.25f;
                        WidenLauncherDeleteSaveButton(menuObj, t);
                    }
                    else if (insideDiscord || (insideButton && trimmed.Length <= 20 && (trimmed.IndexOf("Discord", StringComparison.OrdinalIgnoreCase) >= 0 || trimmed.IndexOf("GitHub", StringComparison.OrdinalIgnoreCase) >= 0)))
                    {
                        t.text = "GitHub";
                        t.enableWordWrapping = false;
                        t.overflowMode = TextOverflowModes.Overflow;
                        t.alignment = TextAlignmentOptions.Center;
                    }
                    else if (!isBack && (!insideButton || cur.IndexOf("Suggestions", StringComparison.OrdinalIgnoreCase) >= 0 || cur.IndexOf("Support", StringComparison.OrdinalIgnoreCase) >= 0 || cur.IndexOf("email", StringComparison.OrdinalIgnoreCase) >= 0 || cur.IndexOf("Discord", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        t.text = LauncherExtrasInfoText;
                        t.enableWordWrapping = true;
                        t.overflowMode = TextOverflowModes.Overflow;
                        t.alignment = TextAlignmentOptions.Center;
                        if (t.fontSize > 4.75f) t.fontSize = 4.75f;
                        RectTransform rt = t.GetComponent<RectTransform>();
                        if (rt != null)
                            rt.sizeDelta = new Vector2(Mathf.Max(rt.sizeDelta.x, 170f), Mathf.Max(rt.sizeDelta.y, 34f));
                    }
                }

                WidenLauncherDeleteSaveButton(menuObj, null);

                foreach (Image img in extras.GetComponentsInChildren<Image>(true))
                {
                    if (img == null || img.gameObject == null) continue;
                    string n = img.gameObject.name ?? string.Empty;
                    string sn = img.sprite != null ? img.sprite.name ?? string.Empty : string.Empty;
                    if (n.IndexOf("discord", StringComparison.OrdinalIgnoreCase) >= 0 || sn.IndexOf("discord", StringComparison.OrdinalIgnoreCase) >= 0) img.enabled = false;
                }
            }
            catch (Exception ex) { Log("ApplyLauncherExtrasText failed: " + ex.Message); }
        }

        private static void WidenLauncherDeleteSaveButton(object menuObj, TMP_Text deleteText)
        {
            try
            {
                GameObject button = GetGameObjectField(menuObj, "buttonDeleteASaveFile");
                if (button == null && deleteText != null)
                {
                    Button b = deleteText.GetComponentInParent<Button>();
                    if (b != null) button = b.gameObject;
                }
                if (button == null) return;

                RectTransform rt = button.GetComponent<RectTransform>();
                if (rt != null)
                {
                    float wantedW = 126f;
                    float wantedH = Mathf.Max(rt.sizeDelta.y, 18f);
                    if (rt.sizeDelta.x < wantedW || rt.sizeDelta.y < wantedH)
                        rt.sizeDelta = new Vector2(Mathf.Max(rt.sizeDelta.x, wantedW), wantedH);
                }

                foreach (TMP_Text t in button.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (t == null) continue;
                    t.enableWordWrapping = false;
                    t.overflowMode = TextOverflowModes.Overflow;
                    t.alignment = TextAlignmentOptions.Center;
                    if (t.fontSize > 5.25f) t.fontSize = 5.25f;
                    RectTransform trt = t.GetComponent<RectTransform>();
                    if (trt != null)
                    {
                        trt.offsetMin = new Vector2(2f, trt.offsetMin.y);
                        trt.offsetMax = new Vector2(-2f, trt.offsetMax.y);
                    }
                }
            }
            catch { }
        }


        private static void SetTextRecursive(GameObject root, string value, bool onlyIfShort)
        {
            try
            {
                if (root == null) return;
                foreach (TMP_Text t in root.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (t == null) continue;
                    if (!onlyIfShort || string.IsNullOrEmpty(t.text) || t.text.Length <= 24) t.text = value;
                }
            }
            catch { }
        }

        private static void ApplyOnlineTitleBackgroundSprite(object menuObj)
        {
            try
            {
                if (!IsLauncherSessionActive()) return;
                if (OnlineTitleBackgroundEnabled != null && !OnlineTitleBackgroundEnabled.Value) return;

                Image bg = GetMainTitleRotationImage("mainBG");
                if (bg == null)
                    bg = FindTitleImageByObjectOrSpriteName("mainBG", "legendaryBG");
                if (bg == null) return;

                Sprite s = GetEmbeddedSprite("OnlinelegendaryBG.png", ref _embeddedOnlineLegendaryBackgroundSprite, ref _embeddedOnlineLegendaryBackgroundTexture, "OnlinelegendaryBG");
                if (s == null) return;

                if (_originalTitleBackgroundSprite == null)
                    _originalTitleBackgroundSprite = bg.sprite;

                if (bg.sprite != s)
                {
                    bg.sprite = s;
                    bg.type = Image.Type.Simple;
                    bg.preserveAspect = false;
                    if (!_onlineTitleBackgroundSwapLogged)
                    {
                        _onlineTitleBackgroundSwapLogged = true;
                        Log("Launcher online title background swapped to embedded OnlinelegendaryBG.png. OnlinemenuBG.png remains save-select only.");
                    }
                }
            }
            catch (Exception ex) { Log("ApplyOnlineTitleBackgroundSprite failed: " + ex.Message); }
        }

        private static void ApplyLauncherTitleLegendaryMons(object menuObj)
        {
            try
            {
                if (!IsLauncherSessionActive()) return;

                // v0.11.3 calibration decision: remove the title legendary mon icons entirely.
                // The previous offset-root approach fought the game's native title animation/layout,
                // so Online Mode now hides the native mon image and any MMOnsterpatch-added clone.
                UnityEngine.Object rot = GetMainTitleRotationObject();
                Type rotType = rot != null ? rot.GetType() : FindGameType("MainTitleRotation");
                Image mon0 = GetImageField(rot, rotType, "legendaryMon");
                HideLauncherTitleMonImage(mon0);

                if (_legendaryMon0OffsetRoot != null)
                    _legendaryMon0OffsetRoot.gameObject.SetActive(false);
                if (_legendaryMon1OffsetRoot != null)
                    _legendaryMon1OffsetRoot.gameObject.SetActive(false);
                if (_launcherLegendaryMon1Object != null)
                    _launcherLegendaryMon1Object.SetActive(false);

                // Extra safety in case the title image was found/cloned by object name instead of the MainTitleRotation field.
                foreach (Image img in Resources.FindObjectsOfTypeAll<Image>())
                {
                    if (img == null || img.gameObject == null) continue;
                    string objName = img.gameObject.name ?? string.Empty;
                    string spriteName = img.sprite != null ? img.sprite.name ?? string.Empty : string.Empty;
                    if (objName.IndexOf("legendaryMon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        objName.IndexOf("legendaryMons", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        spriteName.IndexOf("legendaryMons", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        HideLauncherTitleMonImage(img);
                    }
                }
            }
            catch (Exception ex) { Log("ApplyLauncherTitleLegendaryMons failed: " + ex.Message); }
        }

        private static void HideLauncherTitleMonImage(Image img)
        {
            try
            {
                if (img == null) return;
                img.enabled = false;
                img.raycastTarget = false;
                Color c = img.color;
                c.a = 0f;
                img.color = c;
                if (img.gameObject != null)
                    img.gameObject.SetActive(false);
            }
            catch { }
        }

        private static void ApplyLegendaryMonTransform(Image img, ref RectTransform offsetRoot, ref Vector3? baseLocalPosition, ref Vector3? baseLocalScale, ref Vector2? baseSizeDelta, bool enabled, float offsetX, float offsetY, float scale, float alpha)
        {
            try
            {
                if (img == null) return;
                img.enabled = enabled;
                Color c = img.color;
                c.a = Mathf.Clamp01(alpha);
                img.color = c;

                RectTransform root = EnsureLegendaryMonOffsetRoot(img, ref offsetRoot);
                if (root == null) return;

                if (!baseLocalPosition.HasValue) baseLocalPosition = root.localPosition;
                if (!baseLocalScale.HasValue) baseLocalScale = root.localScale;
                if (!baseSizeDelta.HasValue) baseSizeDelta = root.sizeDelta;

                Vector3 p = baseLocalPosition.Value;
                root.localPosition = new Vector3(p.x + offsetX, p.y + offsetY, p.z);
                float safeScale = Mathf.Max(0.01f, scale);
                Vector3 s = baseLocalScale.Value;
                root.localScale = new Vector3(s.x * safeScale, s.y * safeScale, s.z);
                root.sizeDelta = baseSizeDelta.Value;
                root.gameObject.SetActive(enabled);
            }
            catch { }
        }

        private static RectTransform EnsureLegendaryMonOffsetRoot(Image img, ref RectTransform offsetRoot)
        {
            try
            {
                if (img == null) return null;
                if (offsetRoot != null) return offsetRoot;

                RectTransform src = img.GetComponent<RectTransform>();
                if (src == null) return null;

                Transform parent = src.parent;
                if (parent != null && parent.name.StartsWith("MMOnsterpatch_Online_TitleMonOffsetRoot_", StringComparison.OrdinalIgnoreCase))
                {
                    offsetRoot = parent as RectTransform;
                    return offsetRoot;
                }
                if (parent == null) return src;

                GameObject rootObj = new GameObject("MMOnsterpatch_Online_TitleMonOffsetRoot_" + img.gameObject.name, typeof(RectTransform));
                RectTransform rt = rootObj.GetComponent<RectTransform>();
                int sibling = src.GetSiblingIndex();

                rt.SetParent(parent, false);
                rt.SetSiblingIndex(sibling);
                rt.anchorMin = src.anchorMin;
                rt.anchorMax = src.anchorMax;
                rt.pivot = src.pivot;
                rt.anchoredPosition = src.anchoredPosition;
                rt.localPosition = src.localPosition;
                rt.localRotation = src.localRotation;
                rt.localScale = src.localScale;
                rt.sizeDelta = src.sizeDelta;

                src.SetParent(rt, false);
                src.anchorMin = new Vector2(0.5f, 0.5f);
                src.anchorMax = new Vector2(0.5f, 0.5f);
                src.pivot = new Vector2(0.5f, 0.5f);
                src.anchoredPosition = Vector2.zero;
                src.localPosition = Vector3.zero;
                src.localRotation = Quaternion.identity;
                src.localScale = Vector3.one;
                src.sizeDelta = rt.sizeDelta;

                offsetRoot = rt;
                Log("Launcher online title wrapped " + img.gameObject.name + " in movable offset root so config offsets do not fight the native bounce animation.");
                return offsetRoot;
            }
            catch (Exception ex) { Log("EnsureLegendaryMonOffsetRoot failed: " + ex.Message); return null; }
        }

        private static void SyncLegendaryMonChildAnimation(Image mon0, Image mon1)
        {
            try
            {
                if (mon0 == null || mon1 == null) return;
                RectTransform src = mon0.GetComponent<RectTransform>();
                RectTransform dst = mon1.GetComponent<RectTransform>();
                if (src == null || dst == null) return;

                // Keep the added mon's child transform matching the native mon's bounce, while each wrapper/root keeps its own config offset.
                dst.anchoredPosition = src.anchoredPosition;
                dst.localPosition = src.localPosition;
                dst.localRotation = src.localRotation;
                dst.localScale = src.localScale;
                dst.sizeDelta = src.sizeDelta;
            }
            catch { }
        }

        private static Image GetOrCreateLauncherLegendaryMon1(Image mon0, Sprite sprite)
        {
            try
            {
                if (mon0 == null || sprite == null) return null;
                if (_launcherLegendaryMon1Object == null)
                {
                    Transform parent = mon0.transform.parent;
                    if (parent != null && parent.name.StartsWith("MMOnsterpatch_Online_TitleMonOffsetRoot_", StringComparison.OrdinalIgnoreCase))
                        parent = parent.parent;
                    if (parent == null) return null;

                    GameObject clone = UnityEngine.Object.Instantiate(mon0.gameObject, parent, false);
                    clone.name = "MMOnsterpatch_Online_legendaryMons 1_1";
                    clone.transform.SetSiblingIndex(GetLegendaryMonSiblingIndex(mon0) + 1);
                    _launcherLegendaryMon1Object = clone;
                    _legendaryMon1OffsetRoot = null;
                    _legendaryMon1BaseLocalPosition = null;
                    _legendaryMon1BaseLocalScale = null;
                    _legendaryMon1BaseSizeDelta = null;
                    Log("Launcher online title cloned native legendary mon object for legendaryMons 1_1 so the added mon inherits the same animation/components.");
                }
                Image img = _launcherLegendaryMon1Object.GetComponent<Image>();
                if (img == null) img = _launcherLegendaryMon1Object.GetComponentInChildren<Image>(true);
                if (img != null)
                {
                    img.sprite = sprite;
                    img.type = Image.Type.Simple;
                    img.preserveAspect = true;
                    img.raycastTarget = false;
                    img.material = mon0.material;
                    img.color = mon0.color;
                }
                return img;
            }
            catch (Exception ex) { Log("GetOrCreateLauncherLegendaryMon1 failed: " + ex.Message); return null; }
        }

        private static int GetLegendaryMonSiblingIndex(Image mon0)
        {
            try
            {
                Transform t = mon0.transform;
                if (t.parent != null && t.parent.name.StartsWith("MMOnsterpatch_Online_TitleMonOffsetRoot_", StringComparison.OrdinalIgnoreCase))
                    t = t.parent;
                return t.GetSiblingIndex();
            }
            catch { return 0; }
        }

        private static void ApplyLauncherOnlineVersionTitle(object menuObj)
        {
            try
            {
                if (!IsLauncherSessionActive()) return;
                UnityEngine.Object rot = GetMainTitleRotationObject();
                Type rotType = rot != null ? rot.GetType() : FindGameType("MainTitleRotation");
                TMP_Text txt = rot != null ? GetObjectField(rot, "txtVersion") as TMP_Text : null;
                if (txt == null)
                    txt = FindTitleVersionText(menuObj);
                if (txt == null) return;
                txt.text = "Online Version";
                txt.enableWordWrapping = false;
                txt.overflowMode = TextOverflowModes.Overflow;
            }
            catch (Exception ex) { Log("ApplyLauncherOnlineVersionTitle failed: " + ex.Message); }
        }

        private static TMP_Text FindTitleVersionText(object menuObj)
        {
            try
            {
                GameObject menuGo = menuObj as GameObject;
                if (menuGo == null && menuObj is Component comp) menuGo = comp.gameObject;
                TMP_Text best = null;
                TMP_Text[] texts = menuGo != null ? menuGo.GetComponentsInChildren<TMP_Text>(true) : Resources.FindObjectsOfTypeAll<TMP_Text>();
                foreach (TMP_Text t in texts)
                {
                    if (t == null) continue;
                    string cur = t.text ?? string.Empty;
                    string path = GetTransformPath(t.transform);
                    if (path.IndexOf("version", StringComparison.OrdinalIgnoreCase) >= 0 || cur.IndexOf("Version", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        best = t;
                        if (cur.IndexOf("Skyfarer", StringComparison.OrdinalIgnoreCase) >= 0 || cur.IndexOf("Aurora", StringComparison.OrdinalIgnoreCase) >= 0 || cur.IndexOf("Eldenwood", StringComparison.OrdinalIgnoreCase) >= 0)
                            return t;
                    }
                }
                return best;
            }
            catch { return null; }
        }

        private static void ArmLauncherOnlineVersionSelectReturnGuard(int slot)
        {
            try
            {
                _launcherOnlineVersionSelectReturnGuard = true;
                _launcherOnlineVersionSelectReturnGuardUntil = Time.unscaledTime + 30.0f;
                _launcherOnlineVersionSelectReturnSlot = Mathf.Clamp(slot, 0, 5);
            }
            catch { }
        }

        private static bool IsLauncherOnlineSaveSelectReturnContext(int slot)
        {
            try
            {
                if (!IsLauncherSessionActive()) return false;
                if (!_launcherOnlineVersionSelectReturnGuard && !_officialOnlineSaveSessionActive) return false;
                if (_launcherOnlineVersionSelectReturnGuardUntil > 0f && Time.unscaledTime > _launcherOnlineVersionSelectReturnGuardUntil) return false;
                if (_officialOnlineSaveSessionSlot >= 0 && Mathf.Clamp(slot, 0, 5) == Mathf.Clamp(_officialOnlineSaveSessionSlot, 0, 5)) return true;
                if (_launcherOnlineVersionSelectReturnSlot >= 0 && Mathf.Clamp(slot, 0, 5) == Mathf.Clamp(_launcherOnlineVersionSelectReturnSlot, 0, 5)) return true;
                return _onlineSlotPayloadsLoaded || HasAnyOnlineSaveSlotCache();
            }
            catch { return false; }
        }

        private static bool IsLauncherOnlineVersionSelectBackContext(object menuObj)
        {
            try
            {
                if (!IsLauncherSessionActive()) return false;
                if (IsLauncherVersionSelectVisible(menuObj)) return true;
                if (!_launcherOnlineVersionSelectReturnGuard && !_officialOnlineSaveSessionActive) return false;
                if (_launcherOnlineVersionSelectReturnGuardUntil > 0f && Time.unscaledTime > _launcherOnlineVersionSelectReturnGuardUntil) return false;
                return IsMenuSaveFilesVisible(menuObj) || IsAnyServerSessionActive();
            }
            catch { return false; }
        }

        private static bool IsMenuSaveFilesVisible(object menuObj)
        {
            try
            {
                GameObject saveMenu = GetGameObjectField(menuObj, "menuSaveFiles");
                return saveMenu != null && (saveMenu.activeInHierarchy || saveMenu.activeSelf);
            }
            catch { return false; }
        }

        private static bool HasAnyOnlineSaveSlotCache()
        {
            try
            {
                for (int i = 0; i < _onlineSaveSlotCache.Length; i++)
                {
                    if (_onlineSaveSlotCache[i] != null || !string.IsNullOrEmpty(_onlineSaveSlotHandles[i]))
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static void RepairLauncherOnlineSaveSelectAfterVersionBack(object menuObj)
        {
            try
            {
                if (!IsLauncherSessionActive()) return;
                if (!_launcherOnlineVersionSelectReturnGuard && !_officialOnlineSaveSessionActive) return;
                if (_launcherOnlineVersionSelectReturnGuardUntil > 0f && Time.unscaledTime > _launcherOnlineVersionSelectReturnGuardUntil)
                {
                    _launcherOnlineVersionSelectReturnGuard = false;
                    return;
                }
                if (!IsMenuSaveFilesVisible(menuObj)) return;

                _onlineMode = true;
                _authBusy = false;
                _statusOverride = null;
                ApplyOnlineSaveSlotCacheToMenuData();
                ApplyOnlineSaveSlotVisuals();

                int slot = _launcherOnlineVersionSelectReturnSlot >= 0 ? _launcherOnlineVersionSelectReturnSlot : _officialOnlineSaveSessionSlot;
                slot = Mathf.Clamp(slot, 0, 5);
                _lastHoverSlot = slot;
                SetObjectField(menuObj, "curSaveSlot", slot);
                GameObject slotObj = GetBestSaveSlotSelectable(menuObj, slot);
                if (slotObj != null)
                {
                    EnsureSaveSlotSelectableReady(slotObj);
                    SetObjectField(menuObj, "lastButtonObj", slotObj);
                    EventSystem es = GetMenuEventSystem(menuObj);
                    if (es != null && !IsEventSystemAlreadySelecting(es) && es.currentSelectedGameObject != slotObj)
                        es.SetSelectedGameObject(slotObj);
                }
            }
            catch (Exception ex)
            {
                Log("RepairLauncherOnlineSaveSelectAfterVersionBack failed: " + ex.Message);
            }
        }

        private static bool IsLauncherVersionSelectVisible(object menuObj)
        {
            try
            {
                GameObject version = GetGameObjectField(menuObj, "menuVersionSelect");
                if (version == null)
                    version = FindGameObjectByNameContains("menuVersionSelect");
                return version != null && version.activeInHierarchy;
            }
            catch { return false; }
        }

        private static void EnsureLauncherVersionSelectControlsVisible(object menuObj)
        {
            try
            {
                object arrObj = GetObjectField(menuObj, "bVersion");
                Array arr = arrObj as Array;
                if (arr != null)
                {
                    for (int i = 0; i < arr.Length; i++)
                    {
                        object value = arr.GetValue(i);
                        GameObject go = value as GameObject;
                        if (go == null && value is Component c) go = c.gameObject;
                        if (go == null) continue;

                        // Online Mode currently supports only the released Aurora/Skyfarer versions.
                        // Keep the native first two version buttons alive for character creation and
                        // hide any unreleased/extra version buttons without touching the shared cursor.
                        bool allowedReleasedVersion = i < 2;
                        if (go.activeSelf != allowedReleasedVersion)
                            go.SetActive(allowedReleasedVersion);

                        Selectable selectable = go.GetComponent<Selectable>();
                        if (selectable != null)
                        {
                            selectable.interactable = allowedReleasedVersion;
                            selectable.enabled = allowedReleasedVersion;
                        }
                        CanvasGroup cg = go.GetComponent<CanvasGroup>();
                        if (cg != null)
                        {
                            cg.alpha = allowedReleasedVersion ? 1f : 0f;
                            cg.interactable = allowedReleasedVersion;
                            cg.blocksRaycasts = allowedReleasedVersion;
                        }
                    }
                }

                GameObject arrow = GetGameObjectField(menuObj, "versionArrowObj");
                if (arrow != null && !arrow.activeSelf)
                    arrow.SetActive(true);
            }
            catch (Exception ex) { Log("EnsureLauncherVersionSelectControlsVisible failed: " + ex.Message); }
        }

        private static void ApplyLauncherVersionSelectOnlineTheme(object menuObj)
        {
            try
            {
                if (!_onlineMode && !_officialOnlineSaveSessionActive) return;
                Sprite sprite = GetEmbeddedOnlineWallpaperSprite();
                if (sprite == null) return;

                GameObject version = GetGameObjectField(menuObj, "menuVersionSelect");
                if (version == null)
                    version = FindGameObjectByNameContains("menuVersionSelect");
                if (version == null) return;

                Image best = null;
                float bestArea = 0f;
                foreach (Image img in version.GetComponentsInChildren<Image>(true))
                {
                    if (img == null || img.gameObject == null) continue;
                    if (img.GetComponent<Button>() != null || img.GetComponent<Selectable>() != null) continue;
                    string path = GetTransformPath(img.transform).ToLowerInvariant();
                    if (path.Contains("boxsprite") || path.Contains("button") || path.Contains("arrow") || path.Contains("text")) continue;
                    RectTransform rt = img.GetComponent<RectTransform>();
                    float area = rt != null ? Mathf.Abs(rt.rect.width * rt.rect.height) : 0f;
                    if (best == null || area > bestArea)
                    {
                        best = img;
                        bestArea = area;
                    }
                }

                if (best == null)
                    best = version.GetComponent<Image>();
                if (best == null) return;

                if (best.sprite != sprite)
                {
                    best.sprite = sprite;
                    best.type = Image.Type.Simple;
                    best.preserveAspect = false;
                }
                best.color = Color.white;
            }
            catch (Exception ex) { Log("ApplyLauncherVersionSelectOnlineTheme failed: " + ex.Message); }
        }

        private static void HideLauncherSwitchVersionButton(object menuObj)
        {
            try
            {
                if (!IsLauncherSessionActive()) return;
                if (IsLauncherVersionSelectVisible(menuObj))
                {
                    EnsureLauncherVersionSelectControlsVisible(menuObj);
                    return;
                }

                GameObject menuGo = menuObj as GameObject;
                if (menuGo == null && menuObj is Component comp) menuGo = comp.gameObject;

                // Do not hide MenuScript.bVersion[] here. Those buttons are reused by the native
                // new-character Select a Game Version screen. Hiding the Switch Version button on
                // the title screen is handled by the selectable/path scan below.

                // Do not hide MenuScript.versionArrowObj here. The game reuses that object as
                // the native cursor for alpha warning, title buttons, Extras, save slots, and
                // the version picker.

                if (menuGo == null) return;

                foreach (Selectable selectable in menuGo.GetComponentsInChildren<Selectable>(true))
                {
                    if (selectable == null || selectable.gameObject == null) continue;
                    GameObject go = selectable.gameObject;
                    string path = GetTransformPath(go.transform);
                    string name = go.name ?? string.Empty;
                    string text = string.Join(" ", go.GetComponentsInChildren<TMP_Text>(true).Where(t => t != null).Select(t => t.text ?? string.Empty).ToArray());
                    // Hide only the main-title Switch Version control. The game reuses bVersion[]
                    // under menuVersionSelect for new-character version picking, so those buttons must
                    // never be hidden by the title cleanup pass.
                    if (path.IndexOf("menuVersionSelect", StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;
                    bool isSwitchVersion =
                        name.Equals("bSwapVersion", StringComparison.OrdinalIgnoreCase) ||
                        path.IndexOf("bSwapVersion", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (text.IndexOf("Switch", StringComparison.OrdinalIgnoreCase) >= 0 && text.IndexOf("Version", StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (path.IndexOf("Switch", StringComparison.OrdinalIgnoreCase) >= 0 && path.IndexOf("Version", StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (path.IndexOf("cycle", StringComparison.OrdinalIgnoreCase) >= 0 && path.IndexOf("version", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (isSwitchVersion && go.activeSelf)
                    {
                        go.SetActive(false);
                        Log("Launcher online title hid Switch Version selectable: " + path);
                    }
                }
            }
            catch (Exception ex) { Log("HideLauncherSwitchVersionButton failed: " + ex.Message); }
        }

        private static void EnsureLauncherNativeMenuCursorVisible(object menuObj)
        {
            try
            {
                if (!IsLauncherSessionActive() || menuObj == null) return;
                GameObject arrow = GetGameObjectField(menuObj, "versionArrowObj");
                if (arrow == null) return;
                if (!arrow.activeSelf)
                {
                    arrow.SetActive(true);
                    if (Time.unscaledTime - _lastLauncherCursorRepairLog > 5f)
                    {
                        _lastLauncherCursorRepairLog = Time.unscaledTime;
                        Log("Launcher Online Mode restored native menu cursor visibility.");
                    }
                }
            }
            catch (Exception ex) { Log("EnsureLauncherNativeMenuCursorVisible failed: " + ex.Message); }
        }

        private static UnityEngine.Object GetMainTitleRotationObject()
        {
            try
            {
                Type t = FindGameType("MainTitleRotation");
                if (t == null) return null;
                UnityEngine.Object active = UnityEngine.Object.FindObjectOfType(t);
                if (active != null) return active;
                UnityEngine.Object[] all = Resources.FindObjectsOfTypeAll(t);
                return all != null && all.Length > 0 ? all[0] : null;
            }
            catch { return null; }
        }

        private static Image GetMainTitleRotationImage(string fieldName)
        {
            try
            {
                UnityEngine.Object rot = GetMainTitleRotationObject();
                Type t = rot != null ? rot.GetType() : null;
                return GetImageField(rot, t, fieldName);
            }
            catch { return null; }
        }

        private static Image GetImageField(UnityEngine.Object obj, Type type, string fieldName)
        {
            try
            {
                if (obj == null || type == null) return null;
                FieldInfo f = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                return f != null ? f.GetValue(f.IsStatic ? null : obj) as Image : null;
            }
            catch { return null; }
        }

        private static Sprite[] GetSpriteArrayField(UnityEngine.Object obj, Type type, string fieldName)
        {
            try
            {
                if (obj == null || type == null) return null;
                FieldInfo f = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                return f != null ? f.GetValue(f.IsStatic ? null : obj) as Sprite[] : null;
            }
            catch { return null; }
        }

        private static Image FindTitleImageByObjectOrSpriteName(string objectNeedle, string spriteNeedle)
        {
            try
            {
                foreach (Image img in Resources.FindObjectsOfTypeAll<Image>())
                {
                    if (img == null || img.gameObject == null) continue;
                    string n = img.gameObject.name ?? string.Empty;
                    string sn = img.sprite != null ? img.sprite.name ?? string.Empty : string.Empty;
                    if ((!string.IsNullOrEmpty(objectNeedle) && n.IndexOf(objectNeedle, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (!string.IsNullOrEmpty(spriteNeedle) && sn.IndexOf(spriteNeedle, StringComparison.OrdinalIgnoreCase) >= 0))
                        return img;
                }
            }
            catch { }
            return null;
        }

        private static Sprite FindSpriteByNameContains(string needle)
        {
            try
            {
                foreach (Sprite s in Resources.FindObjectsOfTypeAll<Sprite>())
                {
                    if (s != null && s.name != null && s.name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                        return s;
                }
            }
            catch { }
            return null;
        }

        private static void ApplyOnlineTitleLogoSprite(object menuObj)
        {
            try
            {
                Sprite s = GetEmbeddedSprite("MMOnsterpatchTitleLogo.png", ref _embeddedOnlineTitleLogoSprite, ref _embeddedOnlineTitleLogoTexture, "MMOnsterpatchTitleLogo");
                if (s == null) return;
                GameObject logo = null;
                Type gsType = FindGameType("GameScript");
                UnityEngine.Object gsObj = gsType != null ? UnityEngine.Object.FindObjectOfType(gsType) : null;
                if (gsObj != null)
                {
                    FieldInfo f = gsType.GetField("titleLogoObj", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                    logo = f != null ? f.GetValue(f.IsStatic ? null : gsObj) as GameObject : null;
                }
                if (logo == null) logo = FindGameObjectByNameContains("titleLogo");
                Image img = logo != null ? logo.GetComponent<Image>() : null;
                if (img == null) return;
                if (_originalTitleLogoSprite == null) _originalTitleLogoSprite = img.sprite;
                if (img.sprite != s)
                {
                    img.sprite = s;
                    img.type = Image.Type.Simple;
                    img.preserveAspect = true;
                    Log("Launcher online titleLogoObj sprite swapped to MMOnsterpatchTitleLogo.");
                }
            }
            catch (Exception ex) { Log("ApplyOnlineTitleLogoSprite failed: " + ex.Message); }
        }

        private static void ApplyOnlineSavePlusSprite(GameObject plus)
        {
            try
            {
                if (plus == null || !IsLauncherSessionActive()) return;
                Sprite s = GetEmbeddedSprite("MMOnsterpatchSaveFilePlus.png", ref _embeddedOnlineSavePlusSprite, ref _embeddedOnlineSavePlusTexture, "MMOnsterpatchSaveFilePlus");
                if (s == null) return;
                Image img = plus.GetComponent<Image>();
                if (img == null) img = plus.GetComponentInChildren<Image>(true);
                if (img == null) return;
                if (!_originalSavePlusSprites.ContainsKey(img)) _originalSavePlusSprites[img] = img.sprite;
                if (img.sprite != s)
                {
                    img.sprite = s;
                    img.type = Image.Type.Simple;
                    img.preserveAspect = true;
                }
            }
            catch { }
        }

        private static GameObject FindGameObjectByNameContains(string needle)
        {
            try
            {
                foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
                {
                    if (go != null && go.name != null && go.name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) return go;
                }
            }
            catch { }
            return null;
        }

        private static void ApplyOnlineHouseIcon1ToSavePreviewPanel(object menuObj, GameObject panel)
        {
            try
            {
                if (menuObj == null || panel == null || !_onlineMode || !IsOfficialOnlineModeActive())
                    return;

                // OnlinehouseIcon1.png is a color-swapped atlas/sheet that matches vanilla houseIcon1.
                // Do not assign the full atlas to each Image. Keep the native save-preview layout exactly
                // as-is and replace each Image with a sprite made from the same rect/pivot/PPU as its
                // original vanilla sprite.
                Sprite onlineAtlasSprite = GetEmbeddedSprite("OnlinehouseIcon1.png", ref _embeddedOnlineHouseIcon1Sprite, ref _embeddedOnlineHouseIcon1Texture, "OnlinehouseIcon1");
                Texture2D onlineTexture = _embeddedOnlineHouseIcon1Texture;
                if (onlineAtlasSprite == null || onlineTexture == null)
                    return;

                int changed = 0;
                int found = 0;
                Image[] images = panel.GetComponentsInChildren<Image>(true);
                for (int i = 0; i < images.Length; i++)
                {
                    Image img = images[i];
                    if (img == null)
                        continue;

                    Sprite currentSprite = img.sprite;
                    Sprite originalSprite;
                    bool alreadyTracked = _originalSavePreviewHouseSprites.TryGetValue(img, out originalSprite);
                    if (!alreadyTracked)
                        originalSprite = currentSprite;

                    if (originalSprite == null)
                        continue;

                    string objectName = img.gameObject != null ? (img.gameObject.name ?? string.Empty) : string.Empty;
                    string spriteName = originalSprite != null ? (originalSprite.name ?? string.Empty) : string.Empty;
                    string textureName = originalSprite != null && originalSprite.texture != null ? (originalSprite.texture.name ?? string.Empty) : string.Empty;
                    string path = img.transform != null ? GetTransformPath(img.transform) : string.Empty;

                    bool isPreviewHouseIcon1 =
                        alreadyTracked ||
                        objectName.IndexOf("houseIcon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        spriteName.IndexOf("houseIcon1", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        textureName.IndexOf("houseIcon1", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        path.IndexOf("houseIcon", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (!isPreviewHouseIcon1)
                        continue;

                    found++;
                    if (!alreadyTracked)
                        _originalSavePreviewHouseSprites[img] = originalSprite;

                    Sprite slicedOnlineSprite = GetOnlineHouseIcon1SliceForOriginal(originalSprite, onlineTexture);
                    if (slicedOnlineSprite == null)
                        continue;

                    if (!ReferenceEquals(currentSprite, slicedOnlineSprite))
                    {
                        img.sprite = slicedOnlineSprite;
                        // Preserve the native Image type, size, anchors, preserveAspect, scale, and placement.
                        // The only thing replaced is the displayed sprite slice.
                        changed++;
                    }
                }

                if (found > 0 && !_onlineSavePreviewHouseIcon1Logged)
                {
                    _onlineSavePreviewHouseIcon1Logged = true;
                    Log("Online Mode save-select preview replaced native houseIcon1 slices with matching OnlinehouseIcon1.png slices. found=" + found + ", changed=" + changed + ".");
                }
            }
            catch (Exception ex)
            {
                Log("ApplyOnlineHouseIcon1ToSavePreviewPanel failed: " + ex.Message);
            }
        }

        private static void RestoreOnlineHouseIcon1ToSavePreviewPanel()
        {
            try
            {
                if (_originalSavePreviewHouseSprites.Count == 0)
                    return;

                foreach (KeyValuePair<Image, Sprite> kv in _originalSavePreviewHouseSprites.ToArray())
                {
                    Image img = kv.Key;
                    if (img == null)
                        continue;
                    if (kv.Value != null)
                        img.sprite = kv.Value;
                }
            }
            catch { }
        }

        public static void ApplyOnlineHouseIcon1ToMonSummary(object monSummaryObj)
        {
            try
            {
                if (monSummaryObj == null || !IsOfficialOnlineModeActive())
                    return;

                Sprite onlineAtlasSprite = GetEmbeddedSprite("OnlinehouseIcon1.png", ref _embeddedOnlineHouseIcon1Sprite, ref _embeddedOnlineHouseIcon1Texture, "OnlinehouseIcon1");
                Texture2D onlineTexture = _embeddedOnlineHouseIcon1Texture;
                if (onlineAtlasSprite == null || onlineTexture == null)
                    return;

                Type summaryType = monSummaryObj.GetType();
                FieldInfo imgHouseField = summaryType.GetField("imgHouse", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                if (imgHouseField == null)
                    return;

                Image[] imgHouse = imgHouseField.GetValue(monSummaryObj) as Image[];
                if (imgHouse == null || imgHouse.Length == 0)
                    return;

                Sprite originalHouseIcon1 = null;
                FieldInfo gameScriptField = summaryType.GetField("gameScript", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                object gameScriptObj = gameScriptField != null ? gameScriptField.GetValue(monSummaryObj) : null;
                if (gameScriptObj != null)
                {
                    FieldInfo spriteHouseField = gameScriptObj.GetType().GetField("spriteHouse", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                    Sprite[] spriteHouse = spriteHouseField != null ? spriteHouseField.GetValue(gameScriptObj) as Sprite[] : null;
                    if (spriteHouse != null && spriteHouse.Length > 1)
                        originalHouseIcon1 = spriteHouse[1];
                }

                int changed = 0;
                for (int i = 0; i < imgHouse.Length; i++)
                {
                    Image img = imgHouse[i];
                    if (img == null || img.sprite == null)
                        continue;

                    Sprite current = img.sprite;
                    bool isHouseIcon1 = false;
                    if (originalHouseIcon1 != null && ReferenceEquals(current, originalHouseIcon1))
                        isHouseIcon1 = true;
                    else
                    {
                        string spriteName = current.name ?? string.Empty;
                        string textureName = current.texture != null ? (current.texture.name ?? string.Empty) : string.Empty;
                        isHouseIcon1 = spriteName.IndexOf("houseIcon1", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       textureName.IndexOf("houseIcon1", StringComparison.OrdinalIgnoreCase) >= 0;
                    }

                    if (!isHouseIcon1)
                        continue;

                    Sprite slicedOnlineSprite = GetOnlineHouseIcon1SliceForOriginal(current, onlineTexture);
                    if (slicedOnlineSprite == null)
                        continue;

                    if (!ReferenceEquals(img.sprite, slicedOnlineSprite))
                    {
                        img.sprite = slicedOnlineSprite;
                        changed++;
                    }
                }

                if (changed > 0 && !_onlineHouseIcon1SummaryLogged)
                {
                    _onlineHouseIcon1SummaryLogged = true;
                    Log("Online Mode Mon summary replaced houseIcon1 display with matching OnlinehouseIcon1.png slices.");
                }
            }
            catch (Exception ex)
            {
                Log("ApplyOnlineHouseIcon1ToMonSummary failed: " + ex.Message);
            }
        }

        private static Sprite GetOnlineHouseIcon1SliceForOriginal(Sprite originalSprite, Texture2D onlineTexture)
        {
            try
            {
                if (originalSprite == null || onlineTexture == null)
                    return null;

                Rect rect = originalSprite.rect;
                Texture2D originalTexture = originalSprite.texture;

                // The expected case is that OnlinehouseIcon1.png has the same atlas dimensions and layout
                // as vanilla houseIcon1, so the original sprite rect can be reused directly.
                if ((rect.xMin < 0f || rect.yMin < 0f || rect.xMax > onlineTexture.width || rect.yMax > onlineTexture.height) &&
                    originalTexture != null && originalTexture.width > 0 && originalTexture.height > 0)
                {
                    float scaleX = (float)onlineTexture.width / (float)originalTexture.width;
                    float scaleY = (float)onlineTexture.height / (float)originalTexture.height;
                    rect = new Rect(rect.x * scaleX, rect.y * scaleY, rect.width * scaleX, rect.height * scaleY);
                }

                rect.x = Mathf.Clamp(Mathf.Round(rect.x), 0f, Mathf.Max(0f, onlineTexture.width - 1f));
                rect.y = Mathf.Clamp(Mathf.Round(rect.y), 0f, Mathf.Max(0f, onlineTexture.height - 1f));
                rect.width = Mathf.Clamp(Mathf.Round(rect.width), 1f, Mathf.Max(1f, onlineTexture.width - rect.x));
                rect.height = Mathf.Clamp(Mathf.Round(rect.height), 1f, Mathf.Max(1f, onlineTexture.height - rect.y));

                Vector2 pivot = new Vector2(0.5f, 0.5f);
                if (originalSprite.rect.width > 0f && originalSprite.rect.height > 0f)
                {
                    pivot = new Vector2(
                        Mathf.Clamp01(originalSprite.pivot.x / originalSprite.rect.width),
                        Mathf.Clamp01(originalSprite.pivot.y / originalSprite.rect.height));
                }

                float ppu = originalSprite.pixelsPerUnit > 0f ? originalSprite.pixelsPerUnit : 100f;
                Vector4 border = originalSprite.border;
                string key = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0}:{1}:{2}:{3}:{4}:{5}:{6}:{7}:{8}:{9}:{10}",
                    rect.x, rect.y, rect.width, rect.height, pivot.x, pivot.y, ppu, border.x, border.y, border.z, border.w);

                Sprite cached;
                if (_onlineHouseIcon1SliceSprites.TryGetValue(key, out cached) && cached != null)
                    return cached;

                Sprite sliced = Sprite.Create(onlineTexture, rect, pivot, ppu, 0u, SpriteMeshType.FullRect, border);
                if (sliced == null)
                    return null;
                sliced.name = "OnlinehouseIcon1_slice_" + Mathf.RoundToInt(rect.x) + "_" + Mathf.RoundToInt(rect.y) + "_" + Mathf.RoundToInt(rect.width) + "x" + Mathf.RoundToInt(rect.height);
                _onlineHouseIcon1SliceSprites[key] = sliced;
                return sliced;
            }
            catch (Exception ex)
            {
                Log("GetOnlineHouseIcon1SliceForOriginal failed: " + ex.Message);
                return null;
            }
        }

        private static Sprite GetEmbeddedSprite(string suffix, ref Sprite sprite, ref Texture2D texture, string spriteName)
        {
            try
            {
                if (sprite != null) return sprite;
                Assembly asm = typeof(OfficialServerSaveSelectNativeRuntime).Assembly;
                string resourceName = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrEmpty(resourceName)) { Log("Embedded resource not found: " + suffix); return null; }
                using (Stream stream = asm.GetManifestResourceStream(resourceName))
                {
                    if (stream == null) return null;
                    byte[] bytes = new byte[stream.Length];
                    int read = stream.Read(bytes, 0, bytes.Length);
                    if (read <= 0) return null;
                    Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    tex.name = spriteName;
                    tex.filterMode = FilterMode.Point;
                    tex.wrapMode = TextureWrapMode.Clamp;
                    if (!LoadPngIntoTexture(tex, bytes))
                    {
                        UnityEngine.Object.Destroy(tex);
                        return null;
                    }
                    texture = tex;
                    sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                    sprite.name = spriteName;
                    return sprite;
                }
            }
            catch (Exception ex) { Log("GetEmbeddedSprite failed for " + suffix + ": " + ex.Message); return null; }
        }

        private static bool FetchOnlineSaveSlotsFromServer()
        {
            // v0.17.1.2: server is the source of truth for online slot metadata.
            // The launcher/game cache is still useful for previews/fallback, but it must not
            // override newer server saves after an in-game manual save or logout save.
            if (ShouldLazyLoadOnlineSaveSlotPayloads())
            {
                bool metaOk = FetchOnlineSaveSlotMetadataFromServerV2();
                if (metaOk)
                    return true;
                Log("Official Server metadata-only save slot request failed; falling back to launcher cache/full v2 payload request.");
            }

            // Cache is now fallback-only for missing/offline server metadata, not the first source
            // of truth.  This prevents stale OnlineCache slot JSON from making a successful
            // server save look like it was lost when returning to the title or reopening.
            if (ShouldUseOnlineSaveCache() && LoadOnlineSaveSlotsFromCache())
                return true;

            bool v2Ok = FetchOnlineSaveSlotsFromServerV2();
            if (v2Ok)
                return true;
            return FetchOnlineSaveSlotsFromServerLegacyJson();
        }

        private static bool ShouldLazyLoadOnlineSaveSlotPayloads()
        {
            try { EnsureConfig(); return LazyLoadOnlineSaveSlotPayloads == null || LazyLoadOnlineSaveSlotPayloads.Value; }
            catch { return true; }
        }

        private static bool ShouldUseOnlineSaveCache()
        {
            try { EnsureConfig(); return OnlineSaveCacheEnabled == null || OnlineSaveCacheEnabled.Value; }
            catch { return true; }
        }

        private static bool ShouldUseLauncherOnlineSaveCacheUpload()
        {
            try { EnsureConfig(); return ShouldUseOnlineSaveCache() && LauncherUploadsOnlineSaveCache != null && LauncherUploadsOnlineSaveCache.Value; }
            catch { return false; }
        }

        private static string GetOnlineSaveCacheDirectory()
        {
            try
            {
                string env = Environment.GetEnvironmentVariable("MMONSTERPATCH_ONLINE_SAVE_CACHE") ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(env))
                    return env.Trim();
            }
            catch { }
            try
            {
                string account = GTSRuntimeHost.GetAioSessionTokenForSocial();
                if (string.IsNullOrWhiteSpace(account)) account = "unknown";
                account = new string(account.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '.' ? ch : '_').ToArray()).Trim('_');
                if (string.IsNullOrWhiteSpace(account)) account = "unknown";
                return Path.Combine(Paths.BepInExRootPath, "MMOnsterpatch", "OnlineCache", "account_" + account);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetOnlineSaveCacheManifestPath()
        {
            string dir = GetOnlineSaveCacheDirectory();
            return string.IsNullOrWhiteSpace(dir) ? string.Empty : Path.Combine(dir, "manifest.txt");
        }

        private static string GetOnlineSaveCacheSlotJsonPath(int slot)
        {
            string dir = GetOnlineSaveCacheDirectory();
            return string.IsNullOrWhiteSpace(dir) ? string.Empty : Path.Combine(dir, "slot_" + Mathf.Clamp(slot, 0, 5).ToString() + ".json");
        }

        private static bool LoadOnlineSaveSlotsFromCache()
        {
            try
            {
                string manifest = GetOnlineSaveCacheManifestPath();
                if (string.IsNullOrWhiteSpace(manifest) || !File.Exists(manifest))
                    return false;

                string[] lines = File.ReadAllLines(manifest);
                if (lines.Length == 0 || !lines[0].StartsWith("MMON_SAVE_CACHE_V1", StringComparison.OrdinalIgnoreCase))
                    return false;

                SetAllMenuSaveDataNull();
                for (int i = 0; i < 6; i++)
                {
                    _onlineSaveSlotOccupied[i] = false;
                    _onlineSaveSlotPayloadLoaded[i] = false;
                    _onlineSaveSlotDisplayNames[i] = string.Empty;
                    _onlineSaveSlotUpdatedAt[i] = 0;
                    _onlineSaveSlotPlayerDesign[i] = 0;
                    _onlineSaveSlotPlayerColor1[i] = 0;
                    _onlineSaveSlotPlayerColor2[i] = 0;
                }

                int received = 0;
                int occupied = 0;
                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("OFFICIAL_SAVE_SLOT|", StringComparison.OrdinalIgnoreCase))
                        continue;
                    received++;
                    string[] parts = line.Split('|');
                    if (parts.Length < 6) continue;
                    int safeSlot = Mathf.Clamp(ParseIntSafe(parts[1]), 0, 5);
                    bool occ = ParseIntSafe(parts[2]) != 0;
                    string display = FromB64(parts[3]);
                    int updatedAt = ParseIntSafe(parts.Length >= 5 ? parts[4] : "0");
                    string publicHandle = parts.Length >= 7 ? FromB64(parts[6]) : string.Empty;
                    int pvpRegistered = ParseIntSafe(parts.Length >= 8 ? parts[7] : "0");
                    int pvpRp = ParseIntSafe(parts.Length >= 9 ? parts[8] : "0");
                    string pvpRank = parts.Length >= 10 ? FromB64(parts[9]) : string.Empty;
                    int pvpWins = ParseIntSafe(parts.Length >= 11 ? parts[10] : "0");
                    int pvpLosses = ParseIntSafe(parts.Length >= 12 ? parts[11] : "0");
                    int pvpDraws = ParseIntSafe(parts.Length >= 13 ? parts[12] : "0");
                    int playerDesign = ParseIntSafe(parts.Length >= 14 ? parts[13] : "0");
                    int playerColor1 = ParseIntSafe(parts.Length >= 15 ? parts[14] : "0");
                    int playerColor2 = ParseIntSafe(parts.Length >= 16 ? parts[15] : "0");
                    SetOnlineSlotMetadata(safeSlot, occ, display, updatedAt, publicHandle, pvpRegistered != 0, pvpRp, pvpRank, pvpWins, pvpLosses, pvpDraws, playerDesign, playerColor1, playerColor2);
                    if (occ)
                    {
                        occupied++;

                        // Prefer materializing the full local cache payload immediately.
                        // This is local disk only, so it is much faster than the old server
                        // round-trip and keeps hover previews fully native.  If the slot JSON
                        // is missing/corrupt, fall back to a metadata placeholder so the menu
                        // still shows the occupied slot.
                        bool payloadLoaded = LoadOnlineSaveSlotPayloadFromCache(safeSlot);
                        if (!payloadLoaded)
                        {
                            string embeddedSaveJson = parts.Length >= 6 ? FromB64(parts[5]) : string.Empty;
                            payloadLoaded = MaterializeOnlineSaveSlotPayloadFromJson(safeSlot, embeddedSaveJson, publicHandle, display, "launcher cache manifest");
                        }
                        if (!payloadLoaded)
                        {
                            object placeholder = CreateMetadataOnlySaveDataForSlot(safeSlot, display, publicHandle);
                            if (placeholder != null) SetSaveDataForSlot(safeSlot, placeholder);
                            _onlineSaveSlotPayloadLoaded[safeSlot] = false;
                        }
                    }
                    else
                    {
                        SetSaveDataForSlot(safeSlot, null);
                        _onlineSaveSlotPayloadLoaded[safeSlot] = true;
                    }
                }

                _onlineSlotPayloadsLoaded = false;
                Log("Loaded online save slot metadata from launcher cache. received=" + received + ", occupied=" + occupied + ".");
                return received > 0;
            }
            catch (Exception ex)
            {
                Log("LoadOnlineSaveSlotsFromCache failed: " + ex.Message);
                return false;
            }
        }

        private static bool LoadOnlineSaveSlotPayloadFromCache(int slot)
        {
            try
            {
                int safeSlot = Mathf.Clamp(slot, 0, 5);
                string path = GetOnlineSaveCacheSlotJsonPath(safeSlot);
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return false;
                string saveJson = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(saveJson))
                    return false;
                Type saveType = FindGameType("SaveData");
                if (saveType == null)
                    return false;
                object saveObj = FromJsonCompat(saveJson, saveType);
                if (saveObj == null)
                    return false;
                ApplyOnlineSlotServerHandleToSaveData(saveObj, _onlineSaveSlotHandles[safeSlot], _onlineSaveSlotDisplayNames[safeSlot]);
                SetSaveDataForSlot(safeSlot, saveObj);
                _onlineSaveSlotPayloadLoaded[safeSlot] = true;
                _onlineSlotPayloadsLoaded = true;
                Log("Loaded online save slot" + safeSlot + " payload from launcher cache. bytes=" + Encoding.UTF8.GetByteCount(saveJson) + ".");
                return true;
            }
            catch (Exception ex)
            {
                Log("LoadOnlineSaveSlotPayloadFromCache failed for slot" + slot + ": " + ex.Message);
                return false;
            }
        }

        private static bool MaterializeOnlineSaveSlotPayloadFromJson(int slot, string saveJson, string publicHandle, string display, string sourceLabel)
        {
            try
            {
                int safeSlot = Mathf.Clamp(slot, 0, 5);
                if (string.IsNullOrWhiteSpace(saveJson))
                    return false;
                Type saveType = FindGameType("SaveData");
                if (saveType == null)
                    return false;
                object saveObj = FromJsonCompat(saveJson, saveType);
                if (saveObj == null)
                    return false;
                ApplyOnlineSlotServerHandleToSaveData(saveObj, publicHandle, display);
                SetSaveDataForSlot(safeSlot, saveObj);
                _onlineSaveSlotPayloadLoaded[safeSlot] = true;
                _onlineSlotPayloadsLoaded = true;
                Log("Materialized online save slot" + safeSlot + " payload from " + (sourceLabel ?? "cache") + ". bytes=" + Encoding.UTF8.GetByteCount(saveJson) + ".");
                return true;
            }
            catch (Exception ex)
            {
                Log("MaterializeOnlineSaveSlotPayloadFromJson failed for slot" + slot + ": " + ex.Message);
                return false;
            }
        }


        private static bool WriteOnlineSaveSlotCacheAndMarkDirty(object saveDataObj, int slot)
        {
            try
            {
                if (saveDataObj == null) return false;
                int safeSlot = Mathf.Clamp(slot, 0, 5);
                string dir = GetOnlineSaveCacheDirectory();
                if (string.IsNullOrWhiteSpace(dir)) return false;
                Directory.CreateDirectory(dir);
                string json = ToJsonCompat(saveDataObj, true);
                if (string.IsNullOrEmpty(json) || json == "{}")
                {
                    Log("Official Server cache save failed for slot" + safeSlot + ": SaveData serialized empty.");
                    return false;
                }
                string jsonPath = GetOnlineSaveCacheSlotJsonPath(safeSlot);
                File.WriteAllText(jsonPath, json, Encoding.UTF8);
                File.WriteAllText(Path.Combine(dir, "slot_" + safeSlot.ToString() + ".dirty"), DateTime.UtcNow.ToString("o"), Encoding.UTF8);
                UpdateOnlineSaveCacheManifestSlot(safeSlot, saveDataObj, json);
                _onlineSaveSlotPayloadLoaded[safeSlot] = true;
                _onlineSaveSlotOccupied[safeSlot] = true;
                return true;
            }
            catch (Exception ex)
            {
                Log("WriteOnlineSaveSlotCacheAndMarkDirty failed: " + ex.Message);
                return false;
            }
        }

        private static void UpdateOnlineSaveCacheAfterConfirmedServerWrite(int slot, string json, object saveDataObj, string reason)
        {
            try
            {
                int safeSlot = Mathf.Clamp(slot, 0, 5);
                string jsonPath = GetOnlineSaveCacheSlotJsonPath(safeSlot);
                if (string.IsNullOrWhiteSpace(jsonPath) || string.IsNullOrWhiteSpace(json))
                    return;
                string dir = Path.GetDirectoryName(jsonPath);
                if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(jsonPath, json, Encoding.UTF8);

                // This write has already been accepted by the Official Server, so it is no
                // longer a launcher-cleanup dirty payload.  Leaving a stale dirty marker can
                // make the launcher re-upload old data or keep treating the cache as pending.
                string dirtyPath = Path.Combine(dir ?? string.Empty, "slot_" + safeSlot.ToString() + ".dirty");
                try { if (!string.IsNullOrWhiteSpace(dirtyPath) && File.Exists(dirtyPath)) File.Delete(dirtyPath); } catch { }

                object manifestObj = saveDataObj;
                if (manifestObj == null)
                {
                    Type saveType = FindGameType("SaveData");
                    if (saveType != null) manifestObj = FromJsonCompat(json, saveType);
                }
                UpdateOnlineSaveCacheManifestSlot(safeSlot, manifestObj, json);
                _onlineSaveSlotPayloadLoaded[safeSlot] = true;
                _onlineSaveSlotOccupied[safeSlot] = true;
                Log("Synchronized online save cache slot" + safeSlot + " after confirmed Official Server " + (reason ?? "write") + ".");
            }
            catch (Exception ex)
            {
                Log("UpdateOnlineSaveCacheAfterConfirmedServerWrite failed: " + ex.Message);
            }
        }

        private static void UpdateOnlineSaveCacheManifestSlot(int slot, object saveDataObj, string json)
        {
            try
            {
                string manifest = GetOnlineSaveCacheManifestPath();
                if (string.IsNullOrWhiteSpace(manifest)) return;
                string dir = Path.GetDirectoryName(manifest);
                if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                List<string> lines = File.Exists(manifest) ? File.ReadAllLines(manifest).ToList() : new List<string>();
                if (lines.Count == 0 || !lines[0].StartsWith("MMON_SAVE_CACHE_V1", StringComparison.OrdinalIgnoreCase))
                    lines.Insert(0, "MMON_SAVE_CACHE_V1");
                int safeSlot = Mathf.Clamp(slot, 0, 5);
                string display = TryGetSaveDataDisplayName(saveDataObj);
                if (string.IsNullOrWhiteSpace(display)) display = _onlineSaveSlotDisplayNames[safeSlot];
                string handle = _onlineSaveSlotHandles[safeSlot] ?? string.Empty;
                int playerDesign = TryGetSaveDataInt(saveDataObj, "playerDesign", _onlineSaveSlotPlayerDesign[safeSlot]);
                int playerColor1 = TryGetSaveDataInt(saveDataObj, "playerColor1", _onlineSaveSlotPlayerColor1[safeSlot]);
                int playerColor2 = TryGetSaveDataInt(saveDataObj, "playerColor2", _onlineSaveSlotPlayerColor2[safeSlot]);
                string line = "OFFICIAL_SAVE_SLOT|" + safeSlot + "|1|" + B64(display ?? string.Empty) + "|" + DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString() + "|" + B64(json ?? string.Empty) + "|" + B64(handle) +
                              "|" + (_onlineSaveSlotPvpRegistered[safeSlot] ? "1" : "0") + "|" + Math.Max(0, _onlineSaveSlotPvpRp[safeSlot]).ToString() + "|" + B64(_onlineSaveSlotPvpRank[safeSlot] ?? string.Empty) +
                              "|" + Math.Max(0, _onlineSaveSlotPvpWins[safeSlot]).ToString() + "|" + Math.Max(0, _onlineSaveSlotPvpLosses[safeSlot]).ToString() + "|" + Math.Max(0, _onlineSaveSlotPvpDraws[safeSlot]).ToString() +
                              "|" + playerDesign.ToString() + "|" + playerColor1.ToString() + "|" + playerColor2.ToString();
                bool replaced = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i].StartsWith("OFFICIAL_SAVE_SLOT|" + safeSlot + "|", StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = line;
                        replaced = true;
                        break;
                    }
                }
                if (!replaced) lines.Add(line);
                File.WriteAllLines(manifest, lines.ToArray(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Log("UpdateOnlineSaveCacheManifestSlot failed: " + ex.Message);
            }
        }

        private static string TryGetSaveDataDisplayName(object saveDataObj)
        {
            try
            {
                if (saveDataObj == null) return string.Empty;
                Type t = saveDataObj.GetType();
                string[] names = { "playerName", "PlayerName", "name", "Name", "displayName", "DisplayName" };
                foreach (string n in names)
                {
                    FieldInfo f = t.GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    object v = f != null ? f.GetValue(saveDataObj) : null;
                    if (v != null && !string.IsNullOrWhiteSpace(v.ToString())) return v.ToString();
                    PropertyInfo p = t.GetProperty(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    v = p != null && p.CanRead ? p.GetValue(saveDataObj, null) : null;
                    if (v != null && !string.IsNullOrWhiteSpace(v.ToString())) return v.ToString();
                }
            }
            catch { }
            return string.Empty;
        }


        private static bool FetchOnlineSaveSlotMetadataFromServerV2()
        {
            try
            {
                string token = GTSRuntimeHost.GetAioSessionTokenForSocial();
                if (string.IsNullOrEmpty(token))
                {
                    Log("Cannot load Official Server save slot metadata: launcher session token is empty.");
                    SetAllMenuSaveDataNull();
                    return false;
                }

                List<string> lines = SendOfficialServerRequestLines("OFFICIAL_SAVE_SLOTS_META_REQ|" + B64(token), "OFFICIAL_SAVE_SLOTS_META_DONE", "OFFICIAL_SAVE_ERROR");
                if (lines == null || lines.Count == 0)
                {
                    Log("Official Server metadata save slot request returned no lines.");
                    SetAllMenuSaveDataNull();
                    return false;
                }

                string error = lines.FirstOrDefault(x => x != null && x.StartsWith("OFFICIAL_SAVE_ERROR|", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(error))
                {
                    Log("Official Server metadata save slot request failed: " + DecodeServerError(error));
                    SetAllMenuSaveDataNull();
                    return false;
                }

                SetAllMenuSaveDataNull();
                int received = 0;
                int occupied = 0;
                for (int i = 0; i < 6; i++)
                {
                    _onlineSaveSlotOccupied[i] = false;
                    _onlineSaveSlotPayloadLoaded[i] = false;
                    _onlineSaveSlotDisplayNames[i] = string.Empty;
                    _onlineSaveSlotUpdatedAt[i] = 0;
                    _onlineSaveSlotPlayerDesign[i] = 0;
                    _onlineSaveSlotPlayerColor1[i] = 0;
                    _onlineSaveSlotPlayerColor2[i] = 0;
                }

                foreach (string line in lines)
                {
                    if (string.IsNullOrEmpty(line) || !line.StartsWith("OFFICIAL_SAVE_SLOT_META|", StringComparison.OrdinalIgnoreCase))
                        continue;
                    received++;
                    string[] parts = line.Split('|');
                    if (parts.Length < 6)
                        continue;
                    int safeSlot = ParseIntSafe(parts[1]);
                    safeSlot = Mathf.Clamp(safeSlot, 0, 5);
                    int occ = ParseIntSafe(parts[2]);
                    string display = FromB64(parts[3]);
                    int updatedAt = ParseIntSafe(parts[4]);
                    string publicHandle = parts.Length >= 6 ? FromB64(parts[5]) : string.Empty;
                    int pvpRegistered = ParseIntSafe(parts.Length >= 7 ? parts[6] : "0");
                    int pvpRp = ParseIntSafe(parts.Length >= 8 ? parts[7] : "0");
                    string pvpRank = parts.Length >= 9 ? FromB64(parts[8]) : string.Empty;
                    int pvpWins = ParseIntSafe(parts.Length >= 10 ? parts[9] : "0");
                    int pvpLosses = ParseIntSafe(parts.Length >= 11 ? parts[10] : "0");
                    int pvpDraws = ParseIntSafe(parts.Length >= 12 ? parts[11] : "0");
                    int playerDesign = ParseIntSafe(parts.Length >= 13 ? parts[12] : "0");
                    int playerColor1 = ParseIntSafe(parts.Length >= 14 ? parts[13] : "0");
                    int playerColor2 = ParseIntSafe(parts.Length >= 15 ? parts[14] : "0");
                    SetOnlineSlotMetadata(safeSlot, occ != 0, display, updatedAt, publicHandle, pvpRegistered != 0, pvpRp, pvpRank, pvpWins, pvpLosses, pvpDraws, playerDesign, playerColor1, playerColor2);
                    if (occ != 0)
                    {
                        occupied++;
                        object placeholder = CreateMetadataOnlySaveDataForSlot(safeSlot, display, publicHandle);
                        if (placeholder != null)
                        {
                            SetSaveDataForSlot(safeSlot, placeholder);
                            _onlineSaveSlotPayloadLoaded[safeSlot] = false;
                        }
                    }
                }

                _onlineSlotPayloadsLoaded = false;
                Log("Loaded Official Server online save slot metadata via v2. received=" + received + ", occupied=" + occupied + ". Full save payloads will lazy-load on slot selection.");
                return true;
            }
            catch (Exception ex)
            {
                Log("FetchOnlineSaveSlotMetadataFromServerV2 failed: " + ex.Message);
                SetAllMenuSaveDataNull();
                return false;
            }
        }

        private static object CreateMetadataOnlySaveDataForSlot(int slot, string display, string publicHandle)
        {
            try
            {
                Type saveType = FindGameType("SaveData");
                if (saveType == null)
                    return null;
                object saveObj = Activator.CreateInstance(saveType);
                string name = !string.IsNullOrEmpty(publicHandle) ? publicHandle : (!string.IsNullOrEmpty(display) ? display : ("Online Slot " + (slot + 1)));
                FieldInfo playerName = saveType.GetField("playerName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (playerName != null && playerName.FieldType == typeof(string))
                    playerName.SetValue(saveObj, name);
                ApplyOnlineSlotAppearanceMetadataToSaveData(saveObj, slot);
                FieldInfo day = saveType.GetField("day", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (day != null && day.FieldType == typeof(int))
                    day.SetValue(saveObj, 1);
                FieldInfo teamMons = saveType.GetField("teamMons", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (teamMons != null && teamMons.FieldType == typeof(string[]))
                    teamMons.SetValue(saveObj, new string[4]);
                FieldInfo lastEncounterMons = saveType.GetField("lastEncounterMons", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (lastEncounterMons != null && lastEncounterMons.FieldType == typeof(string[]))
                    lastEncounterMons.SetValue(saveObj, new string[4]);
                return saveObj;
            }
            catch (Exception ex)
            {
                Log("CreateMetadataOnlySaveDataForSlot failed for slot" + slot + ": " + ex.Message);
                return null;
            }
        }

        private static bool EnsureOnlineSaveSlotPayloadLoaded(int slot)
        {
            try
            {
                int safeSlot = Mathf.Clamp(slot, 0, 5);
                if (!_onlineSaveSlotOccupied[safeSlot])
                    return true;

                // v0.17.1.2: always prefer the server payload for actually entering an
                // online character.  Cached payloads are allowed only as fallback/preview.
                // Without this, direct server saves can succeed while a stale local cache
                // still reloads the old position after logout/title return.
                _statusOverride = "Server Status: Loading Slot";
                UpdateVisualState();
                bool loaded = FetchOnlineSaveSlotPayloadFromServerV2(safeSlot);
                if (!loaded && ShouldUseOnlineSaveCache())
                {
                    Log("Official Server slot" + safeSlot + " server payload load failed; falling back to launcher cache payload.");
                    loaded = LoadOnlineSaveSlotPayloadFromCache(safeSlot);
                }
                _statusOverride = loaded ? null : "Server Status: Slot Load Failed";
                UpdateVisualState();
                return loaded;
            }
            catch (Exception ex)
            {
                Log("EnsureOnlineSaveSlotPayloadLoaded failed for slot" + slot + ": " + ex.Message);
                return false;
            }
        }

        private static bool FetchOnlineSaveSlotPayloadFromServerV2(int slot)
        {
            try
            {
                string token = GTSRuntimeHost.GetAioSessionTokenForSocial();
                if (string.IsNullOrEmpty(token))
                {
                    Log("Cannot load Official Server save slot payload: launcher session token is empty.");
                    return false;
                }
                int safeSlot = Mathf.Clamp(slot, 0, 5);
                List<string> lines = SendOfficialServerRequestLines("OFFICIAL_SAVE_SLOT_READ_REQ|" + B64(token) + "|" + safeSlot.ToString(), "OFFICIAL_SAVE_SLOT_READ_DONE", "OFFICIAL_SAVE_ERROR");
                if (lines == null || lines.Count == 0)
                {
                    Log("Official Server slot" + safeSlot + " payload request returned no lines.");
                    return false;
                }
                string error = lines.FirstOrDefault(x => x != null && x.StartsWith("OFFICIAL_SAVE_ERROR|", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(error))
                {
                    Log("Official Server slot" + safeSlot + " payload request failed: " + DecodeServerError(error));
                    return false;
                }
                string data = lines.FirstOrDefault(x => x != null && x.StartsWith("OFFICIAL_SAVE_SLOT_DATA|", StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrEmpty(data))
                {
                    Log("Official Server slot" + safeSlot + " payload response missing data line.");
                    return false;
                }
                string[] parts = data.Split('|');
                if (parts.Length < 6)
                    return false;
                int responseSlot = Mathf.Clamp(ParseIntSafe(parts[1]), 0, 5);
                int occ = ParseIntSafe(parts[2]);
                string display = FromB64(parts[3]);
                int updatedAt = ParseIntSafe(parts[4]);
                string saveJson = FromB64(parts[5]);
                string publicHandle = parts.Length >= 7 ? FromB64(parts[6]) : string.Empty;
                int pvpRegistered = ParseIntSafe(parts.Length >= 8 ? parts[7] : "0");
                int pvpRp = ParseIntSafe(parts.Length >= 9 ? parts[8] : "0");
                string pvpRank = parts.Length >= 10 ? FromB64(parts[9]) : string.Empty;
                int pvpWins = ParseIntSafe(parts.Length >= 11 ? parts[10] : "0");
                int pvpLosses = ParseIntSafe(parts.Length >= 12 ? parts[11] : "0");
                int pvpDraws = ParseIntSafe(parts.Length >= 13 ? parts[12] : "0");
                int playerDesign = ParseIntSafe(parts.Length >= 14 ? parts[13] : "0");
                int playerColor1 = ParseIntSafe(parts.Length >= 15 ? parts[14] : "0");
                int playerColor2 = ParseIntSafe(parts.Length >= 16 ? parts[15] : "0");
                SetOnlineSlotMetadata(responseSlot, occ != 0, display, updatedAt, publicHandle, pvpRegistered != 0, pvpRp, pvpRank, pvpWins, pvpLosses, pvpDraws, playerDesign, playerColor1, playerColor2);
                if (occ == 0)
                {
                    SetSaveDataForSlot(responseSlot, null);
                    _onlineSaveSlotPayloadLoaded[responseSlot] = true;
                    return true;
                }
                Type saveType = FindGameType("SaveData");
                if (saveType == null || string.IsNullOrEmpty(saveJson))
                    return false;
                object saveObj = FromJsonCompat(saveJson, saveType);
                if (saveObj == null)
                    return false;
                ApplyOnlineSlotServerHandleToSaveData(saveObj, publicHandle, display);
                SetSaveDataForSlot(responseSlot, saveObj);
                _onlineSaveSlotPayloadLoaded[responseSlot] = true;
                _onlineSlotPayloadsLoaded = true;
                Log("Lazy-loaded Official Server slot" + responseSlot + " payload: display=" + display + ", handle=" + (string.IsNullOrEmpty(publicHandle) ? "none" : publicHandle) + ", bytes=" + Encoding.UTF8.GetByteCount(saveJson) + ".");
                return true;
            }
            catch (Exception ex)
            {
                Log("FetchOnlineSaveSlotPayloadFromServerV2 failed: " + ex.Message);
                return false;
            }
        }

        private static bool FetchOnlineSaveSlotsFromServerV2()
        {
            try
            {
                string token = GTSRuntimeHost.GetAioSessionTokenForSocial();
                if (string.IsNullOrEmpty(token))
                {
                    Log("Cannot load Official Server save slots: launcher session token is empty.");
                    SetAllMenuSaveDataNull();
                    return false;
                }

                List<string> lines = SendOfficialServerRequestLines("OFFICIAL_SAVE_SLOTS2_REQ|" + B64(token), "OFFICIAL_SAVE_SLOTS_DONE", "OFFICIAL_SAVE_ERROR");
                if (lines == null || lines.Count == 0)
                {
                    Log("Official Server v2 save slot request returned no lines.");
                    SetAllMenuSaveDataNull();
                    return false;
                }

                string error = lines.FirstOrDefault(x => x != null && x.StartsWith("OFFICIAL_SAVE_ERROR|", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(error))
                {
                    Log("Official Server v2 save slot request failed: " + DecodeServerError(error));
                    SetAllMenuSaveDataNull();
                    return false;
                }

                Type saveType = FindGameType("SaveData");
                if (saveType == null)
                {
                    Log("Official Server v2 save slot request failed: SaveData type not found.");
                    SetAllMenuSaveDataNull();
                    return false;
                }

                SetAllMenuSaveDataNull();
                int received = 0;
                int occupied = 0;
                int materialized = 0;
                foreach (string line in lines)
                {
                    if (string.IsNullOrEmpty(line) || !line.StartsWith("OFFICIAL_SAVE_SLOT|", StringComparison.OrdinalIgnoreCase))
                        continue;
                    received++;
                    string[] parts = line.Split('|');
                    if (parts.Length < 6)
                        continue;
                    int safeSlot = 0;
                    int.TryParse(parts[1], out safeSlot);
                    safeSlot = Mathf.Clamp(safeSlot, 0, 5);
                    int occ = 0;
                    int.TryParse(parts[2], out occ);
                    string display = FromB64(parts[3]);
                    string saveJson = FromB64(parts[5]);
                    string publicHandle = parts.Length >= 7 ? FromB64(parts[6]) : string.Empty;
                    int pvpRegistered = ParseIntSafe(parts.Length >= 8 ? parts[7] : "0");
                    int pvpRp = ParseIntSafe(parts.Length >= 9 ? parts[8] : "0");
                    string pvpRank = parts.Length >= 10 ? FromB64(parts[9]) : string.Empty;
                    int pvpWins = ParseIntSafe(parts.Length >= 11 ? parts[10] : "0");
                    int pvpLosses = ParseIntSafe(parts.Length >= 12 ? parts[11] : "0");
                    int pvpDraws = ParseIntSafe(parts.Length >= 13 ? parts[12] : "0");
                    int playerDesign = ParseIntSafe(parts.Length >= 14 ? parts[13] : "0");
                    int playerColor1 = ParseIntSafe(parts.Length >= 15 ? parts[14] : "0");
                    int playerColor2 = ParseIntSafe(parts.Length >= 16 ? parts[15] : "0");
                    SetOnlineSlotMetadata(safeSlot, occ != 0, display, 0, publicHandle, pvpRegistered != 0, pvpRp, pvpRank, pvpWins, pvpLosses, pvpDraws, playerDesign, playerColor1, playerColor2);
                    if (occ != 0 && !string.IsNullOrEmpty(saveJson))
                    {
                        occupied++;
                        object saveObj = FromJsonCompat(saveJson, saveType);
                        if (saveObj != null)
                        {
                            ApplyOnlineSlotServerHandleToSaveData(saveObj, publicHandle, display);
                            SetSaveDataForSlot(safeSlot, saveObj);
                            _onlineSaveSlotPayloadLoaded[safeSlot] = true;
                            materialized++;
                            Log("Loaded Official Server slot" + safeSlot + " from server: display=" + display + ", handle=" + (string.IsNullOrEmpty(publicHandle) ? "none" : publicHandle) + ", bytes=" + Encoding.UTF8.GetByteCount(saveJson) + ".");
                        }
                        else
                        {
                            Log("Official Server slot" + safeSlot + " was occupied but SaveData deserialize returned null. display=" + display + ", bytes=" + Encoding.UTF8.GetByteCount(saveJson) + ".");
                        }
                    }
                }

                string done = lines.FirstOrDefault(x => x != null && x.StartsWith("OFFICIAL_SAVE_SLOTS_DONE|", StringComparison.OrdinalIgnoreCase));
                _onlineSlotPayloadsLoaded = true;
                Log("Loaded Official Server online save slots via v2. received=" + received + ", occupied=" + occupied + ", materialized=" + materialized + ". done=" + (done ?? "missing") + ".");
                return true;
            }
            catch (Exception ex)
            {
                Log("FetchOnlineSaveSlotsFromServerV2 failed: " + ex.Message);
                SetAllMenuSaveDataNull();
                return false;
            }
        }

        private static bool FetchOnlineSaveSlotsFromServerLegacyJson()
        {
            try
            {
                string token = GTSRuntimeHost.GetAioSessionTokenForSocial();
                if (string.IsNullOrEmpty(token))
                {
                    Log("Cannot load Official Server save slots: launcher session token is empty.");
                    SetAllMenuSaveDataNull();
                    return false;
                }

                string line = SendOfficialServerRequest("OFFICIAL_SAVE_SLOTS_REQ|" + B64(token), "OFFICIAL_SAVE_SLOTS", "OFFICIAL_SAVE_ERROR");
                if (string.IsNullOrEmpty(line) || line.StartsWith("OFFICIAL_SAVE_ERROR|", StringComparison.OrdinalIgnoreCase))
                {
                    Log("Official Server legacy save slot request failed: " + DecodeServerError(line));
                    SetAllMenuSaveDataNull();
                    return false;
                }

                string[] parts = line.Split(new char[] { '|' }, 2);
                if (parts.Length < 2)
                {
                    SetAllMenuSaveDataNull();
                    return false;
                }

                string json = FromB64(parts[1]);
                OfficialOnlineSaveSlotsResponse resp = FromJsonCompat(json, typeof(OfficialOnlineSaveSlotsResponse)) as OfficialOnlineSaveSlotsResponse;
                SetAllMenuSaveDataNull();
                int occupied = 0;
                int materialized = 0;
                if (resp != null && resp.slots != null)
                {
                    Type saveType = FindGameType("SaveData");
                    foreach (OfficialOnlineSaveSlotInfo slot in resp.slots)
                    {
                        if (slot == null) continue;
                        int safeSlot = Mathf.Clamp(slot.slot, 0, 5);
                        if (slot.occupied != 0 && !string.IsNullOrEmpty(slot.save_json) && saveType != null)
                        {
                            occupied++;
                            object saveObj = FromJsonCompat(slot.save_json, saveType);
                            if (saveObj != null)
                            {
                                SetOnlineSlotProfile(safeSlot, slot.public_handle, slot.pvp_registered != 0, slot.pvp_rp, slot.pvp_rank, slot.pvp_wins, slot.pvp_losses, slot.pvp_draws);
                                ApplyOnlineSlotServerHandleToSaveData(saveObj, slot.public_handle, slot.display_name);
                                SetSaveDataForSlot(safeSlot, saveObj);
                                materialized++;
                            }
                        }
                    }
                }
                _onlineSlotPayloadsLoaded = true;
                Log("Loaded Official Server online save slots via legacy JSON. Occupied=" + occupied + "/6, materialized=" + materialized + "/6.");
                return true;
            }
            catch (Exception ex)
            {
                Log("FetchOnlineSaveSlotsFromServerLegacyJson failed: " + ex.Message);
                SetAllMenuSaveDataNull();
                return false;
            }
        }

        private static Type GetJsonUtilityTypeCompat()
        {
            try
            {
                Type t = Type.GetType("UnityEngine.JsonUtility, UnityEngine.JSONSerializeModule");
                if (t != null)
                    return t;

                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        t = asm.GetType("UnityEngine.JsonUtility");
                        if (t != null)
                            return t;
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }

        private static object FromJsonCompat(string json, Type targetType)
        {
            try
            {
                if (string.IsNullOrEmpty(json) || targetType == null)
                    return null;

                Type jsonUtilityType = GetJsonUtilityTypeCompat();
                if (jsonUtilityType == null)
                {
                    Log("UnityEngine.JsonUtility type was not found for Official Server JSON load.");
                    return null;
                }

                MethodInfo typedFromJson = jsonUtilityType.GetMethod("FromJson", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(string), typeof(Type) }, null);
                if (typedFromJson != null)
                    return typedFromJson.Invoke(null, new object[] { json, targetType });

                MethodInfo genericFromJson = jsonUtilityType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "FromJson" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
                if (genericFromJson != null)
                    return genericFromJson.MakeGenericMethod(targetType).Invoke(null, new object[] { json });
            }
            catch (Exception ex)
            {
                Log("Official Server JSON load failed: " + ex.Message);
            }
            return null;
        }

        private static string ToJsonCompat(object obj, bool prettyPrint)
        {
            try
            {
                if (obj == null)
                    return null;

                Type jsonUtilityType = GetJsonUtilityTypeCompat();
                if (jsonUtilityType == null)
                {
                    Log("UnityEngine.JsonUtility type was not found for Official Server JSON save.");
                    return null;
                }

                MethodInfo toJson = jsonUtilityType.GetMethod("ToJson", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(object), typeof(bool) }, null);
                if (toJson != null)
                    return toJson.Invoke(null, new object[] { obj, prettyPrint }) as string;

                toJson = jsonUtilityType.GetMethod("ToJson", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(object) }, null);
                if (toJson != null)
                    return toJson.Invoke(null, new object[] { obj }) as string;
            }
            catch (Exception ex)
            {
                Log("Official Server JSON save failed: " + ex.Message);
            }
            return null;
        }

        private static bool ShouldQueueAsyncOfficialSaveUploads()
        {
            try { EnsureConfig(); return AsyncOfficialSaveUploads == null || AsyncOfficialSaveUploads.Value; }
            catch { return true; }
        }

        private static int GetFinalSaveFlushTimeoutMs()
        {
            try { EnsureConfig(); return FinalSaveFlushTimeoutMs != null ? Mathf.Clamp(FinalSaveFlushTimeoutMs.Value, 0, 15000) : 2500; }
            catch { return 2500; }
        }

        private static bool QueueOnlineSaveSlotUploadToServer(object saveDataObj, int slot)
        {
            try
            {
                if (saveDataObj == null)
                {
                    Log("Official Server async save upload skipped for slot" + slot + ": saveData is null.");
                    return false;
                }
                string token = GTSRuntimeHost.GetAioSessionTokenForSocial();
                if (string.IsNullOrEmpty(token))
                {
                    Log("Official Server async save upload failed for slot" + slot + ": launcher session token is empty.");
                    return false;
                }
                string json = ToJsonCompat(saveDataObj, true);
                if (string.IsNullOrEmpty(json) || json == "{}")
                {
                    Log("Official Server async save upload failed for slot" + slot + ": SaveData serialized empty.");
                    return false;
                }

                EnsureConfig();
                string host = ServerHost != null ? (ServerHost.Value ?? "").Trim() : "mmo.gamingwithgoose.com";
                int port = SocialPort != null ? SocialPort.Value : 61529;
                int timeout = NetworkTimeoutMs != null ? Mathf.Clamp(NetworkTimeoutMs.Value, 1000, 20000) : 5000;
                int safeSlot = Mathf.Clamp(slot, 0, 5);

                lock (_officialSaveUploadLock)
                {
                    _officialSaveUploadPendingSlot = safeSlot;
                    _officialSaveUploadPendingToken = token;
                    _officialSaveUploadPendingJson = json;
                    _officialSaveUploadPendingHost = host;
                    _officialSaveUploadPendingPort = port;
                    _officialSaveUploadPendingTimeoutMs = timeout;
                    _officialSaveUploadPending = true;
                    _officialSaveUploadGeneration++;
                    if (!_officialSaveUploadInFlight)
                    {
                        _officialSaveUploadInFlight = true;
                        ThreadPool.QueueUserWorkItem(_ => OfficialSaveUploadWorker());
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Log("QueueOnlineSaveSlotUploadToServer failed: " + ex.Message);
                return false;
            }
        }

        private static void OfficialSaveUploadWorker()
        {
            for (;;)
            {
                int slot;
                int generation;
                string token;
                string json;
                string host;
                int port;
                int timeout;
                lock (_officialSaveUploadLock)
                {
                    if (!_officialSaveUploadPending)
                    {
                        _officialSaveUploadInFlight = false;
                        return;
                    }
                    slot = _officialSaveUploadPendingSlot;
                    token = _officialSaveUploadPendingToken;
                    json = _officialSaveUploadPendingJson;
                    host = _officialSaveUploadPendingHost;
                    port = _officialSaveUploadPendingPort;
                    timeout = _officialSaveUploadPendingTimeoutMs;
                    generation = _officialSaveUploadGeneration;
                    _officialSaveUploadPending = false;
                }

                string error;
                bool ok = UploadOnlineSaveJsonToServerBlocking(token, slot, json, host, port, timeout, out error);
                lock (_officialSaveUploadLock)
                {
                    _officialSaveUploadLastCompletedGeneration = Math.Max(_officialSaveUploadLastCompletedGeneration, generation);
                    _officialSaveUploadLastSuccess = ok;
                    _officialSaveUploadLastError = error ?? string.Empty;
                    if (ok)
                    {
                        _statusOverride = null;
                        _lastOfficialSaveUploadCompletedAt = DateTime.UtcNow.Ticks / (double)TimeSpan.TicksPerSecond;
                    }
                    else
                        _statusOverride = "Server Status: Save Failed";
                }
                if (ok)
                {
                    UpdateOnlineSaveCacheAfterConfirmedServerWrite(slot, json, null, "async upload");
                }
                try
                {
                    if (ok) Console.WriteLine("[MMOnsterpatch Official Server] Async Official Server save upload completed for slot" + slot + ".");
                    else Console.WriteLine("[MMOnsterpatch Official Server] Async Official Server save upload failed for slot" + slot + ": " + error);
                }
                catch { }
            }
        }

        private static bool WaitForQueuedOfficialSaveUpload(int timeoutMs, string reason)
        {
            try
            {
                int target;
                lock (_officialSaveUploadLock)
                    target = _officialSaveUploadGeneration;
                if (target <= 0)
                    return true;

                DateTime until = DateTime.UtcNow.AddMilliseconds(Mathf.Max(0, timeoutMs));
                while (DateTime.UtcNow < until)
                {
                    lock (_officialSaveUploadLock)
                    {
                        if (!_officialSaveUploadPending && !_officialSaveUploadInFlight && _officialSaveUploadLastCompletedGeneration >= target)
                            return _officialSaveUploadLastSuccess;
                    }
                    Thread.Sleep(25);
                }
                Log("Timed out waiting " + timeoutMs + "ms for queued Official Server save upload before " + reason + ". Continuing disconnect to avoid a long freeze.");
                return false;
            }
            catch (Exception ex)
            {
                Log("WaitForQueuedOfficialSaveUpload failed: " + ex.Message);
                return false;
            }
        }

        private static bool UploadOnlineSaveJsonToServerBlocking(string token, int slot, string json, string host, int port, int timeout, out string error)
        {
            error = string.Empty;
            try
            {
                if (string.IsNullOrEmpty(token)) { error = "launcher session token is empty"; return false; }
                if (string.IsNullOrEmpty(json) || json == "{}") { error = "SaveData serialized empty"; return false; }
                string request = "OFFICIAL_SAVE_WRITE|" + B64(token) + "|" + Math.Max(0, Math.Min(5, slot)).ToString() + "|" + B64(json);
                string line = SendOfficialServerRequestBlocking(host, port, timeout, request, "OFFICIAL_SAVE_WRITE_OK", "OFFICIAL_SAVE_ERROR");
                if (!string.IsNullOrEmpty(line) && line.StartsWith("OFFICIAL_SAVE_WRITE_OK|", StringComparison.OrdinalIgnoreCase))
                    return true;
                error = DecodeServerError(line);
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string SendOfficialServerRequestBlocking(string host, int port, int timeout, string request, params string[] acceptedPrefixes)
        {
            using (TcpClient c = new TcpClient())
            {
                c.NoDelay = true;
                c.ReceiveTimeout = timeout;
                c.SendTimeout = timeout;
                IAsyncResult ar = c.BeginConnect(host, port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(timeout))
                    throw new TimeoutException("Timed out connecting to " + host + ":" + port);
                c.EndConnect(ar);
                using (NetworkStream ns = c.GetStream())
                using (StreamReader reader = new StreamReader(ns, Encoding.UTF8))
                using (StreamWriter writer = new StreamWriter(ns, new UTF8Encoding(false)))
                {
                    writer.AutoFlush = true;
                    try { reader.ReadLine(); } catch { }
                    writer.WriteLine(request);
                    DateTime until = DateTime.UtcNow.AddMilliseconds(timeout);
                    while (DateTime.UtcNow < until)
                    {
                        string line = reader.ReadLine();
                        if (line == null)
                            break;
                        foreach (string prefix in acceptedPrefixes)
                        {
                            if (!string.IsNullOrEmpty(prefix) && line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                                return line;
                        }
                    }
                }
            }
            return string.Empty;
        }

        private static bool UploadOnlineSaveSlotToServer(object saveDataObj, int slot)
        {
            try
            {
                if (saveDataObj == null)
                {
                    Log("Official Server save upload skipped for slot" + slot + ": saveData is null.");
                    return false;
                }
                string token = GTSRuntimeHost.GetAioSessionTokenForSocial();
                if (string.IsNullOrEmpty(token))
                {
                    Log("Official Server save upload failed for slot" + slot + ": launcher session token is empty.");
                    return false;
                }
                string json = ToJsonCompat(saveDataObj, true);
                if (string.IsNullOrEmpty(json) || json == "{}")
                {
                    Log("Official Server save upload failed for slot" + slot + ": SaveData serialized empty.");
                    return false;
                }
                string line = SendOfficialServerRequest("OFFICIAL_SAVE_WRITE|" + B64(token) + "|" + Math.Max(0, Math.Min(5, slot)).ToString() + "|" + B64(json), "OFFICIAL_SAVE_WRITE_OK", "OFFICIAL_SAVE_ERROR");
                if (!string.IsNullOrEmpty(line) && line.StartsWith("OFFICIAL_SAVE_WRITE_OK|", StringComparison.OrdinalIgnoreCase))
                {
                    _lastOfficialSaveUploadCompletedAt = DateTime.UtcNow.Ticks / (double)TimeSpan.TicksPerSecond;
                    UpdateOnlineSaveCacheAfterConfirmedServerWrite(Mathf.Clamp(slot, 0, 5), json, saveDataObj, "direct upload");
                    return true;
                }
                Log("Official Server save upload failed for slot" + slot + ": " + DecodeServerError(line));
                return false;
            }
            catch (Exception ex)
            {
                Log("UploadOnlineSaveSlotToServer failed: " + ex.Message);
                return false;
            }
        }

        private static List<string> SendOfficialServerRequestLines(string request, string donePrefix, string errorPrefix)
        {
            EnsureConfig();
            string host = ServerHost != null ? (ServerHost.Value ?? "").Trim() : "mmo.gamingwithgoose.com";
            int port = SocialPort != null ? SocialPort.Value : 61529;
            int timeout = NetworkTimeoutMs != null ? Mathf.Clamp(NetworkTimeoutMs.Value, 1000, 20000) : 5000;
            List<string> lines = new List<string>();
            using (TcpClient c = new TcpClient())
            {
                c.NoDelay = true;
                c.ReceiveTimeout = timeout;
                c.SendTimeout = timeout;
                IAsyncResult ar = c.BeginConnect(host, port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(timeout))
                    throw new TimeoutException("Timed out connecting to " + host + ":" + port);
                c.EndConnect(ar);
                using (NetworkStream ns = c.GetStream())
                using (StreamReader reader = new StreamReader(ns, Encoding.UTF8))
                using (StreamWriter writer = new StreamWriter(ns, new UTF8Encoding(false)))
                {
                    writer.AutoFlush = true;
                    try { reader.ReadLine(); } catch { } // initial WELCOME|server-ready
                    writer.WriteLine(request);
                    DateTime until = DateTime.UtcNow.AddMilliseconds(timeout);
                    while (DateTime.UtcNow < until)
                    {
                        string line = reader.ReadLine();
                        if (line == null)
                            break;
                        lines.Add(line);
                        if ((!string.IsNullOrEmpty(donePrefix) && line.StartsWith(donePrefix, StringComparison.OrdinalIgnoreCase)) ||
                            (!string.IsNullOrEmpty(errorPrefix) && line.StartsWith(errorPrefix, StringComparison.OrdinalIgnoreCase)))
                            break;
                    }
                }
            }
            return lines;
        }

        private static string SendOfficialServerRequest(string request, params string[] acceptedPrefixes)
        {
            EnsureConfig();
            string host = ServerHost != null ? (ServerHost.Value ?? "").Trim() : "mmo.gamingwithgoose.com";
            int port = SocialPort != null ? SocialPort.Value : 61529;
            int timeout = NetworkTimeoutMs != null ? Mathf.Clamp(NetworkTimeoutMs.Value, 1000, 20000) : 5000;
            using (TcpClient c = new TcpClient())
            {
                c.NoDelay = true;
                c.ReceiveTimeout = timeout;
                c.SendTimeout = timeout;
                IAsyncResult ar = c.BeginConnect(host, port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(timeout))
                    throw new TimeoutException("Timed out connecting to " + host + ":" + port);
                c.EndConnect(ar);
                using (NetworkStream ns = c.GetStream())
                using (StreamReader reader = new StreamReader(ns, Encoding.UTF8))
                using (StreamWriter writer = new StreamWriter(ns, new UTF8Encoding(false)))
                {
                    writer.AutoFlush = true;
                    try { reader.ReadLine(); } catch { } // initial WELCOME|server-ready
                    writer.WriteLine(request);
                    DateTime until = DateTime.UtcNow.AddMilliseconds(timeout);
                    while (DateTime.UtcNow < until)
                    {
                        string line = reader.ReadLine();
                        if (line == null)
                            break;
                        foreach (string prefix in acceptedPrefixes)
                        {
                            if (!string.IsNullOrEmpty(prefix) && line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                                return line;
                        }
                    }
                }
            }
            return string.Empty;
        }

        private static string DecodeServerError(string line)
        {
            try
            {
                if (string.IsNullOrEmpty(line)) return "no response";
                string[] p = line.Split('|');
                if (p.Length >= 2) return FromB64(p[1]);
                return line;
            }
            catch { return line ?? "unknown error"; }
        }

        private static string B64(string value)
        {
            try { return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty)); } catch { return string.Empty; }
        }

        private static string FromB64(string value)
        {
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(value ?? string.Empty)); } catch { return value ?? string.Empty; }
        }

        private static void SetOnlineSlotMetadata(int slot, bool occupied, string display, int updatedAt, string publicHandle, bool pvpRegistered, int rp, string rank, int wins, int losses, int draws)
        {
            SetOnlineSlotMetadata(slot, occupied, display, updatedAt, publicHandle, pvpRegistered, rp, rank, wins, losses, draws, 0, 0, 0);
        }

        private static void SetOnlineSlotMetadata(int slot, bool occupied, string display, int updatedAt, string publicHandle, bool pvpRegistered, int rp, string rank, int wins, int losses, int draws, int playerDesign, int playerColor1, int playerColor2)
        {
            try
            {
                if (slot < 0 || slot >= _onlineSaveSlotOccupied.Length)
                    return;
                _onlineSaveSlotOccupied[slot] = occupied;
                _onlineSaveSlotDisplayNames[slot] = (display ?? string.Empty).Trim();
                _onlineSaveSlotUpdatedAt[slot] = Math.Max(0, updatedAt);
                SetOnlineSlotProfile(slot, publicHandle, pvpRegistered, rp, rank, wins, losses, draws);
                SetOnlineSlotAppearance(slot, playerDesign, playerColor1, playerColor2);
                if (!occupied)
                    _onlineSaveSlotPayloadLoaded[slot] = true;
            }
            catch { }
        }

        private static bool ShouldSkipDuplicateFinalDisconnectSave(string reason)
        {
            try
            {
                EnsureConfig();
                float recentSeconds = SkipFinalDisconnectSaveIfLastUploadWithinSeconds != null ? Mathf.Max(0f, SkipFinalDisconnectSaveIfLastUploadWithinSeconds.Value) : 8f;
                if (recentSeconds <= 0f)
                    return false;
                if (_lastOfficialSaveUploadCompletedAt <= 0f || !_officialSaveUploadLastSuccess)
                    return false;
                float age = (float)((DateTime.UtcNow.Ticks / (double)TimeSpan.TicksPerSecond) - _lastOfficialSaveUploadCompletedAt);
                if (age >= 0f && age <= recentSeconds)
                {
                    Log("Skipping duplicate final Official Server save for " + reason + " because the latest online save completed successfully " + age.ToString("0.00") + "s ago.");
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static object GetSaveDataForSlot(int slot)
        {
            try
            {
                Type menuType = FindGameType("MenuScript");
                FieldInfo f = menuType != null ? menuType.GetField("saveData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static) : null;
                Array arr = f != null ? f.GetValue(null) as Array : null;
                if (arr == null || slot < 0 || slot >= arr.Length) return null;
                return arr.GetValue(slot);
            }
            catch { return null; }
        }

        private static int ParseIntSafe(string value)
        {
            int parsed = 0;
            try { int.TryParse(value ?? string.Empty, out parsed); } catch { parsed = 0; }
            return parsed;
        }

        private static void SetOnlineSlotAppearance(int slot, int playerDesign, int playerColor1, int playerColor2)
        {
            try
            {
                if (slot < 0 || slot >= _onlineSaveSlotPlayerDesign.Length)
                    return;
                _onlineSaveSlotPlayerDesign[slot] = Math.Max(0, playerDesign);
                _onlineSaveSlotPlayerColor1[slot] = Math.Max(0, playerColor1);
                _onlineSaveSlotPlayerColor2[slot] = Math.Max(0, playerColor2);
            }
            catch { }
        }

        private static void ApplyOnlineSlotAppearanceMetadataToSaveData(object saveDataObj, int slot)
        {
            try
            {
                if (saveDataObj == null || slot < 0 || slot >= _onlineSaveSlotPlayerDesign.Length)
                    return;
                SetIntFieldIfPresent(saveDataObj, "playerDesign", _onlineSaveSlotPlayerDesign[slot]);
                SetIntFieldIfPresent(saveDataObj, "playerColor1", _onlineSaveSlotPlayerColor1[slot]);
                SetIntFieldIfPresent(saveDataObj, "playerColor2", _onlineSaveSlotPlayerColor2[slot]);
            }
            catch { }
        }

        private static void SetIntFieldIfPresent(object obj, string fieldName, int value)
        {
            try
            {
                if (obj == null || string.IsNullOrEmpty(fieldName))
                    return;
                FieldInfo f = obj.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null && f.FieldType == typeof(int))
                    f.SetValue(obj, Math.Max(0, value));
            }
            catch { }
        }

        private static int TryGetSaveDataInt(object saveDataObj, string fieldName, int fallback)
        {
            try
            {
                if (saveDataObj == null || string.IsNullOrEmpty(fieldName))
                    return fallback;
                FieldInfo f = saveDataObj.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null)
                {
                    object v = f.GetValue(saveDataObj);
                    if (v != null) return Math.Max(0, Convert.ToInt32(v));
                }
                PropertyInfo p = saveDataObj.GetType().GetProperty(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null && p.CanRead)
                {
                    object v = p.GetValue(saveDataObj, null);
                    if (v != null) return Math.Max(0, Convert.ToInt32(v));
                }
            }
            catch { }
            return fallback;
        }

        private static void SetOnlineSlotProfile(int slot, string publicHandle, bool pvpRegistered, int rp, string rank, int wins, int losses, int draws)
        {
            try
            {
                if (slot < 0 || slot >= _onlineSaveSlotHandles.Length)
                    return;
                _onlineSaveSlotHandles[slot] = (publicHandle ?? string.Empty).Trim();
                _onlineSaveSlotPvpRegistered[slot] = pvpRegistered;
                _onlineSaveSlotPvpRp[slot] = Math.Max(0, rp);
                _onlineSaveSlotPvpRank[slot] = (rank ?? string.Empty).Trim();
                _onlineSaveSlotPvpWins[slot] = Math.Max(0, wins);
                _onlineSaveSlotPvpLosses[slot] = Math.Max(0, losses);
                _onlineSaveSlotPvpDraws[slot] = Math.Max(0, draws);
            }
            catch { }
        }

        private static void ApplyOnlineSlotServerHandleToSaveData(object saveDataObj, string publicHandle, string fallbackDisplayName)
        {
            try
            {
                if (saveDataObj == null)
                    return;
                string handle = (publicHandle ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(handle))
                    handle = (fallbackDisplayName ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(handle) || handle.IndexOf('#') < 0)
                    return;
                FieldInfo f = saveDataObj.GetType().GetField("playerName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null && f.FieldType == typeof(string))
                    f.SetValue(saveDataObj, handle);
            }
            catch (Exception ex)
            {
                Log("ApplyOnlineSlotServerHandleToSaveData failed: " + ex.Message);
            }
        }

        private static void SetSaveDataForSlot(int slot, object saveDataObj)
        {
            try
            {
                Type menuType = FindGameType("MenuScript");
                FieldInfo f = menuType != null ? menuType.GetField("saveData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static) : null;
                Array arr = f != null ? f.GetValue(null) as Array : null;
                if (arr == null || slot < 0 || slot >= arr.Length) return;
                SanitizeSaveDataForMenuPreview(saveDataObj, "slot" + slot);
                arr.SetValue(saveDataObj, slot);
                if (_onlineMode && slot >= 0 && slot < _onlineSaveSlotCache.Length)
                    _onlineSaveSlotCache[slot] = saveDataObj;
            }
            catch { }
        }

        private static void SanitizeSaveDataForMenuPreview(object saveDataObj, string context)
        {
            try
            {
                if (saveDataObj == null)
                    return;

                // MenuScript.HoverSaveFile assumes teamMons entries are either null or a full pipe-delimited
                // Mon save string. Some saves contain the literal placeholder "NULL" in the team array.
                // Vanilla then calls GetMonFromSaveString("NULL") and spams warnings/stack traces while
                // moving between slots. Normalize those placeholders to null before the native preview code runs.
                int cleaned = 0;
                cleaned += SanitizeStringArrayField(saveDataObj, "teamMons", 4, requireFullMonSave: true);
                cleaned += SanitizeStringArrayField(saveDataObj, "lastEncounterMons", 4, requireFullMonSave: false);

                if (cleaned > 0)
                    Log("Sanitized " + cleaned + " placeholder save-string value(s) for native save preview in " + context + ".");
            }
            catch (Exception ex)
            {
                Log("SanitizeSaveDataForMenuPreview failed for " + context + ": " + ex.Message);
            }
        }

        private static int SanitizeStringArrayField(object obj, string fieldName, int minimumLength, bool requireFullMonSave)
        {
            try
            {
                if (obj == null)
                    return 0;
                FieldInfo f = obj.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f == null || f.FieldType != typeof(string[]))
                    return 0;

                string[] arr = f.GetValue(obj) as string[];
                int changed = 0;
                if (arr == null)
                {
                    f.SetValue(obj, new string[Math.Max(0, minimumLength)]);
                    return Math.Max(0, minimumLength);
                }

                for (int i = 0; i < arr.Length; i++)
                {
                    string v = arr[i];
                    if (string.IsNullOrWhiteSpace(v) ||
                        string.Equals(v, "NULL", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(v, "null", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(v, "None", StringComparison.OrdinalIgnoreCase))
                    {
                        if (v != null)
                        {
                            arr[i] = null;
                            changed++;
                        }
                        continue;
                    }

                    // For teamMons, the native preview parser requires at least 10 pipe fields.
                    // If a malformed placeholder slips through, treat it as an empty team slot so
                    // HoverSaveFile can keep using the official preview flow without warning spam.
                    if (requireFullMonSave && v.IndexOf('|') < 0)
                    {
                        arr[i] = null;
                        changed++;
                    }
                }
                return changed;
            }
            catch
            {
                return 0;
            }
        }

        private static void SetAllMenuSaveDataNull()
        {
            try
            {
                Type menuType = FindGameType("MenuScript");
                FieldInfo f = menuType != null ? menuType.GetField("saveData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static) : null;
                Array arr = f != null ? f.GetValue(null) as Array : null;
                int count = arr != null ? Math.Min(6, arr.Length) : 6;
                for (int i = 0; i < count; i++)
                {
                    try { if (arr != null) arr.SetValue(null, i); } catch { }
                    if (i < _onlineSaveSlotCache.Length) _onlineSaveSlotCache[i] = null;
                    if (i < _onlineSaveSlotOccupied.Length) _onlineSaveSlotOccupied[i] = false;
                    if (i < _onlineSaveSlotPayloadLoaded.Length) _onlineSaveSlotPayloadLoaded[i] = false;
                    if (i < _onlineSaveSlotDisplayNames.Length) _onlineSaveSlotDisplayNames[i] = string.Empty;
                    if (i < _onlineSaveSlotUpdatedAt.Length) _onlineSaveSlotUpdatedAt[i] = 0;
                    if (i < _onlineSaveSlotPlayerDesign.Length) _onlineSaveSlotPlayerDesign[i] = 0;
                    if (i < _onlineSaveSlotPlayerColor1.Length) _onlineSaveSlotPlayerColor1[i] = 0;
                    if (i < _onlineSaveSlotPlayerColor2.Length) _onlineSaveSlotPlayerColor2[i] = 0;
                }
            }
            catch { }
        }

        private static void RepairLocalSaveSlotVisuals()
        {
            try
            {
                if (_menuObj == null || _onlineMode)
                    return;
                for (int i = 0; i < 6; i++)
                {
                    object save = GetSaveDataForSlot(i);
                    GameObject plus = GetGameObjectFromArrayField(_menuObj, "savePlusObj", i);
                    GameObject preview = GetGameObjectFromArrayField(_menuObj, "savePlayerPreviewObj", i);
                    GameObject secondary = GetGameObjectFromArrayField(_menuObj, "savePlayerPreviewSecondaryColor", i);
                    if (save == null)
                    {
                        if (plus != null) plus.SetActive(true);
                        if (preview != null) preview.SetActive(false);
                    }
                    else
                    {
                        if (plus != null) plus.SetActive(false);
                        if (preview != null) preview.SetActive(true);
                        if (secondary != null) secondary.SetActive(true);
                    }
                }
                Log("Repaired local save slot preview/secondary-color visibility after leaving Online Mode.");
            }
            catch (Exception ex)
            {
                Log("RepairLocalSaveSlotVisuals failed: " + ex.Message);
            }
        }

        private static Type FindGameType(string name)
        {
            try
            {
                Type t = Type.GetType(name + ", Assembly-CSharp");
                if (t != null)
                    return t;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        t = asm.GetType(name);
                        if (t != null)
                            return t;
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }

        private static void ClearMenuSaveDataForOnline()
        {
            try
            {
                if (!_onlineMode)
                    return;
                Type menuType = FindGameType("MenuScript");
                FieldInfo f = menuType != null ? menuType.GetField("saveData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static) : null;
                Array arr = f != null ? f.GetValue(null) as Array : null;
                if (arr == null)
                    return;
                for (int i = 0; i < Math.Min(6, arr.Length); i++)
                    arr.SetValue(null, i);
                if (!_onlineSaveDataClearedLogged)
                {
                    _onlineSaveDataClearedLogged = true;
                    Log("Cleared in-memory MenuScript.saveData while in Online Mode so native save slots stay empty and local saves cannot flash/load.");
                }
            }
            catch (Exception ex)
            {
                Log("ClearMenuSaveDataForOnline failed: " + ex.Message);
            }
        }

        private static void RestoreLocalSaveDataFromDisk()
        {
            try
            {
                Type menuType = FindGameType("MenuScript");
                Type saveSystemType = FindGameType("SaveSystem");
                FieldInfo f = menuType != null ? menuType.GetField("saveData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static) : null;
                MethodInfo load = saveSystemType != null ? saveSystemType.GetMethod("LoadGame", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static) : null;
                Array arr = f != null ? f.GetValue(null) as Array : null;
                if (arr == null || load == null)
                    return;
                for (int i = 0; i < Math.Min(6, arr.Length); i++)
                {
                    object save = null;
                    try { save = load.Invoke(null, new object[] { i }); } catch { save = null; }
                    SanitizeSaveDataForMenuPreview(save, "local slot" + i);
                    arr.SetValue(save, i);
                }
                Log("Restored local MenuScript.saveData from disk after leaving Online Mode.");
            }
            catch (Exception ex)
            {
                Log("RestoreLocalSaveDataFromDisk failed: " + ex.Message);
            }
        }

        private static void EnsureTitleCoroutineHost(object menuObj)
        {
            try
            {
                // Prefer the live MenuScript object for title/login coroutines. The save-select helper
                // root is intentionally hidden when menuSaveFiles is closed, and Unity refuses to
                // start coroutines from an inactive GameObject.
                MonoBehaviour mb = menuObj as MonoBehaviour;
                if (mb != null && mb.gameObject != null && mb.gameObject.activeInHierarchy)
                {
                    OfficialServerSaveSelectCoroutineHost liveHost = mb.GetComponent<OfficialServerSaveSelectCoroutineHost>();
                    if (liveHost == null)
                        liveHost = mb.gameObject.AddComponent<OfficialServerSaveSelectCoroutineHost>();
                    _host = liveHost;
                    return;
                }

                if (_host != null && _host.gameObject != null && _host.gameObject.activeInHierarchy)
                    return;

                _host = null;
                EnsureHostComponent();
            }
            catch (Exception ex)
            {
                Log("EnsureTitleCoroutineHost failed: " + ex.Message);
            }
        }

        private static void UpdateLauncherTitleLogInText()
        {
            try
            {
                if (_menuObj == null || !IsLauncherSessionActive())
                    return;
                GameObject play = GetGameObjectField(_menuObj, "bPlay");
                SetTextRecursive(play, (_launcherPreAuthBusy || _authBusy) ? "Logging In..." : "Log In", true);
            }
            catch { }
        }

        private static void EnsureHostComponent()
        {
            try
            {
                if (_host != null)
                    return;
                if (_root != null)
                {
                    _host = _root.GetComponent<OfficialServerSaveSelectCoroutineHost>();
                    if (_host == null)
                        _host = _root.AddComponent<OfficialServerSaveSelectCoroutineHost>();
                }
            }
            catch { }
        }

        private static void InvokeMenuMethod(object menuObj, string methodName)
        {
            try
            {
                MethodInfo m = menuObj != null ? menuObj.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) : null;
                if (m != null)
                    m.Invoke(menuObj, null);
            }
            catch { }
        }

        private static bool TryInvokeMenuMethod(object menuObj, string methodName)
        {
            try
            {
                MethodInfo m = menuObj != null ? menuObj.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) : null;
                if (m == null)
                    return false;
                m.Invoke(menuObj, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryInvokeMenuMethodWithInt(object menuObj, string methodName, int value)
        {
            try
            {
                MethodInfo m = menuObj != null ? menuObj.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(int) }, null) : null;
                if (m == null)
                    return false;
                m.Invoke(menuObj, new object[] { value });
                return true;
            }
            catch (Exception ex)
            {
                Log("TryInvokeMenuMethodWithInt " + methodName + " failed: " + ex.Message);
                return false;
            }
        }

        private static bool TryInvokeMenuMethodWithString(object menuObj, string methodName, string value)
        {
            try
            {
                MethodInfo m = menuObj != null ? menuObj.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(string) }, null) : null;
                if (m == null)
                    return false;
                m.Invoke(menuObj, new object[] { value });
                return true;
            }
            catch (Exception ex)
            {
                Log("TryInvokeMenuMethodWithString " + methodName + " failed: " + ex.Message);
                return false;
            }
        }

        private static bool TryStartMenuCoroutine(object menuObj, string methodName)
        {
            try
            {
                MonoBehaviour mb = menuObj as MonoBehaviour;
                if (mb == null)
                    return false;
                MethodInfo m = menuObj.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (m == null)
                    return false;
                IEnumerator routine = m.Invoke(menuObj, null) as IEnumerator;
                if (routine == null)
                    return false;
                mb.StartCoroutine(routine);
                return true;
            }
            catch (Exception ex)
            {
                Log("TryStartMenuCoroutine " + methodName + " failed: " + ex.Message);
                return false;
            }
        }

        private static void SetObjectField(object obj, string field, object value)
        {
            try
            {
                if (obj == null)
                    return;
                FieldInfo f = obj.GetType().GetField(field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                if (f != null)
                    f.SetValue(obj, value);
            }
            catch { }
        }

        private static void ResetClonedButtonComponents(GameObject go)
        {
            try
            {
                foreach (EventTrigger trig in go.GetComponents<EventTrigger>())
                    UnityEngine.Object.DestroyImmediate(trig);
                foreach (Button b in go.GetComponents<Button>())
                    UnityEngine.Object.DestroyImmediate(b);
                foreach (Selectable s in go.GetComponents<Selectable>())
                {
                    if (!(s is Button))
                        UnityEngine.Object.DestroyImmediate(s);
                }
                if (go.GetComponent<Button>() == null)
                    go.AddComponent<Button>();
            }
            catch { }
        }

        private static void StripNonTextComponents(GameObject go)
        {
            try
            {
                foreach (Button b in go.GetComponents<Button>()) UnityEngine.Object.DestroyImmediate(b);
                foreach (Image img in go.GetComponents<Image>()) UnityEngine.Object.DestroyImmediate(img);
                foreach (EventTrigger e in go.GetComponents<EventTrigger>()) UnityEngine.Object.DestroyImmediate(e);
            }
            catch { }
        }

        private static TMP_Text FindText(GameObject go)
        {
            if (go == null)
                return null;
            TMP_Text t = go.GetComponent<TMP_Text>();
            if (t != null)
                return t;
            return go.GetComponentInChildren<TMP_Text>(true);
        }

        private static RectTransform GetRectField(object obj, string field)
        {
            GameObject go = GetGameObjectField(obj, field);
            if (go == null)
                return null;
            return go.GetComponent<RectTransform>();
        }

        private static GameObject GetGameObjectField(object obj, string field)
        {
            object value = GetObjectField(obj, field);
            if (value is GameObject go)
                return go;
            Component comp = value as Component;
            return comp != null ? comp.gameObject : null;
        }

        private static GameObject GetGameObjectFromArrayField(object obj, string field, int index)
        {
            try
            {
                object arrObj = GetObjectField(obj, field);
                Array arr = arrObj as Array;
                if (arr == null || index < 0 || index >= arr.Length)
                    return null;
                object value = arr.GetValue(index);
                if (value is GameObject go)
                    return go;
                Component c = value as Component;
                return c != null ? c.gameObject : null;
            }
            catch { return null; }
        }

        private static object GetObjectField(object obj, string field)
        {
            if (obj == null)
                return null;
            FieldInfo f = obj.GetType().GetField(field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            return f != null ? f.GetValue(obj) : null;
        }

        private static void LogOncePerSlot(int slot, string message)
        {
            // Keep hover logging finite so BepInEx does not get spammed during menu navigation.
            if (slot == _lastHoverSlot)
                Log(message);
        }

        private static void Log(string msg)
        {
            try { Console.WriteLine("[MMOnsterpatch Official Server] " + msg); } catch { }
            try { Debug.Log("[MMOnsterpatch Official Server] " + msg); } catch { }
        }
    }
}
