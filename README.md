![Main branch building](https://github.com/Simpit-team/KerbalSimpitRevamped-Arduino/actions/workflows/cy-arduino.yml/badge.svg?branch=develop)
[![Documentation Status](https://readthedocs.org/projects/kerbalsimpitrevamped-arduino/badge/?version=latest)](https://kerbalsimpitrevamped-arduino.readthedocs.io/en/latest/?badge=latest)
![CKAN badge](https://raw.githubusercontent.com/KSP-CKAN/CKAN/master/assets/ckan-indexed.svg)
[![Documentation Status](https://img.shields.io/discord/775343611780399144.svg?style=flat&logo=discord&label=discord)](https://discord.gg/ZwcPdNcaRN)

# Kerbal Simpit Revamped

This is the repository for the revamped version of the excellent KSP mod Kerbal Simpit, to try and bring it up to date with some of the recent changes to the game. This is a [Kerbal Space Program](https://kerbalspaceprogram.com/) plugin to enable communication with devices over a serial connection.

It works with an accompanying [Arduino library](https://github.com/Simpit-team/KerbalSimpitRevamped-Arduino) to make building hardware devices simpler.

We have a Discord Server! [Invite Link](https://discord.gg/ZwcPdNcaRN)
We have an [online documentation](https://kerbalsimpitrevamped-arduino.readthedocs.io) for using this mod.

Feel free to raise any issue or idea of improvement you have with us, either in Discord or through the GitHub Issues.

## How to install

This mod comes in two parts : the KSP mod and the Arduino lib.

To install the KSP mod, you can either :
 - install it through [CKAN](https://github.com/KSP-CKAN/CKAN) by installing Simpit (version 2.0.0 or after).
 - go the [release](https://github.com/Simpit-team/KerbalSimpitRevamped/releases) tab and dowload the last one. Copy the `KerbalSimpit` folder into the `GameData` folder of your KSP install

Don't forget to update your port name in the `KerbalSimpit\Settings.cfg` file ! Copy it from `Settings.cfg.sample` if it does not already exists. You can find the right port name by copying the port name you are using in the Arduino IDE.

To install the Arduino lib, you can go to the `KerbalSimpit` folder installed previously and copy the `KerbalSimpitRevamped-Arduino` into your Arduino library folder (usually under `Documents\Arduino\libraries`). Then you can open your Arduino IDE and you should find some Simpit examples in the example list. It should be included in the Arduino library manager shortly.

## Controller support for solar panels, radiators and science

This KSP 1 fork extends the standard action-group protocol with the advanced
action-group channels already provided by the Simpit Arduino library:

- Subscribe to outbound channel `56` (`AdvancedActionGroups`) to receive a
  32-bit status value. Each action uses two bits: `0` not available, `1`
  active, `2` inactive and `3` mixed. Solar panels are index `7` and
  radiators index `8`. Science uses index `9` as an extension; send this
  numeric value if your installed Arduino library does not define
  `ADVANCED_SCIENCE_ACTION` yet.
- Send one byte to inbound channel `58` (`SetSingleActionGroup`) to control an
  action. Encode it as `(index << 2) | setting`, where setting is `1` activate,
  `2` deactivate/reset or `3` toggle. Solar and radiator commands operate on
  every matching module on the active vessel; radiator activate/deactivate also
  extend/retract them. Science activate/toggle deploys all science experiments
  that can run, while deactivate resets experiments where KSP permits it.

The science status and channel `61` (`ScienceValue`) use the active
`ScienceSubject` and stored `ScienceData` state. Experiments whose data is
already on the experiment or in a `ModuleScienceContainer` are not counted
again, so the controller only reports science that is still available at the
current situation and biome. The value is a 32-bit float and the controller
blinks only when more than 10 science points are available.

The Kerbal Simpit in-game window also exposes persistent controller settings:
the science threshold, LED blink interval, science collection delay, automatic
collection, solar-to-antenna coupling, status update interval, refresh rate
and verbose logging. Threshold and blink settings are sent to the controller
over channels `62` and `63`.

After science is executed, newly generated `ScienceData` is automatically moved
through one KSP 1 `CollectAllEvent` call five seconds after the controller
button is pressed. The provider compares the review/potential value before and
after that collection and suppresses the alarm until a higher value is found
in a new science context. If no container exists, the data remains on the
experiment. The three-second science-button action still resets resettable
experiments.
