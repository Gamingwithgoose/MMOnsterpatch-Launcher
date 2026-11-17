using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Goose.Monsterpatch.FollowersPatcher
{
    /// <summary>
    /// BepInEx 5 preloader patcher. This DLL belongs in BepInEx\patchers.
    /// Runtime Unity code lives in Monsterpatch_Followers_Runtime.dll in BepInEx\plugins.
    /// This split avoids making Assembly-CSharp depend directly on the preloader patcher DLL.
    /// </summary>
    public static class FollowersPatcher
    {
        public static IEnumerable<string> TargetDLLs => new[] { "Assembly-CSharp.dll" };

        public static void Patch(AssemblyDefinition assembly)
        {
            var module = assembly.MainModule;
            var gameScriptType = module.Types.FirstOrDefault(t => t.Name == "GameScript");
            if (gameScriptType == null)
            {
                Log("GameScript not found. No follower runtime hook applied.");
                return;
            }

            EnsureRuntimeAssemblyReference(module);

            int patched = 0;

            MethodDefinition start = gameScriptType.Methods.FirstOrDefault(m => m.Name == "Start" && !m.HasParameters);
            if (InjectBeforeReturns(module, start, "OnGameScriptStart", ArgKind.Component)) patched++;

            Log("Applied split-runtime follower hooks (GameScript.Start only, no PlayerController hooks, visual offset + facing/travel-direction sorting runtime): " + patched);
        }
        private enum ArgKind { Component, Int32, Vector3 }

        private static bool InjectAtStart(ModuleDefinition module, MethodDefinition target, string runtimeMethodName, params ArgKind[] args)
        {
            if (target == null || !target.HasBody)
            {
                Log("Skipped missing method for " + runtimeMethodName);
                return false;
            }
            if (AlreadyCalls(target, runtimeMethodName))
            {
                Log("Already patched " + target.FullName + " -> " + runtimeMethodName);
                return false;
            }

            var il = target.Body.GetILProcessor();
            var first = target.Body.Instructions.First();
            var call = BuildRuntimeMethodReference(module, runtimeMethodName, args);
            foreach (var ins in BuildLoadArgsAndCall(il, call, args))
                il.InsertBefore(first, ins);

            Log("Patched start of " + target.FullName + " -> " + runtimeMethodName);
            return true;
        }

        private static bool InjectBeforeReturns(ModuleDefinition module, MethodDefinition target, string runtimeMethodName, params ArgKind[] args)
        {
            if (target == null || !target.HasBody)
            {
                Log("Skipped missing method for " + runtimeMethodName);
                return false;
            }
            if (AlreadyCalls(target, runtimeMethodName))
            {
                Log("Already patched " + target.FullName + " -> " + runtimeMethodName);
                return false;
            }

            var il = target.Body.GetILProcessor();
            var call = BuildRuntimeMethodReference(module, runtimeMethodName, args);
            var returns = target.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToList();
            if (returns.Count == 0)
            {
                Log("No return instructions found in " + target.FullName);
                return false;
            }

            foreach (var ret in returns)
            {
                foreach (var ins in BuildLoadArgsAndCall(il, call, args))
                    il.InsertBefore(ret, ins);
            }

            Log("Patched returns of " + target.FullName + " -> " + runtimeMethodName);
            return true;
        }

        private static IEnumerable<Instruction> BuildLoadArgsAndCall(ILProcessor il, MethodReference call, ArgKind[] args)
        {
            // All runtime hooks use PlayerController/this as arg0, then optional original method arg1.
            yield return il.Create(OpCodes.Ldarg_0);
            if (args.Length >= 2)
                yield return il.Create(OpCodes.Ldarg_1);
            yield return il.Create(OpCodes.Call, call);
        }

        private static MethodReference BuildRuntimeMethodReference(ModuleDefinition module, string methodName, ArgKind[] args)
        {
            var runtimeAsm = EnsureRuntimeAssemblyReference(module);
            var runtimeType = new TypeReference(
                "Goose.Monsterpatch.FollowersRuntime",
                "FollowersRuntime",
                module,
                runtimeAsm);

            var method = new MethodReference(methodName, module.TypeSystem.Void, runtimeType)
            {
                HasThis = false
            };

            foreach (var arg in args)
                method.Parameters.Add(new ParameterDefinition(GetArgType(module, arg)));

            return module.ImportReference(method);
        }

        private static TypeReference GetArgType(ModuleDefinition module, ArgKind arg)
        {
            switch (arg)
            {
                case ArgKind.Int32:
                    return module.TypeSystem.Int32;
                case ArgKind.Component:
                    return new TypeReference("UnityEngine", "Component", module, FindAssemblyRef(module, "UnityEngine.CoreModule"));
                case ArgKind.Vector3:
                    return new TypeReference("UnityEngine", "Vector3", module, FindAssemblyRef(module, "UnityEngine.CoreModule"));
                default:
                    throw new ArgumentOutOfRangeException(nameof(arg));
            }
        }

        private static AssemblyNameReference EnsureRuntimeAssemblyReference(ModuleDefinition module)
        {
            var existing = module.AssemblyReferences.FirstOrDefault(a => a.Name == "Monsterpatch_Followers_Runtime");
            if (existing != null)
                return existing;

            var runtimeAsm = new AssemblyNameReference("Monsterpatch_Followers_Runtime", new Version(0, 3, 7, 0));
            module.AssemblyReferences.Add(runtimeAsm);
            return runtimeAsm;
        }

        private static AssemblyNameReference FindAssemblyRef(ModuleDefinition module, string name)
        {
            var found = module.AssemblyReferences.FirstOrDefault(a => a.Name == name);
            if (found != null)
                return found;
            var added = new AssemblyNameReference(name, new Version(0, 0, 0, 0));
            module.AssemblyReferences.Add(added);
            return added;
        }

        private static bool AlreadyCalls(MethodDefinition method, string runtimeMethodName)
        {
            if (method == null || !method.HasBody) return false;
            return method.Body.Instructions.Any(i =>
                i.OpCode == OpCodes.Call &&
                i.Operand is MethodReference mr &&
                mr.DeclaringType.FullName == "Goose.Monsterpatch.FollowersRuntime.FollowersRuntime" &&
                mr.Name == runtimeMethodName);
        }

        private static void Log(string msg)
        {
            Console.WriteLine("[Monsterpatch Followers Patcher] " + msg);
        }
    }
}
