# Documentation

`USER-MANUAL.md` is for whoever operates the stand: the workflows, the alarms, the rules of
operation and the known limitations. The only document here written for the operator. Its Romanian
edition is handed out as a PDF, because the stand is operated in Romanian.

The other two are what cannot be reconstructed from the code: what the PLC publishes, and what the
logic behind each button does. The starting point for any work on the HMI remains `HMI-HANDOFF.md`,
in the root.

| file | what it holds |
|---|---|
| `USER-MANUAL.md` | the operator's manual, organised around workflows |
| `OPCUA-HMI.md` | the 755 published variables, types, enums, what the HMI is allowed to write, and the recipe for drawing the cell |
| `HMI-CONTEXT.md` | what each page of the old HMI displayed and what PLC logic sits behind each button |

Between them, `OPCUA-HMI.md` and `HMI-CONTEXT.md` are enough to work on the interface. The CODESYS
project does not need to be opened.

## What is deliberately not here

**The CODESYS project's own handover document.** It describes a different project — its tooling, its
working tree, its session workflow — and its paths point at things that are not part of this
repository. It stays next to that project.

**The old CODESYS visualization** (its JavaScript, the layer-tree screenshots, the pages of the HMI
on the basic system). It is where the constants in `StandGeometry` came from — puller travel, queue
pitch, the belt's reference distance, column offset — and the screenshots are what settled that the
stand has a single vertical piston rather than one each for the gripper and the vacuum. All of that
is already recorded in `HMI-HANDOFF.md`, under "The stand drawings", so the files themselves are
kept outside the repository rather than carried along with it.

The drawings actually in use are in `ZEM_BoschRexrothSystemByASTI/wwwroot/cell/`, renamed to
kebab-case.
