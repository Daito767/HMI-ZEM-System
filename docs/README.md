# Documentation

`USER-MANUAL.md` is for whoever operates the stand: the workflows, the alarms, the rules of
operation and the known limitations. The only document here written for the operator. Its Romanian
edition is handed out as a PDF, because the stand is operated in Romanian.

The rest is what cannot be reconstructed from the code: the description of the PLC and the trace of
the visualization that ran on the target before this application. The starting point for any work on
the HMI remains `HMI-HANDOFF.md`, in the root.

| file | what it holds |
|---|---|
| `USER-MANUAL.md` | the operator's manual, organised around workflows |
| `OPCUA-HMI.md` | the 755 published variables, types, enums, what the HMI is allowed to write, and the recipe for drawing the cell |
| `HMI-CONTEXT.md` | what each page of the old HMI displayed and what PLC logic sits behind each button |
| `PLC-HANDOFF.md` | context about the stand and about the CODESYS project. The paths in it point at tools and working trees that are **not** in this project |

## `old-hmi/`

The CODESYS visualization this application replaces. Nothing here runs any more — it was kept
because it is the only source for a few things that do not show in the drawings.

| file | why it matters |
|---|---|
| `top_view.js`, `front_view.js`, `top_pallete_animation.js` | the constants in `StandGeometry` come from here: puller travel, queue pitch, the belt's reference distance, column offset |
| `custom.tools.js`, `*_scale_provider.js` | the px/mm scale bus. It has no counterpart any more: movements are now written in percentages |
| `layers-top.png`, `layers-front.png` | the layer tree of the old visualization — the only source for the drawing order |
| `page-*.png` | screenshots of the HMI on the basic system. The Pneumatic page is the one that settles that there is **a single** vertical piston, not one each for the gripper and the vacuum |
| `unused-svg/` | two drawings from the original set that did not make it into `wwwroot/cell/`: the rail variant of the upgraded system, with pushers, and an empty canvas |

The drawings in use are in `ZEM_BoschRexrothSystemByASTI/wwwroot/cell/`, renamed to kebab-case.
