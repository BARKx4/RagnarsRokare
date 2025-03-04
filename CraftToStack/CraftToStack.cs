﻿using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using static ItemDrop;

namespace RagnarsRokare.CraftToStack
{
    [BepInPlugin(ModId, ModName, ModVersion)]
    [BepInProcess("valheim.exe")]
    public class CraftToStack : BaseUnityPlugin
    {
        public const string ModId = "RagnarsRokare.CraftToStack";
        public const string ModName = "RagnarsRökare CraftToStackMod";
        public const string ModVersion = "0.2";

        private readonly Harmony harmony = new Harmony(ModId);
        public static ConfigEntry<int> NexusID;

        void Awake()
        {
            Debug.Log($"Loading {ModName} v{ModVersion}, Barg Bug Bash!");
            harmony.PatchAll();
            NexusID = Config.Bind<int>("General", "NexusID", 982, "Nexus mod ID for updates");
        }

        [HarmonyPatch(typeof(InventoryGui), "UpdateRecipe")]
        class InventoryGui_UpdateRecipe_Patch
        {
            // ORIGINAL
            // brtrue Label22
            // ldarg.1 NULL
            // callvirt Inventory Humanoid::GetInventory()
            // callvirt bool Inventory::HaveEmptySlot()  <----   new opCodes replace this
            // br Label23
            // ldc.i4.1 NULL[Label22]
            // stloc.s 9 (System.Boolean)[Label23]

            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> originalCIL)
            {
                var findEmptySlotMethod = typeof(Inventory).GetMethod("HaveEmptySlot", BindingFlags.Public | BindingFlags.Instance);
                var canAddItemMethod = typeof(Inventory).GetMethod("CanAddItem", new Type[] { typeof(ItemData), typeof(int) });
                var selectedRecepieField = typeof(InventoryGui).GetField("m_selectedRecipe", BindingFlags.NonPublic | BindingFlags.Instance);
                var getKeyFromRecipeProperty = typeof(KeyValuePair<Recipe, ItemData>).GetMethod("get_Key", BindingFlags.Public | BindingFlags.Instance);
                foreach (var operation in originalCIL)
                {
                    if (operation.Calls(findEmptySlotMethod))
                    {
                        yield return new CodeInstruction(OpCodes.Ldarg_0);
                        yield return new CodeInstruction(OpCodes.Ldflda, selectedRecepieField);
                        yield return new CodeInstruction(OpCodes.Call, getKeyFromRecipeProperty);
                        yield return new CodeInstruction(OpCodes.Ldfld, typeof(Recipe).GetField("m_item", BindingFlags.Public | BindingFlags.Instance));
                        yield return new CodeInstruction(OpCodes.Ldfld, typeof(ItemDrop).GetField("m_itemData", BindingFlags.Public | BindingFlags.Instance));

                        yield return new CodeInstruction(OpCodes.Ldarg_0);
                        yield return new CodeInstruction(OpCodes.Ldflda, selectedRecepieField);
                        yield return new CodeInstruction(OpCodes.Call, getKeyFromRecipeProperty);
                        yield return new CodeInstruction(OpCodes.Ldfld, typeof(Recipe).GetField("m_amount"));

                        yield return new CodeInstruction(OpCodes.Call, canAddItemMethod);
                        continue;
                    }
                    yield return operation;
                }
            }
        }

        [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
        class InventoryGui_DoCrafting_Patch
        {
            // Original code
            // ldarg.1 NULL
            // callvirt Inventory Humanoid::GetInventory()
            // callvirt bool Inventory::HaveEmptySlot() <- This is replaced with new OpCodes
            // brtrue Label10
            // ret NULL

            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> originalCIL)
            {
                var findEmptySlotMethod = typeof(Inventory).GetMethod("HaveEmptySlot", BindingFlags.Public | BindingFlags.Instance);
                var canAddItemMethod = typeof(Inventory).GetMethod("CanAddItem", new Type[] { typeof(ItemData), typeof(int) });
                var craftRecipeField = typeof(InventoryGui).GetField("m_craftRecipe", BindingFlags.NonPublic | BindingFlags.Instance);
                var getKeyFromRecipeProperty = typeof(KeyValuePair<Recipe, ItemData>).GetMethod("get_Key", BindingFlags.Public | BindingFlags.Instance);
                foreach (var operation in originalCIL)
                {
                    if (operation.Calls(findEmptySlotMethod))
                    {
                        yield return new CodeInstruction(OpCodes.Ldarg_0);
                        yield return new CodeInstruction(OpCodes.Ldflda, craftRecipeField);
                        yield return new CodeInstruction(OpCodes.Call, getKeyFromRecipeProperty);
                        yield return new CodeInstruction(OpCodes.Ldfld, typeof(Recipe).GetField("m_item", BindingFlags.Public | BindingFlags.Instance));
                        yield return new CodeInstruction(OpCodes.Ldfld, typeof(ItemDrop).GetField("m_itemData", BindingFlags.Public | BindingFlags.Instance));

                        yield return new CodeInstruction(OpCodes.Ldarg_0);
                        yield return new CodeInstruction(OpCodes.Ldflda, craftRecipeField);
                        yield return new CodeInstruction(OpCodes.Call, getKeyFromRecipeProperty);
                        yield return new CodeInstruction(OpCodes.Ldfld, typeof(Recipe).GetField("m_amount"));

                        yield return new CodeInstruction(OpCodes.Call, canAddItemMethod);
                        continue;
                    }
                    yield return operation;
                }
            }
        }

        [HarmonyPatch(typeof(Inventory), "AddItem", argumentTypes: new Type[] { typeof(string), typeof(int), typeof(int), typeof(int), typeof(long), typeof(string) })]
        class Inventory_AddItem_Patch
        {
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                //   ORIGINAL
                //   ldarg.0 NULL[Label2]
                //   ldarg.0 NULL
                //   ldloc.1 NULL
                //   ldfld ItemDrop+ItemData ItemDrop::m_itemData
                //   call bool Inventory::TopFirst(ItemDrop + ItemData item)
                //   call Vector2i Inventory::FindEmptySlot(bool topFirst)
                //   ldfld int Vector2i::x
                //   ldc.i4.m1 NULL
                //   bne.un Label3

                //   REPLACEMENT:
                //   ldarg.0
                //   ldloc.0
                //   ldarg.2
                //   call bool Inventory::CanAddItem(UnityEngine::GameObject, int32 stack)
                //   brtrue Label3

                bool foundCallToFindEmptySlot = false;

                var findEmptySlotMethod = typeof(Inventory).GetMethod("FindEmptySlot", BindingFlags.NonPublic | BindingFlags.Instance);
                var canAddItemMethod = typeof(Inventory).GetMethod("CanAddItem", new Type[] { typeof(GameObject), typeof(int) });
                var newCodes = new List<CodeInstruction>(instructions);
                for (int i = 0; i < newCodes.Count(); i++)
                {
                    var instruction = newCodes[i];
                    if (instruction.operand?.ToString().Contains("FindEmptySlot") ?? false) // Calls(findEmptySlotMethod))
                    {
                        foundCallToFindEmptySlot = true;
                    }

                    if (instruction.IsLdarg(0) && i > 0 && instructions.ElementAt(i - 1).IsLdarg(0))
                    {
                        newCodes[i] = new CodeInstruction(OpCodes.Ldloc_0);
                        newCodes[i + 1] = new CodeInstruction(OpCodes.Ldarg_2);
                        newCodes[i + 2] = new CodeInstruction(OpCodes.Call, canAddItemMethod);
                        newCodes[i + 3].opcode = OpCodes.Nop;
                        newCodes[i + 4].opcode = OpCodes.Nop;
                        newCodes[i + 5].opcode = OpCodes.Nop;
                        newCodes[i + 6].opcode = OpCodes.Nop;
                        newCodes[i + 7].opcode = OpCodes.Brtrue;
                    }
                }
                if (foundCallToFindEmptySlot)
                {
                    return newCodes;
                }
                else
                {
                    // Since we cannot locate the method we wanna remove, we don't change anything.
                    Debug.LogWarning($"{ModId}:Inventory.AddItem not patched, original method is changed");
                    return instructions;
                }
            }
        }

        [HarmonyPatch(typeof(Inventory), "RemoveItem", argumentTypes: new Type[] { typeof(string), typeof(int) })]
        class Inventory_RemoveItem_Patch
        {
            static bool Prefix(ref Inventory __instance, ref List<ItemDrop.ItemData> ___m_inventory, string name, ref int amount)
            {
                var sortedInventoryList = ___m_inventory.OrderBy(i => i.m_stack);
                foreach (ItemDrop.ItemData item in sortedInventoryList)
                {
                    if (item.m_shared.m_name == name)
                    {
                        int num = Mathf.Min(item.m_stack, amount);
                        item.m_stack -= num;
                        amount -= num;
                        if (amount <= 0)
                        {
                            break;
                        }
                    }
                }
                return true;
            }
        }
    }
}