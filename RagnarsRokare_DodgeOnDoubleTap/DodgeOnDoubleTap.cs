﻿using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using UnityEngine;

namespace RagnarsRokare_DodgeOnDoubleTap
{
	[BepInPlugin(ModId, ModName, ModVersion)]
	[BepInProcess("valheim.exe")]
    public class DodgeOnDoubleTap : BaseUnityPlugin
    {
		public const string ModId = "RagnarsRokare.DodgeOnDoubleTap";
		public const string ModName = "RagnarsRökare DodgeOnDoubleTapMod";
		public const string ModVersion = "0.4";

		private readonly Harmony harmony = new Harmony(ModId);
		public static ConfigEntry<int> DodgeTapHoldMax;
		public static ConfigEntry<int> DodgeDoubleTapDelay;
		public static ConfigEntry<int> NexusID;

		void Awake()
        {
			Debug.Log($"Loading {ModName} v{ModVersion}, lets get rolling!");
			harmony.PatchAll();
			DodgeTapHoldMax = Config.Bind("General", "DodgeTapHoldMax", 200);
			DodgeDoubleTapDelay = Config.Bind("General", "DodgeDoubleTapDelay", 300);
			NexusID = Config.Bind<int>("General", "NexusID", 871, "Nexus mod ID for updates");
		}

		public enum DodgeDirection { None, Forward, Backward, Left, Right };
		public static DodgeDirection DodgeDir { get; set; } = DodgeDirection.None;

		[HarmonyPatch(typeof(Player), "SetControls")]
		class SetControls_Patch
		{
			static void Postfix(ref float ___m_queuedDodgeTimer, ref Vector3 ___m_queuedDodgeDir, Vector3 ___m_lookDir)
            {
				if (DodgeDir == DodgeDirection.None)
                {
					return ;
                }

				___m_queuedDodgeTimer = 0.5f;

				var dodgeDir = ___m_lookDir;
				dodgeDir.y = 0f;
				dodgeDir.Normalize();

				if (DodgeDir == DodgeDirection.Backward)  dodgeDir = -dodgeDir;
				else if (DodgeDir == DodgeDirection.Left) dodgeDir = Quaternion.AngleAxis(-90, Vector3.up) * dodgeDir;
				else if (DodgeDir == DodgeDirection.Right) dodgeDir = Quaternion.AngleAxis(90, Vector3.up) * dodgeDir;

				___m_queuedDodgeDir = dodgeDir;
				DodgeDir = DodgeDirection.None;
			}
		}

		[HarmonyPatch(typeof(PlayerController), "FixedUpdate")]
        class FixedUpdate_Patch
        {
			private static DateTime? m_forwardLastTapRegistered = DateTime.Now;
			private static DateTime? m_backwardLastTapRegistered = DateTime.Now;
			private static DateTime? m_leftLastTapRegistered = DateTime.Now;
			private static DateTime? m_rightLastTapRegistered = DateTime.Now;

			private static DateTime m_forwardLastTapCheck = DateTime.Now;
			private static DateTime m_backwardLastTapCheck = DateTime.Now;
			private static DateTime m_leftLastTapCheck = DateTime.Now;
			private static DateTime m_rightLastTapCheck = DateTime.Now;

			private static float m_forwardPressTimer = 0;
			private static float m_backwardPressTimer = 0;
			private static float m_leftPressTimer = 0;
			private static float m_rightPressTimer = 0;

			static bool Prefix(PlayerController __instance, ZNetView ___m_nview)
            {
				if ((bool)___m_nview && !___m_nview.IsOwner())
				{
					return true;
				}
				if (!(bool)Traverse.Create(__instance).Method("TakeInput").GetValue())
				{
					return true;
				}
				if ((bool)Traverse.Create(__instance).Method("InInventoryEtc").GetValue())
                {
					return true;
                }
				if (ZInput.GetButton("Forward"))
				{
					DetectTap(true, (float)(DateTime.Now - m_forwardLastTapCheck).TotalMilliseconds, DodgeTapHoldMax.Value, ref m_forwardPressTimer);
					m_forwardLastTapCheck = DateTime.Now;
				}
				else
                {
					var isTap = DetectTap(false, (float)(DateTime.Now - m_forwardLastTapCheck).TotalMilliseconds, DodgeTapHoldMax.Value, ref m_forwardPressTimer);
					m_forwardLastTapCheck = DateTime.Now;
					CheckForDoubleTapDodge(isTap, ref m_forwardLastTapRegistered, DodgeDirection.Forward);
                }
                if (ZInput.GetButton("Backward"))
				{
					DetectTap(true, (float)(DateTime.Now - m_backwardLastTapCheck).TotalMilliseconds, DodgeTapHoldMax.Value, ref m_backwardPressTimer);
					m_backwardLastTapCheck = DateTime.Now;
				}
				else
				{
					bool isTap = DetectTap(false, (float)(DateTime.Now - m_backwardLastTapCheck).TotalMilliseconds, DodgeTapHoldMax.Value, ref m_backwardPressTimer);
					m_backwardLastTapCheck = DateTime.Now;
					CheckForDoubleTapDodge(isTap, ref m_backwardLastTapRegistered, DodgeDirection.Backward);
				}
				if (ZInput.GetButton("Left"))
				{
					DetectTap(true, (float)(DateTime.Now - m_leftLastTapCheck).TotalMilliseconds, DodgeTapHoldMax.Value, ref m_leftPressTimer);
					m_leftLastTapCheck = DateTime.Now;
				}
				else
				{
					bool isTap = DetectTap(false, (float)(DateTime.Now - m_leftLastTapCheck).TotalMilliseconds, DodgeTapHoldMax.Value, ref m_leftPressTimer);
					m_leftLastTapCheck = DateTime.Now;
					CheckForDoubleTapDodge(isTap, ref m_leftLastTapRegistered, DodgeDirection.Left);
				}
				if (ZInput.GetButton("Right"))
				{
					DetectTap(true, (float)(DateTime.Now - m_rightLastTapCheck).TotalMilliseconds, DodgeTapHoldMax.Value, ref m_rightPressTimer);
					m_rightLastTapCheck = DateTime.Now;
				}
				else
				{
					bool isTap = DetectTap(false, (float)(DateTime.Now - m_rightLastTapCheck).TotalMilliseconds, DodgeTapHoldMax.Value, ref m_rightPressTimer);
					m_rightLastTapCheck = DateTime.Now;
					CheckForDoubleTapDodge(isTap, ref m_rightLastTapRegistered, DodgeDirection.Right);
				}
				return true;
			}

            private static void CheckForDoubleTapDodge(bool isTap, ref DateTime? lastTapRegistered, DodgeDirection dodgeDirection)
            {
                if (isTap)
                {
					var milliesSinceLastTap = (DateTime.Now - lastTapRegistered)?.TotalMilliseconds ?? DodgeDoubleTapDelay.Value;
					if (milliesSinceLastTap < DodgeDoubleTapDelay.Value)
					{
						DodgeDir = dodgeDirection;
					}
					else
					{
						lastTapRegistered = null;
					}

					if (lastTapRegistered == null)
                    {
						lastTapRegistered = DateTime.Now;
					}
                }
            }

			private static bool DetectTap(bool isPressed, float timeSinceLastCheck, float maxPressTime, ref float pressTimer)
            {
				if (isPressed)
                {
					pressTimer += timeSinceLastCheck;
					return false;
                }
                else if (pressTimer > 0)
				{
					bool isTap = pressTimer < maxPressTime;
					pressTimer = 0;
					return isTap;
				}
				return false;
            }
		}
	}
}
