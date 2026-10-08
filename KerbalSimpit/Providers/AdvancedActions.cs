using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;

namespace KerbalSimpit.Providers
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ScienceValueMessage
    {
        public float value;
    }

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class KerbalSimpitAdvancedActionProvider : MonoBehaviour
    {
        private EventData<byte, object> setSingleActionChannel;
        private EventData<byte, object> advancedStateChannel;
        private EventData<byte, object> scienceValueChannel;
        private EventData<byte, object> scienceThresholdChannel;
        private EventData<byte, object> scienceBlinkIntervalChannel;
        private float lastSentScienceValue = -1f;
        private float scienceValueSendTimer;
        private bool collectScheduled;
        private float collectTimer;
        private float acknowledgedScienceValue = -1f;
        private string acknowledgedScienceContext;
        private readonly ConcurrentQueue<byte> pendingActions = new ConcurrentQueue<byte>();
        private uint currentState;
        private bool resendState;
        private float stateSendTimer;
        private float settingsSendTimer;

        public void Start()
        {
            setSingleActionChannel = GameEvents.FindEvent<EventData<byte, object>>(
                "onSerialReceived" + InboundPackets.SetSingleActionGroup);
            if (setSingleActionChannel != null)
            {
                setSingleActionChannel.Add(SetSingleActionCallback);
            }

            advancedStateChannel = GameEvents.FindEvent<EventData<byte, object>>(
                "toSerial" + OutboundPackets.AdvancedActionGroups);
            EventData<byte, object> forceSendChannel = GameEvents.FindEvent<EventData<byte, object>>(
                "onSerialChannelForceSend" + OutboundPackets.AdvancedActionGroups);
            if (forceSendChannel != null)
            {
                forceSendChannel.Add(ResendState);
            }

            scienceValueChannel = GameEvents.FindEvent<EventData<byte, object>>(
                "toSerial" + OutboundPackets.ScienceValue);
            scienceThresholdChannel = GameEvents.FindEvent<EventData<byte, object>>(
                "toSerial" + OutboundPackets.ScienceThreshold);
            scienceBlinkIntervalChannel = GameEvents.FindEvent<EventData<byte, object>>(
                "toSerial" + OutboundPackets.ScienceBlinkInterval);
        }

        private static void ApplyToRadiators(byte setting)
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null) return;

            foreach (ModuleDeployableRadiator radiator in vessel.FindPartModulesImplementing<ModuleDeployableRadiator>())
            {
                if (setting == ActionGroupSettings.activate)
                {
                    radiator.Extend();
                }
                else if (setting == ActionGroupSettings.deactivate)
                {
                    radiator.Retract();
                }
                else if (setting == ActionGroupSettings.toggle)
                {
                    object state = GetMemberValue(radiator, "deployState");
                    if (state != null && state.ToString().IndexOf("EXTENDED", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        radiator.Retract();
                    }
                    else
                    {
                        radiator.Extend();
                    }
                }
            }

            foreach (ModuleActiveRadiator radiator in vessel.FindPartModulesImplementing<ModuleActiveRadiator>())
            {
                if (setting == ActionGroupSettings.activate)
                {
                    radiator.Activate();
                }
                else if (setting == ActionGroupSettings.deactivate)
                {
                    radiator.Shutdown();
                }
                else if (setting == ActionGroupSettings.toggle)
                {
                    if (radiator.IsCooling)
                    {
                        radiator.Shutdown();
                    }
                    else
                    {
                        radiator.Activate();
                    }
                }
            }
        }

        public void OnDestroy()
        {
            if (setSingleActionChannel != null)
            {
                setSingleActionChannel.Remove(SetSingleActionCallback);
            }
        }

        public void Update()
        {
            byte action;
            while (pendingActions.TryDequeue(out action))
            {
                ApplyAction(action);
            }

            if (collectScheduled)
            {
                collectTimer += Time.deltaTime;
                if (collectTimer >= Mathf.Max(0f, KSPit.Config.ScienceCollectDelay))
                {
                    CollectAllScienceOnce();
                    collectScheduled = false;
                    collectTimer = 0f;
                }
            }

            uint newState = GetState();
            stateSendTimer += Time.deltaTime;
            if (advancedStateChannel != null &&
                (newState != currentState || resendState ||
                 stateSendTimer >= Mathf.Max(0.05f, KSPit.Config.StatusUpdateIntervalMs / 1000f)))
            {
                resendState = false;
                advancedStateChannel.Fire(OutboundPackets.AdvancedActionGroups, newState);
                currentState = newState;
                stateSendTimer = 0f;
            }

            float scienceValue = GetScienceValue();
            scienceValueSendTimer += Time.deltaTime;
            if (scienceValueChannel != null &&
                (Mathf.Abs(scienceValue - lastSentScienceValue) > 0.01f ||
                 scienceValueSendTimer >= 0.5f))
            {
                ScienceValueMessage message = new ScienceValueMessage { value = scienceValue };
                scienceValueChannel.Fire(OutboundPackets.ScienceValue, message);
                lastSentScienceValue = scienceValue;
                scienceValueSendTimer = 0f;
            }

            settingsSendTimer += Time.deltaTime;
            if (settingsSendTimer >= 1f)
            {
                SendScienceSettings();
                settingsSendTimer = 0f;
            }
        }

        private void SendScienceSettings()
        {
            if (scienceThresholdChannel != null)
            {
                scienceThresholdChannel.Fire(
                    OutboundPackets.ScienceThreshold,
                    new ScienceValueMessage { value = Mathf.Max(0f, KSPit.Config.ScienceThreshold) });
            }
            if (scienceBlinkIntervalChannel != null)
            {
                scienceBlinkIntervalChannel.Fire(
                    OutboundPackets.ScienceBlinkInterval,
                    new ScienceValueMessage { value = Mathf.Max(50f, KSPit.Config.ScienceBlinkIntervalMs) });
            }
        }

        private void SetSingleActionCallback(byte id, object data)
        {
            byte[] payload = data as byte[];
            if (payload != null && payload.Length > 0)
            {
                pendingActions.Enqueue(payload[0]);
            }
        }

        private void ResendState(byte id, object data)
        {
            resendState = true;
        }

        private void ApplyAction(byte action)
        {
            int actionIndex = (action & 0xfc) >> 2;
            byte setting = (byte)(action & 0x03);

            switch (actionIndex)
            {
                case AdvancedActionGroupIndexes.advancedSolarAction:
                    ApplyToSolarAndAntennas(setting);
                    break;
                case AdvancedActionGroupIndexes.advancedRadiatorAction:
                    ApplyToRadiators(setting);
                    break;
                case AdvancedActionGroupIndexes.advancedScienceAction:
                    if (setting == ActionGroupSettings.activate || setting == ActionGroupSettings.toggle)
                    {
                        CollectScience();
                    }
                    else if (setting == ActionGroupSettings.deactivate)
                    {
                        ResetScience();
                    }
                    break;
            }
        }

        private void ApplyToSolarAndAntennas(byte setting)
        {
            ApplyToModules<ModuleDeployableSolarPanel>(setting);
            if (KSPit.Config.SolarControlsAntennas)
            {
                ApplyToModules<ModuleDeployableAntenna>(setting);
            }
        }

        private void ApplyToModules<T>(byte setting) where T : PartModule
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null) return;

            foreach (T module in vessel.FindPartModulesImplementing<T>())
            {
                if (setting == ActionGroupSettings.activate)
                {
                    InvokeModuleMethod(module, "Extend");
                }
                else if (setting == ActionGroupSettings.deactivate)
                {
                    InvokeModuleMethod(module, "Retract");
                }
                else if (setting == ActionGroupSettings.toggle)
                {
                    InvokeModuleMethod(module, "Toggle");
                }
            }
        }

        private static void InvokeModuleMethod(PartModule module, string methodName)
        {
            MethodInfo method = module.GetType().GetMethod(
                methodName, BindingFlags.Instance | BindingFlags.Public);
            if (method != null && method.GetParameters().Length == 0)
            {
                method.Invoke(module, null);
            }
        }

        private void CollectScience()
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null) return;

            foreach (ModuleScienceExperiment experiment in vessel.FindPartModulesImplementing<ModuleScienceExperiment>())
            {
                experiment.DeployExperiment();
            }
            acknowledgedScienceValue = GetRawScienceValue(vessel);
            acknowledgedScienceContext = GetScienceContext(vessel);
            collectScheduled = KSPit.Config.ScienceAutoCollect;
            collectTimer = 0f;
        }

        private void ResetScience()
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null) return;

            foreach (ModuleScienceExperiment experiment in vessel.FindPartModulesImplementing<ModuleScienceExperiment>())
            {
                if (!experiment.Inoperable)
                {
                    experiment.ResetExperiment();
                }
            }

            collectScheduled = false;
            acknowledgedScienceValue = -1f;
            acknowledgedScienceContext = null;
        }
        private void CollectAllScienceOnce()
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null) return;

            ModuleScienceContainer[] containers =
                vessel.FindPartModulesImplementing<ModuleScienceContainer>().ToArray();
            if (containers.Length == 0) return;

            containers[0].CollectAllEvent();

            float reviewValue = GetRawScienceValue(vessel);
            if (reviewValue > acknowledgedScienceValue + 0.01f)
            {
                acknowledgedScienceValue = reviewValue;
            }
        }

        private uint GetState()
        {
            uint state = 0;
            state |= (uint)GetSolarAndAntennaState() <<
                     (AdvancedActionGroupIndexes.advancedSolarAction * 2);
            state |= (uint)GetRadiatorState() <<
                     (AdvancedActionGroupIndexes.advancedRadiatorAction * 2);
            state |= (uint)GetBrakesState() <<
                     (AdvancedActionGroupIndexes.advancedBrakesAction * 2);
            state |= (uint)GetGearState() <<
                     (AdvancedActionGroupIndexes.advancedGearAction * 2);

            if (HasScienceData())
            {
                state |= (uint)AdvancedActionGroupStates.active <<
                         (AdvancedActionGroupIndexes.advancedScienceAction * 2);
            }
            return state;
        }

        private static byte GetDeployableState<T>() where T : PartModule
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null) return AdvancedActionGroupStates.notAvailable;

            int moduleCount = 0;
            int extendedCount = 0;
            int retractedCount = 0;
            foreach (T module in vessel.FindPartModulesImplementing<T>())
            {
                moduleCount++;
                object value = GetMemberValue(module, "panelState") ??
                               GetMemberValue(module, "deployState");
                if (value != null && value.ToString().IndexOf("EXTENDED", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    extendedCount++;
                }
                else if (value != null && value.ToString().IndexOf("RETRACTED", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    retractedCount++;
                }
            }

            if (moduleCount == 0) return AdvancedActionGroupStates.notAvailable;
            if (extendedCount == moduleCount) return AdvancedActionGroupStates.active;
            if (retractedCount == moduleCount) return AdvancedActionGroupStates.inactive;
            return AdvancedActionGroupStates.mixed;
        }

        private static byte GetSolarAndAntennaState()
        {
            return MergeDeployableStates(
                GetDeployableState<ModuleDeployableSolarPanel>(),
                GetDeployableState<ModuleDeployableAntenna>());
        }

        private static byte MergeDeployableStates(byte first, byte second)
        {
            if (first == AdvancedActionGroupStates.notAvailable) return second;
            if (second == AdvancedActionGroupStates.notAvailable) return first;
            if (first == second) return first;
            return AdvancedActionGroupStates.mixed;
        }

        private static byte GetBrakesState()
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null) return AdvancedActionGroupStates.notAvailable;
            return vessel.ActionGroups[KSPActionGroup.Brakes]
                ? AdvancedActionGroupStates.active
                : AdvancedActionGroupStates.inactive;
        }

        private static byte GetGearState()
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null) return AdvancedActionGroupStates.notAvailable;
            return vessel.ActionGroups[KSPActionGroup.Gear]
                ? AdvancedActionGroupStates.active
                : AdvancedActionGroupStates.inactive;
        }

        private static byte GetRadiatorState()
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null) return AdvancedActionGroupStates.notAvailable;

            int count = 0;
            int active = 0;
            foreach (ModuleActiveRadiator radiator in vessel.FindPartModulesImplementing<ModuleActiveRadiator>())
            {
                count++;
                if (radiator.IsCooling) active++;
            }

            if (count == 0) return AdvancedActionGroupStates.notAvailable;
            if (active == count) return AdvancedActionGroupStates.active;
            if (active == 0) return AdvancedActionGroupStates.inactive;
            return AdvancedActionGroupStates.mixed;
        }

        private static object GetMemberValue(object instance, string memberName)
        {
            Type type = instance.GetType();
            FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null) return field.GetValue(instance);
            PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return property == null ? null : property.GetValue(instance, null);
        }

        private bool HasScienceData()
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null) return false;
            if (IsAcknowledged(vessel)) return false;

            foreach (ModuleScienceExperiment experiment in vessel.FindPartModulesImplementing<ModuleScienceExperiment>())
            {
                if (!experiment.Inoperable &&
                    (!experiment.Deployed || experiment.rerunnable) &&
                    HasUnstoredScience(experiment, vessel))
                {
                    return true;
                }
            }
            return false;
        }

        private float GetScienceValue()
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null) return 0f;
            if (IsAcknowledged(vessel)) return 0f;

            return GetRawScienceValue(vessel);
        }

        private float GetRawScienceValue(Vessel vessel)
        {
            if (vessel == null) return 0f;

            float total = 0f;
            foreach (ModuleScienceExperiment experiment in vessel.FindPartModulesImplementing<ModuleScienceExperiment>())
            {
                if (experiment.Inoperable || (experiment.Deployed && !experiment.rerunnable))
                {
                    continue;
                }

                ScienceExperiment definition = ResearchAndDevelopment.GetExperiment(experiment.experimentID);
                if (definition == null) continue;

                ExperimentSituations situation = ScienceUtil.GetExperimentSituation(vessel);
                string biome = ScienceUtil.GetExperimentBiome(
                    vessel.mainBody, vessel.latitude, vessel.longitude);
                ScienceSubject subject = ResearchAndDevelopment.GetExperimentSubject(
                    definition, situation, vessel.mainBody, biome, vessel.vesselType.ToString());
                if (subject != null && HasUnstoredScience(experiment, vessel))
                {
                    total += ResearchAndDevelopment.GetScienceValue(
                        definition.dataScale, experiment.scienceValueRatio, subject);
                }
            }
            return total;
        }

        private bool IsAcknowledged(Vessel vessel)
        {
            return acknowledgedScienceContext == GetScienceContext(vessel) &&
                   GetRawScienceValue(vessel) <= acknowledgedScienceValue + 0.01f;
        }

        private static string GetScienceContext(Vessel vessel)
        {
            ExperimentSituations situation = ScienceUtil.GetExperimentSituation(vessel);
            string biome = ScienceUtil.GetExperimentBiome(
                vessel.mainBody, vessel.latitude, vessel.longitude);
            return vessel.mainBody.flightGlobalsIndex + "|" + situation + "|" + biome;
        }

        private static bool HasUnstoredScience(
            ModuleScienceExperiment experiment, Vessel vessel)
        {
            ScienceExperiment definition = ResearchAndDevelopment.GetExperiment(experiment.experimentID);
            if (definition == null) return false;

            ExperimentSituations situation = ScienceUtil.GetExperimentSituation(vessel);
            string biome = ScienceUtil.GetExperimentBiome(
                vessel.mainBody, vessel.latitude, vessel.longitude);
            ScienceSubject subject = ResearchAndDevelopment.GetExperimentSubject(
                definition, situation, vessel.mainBody, biome, vessel.vesselType.ToString());
            if (subject == null) return false;

            foreach (ScienceData data in experiment.GetData())
            {
                if (data != null && data.subjectID == subject.id) return false;
            }

            foreach (ModuleScienceContainer container in
                     vessel.FindPartModulesImplementing<ModuleScienceContainer>())
            {
                foreach (ScienceData data in container.GetData())
                {
                    if (data != null && data.subjectID == subject.id)
                    {
                        return false;
                    }
                }
            }
            return true;
        }
    }
}
