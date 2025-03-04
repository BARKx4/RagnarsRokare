﻿using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace SlaveGreylings
{
    public partial class SlaveGreylings
    {
        [HarmonyPatch(typeof(BaseAI), "UpdateAI")]
        class BaseAI_UpdateAI_ReversePatch
        {
            [HarmonyReversePatch]
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static void UpdateAI(BaseAI instance, float dt, ZNetView m_nview, ref float m_jumpInterval, ref float m_jumpTimer,
                ref float m_randomMoveUpdateTimer, ref float m_timeSinceHurt, ref bool m_alerted)
            {
                if (m_nview.IsOwner())
                {
                    instance.UpdateTakeoffLanding(dt);
                    if (m_jumpInterval > 0f)
                    {
                        m_jumpTimer += dt;
                    }
                    if (m_randomMoveUpdateTimer > 0f)
                    {
                        m_randomMoveUpdateTimer -= dt;
                    }
                    typeof(BaseAI).GetMethod("UpdateRegeneration", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(instance, new object[] { dt });
                    m_timeSinceHurt += dt;
                }
                else
                {
                    m_alerted = m_nview.GetZDO().GetBool("alert");
                }
            }
        }

        [HarmonyPatch(typeof(MonsterAI), "MakeTame")]
        static class MonsterAI_MakeTame_Patch
        {
            static void Postfix(MonsterAI __instance)
            {
                if (__instance.name.Contains("Greyling"))
                {
                    __instance.m_consumeItems.Clear();
                    __instance.m_consumeItems.Add(ObjectDB.instance.GetAllItems(ItemDrop.ItemData.ItemType.Material, "Resin").FirstOrDefault());
                    __instance.m_consumeSearchRange = 50;
                }
            }
        }

        [HarmonyPatch(typeof(MonsterAI), "UpdateAI")]
        static class MonsterAI_UpdateAI_Patch
        {

            public static string UpdateAiStatus(ZNetView nview, string newStatus)
            {
                string currentAiStatus = nview?.GetZDO()?.GetString(Constants.Z_AiStatus);
                if (currentAiStatus != newStatus)
                {
                    string name = nview?.GetZDO()?.GetString(Constants.Z_GivenName);
                    //Debug.Log($"{name}: {newStatus}");
                    nview.GetZDO().Set(Constants.Z_AiStatus, newStatus);
                }
                return newStatus;
            }

            static MonsterAI_UpdateAI_Patch()
            {
                m_assignment = new Dictionary<int, MaxStack<Assignment>>();
                m_assigned = new Dictionary<int, bool>();
                m_containers = new Dictionary<int, MaxStack<Container>>();
                m_searchcontainer = new Dictionary<int, bool>();
                m_fetchitems = new Dictionary<int, List<ItemDrop.ItemData>>();
                m_carrying = new Dictionary<int, ItemDrop.ItemData>();
                m_spottedItem = new Dictionary<int, ItemDrop>();
                m_aiStatus = new Dictionary<int, string>();
                m_assignedTimer = new Dictionary<int, float>();
                m_stateChangeTimer = new Dictionary<int, float>();
                m_acceptedContainerNames = new List<string>();
                m_acceptedContainerNames.AddRange(GreylingsConfig.IncludedContainersList.Value.Split());
            }
            public static Dictionary<int, MaxStack<Assignment>> m_assignment;
            public static Dictionary<int, MaxStack<Container>> m_containers;
            public static Dictionary<int, bool> m_assigned;
            public static Dictionary<int, bool> m_searchcontainer;
            public static Dictionary<int, List<ItemDrop.ItemData>> m_fetchitems;
            public static Dictionary<int, ItemDrop.ItemData> m_carrying;
            public static Dictionary<int, ItemDrop> m_spottedItem;
            public static Dictionary<int, string> m_aiStatus;
            public static Dictionary<int, float> m_assignedTimer;
            public static Dictionary<int, float> m_stateChangeTimer;
            private static List<string> m_acceptedContainerNames;

            static bool Prefix(MonsterAI __instance, float dt, ref ZNetView ___m_nview, ref Character ___m_character, ref float ___m_fleeIfLowHealth,
                ref float ___m_timeSinceHurt, ref string ___m_aiStatus, ref Vector3 ___arroundPointTarget, ref float ___m_jumpInterval, ref float ___m_jumpTimer,
                ref float ___m_randomMoveUpdateTimer, ref bool ___m_alerted, ref Tameable ___m_tamable)
            {
                if (!___m_nview.IsOwner())
                {
                    return false;
                }
                if (!___m_character.IsTamed())
                {
                    return true;
                }
                if (!__instance.name.Contains("Greyling"))
                {
                    return true;
                }
                if (__instance.IsSleeping())
                {
                    Invoke(__instance, "UpdateSleep", new object[] { dt });
                    Dbgl($"{___m_character.GetHoverName()}: Sleep updated");
                    return false;
                }

                BaseAI_UpdateAI_ReversePatch.UpdateAI(__instance, dt, ___m_nview, ref ___m_jumpInterval, ref ___m_jumpTimer, ref ___m_randomMoveUpdateTimer, ref ___m_timeSinceHurt, ref ___m_alerted);

                int instanceId = InitInstanceIfNeeded(__instance);
                Dbgl("GetInstanceID ok");

                ___m_aiStatus = "";
                Vector3 greylingPosition = ___m_character.transform.position;

                if (___m_timeSinceHurt < 20f)
                {
                    __instance.Alert();
                    var fleeFrom = m_attacker == null ? ___m_character.transform.position : m_attacker.transform.position;
                    Invoke(__instance, "Flee", new object[] { dt, fleeFrom });
                    ___m_aiStatus = UpdateAiStatus(___m_nview, "Got hurt, flee!");
                    return false;
                }
                else
                {
                    m_attacker = null;
                    Invoke(__instance, "SetAlerted", new object[] { false });
                }
                if ((bool)__instance.GetFollowTarget())
                {
                    Invoke(__instance, "Follow", new object[] { __instance.GetFollowTarget(), dt });
                    ___m_aiStatus = UpdateAiStatus(___m_nview, "Follow");
                    Invoke(__instance, "SetAlerted", new object[] { false });
                    m_assignment[instanceId].Clear();
                    m_fetchitems[instanceId].Clear();
                    m_assigned[instanceId] = false;
                    m_spottedItem[instanceId] = null;
                    m_containers[instanceId].Clear();
                    m_searchcontainer[instanceId] = false;
                    m_stateChangeTimer[instanceId] = 0;
                    return false;
                }
                if (AvoidFire(__instance, dt, m_assigned[instanceId] ? m_assignment[instanceId].Peek().Position : __instance.transform.position))
                {
                    ___m_aiStatus = UpdateAiStatus(___m_nview, "Avoiding fire");
                    if (m_assignment[instanceId].Any() && m_assignment[instanceId].Peek().IsClose(___m_character.transform.position))
                    {
                        m_assigned[instanceId] = false;
                    }
                    return false;
                }
                if (!__instance.IsAlerted() && (bool)Invoke(__instance, "UpdateConsumeItem", new object[] { ___m_character as Humanoid, dt }))
                {
                    ___m_aiStatus = UpdateAiStatus(___m_nview, "Consume item");
                    return false;
                }
                if (___m_tamable.IsHungry())
                {
                    ___m_aiStatus = UpdateAiStatus(___m_nview, "Is hungry, no work a do");
                    if (m_searchcontainer[instanceId] && m_containers[instanceId].Any())
                    {
                        bool containerIsInvalid = m_containers[instanceId].Peek()?.GetComponent<ZNetView>()?.IsValid() == false;
                        if (containerIsInvalid)
                        {
                            m_containers[instanceId].Pop();
                            m_searchcontainer[instanceId] = false;
                            return false;
                        }
                        bool isCloseToContainer = Vector3.Distance(greylingPosition, m_containers[instanceId].Peek().transform.position) < 1.5;
                        if (!isCloseToContainer)
                        {
                            Invoke(__instance, "MoveAndAvoid", new object[] { dt, m_containers[instanceId].Peek().transform.position, 0.5f, false });
                            return false;
                        }
                        else
                        {
                            ItemDrop foodItem = __instance.m_consumeItems.ElementAt<ItemDrop>(0);
                            ItemDrop.ItemData item = m_containers[instanceId].Peek()?.GetInventory()?.GetItem(foodItem.m_itemData.m_shared.m_name);
                            if (item == null)
                            {
                                ___m_aiStatus = UpdateAiStatus(___m_nview, "No Resin in chest");
                                Container nearbyChest = FindRandomNearbyContainer(greylingPosition, m_containers[instanceId]);
                                if (nearbyChest != null)
                                {
                                    m_containers[instanceId].Push(nearbyChest);
                                    m_searchcontainer[instanceId] = true;
                                    return false;
                                }
                                else
                                {
                                    m_containers[instanceId].Clear();
                                    m_searchcontainer[instanceId] = false;
                                    return false;
                                }
                            }
                            else
                            {
                                ___m_aiStatus = UpdateAiStatus(___m_nview, "Resin in chest");
                                m_containers[instanceId].Peek().GetInventory().RemoveItem(item, 1);
                                typeof(Container).GetMethod("Save", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(m_containers[instanceId].Peek(), new object[] { });
                                typeof(Inventory).GetMethod("Changed", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(m_containers[instanceId].Peek().GetInventory(), new object[] { });
                                __instance.m_onConsumedItem(foodItem);
                                ___m_aiStatus = UpdateAiStatus(___m_nview, "Consume item");
                                m_assigned[instanceId] = false;
                                m_spottedItem[instanceId] = null;
                                m_searchcontainer[instanceId] = false;
                                m_stateChangeTimer[instanceId] = 0;
                                return false;
                            }
                        }
                    }
                    else
                    {
                        Container nearbyChest = FindRandomNearbyContainer(greylingPosition, m_containers[instanceId]);
                        if (nearbyChest != null)
                        {
                            m_containers[instanceId].Push(nearbyChest);
                            m_searchcontainer[instanceId] = true;
                            return false;
                        }
                        else
                        {
                            m_searchcontainer[instanceId] = false;
                            return false;
                        }
                    }
                }

                // Here starts the fun.

                //Assigned timeout-function 
                m_assignedTimer[instanceId] += dt;
                if (m_assignedTimer[instanceId] > GreylingsConfig.TimeLimitOnAssignment.Value) m_assigned[instanceId] = false;

                //Assignment timeout-function
                foreach (Assignment assignment in m_assignment[instanceId])
                {
                    assignment.AssignmentTime += dt;
                    int multiplicator = 1;
                    if (assignment.TypeOfAssignment.ComponentType == typeof(Fireplace))
                    {
                        multiplicator = 3;
                    }
                    if (assignment.AssignmentTime > GreylingsConfig.TimeBeforeAssignmentCanBeRepeated.Value * multiplicator)
                    {
                        ___m_aiStatus = UpdateAiStatus(___m_nview, $"removing outdated Assignment of {m_assignment[instanceId].Count()}");
                        m_assignment[instanceId].Remove(assignment);
                        ___m_aiStatus = UpdateAiStatus(___m_nview, $"remaining Assignments {m_assignment[instanceId].Count()}");
                        if (!m_assignment[instanceId].Any())
                        {
                            m_assigned[instanceId] = false;
                        }
                        break;
                    }
                }

                //stateChangeTimer Updated
                m_stateChangeTimer[instanceId] += dt;
                if (m_stateChangeTimer[instanceId] < 1) return false;


                if (!m_assigned[instanceId])
                {
                    if (FindRandomNearbyAssignment(instanceId, greylingPosition))
                    {
                        ___m_aiStatus = UpdateAiStatus(___m_nview, $"Doing assignment: {m_assignment[instanceId].Peek().TypeOfAssignment.Name}");
                        return false;
                    }
                    else
                    {
                        //___m_aiStatus = UpdateAiStatus(___m_nview, $"No new assignments found");
                        m_assignment[instanceId].Clear();
                    }
                }

                if (m_assigned[instanceId])
                {
                    var humanoid = ___m_character as Humanoid;
                    Assignment assignment = m_assignment[instanceId].Peek();
                    bool assignmentIsInvalid = assignment?.AssignmentObject?.GetComponent<ZNetView>()?.IsValid() == false;
                    if (assignmentIsInvalid)
                    {
                        m_assignment[instanceId].Pop();
                        m_assigned[instanceId] = false;
                        return false;
                    }

                    bool knowWhattoFetch = m_fetchitems[instanceId].Any();
                    bool isCarryingItem = m_carrying[instanceId] != null;
                    if ((!knowWhattoFetch || isCarryingItem) && !assignment.IsClose(greylingPosition))
                    {
                        ___m_aiStatus = UpdateAiStatus(___m_nview, $"Move To Assignment: {assignment.TypeOfAssignment.Name} ");
                        Invoke(__instance, "MoveAndAvoid", new object[] { dt, assignment.Position, 0.5f, false });
                        if (m_stateChangeTimer[instanceId] < 30)
                        {
                            return false;
                        }
                    }

                    bool isLookingAtAssignment
                        = (bool)Invoke(__instance, "IsLookingAt", new object[] { assignment.Position, 20f });
                    if (isCarryingItem && assignment.IsClose(greylingPosition) && !isLookingAtAssignment)
                    {
                        ___m_aiStatus = UpdateAiStatus(___m_nview, $"Looking at Assignment: {assignment.TypeOfAssignment.Name} ");
                        humanoid.SetMoveDir(Vector3.zero);
                        Invoke(__instance, "LookAt", new object[] { assignment.Position });
                        return false;
                    }

                    if (isCarryingItem && assignment.IsCloseEnough(greylingPosition))
                    {
                        humanoid.SetMoveDir(Vector3.zero);
                        var needFuel = assignment.NeedFuel;
                        var needOre = assignment.NeedOre;
                        bool isCarryingFuel = m_carrying[instanceId].m_shared.m_name == needFuel?.m_shared?.m_name;
                        bool isCarryingMatchingOre = needOre?.Any(c => m_carrying[instanceId].m_shared.m_name == c?.m_shared?.m_name) ?? false;

                        if (isCarryingFuel)
                        {
                            ___m_aiStatus = UpdateAiStatus(___m_nview, $"Unload to {assignment.TypeOfAssignment.Name} -> Fuel");
                            assignment.AssignmentObject.GetComponent<ZNetView>().InvokeRPC("AddFuel", new object[] { });
                            humanoid.GetInventory().RemoveOneItem(m_carrying[instanceId]);
                        }
                        else if (isCarryingMatchingOre)
                        {
                            ___m_aiStatus = UpdateAiStatus(___m_nview, $"Unload to {assignment.TypeOfAssignment.Name} -> Ore");
                            assignment.AssignmentObject.GetComponent<ZNetView>().InvokeRPC("AddOre", new object[] { GetPrefabName(m_carrying[instanceId].m_dropPrefab.name) });
                            humanoid.GetInventory().RemoveOneItem(m_carrying[instanceId]);
                        }
                        else
                        {
                            ___m_aiStatus = UpdateAiStatus(___m_nview, Localization.instance.Localize($"Dropping {m_carrying[instanceId].m_shared.m_name} on the ground"));
                            humanoid.DropItem(humanoid.GetInventory(), m_carrying[instanceId], 1);
                        }

                        humanoid.UnequipItem(m_carrying[instanceId], false);
                        m_carrying[instanceId] = null;
                        m_fetchitems[instanceId].Clear();
                        m_stateChangeTimer[instanceId] = 0;
                        return false;
                    }

                    if (!knowWhattoFetch && assignment.IsCloseEnough(greylingPosition))
                    {
                        humanoid.SetMoveDir(Vector3.zero);
                        ___m_aiStatus = UpdateAiStatus(___m_nview, "Checking assignment for task");
                        var needFuel = assignment.NeedFuel;
                        var needOre = assignment.NeedOre;
                        Dbgl($"Ore:{needOre.Join(j => j.m_shared.m_name)}, Fuel:{needFuel?.m_shared.m_name}");
                        if (needFuel != null)
                        {
                            m_fetchitems[instanceId].Add(needFuel);
                            ___m_aiStatus = UpdateAiStatus(___m_nview, Localization.instance.Localize($"Adding {needFuel.m_shared.m_name} to search list"));
                        }
                        if (needOre.Any())
                        {
                            m_fetchitems[instanceId].AddRange(needOre);
                            ___m_aiStatus = UpdateAiStatus(___m_nview, Localization.instance.Localize($"Adding {needOre.Join(o => o.m_shared.m_name)} to search list"));
                        }
                        if (!m_fetchitems[instanceId].Any())
                        {
                            m_assigned[instanceId] = false;
                        }
                        m_stateChangeTimer[instanceId] = 0;
                        return false;
                    }

                    bool hasSpottedAnItem = m_spottedItem[instanceId] != null;
                    bool searchForItemToPickup = knowWhattoFetch && !hasSpottedAnItem && !isCarryingItem && !m_searchcontainer[instanceId];
                    if (searchForItemToPickup)
                    {
                        ___m_aiStatus = UpdateAiStatus(___m_nview, "Search the ground for item to pickup");
                        ItemDrop spottedItem = GetNearbyItem(greylingPosition, m_fetchitems[instanceId], GreylingsConfig.ItemSearchRadius.Value);
                        if (spottedItem != null)
                        {
                            m_spottedItem[instanceId] = spottedItem;
                            m_stateChangeTimer[instanceId] = 0;
                            return false;
                        }

                        ___m_aiStatus = UpdateAiStatus(___m_nview, "Trying to remeber content of known Chests");
                        foreach (Container chest in m_containers[instanceId])
                        {
                            foreach (var fetchItem in m_fetchitems[instanceId])
                            {
                                ItemDrop.ItemData item = chest?.GetInventory()?.GetItem(fetchItem.m_shared.m_name);
                                if (item == null) continue;
                                else
                                {
                                    ___m_aiStatus = UpdateAiStatus(___m_nview, "Item found in old chest");
                                    m_containers[instanceId].Remove(chest);
                                    m_containers[instanceId].Push(chest);
                                    m_searchcontainer[instanceId] = true;
                                    m_stateChangeTimer[instanceId] = 0;
                                    return false;
                                }
                            }
                        }

                        ___m_aiStatus = UpdateAiStatus(___m_nview, "Search for nerby Chests");
                        Container nearbyChest = FindRandomNearbyContainer(greylingPosition, m_containers[instanceId]);
                        if (nearbyChest != null)
                        {
                            ___m_aiStatus = UpdateAiStatus(___m_nview, "Chest found");
                            m_containers[instanceId].Push(nearbyChest);
                            m_searchcontainer[instanceId] = true;
                            m_stateChangeTimer[instanceId] = 0;
                            return false;
                        }
                    }

                    if (m_searchcontainer[instanceId])
                    {
                        bool containerIsInvalid = m_containers[instanceId].Peek()?.GetComponent<ZNetView>()?.IsValid() == false;
                        if (containerIsInvalid)
                        {
                            m_containers[instanceId].Pop();
                            m_searchcontainer[instanceId] = false;
                            return false;
                        }
                        bool isCloseToContainer = Vector3.Distance(greylingPosition, m_containers[instanceId].Peek().transform.position) < 1.5;
                        if (!isCloseToContainer)
                        {
                            ___m_aiStatus = UpdateAiStatus(___m_nview, "Heading to Container");
                            Invoke(__instance, "MoveAndAvoid", new object[] { dt, m_containers[instanceId].Peek().transform.position, 0.5f, false });
                            return false;
                        }
                        else
                        {
                            humanoid.SetMoveDir(Vector3.zero);
                            ___m_aiStatus = UpdateAiStatus(___m_nview, $"Chest inventory:{m_containers[instanceId].Peek()?.GetInventory().GetAllItems().Join(i => i.m_shared.m_name)} from Chest ");
                            var wantedItemsInChest = m_containers[instanceId].Peek()?.GetInventory()?.GetAllItems()?.Where(i => m_fetchitems[instanceId].Contains(i));
                            foreach (var fetchItem in m_fetchitems[instanceId])
                            {
                                ItemDrop.ItemData item = m_containers[instanceId].Peek()?.GetInventory()?.GetItem(fetchItem.m_shared.m_name);
                                if (item == null) continue;
                                else
                                {
                                    ___m_aiStatus = UpdateAiStatus(___m_nview, $"Trying to Pickup {item} from Chest ");
                                    var pickedUpInstance = humanoid.PickupPrefab(item.m_dropPrefab);
                                    humanoid.GetInventory().Print();
                                    humanoid.EquipItem(pickedUpInstance);
                                    m_containers[instanceId].Peek().GetInventory().RemoveItem(item, 1);
                                    typeof(Container).GetMethod("Save", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(m_containers[instanceId].Peek(), new object[] { });
                                    typeof(Inventory).GetMethod("Changed", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(m_containers[instanceId].Peek().GetInventory(), new object[] { });
                                    m_carrying[instanceId] = pickedUpInstance;
                                    m_spottedItem[instanceId] = null;
                                    m_fetchitems[instanceId].Clear();
                                    m_searchcontainer[instanceId] = false;
                                    m_stateChangeTimer[instanceId] = 0;
                                    return false;
                                }
                            }

                            m_searchcontainer[instanceId] = false;
                            m_stateChangeTimer[instanceId] = 0;
                            return false;
                        }
                    }

                    if (hasSpottedAnItem)
                    {
                        bool isNotCloseToPickupItem = Vector3.Distance(greylingPosition, m_spottedItem[instanceId].transform.position) > 1;
                        if (isNotCloseToPickupItem)
                        {
                            ___m_aiStatus = UpdateAiStatus(___m_nview, "Heading to pickup item");
                            Invoke(__instance, "MoveAndAvoid", new object[] { dt, m_spottedItem[instanceId].transform.position, 0.5f, false });
                            return false;
                        }
                        else // Pickup item from ground
                        {
                            humanoid.SetMoveDir(Vector3.zero);
                            ___m_aiStatus = UpdateAiStatus(___m_nview, $"Trying to Pickup {m_spottedItem[instanceId].gameObject.name}");
                            var pickedUpInstance = humanoid.PickupPrefab(m_spottedItem[instanceId].m_itemData.m_dropPrefab);

                            humanoid.GetInventory().Print();

                            humanoid.EquipItem(pickedUpInstance);
                            if (m_spottedItem[instanceId].m_itemData.m_stack == 1)
                            {
                                if (___m_nview.GetZDO() == null)
                                {
                                    Destroy(m_spottedItem[instanceId].gameObject);
                                }
                                else
                                {
                                    ZNetScene.instance.Destroy(m_spottedItem[instanceId].gameObject);
                                }
                            }
                            else
                            {
                                m_spottedItem[instanceId].m_itemData.m_stack--;
                                Traverse.Create(m_spottedItem[instanceId]).Method("Save").GetValue();
                            }
                            m_carrying[instanceId] = pickedUpInstance;
                            m_spottedItem[instanceId] = null;
                            m_fetchitems[instanceId].Clear();
                            m_stateChangeTimer[instanceId] = 0;
                            return false;
                        }
                    }

                    ___m_aiStatus = UpdateAiStatus(___m_nview, $"Done with assignment");
                    if (m_carrying[instanceId] != null)
                    {
                        humanoid.UnequipItem(m_carrying[instanceId], false);
                        m_carrying[instanceId] = null;
                        ___m_aiStatus = UpdateAiStatus(___m_nview, $"Dropping unused item");
                    }
                    m_fetchitems[instanceId].Clear();
                    m_spottedItem[instanceId] = null;
                    m_containers[instanceId].Clear();
                    m_searchcontainer[instanceId] = false;
                    m_assigned[instanceId] = false;
                    m_stateChangeTimer[instanceId] = 0;
                    return false;
                }

                ___m_aiStatus = UpdateAiStatus(___m_nview, "Random movement (No new assignments found)");
                typeof(MonsterAI).GetMethod("IdleMovement", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(__instance, new object[] { dt });
                return false;
            }

            public static ItemDrop GetNearbyItem(Vector3 center, List<ItemDrop.ItemData> acceptedNames, int range = 10)
            {
                ItemDrop ClosestObject = null;
                foreach (Collider collider in Physics.OverlapSphere(center, range, LayerMask.GetMask(new string[] { "item" })))
                {
                    ItemDrop item = collider.transform.parent?.parent?.gameObject?.GetComponent<ItemDrop>();
                    if (item?.GetComponent<ZNetView>()?.IsValid() != true)
                    {
                        item = collider.transform.parent?.gameObject?.GetComponent<ItemDrop>();
                        if (item?.GetComponent<ZNetView>()?.IsValid() != true)
                        {
                            item = collider.transform?.gameObject?.GetComponent<ItemDrop>();
                            if (item?.GetComponent<ZNetView>()?.IsValid() != true)
                            {
                                continue;
                            }
                        }
                    }
                    if (item?.transform?.position != null && acceptedNames.Select(n => n.m_shared.m_name).Contains(item.m_itemData.m_shared.m_name) && (ClosestObject == null || Vector3.Distance(center, item.transform.position) < Vector3.Distance(center, ClosestObject.transform.position)))
                    {
                        ClosestObject = item;
                    }
                }
                return ClosestObject;
            }

            private static bool FindRandomNearbyAssignment(int instanceId, Vector3 greylingPosition)
            {
                Dbgl($"Enter {nameof(FindRandomNearbyAssignment)}");
                //Generate list of acceptable assignments
                var pieceList = new List<Piece>();
                Piece.GetAllPiecesInRadius(greylingPosition, (float)GreylingsConfig.AssignmentSearchRadius.Value, pieceList);
                var allAssignablePieces = pieceList.Where(p => Assignment.AssignmentTypes.Any(a => GetPrefabName(p.name) == a.PieceName && a.Activated));
                // no assignments detekted, return false
                if (!allAssignablePieces.Any())
                {
                    return false;
                }

                // filter out assignments already in list
                var newAssignments = allAssignablePieces.Where(p => !m_assignment[instanceId].Any(a => a.AssignmentObject == p.gameObject));

                // filter out inaccessible assignments
                //newAssignments = newAssignments.Where(p => Pathfinding.instance.GetPath(greylingPosition, p.gameObject.transform.position, null, Pathfinding.AgentType.Humanoid, true, true));

                if (!newAssignments.Any())
                {
                    return false;
                }

                // select random piece
                var random = new System.Random();
                int index = random.Next(newAssignments.Count());
                Assignment randomAssignment = new Assignment(instanceId, newAssignments.ElementAt(index));
                // Create assignment and return true
                m_assignment[instanceId].Push(randomAssignment);
                m_assigned[instanceId] = true;
                m_assignedTimer[instanceId] = 0;
                m_fetchitems[instanceId].Clear();
                m_spottedItem[instanceId] = null;
                return true;
            }

            private static Container FindRandomNearbyContainer(Vector3 greylingPosition, MaxStack<Container> knownContainers)
            {
                Dbgl($"Enter {nameof(FindRandomNearbyContainer)}");
                var pieceList = new List<Piece>();
                Piece.GetAllPiecesInRadius(greylingPosition, (float)GreylingsConfig.ContainerSearchRadius.Value, pieceList);
                var allcontainerPieces = pieceList.Where(p => m_acceptedContainerNames.Contains(GetPrefabName(p.name)));
                // no containers detected, return false

                var containers = allcontainerPieces?.Select(p => p.gameObject.GetComponent<Container>()).Where(c => !knownContainers.Contains(c));
                if (!containers.Any())
                {
                    return null;
                }

                // select random piece
                var random = new System.Random();
                int index = random.Next(containers.Count());
                return containers.ElementAt(index);
            }

            private static object Invoke(MonsterAI instance, string methodName, object[] argumentList)
            {
                return typeof(MonsterAI).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(instance, argumentList);
            }

            private static int InitInstanceIfNeeded(MonsterAI instance)
            {
                int instanceId = instance.GetInstanceID();
                bool isNewInstance = !m_assignment.ContainsKey(instanceId);
                if (isNewInstance)
                {
                    m_assignment.Add(instanceId, new MaxStack<Assignment>(20));
                    m_containers.Add(instanceId, new MaxStack<Container>(GreylingsConfig.MaxContainersInMemory.Value));
                    m_assigned.Add(instanceId, false);
                    m_searchcontainer.Add(instanceId, false);
                    m_fetchitems.Add(instanceId, new List<ItemDrop.ItemData>());
                    m_carrying.Add(instanceId, null);
                    m_spottedItem.Add(instanceId, null);
                    m_aiStatus.Add(instanceId, "Init");
                    m_assignedTimer.Add(instanceId, 0);
                    m_stateChangeTimer.Add(instanceId, 0);
                }
                return instanceId;
            }

            static bool AvoidFire(MonsterAI instance, float dt, Vector3 targetPosition)
            {
                EffectArea effectArea2 = EffectArea.IsPointInsideArea(instance.transform.position, EffectArea.Type.Burning, 2f);
                if ((bool)effectArea2)
                {
                    typeof(MonsterAI).GetMethod("RandomMovementArroundPoint", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(instance, new object[] { dt, effectArea2.transform.position, effectArea2.GetRadius() + 3f, true });
                    return true;
                }
                return false;
            }

            private static string GetPrefabName(string name)
            {
                char[] anyOf = new char[] { '(', ' ' };
                int num = name.IndexOfAny(anyOf);
                string result;
                if (num >= 0)
                    result = name.Substring(0, num);
                else
                    result = name;
                return result;
            }
        }
    }
}
