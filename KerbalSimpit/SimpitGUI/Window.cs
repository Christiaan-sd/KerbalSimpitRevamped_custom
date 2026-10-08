using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace KerbalSimpit.SimpitGUI
{


	[KSPAddon(KSPAddon.Startup.Flight, false)]
	public class WindowFlight : Window
	{

	}

	[KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
	public class WindowSpaceCenter : Window
	{

	}
	
	public class Window : MonoBehaviour
	{
		static Rect windowpos;
		private static bool gui_enabled;
		private static bool hide_ui;
		private KSPit simpitInstance;
		private string scienceThresholdText;
		private string blinkIntervalText;
		private string collectDelayText;
		private string statusIntervalText;
		private string refreshRateText;

		static Window instance;


		public static void ToggleGUI()
		{
			gui_enabled = !gui_enabled;
			if (instance != null)
			{
				instance.UpdateGUIState();
			}
		}

		public static void HideGUI()
		{
			gui_enabled = false;
			if (instance != null)
			{
				instance.UpdateGUIState();
			}
		}

		public static void ShowGUI()
		{
			gui_enabled = true;
			if (instance != null)
			{
				instance.UpdateGUIState();
			}
		}

		void UpdateGUIState()
		{
			enabled = !hide_ui && gui_enabled;
		}

		void onHideUI()
		{
			hide_ui = true;
			UpdateGUIState();
		}

		void onShowUI()
		{
			hide_ui = false;
			UpdateGUIState();
		}

		public void Awake()
		{
			instance = this;
			GameEvents.onHideUI.Add(onHideUI);
			GameEvents.onShowUI.Add(onShowUI);
		}

		void OnDestroy()
		{
			instance = null;
			GameEvents.onHideUI.Remove(onHideUI);
			GameEvents.onShowUI.Remove(onShowUI);
		}

		void Start()
		{
			UpdateGUIState();
			simpitInstance = (KSPit) FindObjectOfType(typeof(KSPit));
			scienceThresholdText = KSPit.Config.ScienceThreshold.ToString("0.##");
			blinkIntervalText = KSPit.Config.ScienceBlinkIntervalMs.ToString();
			collectDelayText = KSPit.Config.ScienceCollectDelay.ToString("0.##");
			statusIntervalText = KSPit.Config.StatusUpdateIntervalMs.ToString();
			refreshRateText = KSPit.Config.RefreshRate.ToString();
			if(simpitInstance == null)
            {
				Debug.Log("Simpit : the GUI could not locate the KSPit instance. GUI will not work");
            }
		}

		void WindowGUI(int windowID)
		{
			GUILayout.BeginVertical();

			foreach (Serial.KSPSerialPort port in KSPit.SerialPorts)
			{
				// For all port (except the first one), add a prefix to indicate which port we refer to.
				// For the first one, nothing is written so that for the vast majority if users (that only use a single controller), they are not bothered by port ID.
				String portName = "";
				if (port.ID > 0)
				{
					portName = "(" + port.ID + ") ";
				}

				GUILayout.Label("Status " + portName + ": " + port.portStatus);
				GUILayout.Label("Port used " + portName + ": " + port.PortName);

				GUILayout.BeginHorizontal();
				if (GUILayout.Button("Start " + portName))
				{
					if (simpitInstance != null)
					{
						simpitInstance.OpenPort(port.ID);
					}
				}
				GUILayout.FlexibleSpace();
				if (GUILayout.Button("Close " + portName))
				{
					if (simpitInstance != null)
					{
						simpitInstance.ClosePort(port.ID);
					}
				}
				GUILayout.EndHorizontal();
			}

			if (KSPit.SerialPorts.Count > 1) {
				//only put the Start all/Close all button if there is several ports
				GUILayout.BeginHorizontal();
				if (GUILayout.Button("Start all"))
				{
					if(simpitInstance != null)
					{
						simpitInstance.OpenPorts();
					}
				}
				GUILayout.FlexibleSpace();
				if (GUILayout.Button("Close all"))
				{
					if (simpitInstance != null)
					{
						simpitInstance.ClosePorts();
					}
				}
				GUILayout.EndHorizontal();
			}

			GUILayout.Space(8);
			GUILayout.Label("Controller settings");
			GUILayout.Label("Science threshold");
			scienceThresholdText = GUILayout.TextField(scienceThresholdText);
			GUILayout.Label("Blink interval (ms)");
			blinkIntervalText = GUILayout.TextField(blinkIntervalText);
			GUILayout.Label("Collect delay (s)");
			collectDelayText = GUILayout.TextField(collectDelayText);
			GUILayout.Label("Status update interval (ms)");
			statusIntervalText = GUILayout.TextField(statusIntervalText);
			GUILayout.Label("Plugin refresh rate (ms)");
			refreshRateText = GUILayout.TextField(refreshRateText);
			KSPit.Config.Verbose = GUILayout.Toggle(KSPit.Config.Verbose, "Verbose logging");
			KSPit.Config.ScienceAutoCollect = GUILayout.Toggle(
				KSPit.Config.ScienceAutoCollect, "Auto-collect science");
			KSPit.Config.SolarControlsAntennas = GUILayout.Toggle(
				KSPit.Config.SolarControlsAntennas, "Solar controls antennas");
			if (GUILayout.Button("Save settings"))
			{
				float threshold;
				float delay;
				int blink;
				int status;
				int refresh;
				if (float.TryParse(scienceThresholdText, out threshold) &&
					float.TryParse(collectDelayText, out delay) &&
					int.TryParse(blinkIntervalText, out blink) &&
					int.TryParse(statusIntervalText, out status) &&
					int.TryParse(refreshRateText, out refresh))
				{
					KSPit.Config.ScienceThreshold = Mathf.Clamp(threshold, 0f, 1000f);
					KSPit.Config.ScienceBlinkIntervalMs = Mathf.Clamp(blink, 50, 10000);
					KSPit.Config.ScienceCollectDelay = Mathf.Clamp(delay, 0f, 60f);
					KSPit.Config.StatusUpdateIntervalMs = Mathf.Clamp(status, 50, 5000);
					KSPit.Config.RefreshRate = Mathf.Clamp(refresh, 25, 1000);
					KSPit.Config.Save();
				}
			}

			GUILayout.EndVertical();
			UnityEngine.GUI.DragWindow(new Rect(0, 0, 1000, 20));
		}

		void OnGUI()
		{
			if (gui_enabled)
			{
				UnityEngine.GUI.skin = HighLogic.Skin;
				windowpos = GUILayout.Window(GetInstanceID(), windowpos, WindowGUI, "Kerbal Simpit", GUILayout.Width(200));
			}
		}
	}
}
